"""Database management blueprint — statistics, backup, and connection configuration."""
import os
import re
import shutil
import tempfile
from datetime import datetime, timezone, timedelta

from flask import (
    Blueprint, render_template, redirect, url_for, flash,
    request, jsonify, send_file, abort,
)
from flask_login import login_required, current_user
from sqlalchemy import inspect, text

from app.extensions import db
from app.utils.helpers import log_audit

database_bp = Blueprint("database", __name__, url_prefix="/admin/database")


# ── Blueprint-wide access guard ──────────────────────────────────────────────
@database_bp.before_request
@login_required
def require_admin_access():
    if not (current_user.is_admin or current_user.has_permission("admin.full_access")):
        abort(403)


# ── Helpers ───────────────────────────────────────────────────────────────────
def _human_size(size_bytes: int) -> str:
    for unit in ("B", "KB", "MB", "GB"):
        if size_bytes < 1024:
            return f"{size_bytes:.1f} {unit}"
        size_bytes /= 1024
    return f"{size_bytes:.1f} TB"


def _mask_db_url(url: str) -> str:
    """Hide password in connection strings for display."""
    return re.sub(r"://([^:]+):([^@]+)@", r"://\1:***@", url)


def _get_db_info() -> dict:
    """Gather database statistics and metadata."""
    engine = db.engine
    db_url = str(engine.url)
    is_sqlite = db_url.startswith("sqlite")

    inspector = inspect(engine)
    tables = sorted(inspector.get_table_names())

    table_stats = []
    total_rows = 0
    for table in tables:
        try:
            count = db.session.execute(
                text(f'SELECT COUNT(*) FROM "{table}"')
            ).scalar() or 0
        except Exception:
            count = 0
        total_rows += count
        table_stats.append({"name": table, "rows": count})

    table_stats.sort(key=lambda x: x["rows"], reverse=True)

    db_size_bytes = None
    db_path = None
    if is_sqlite:
        db_path = db_url.replace("sqlite:///", "").replace("sqlite://", "")
        if os.path.isfile(db_path):
            db_size_bytes = os.path.getsize(db_path)

    return {
        "is_sqlite": is_sqlite,
        "db_url_safe": _mask_db_url(db_url),
        "db_path": db_path,
        "tables": table_stats,
        "table_count": len(tables),
        "total_rows": total_rows,
        "db_size_bytes": db_size_bytes,
        "db_size_human": _human_size(db_size_bytes) if db_size_bytes is not None else "N/A",
    }


def _get_activity_data(days: int = 14) -> list:
    """Audit log entry counts per day for the last N days."""
    from app.models.audit import AuditLog
    today = datetime.now(timezone.utc).date()
    result = []
    for i in range(days - 1, -1, -1):
        day = today - timedelta(days=i)
        start = datetime(day.year, day.month, day.day, tzinfo=timezone.utc)
        end = start + timedelta(days=1)
        count = AuditLog.query.filter(
            AuditLog.created_at >= start,
            AuditLog.created_at < end,
        ).count()
        result.append({"date": day.strftime("%b %d"), "count": count})
    return result


def _update_env_db_uri(new_uri: str) -> bool:
    """Update DATABASE_URI in .env. Returns True on success.
    Raises ValueError if new_uri contains line-break characters.
    """
    if any(c in new_uri for c in ("\n", "\r", "\x00")):
        raise ValueError("URI must not contain newline or null characters.")
    here = os.path.dirname(os.path.abspath(__file__))       # app/blueprints/
    project_root = os.path.dirname(os.path.dirname(here))   # project root
    env_path = os.path.join(project_root, ".env")
    if not os.path.isfile(env_path):
        return False
    with open(env_path, "r", encoding="utf-8") as f:
        lines = f.readlines()
    new_lines = []
    updated = False
    for line in lines:
        if line.startswith("DATABASE_URI="):
            new_lines.append(f"DATABASE_URI={new_uri}\n")
            updated = True
        else:
            new_lines.append(line)
    if not updated:
        new_lines.append(f"DATABASE_URI={new_uri}\n")
    with open(env_path, "w", encoding="utf-8") as f:
        f.writelines(new_lines)
    return True


