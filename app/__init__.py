"""
LocalVibe – Flask application factory.
"""
import os
from flask import Flask, render_template, url_for
from sqlalchemy import inspect, text
from app.config import config
from app.extensions import db, login_manager, csrf, limiter, migrate


def create_app(config_name: str = "default") -> Flask:
    app = Flask(__name__, instance_relative_config=True)
    app.config.from_object(config[config_name])

    # Ensure instance directory exists (SQLite lives here)
    os.makedirs(app.instance_path, exist_ok=True)

    # ── Initialise extensions ────────────────────────────────────────────────
    db.init_app(app)
    login_manager.init_app(app)
    csrf.init_app(app)
    limiter.init_app(app)
    migrate.init_app(app, db)

    if app.config.get("TALISMAN_ENABLED"):
        from flask_talisman import Talisman
        Talisman(app, **app.config.get("TALISMAN_CONFIG", {}))

    # ── Register blueprints ──────────────────────────────────────────────────
    from app.blueprints.auth import auth_bp
    from app.blueprints.main import main_bp
    from app.blueprints.admin import admin_bp
    from app.blueprints.database_mgr import database_bp

    app.register_blueprint(auth_bp)
    app.register_blueprint(main_bp)
    app.register_blueprint(admin_bp)
    app.register_blueprint(database_bp)

    # ── Context processors ───────────────────────────────────────────────────
    @app.context_processor
    def inject_globals():
        from flask_login import current_user
        from app.utils.settings import get_setting

        def admin_nav():
            """Build admin sidebar items — only called when user is admin."""
            return [
                {"icon": "bi-gauge",        "label": "Overview",    "url": url_for("admin.index"),    "key": "admin"},
                {"icon": "bi-people",        "label": "Users",       "url": url_for("admin.users"),    "key": "admin_users"},
                {"icon": "bi-shield-check",  "label": "Roles & Perms", "url": url_for("admin.roles"), "key": "admin_roles"},
                {"icon": "bi-sliders",       "label": "Settings",    "url": url_for("admin.settings"), "key": "admin_settings"},
                {"icon": "bi-journal-text",  "label": "Audit Log",   "url": url_for("admin.audit"),      "key": "admin_audit"},
                {"icon": "bi-database",      "label": "Database",    "url": url_for("database.index"),   "key": "admin_database"},
            ]

        return dict(get_setting=get_setting, admin_nav=admin_nav)

    # ── Error handlers ───────────────────────────────────────────────────────
    @app.errorhandler(403)
    def forbidden(e):
        return render_template("errors/403.html"), 403

    @app.errorhandler(404)
    def not_found(e):
        return render_template("errors/404.html"), 404

    @app.errorhandler(500)
    def internal_error(e):
        db.session.rollback()
        return render_template("errors/500.html"), 500

    # ── Database + seed ──────────────────────────────────────────────────────
    with app.app_context():
        # Register all models with SQLAlchemy before create_all().
        # Using importlib avoids the "import app.models" pattern which would
        # silently shadow the local 'app' Flask-instance variable with the module.
        import importlib
        importlib.import_module("app.models")
        db.create_all()
        _run_migrations()
        _seed_database()

    return app


# ── Migration helper ─────────────────────────────────────────────────────────
def _run_migrations() -> None:
    """Apply lightweight schema migrations that Flask-Migrate doesn't handle for SQLite."""
    inspector = inspect(db.engine)
    cols = [c["name"] for c in inspector.get_columns("users")]
    if "theme" not in cols:
        db.session.execute(text(
            "ALTER TABLE users ADD COLUMN theme VARCHAR(32) NOT NULL DEFAULT 'light'"
        ))
        if "dark_mode" in cols:
            db.session.execute(text(
                "UPDATE users SET theme='dark' WHERE dark_mode=1"
            ))
        db.session.commit()


# ── Seed helper ──────────────────────────────────────────────────────────────
def _seed_database() -> None:
    """Populate initial data on first run (runs only if no users exist)."""
    from app.models.user import User
    from app.models.role import Role
    from app.models.permission import Permission
    from app.models.setting import Setting

    if User.query.first():
        return  # already seeded

    # ── Permissions ──────────────────────────────────────────────────────────
    perm_defs = [
        ("admin.full_access",  "Full admin panel access"),
        ("dashboard.view",     "View dashboard"),
        ("users.view",         "View user list"),
        ("users.create",       "Create users"),
        ("users.edit",         "Edit users"),
        ("users.delete",       "Delete users"),
        ("roles.view",         "View roles"),
        ("roles.create",       "Create roles"),
        ("roles.edit",         "Edit roles"),
        ("roles.delete",       "Delete roles"),
        ("settings.view",      "View settings"),
        ("settings.edit",      "Edit settings"),
        ("audit.view",         "View audit log"),
        ("database.view",      "View database management"),
        ("database.backup",    "Download database backups"),
        ("database.configure", "Configure database connection"),
    ]
    perms: dict[str, Permission] = {}
    for name, desc in perm_defs:
        p = Permission(name=name, description=desc)
        db.session.add(p)
        perms[name] = p
    db.session.flush()

    # ── Roles ────────────────────────────────────────────────────────────────
    admin_role = Role(name="Administrator", description="Full system access")
    admin_role.permissions = list(perms.values())

    user_role = Role(name="Standard User", description="Basic read-only access")
    user_role.permissions = [perms["dashboard.view"]]

    db.session.add_all([admin_role, user_role])
    db.session.flush()

    # ── Default admin user ───────────────────────────────────────────────────
    admin = User(
        username="admin",
        email="admin@localvibe.local",
        is_admin=True,
        is_active=True,
        role_id=admin_role.id,
    )
    admin.set_password("Admin@1234!")
    db.session.add(admin)

    # ── Default settings ─────────────────────────────────────────────────────
    # (key, value, type, description, category, options_json)
    setting_defs = [
        ("app_name",          "LocalVibe",                  "text",    "Application display name",                        "general",    None),
        ("app_tagline",       "Your Local Network Hub",     "text",    "Tagline shown on the login page",                 "general",    None),
        ("app_icon",          "bi-lightning-charge-fill",   "text",    "Bootstrap Icons class for the sidebar logo",      "appearance", None),
        ("footer_text",       "LocalVibe \u2014 Built with Flask", "text", "Footer copyright text",                      "general",    None),
        ("primary_color",     "#2563eb",                    "color",   "Primary brand/accent colour",                     "appearance", None),
        ("default_theme",     "light",                      "select",  "Default colour theme for new users",              "appearance", '["light","dark","terminal"]'),
        ("allow_registration","false",                      "boolean", "Allow new visitors to self-register",             "security",   None),
        ("maintenance_mode",  "false",                      "boolean", "Show maintenance page to non-admin users",        "general",    None),
        ("items_per_page",    "20",                         "number",  "Rows shown per page in data tables",              "general",    None),
        ("session_timeout",   "480",                        "number",  "Session idle timeout in minutes (0 = never)",     "security",   None),
    ]
    for key, value, stype, desc, cat, opts in setting_defs:
        db.session.add(Setting(key=key, value=value, type=stype,
                               description=desc, category=cat, options=opts))

    db.session.commit()