# ── Routes ────────────────────────────────────────────────────────────────────
@database_bp.route("/")
def index():
    db_info      = _get_db_info()
    activity     = _get_activity_data(14)
    max_activity = max((d["count"] for d in activity), default=1) or 1
    max_rows     = max((t["rows"] for t in db_info["tables"]), default=1) or 1

    from app.models.setting import Setting
    db_type_s  = Setting.query.filter_by(key="db_type").first()
    ext_uri_s  = Setting.query.filter_by(key="external_db_uri").first()
    pending_db_type = db_type_s.value if db_type_s else "sqlite"
    pending_ext_uri = ext_uri_s.value if ext_uri_s else ""

    return render_template(
        "admin/database.html",
        db_info=db_info,
        activity=activity,
        max_activity=max_activity,
        max_rows=max_rows,
        pending_db_type=pending_db_type,
        pending_ext_uri=pending_ext_uri,
        active_page="admin_database",
    )


@database_bp.route("/backup/download")
def backup_download():
    engine = db.engine
    db_url = str(engine.url)

    if not db_url.startswith("sqlite"):
        flash(
            "Backup download is only available for SQLite. "
            "Use <code>pg_dump</code> for PostgreSQL.",
            "warning",
        )
        return redirect(url_for("database.index") + "#backup")

    db_path = db_url.replace("sqlite:///", "").replace("sqlite://", "")
    if not os.path.isfile(db_path):
        flash("Database file not found.", "danger")
        return redirect(url_for("database.index"))

    # Copy to a temp file so we read safely while DB may be open
    tmp = tempfile.NamedTemporaryFile(suffix=".db", delete=False)
    tmp.close()
    shutil.copy2(db_path, tmp.name)

    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    filename  = f"localvibe_backup_{timestamp}.db"
    log_audit("backup_download", "database", details=f"file={filename}")

    return send_file(
        tmp.name,
        as_attachment=True,
        download_name=filename,
        mimetype="application/octet-stream",
    )


@database_bp.route("/config", methods=["POST"])
def config_save():
    new_type = request.form.get("db_type", "sqlite")
    new_uri  = request.form.get("external_db_uri", "").strip()

    if new_type not in ("sqlite", "postgresql"):
        flash("Invalid database type.", "danger")
        return redirect(url_for("database.index") + "#config")

    if new_type == "postgresql" and not new_uri:
        flash("A PostgreSQL connection URI is required.", "danger")
        return redirect(url_for("database.index") + "#config")

    # Reject control characters that could inject extra lines into .env
    if any(c in new_uri for c in ("\n", "\r", "\x00")):
        flash("Connection URI must not contain newline or null characters.", "danger")
        return redirect(url_for("database.index") + "#config")

    from app.models.setting import Setting

    def _upsert(key, value, desc, cat):
        s = Setting.query.filter_by(key=key).first()
        if s:
            s.value = value
        else:
            db.session.add(Setting(
                key=key, value=value, type="text",
                description=desc, category=cat,
            ))

    _upsert("db_type",        new_type, "Active database type",             "database")
    _upsert("external_db_uri", new_uri, "External database connection URI", "database")

    env_updated = False
    if new_type == "postgresql" and new_uri:
        env_updated = _update_env_db_uri(new_uri)
    elif new_type == "sqlite":
        from app.config import _DEFAULT_DB
        env_updated = _update_env_db_uri(_DEFAULT_DB)

    db.session.commit()
    log_audit("updated", "database_config", details=f"db_type={new_type}")

    if env_updated:
        flash(
            "Configuration saved and <strong>.env</strong> updated. "
            "Restart the server to apply the new database connection.",
            "success",
        )
    else:
        flash(
            "Configuration saved. Update <strong>DATABASE_URI</strong> in your "
            "<code>.env</code> file manually, then restart the server.",
            "warning",
        )

    return redirect(url_for("database.index") + "#config")


@database_bp.route("/test-connection", methods=["POST"])
def test_connection():
    data = request.get_json(silent=True) or {}
    uri  = data.get("uri", "").strip()

    if not uri:
        return jsonify(ok=False, error="Connection URI is required.")

    if not uri.startswith((
        "postgresql://", "postgresql+psycopg2://",
        "postgresql+pg8000://", "postgresql+asyncpg://",
    )):
        return jsonify(ok=False, error="URI must start with postgresql://")

    try:
        from sqlalchemy import create_engine
        test_engine = create_engine(uri, connect_args={"connect_timeout": 5})
        with test_engine.connect() as conn:
            conn.execute(text("SELECT 1"))
        test_engine.dispose()
        return jsonify(ok=True, message="Connection successful!")
    except ImportError:
        return jsonify(
            ok=False,
            error="psycopg2 driver not installed. Run: pip install psycopg2-binary",
        )
    except Exception as e:
        return jsonify(ok=False, error=str(e))
