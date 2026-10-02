import os

from flask import Blueprint, render_template, redirect, url_for, flash, request, abort, current_app
from flask_login import login_required, current_user
from markupsafe import Markup, escape

from app.extensions import db
from app.models.user import User
from app.models.role import Role
from app.models.permission import Permission
from app.models.setting import Setting
from app.models.audit import AuditLog
from app.forms.admin import UserCreateForm, UserEditForm, RoleForm
from app.utils.helpers import log_audit
from app.utils.settings import get_settings_by_category

admin_bp = Blueprint("admin", __name__, url_prefix="/admin")


# ── Blueprint-wide access guard ─────────────────────────────────────────────
@admin_bp.before_request
@login_required
def require_admin_access():
    """Every admin route requires authentication + admin/full_access permission."""
    if not current_user.is_authenticated:
        return redirect(url_for("auth.login", next=request.url))
    if not (current_user.is_admin or current_user.has_permission("admin.full_access")):
        abort(403)


# ── Dashboard ────────────────────────────────────────────────────────────────
@admin_bp.route("/")
def index():
    stats = {
        "total_users":      User.query.count(),
        "active_users":     User.query.filter_by(is_active=True).count(),
        "total_roles":      Role.query.count(),
        "total_permissions": Permission.query.count(),
        "total_settings":   Setting.query.count(),
        "total_logs":       AuditLog.query.count(),
    }
    recent_logs = AuditLog.query.order_by(AuditLog.created_at.desc()).limit(15).all()
    return render_template("admin/index.html", stats=stats, recent_logs=recent_logs, active_page="admin")


# ── Users ─────────────────────────────────────────────────────────────────────
@admin_bp.route("/users")
def users():
    page   = request.args.get("page", 1, type=int)
    search = request.args.get("search", "").strip()
    query  = User.query
    if search:
        like = f"%{search}%"
        query = query.filter(
            db.or_(User.username.ilike(like), User.email.ilike(like))
        )
    pagination = query.order_by(User.created_at.desc()).paginate(
        page=page, per_page=20, error_out=False
    )
    return render_template(
        "admin/users.html",
        users=pagination,
        search=search,
        active_page="admin_users",
    )


@admin_bp.route("/users/new", methods=["GET", "POST"])
def user_create():
    form = UserCreateForm()
    form.role_id.choices = [(0, "— No Role —")] + [(r.id, r.name) for r in Role.query.order_by(Role.name)]

    if form.validate_on_submit():
        user = User(
            username=form.username.data.strip(),
            email=form.email.data.strip().lower(),
            is_admin=form.is_admin.data,
            is_active=form.is_active.data,
            role_id=form.role_id.data or None,
        )
        user.set_password(form.password.data)
        db.session.add(user)
        db.session.flush()
        log_audit("created", "user", resource_id=user.id, details=f"username={user.username}")
        db.session.commit()
        flash(Markup(f"User <strong>{escape(user.username)}</strong> created successfully."), "success")
        return redirect(url_for("admin.users"))

    return render_template(
        "admin/user_form.html", form=form, user=None,
        title="Create User", active_page="admin_users",
    )


@admin_bp.route("/users/<int:user_id>/edit", methods=["GET", "POST"])
def user_edit(user_id: int):
    user = db.get_or_404(User, user_id)
    form = UserEditForm(user=user, obj=user)
    form.role_id.choices = [(0, "— No Role —")] + [(r.id, r.name) for r in Role.query.order_by(Role.name)]

    if request.method == "GET":
        form.role_id.data = user.role_id or 0

    if form.validate_on_submit():
        user.username  = form.username.data.strip()
        user.email     = form.email.data.strip().lower()
        user.is_admin  = form.is_admin.data
        user.is_active = form.is_active.data
        user.role_id   = form.role_id.data or None
        if form.password.data:
            user.set_password(form.password.data)
        log_audit("updated", "user", resource_id=user.id, details=f"username={user.username}")
        db.session.commit()
        flash(Markup(f"User <strong>{escape(user.username)}</strong> updated."), "success")
        return redirect(url_for("admin.users"))

    return render_template(
        "admin/user_form.html", form=form, user=user,
        title="Edit User", active_page="admin_users",
    )


@admin_bp.route("/users/<int:user_id>/delete", methods=["POST"])
def user_delete(user_id: int):
    user = db.get_or_404(User, user_id)
    if user.id == current_user.id:
        flash("You cannot delete your own account.", "danger")
        return redirect(url_for("admin.users"))
    username = user.username
    log_audit("deleted", "user", resource_id=user.id, details=f"username={username}")
    db.session.delete(user)
    db.session.commit()
    flash(Markup(f"User <strong>{escape(username)}</strong> deleted."), "success")
    return redirect(url_for("admin.users"))


@admin_bp.route("/users/<int:user_id>/toggle", methods=["POST"])
def user_toggle(user_id: int):
    user = db.get_or_404(User, user_id)
    if user.id == current_user.id:
        flash("You cannot deactivate your own account.", "danger")
        return redirect(url_for("admin.users"))
    user.is_active = not user.is_active
    status = "activated" if user.is_active else "deactivated"
    log_audit(status, "user", resource_id=user.id)
    db.session.commit()
    flash(Markup(f"User <strong>{escape(user.username)}</strong> {escape(status)}."), "success")
    return redirect(url_for("admin.users"))


# ── Roles ─────────────────────────────────────────────────────────────────────
@admin_bp.route("/roles")
def roles():
    all_roles = Role.query.order_by(Role.name).all()
    return render_template("admin/roles.html", roles=all_roles, active_page="admin_roles")


@admin_bp.route("/roles/new", methods=["GET", "POST"])
def role_create():
    form = RoleForm()
    all_permissions = Permission.query.order_by(Permission.name).all()
    form.permissions.choices = [(p.id, p.name) for p in all_permissions]

    if form.validate_on_submit():
        role = Role(name=form.name.data.strip(), description=form.description.data)
        if form.permissions.data:
            role.permissions = Permission.query.filter(
                Permission.id.in_(form.permissions.data)
            ).all()
        db.session.add(role)
        db.session.flush()
        log_audit("created", "role", resource_id=role.id, details=f"name={role.name}")
        db.session.commit()
        flash(Markup(f"Role <strong>{escape(role.name)}</strong> created."), "success")
        return redirect(url_for("admin.roles"))

    return render_template(
        "admin/role_form.html", form=form, role=None,
        all_permissions=all_permissions, title="Create Role", active_page="admin_roles",
    )


@admin_bp.route("/roles/<int:role_id>/edit", methods=["GET", "POST"])
def role_edit(role_id: int):
    role = db.get_or_404(Role, role_id)
    form = RoleForm(obj=role)
    all_permissions = Permission.query.order_by(Permission.name).all()
    form.permissions.choices = [(p.id, p.name) for p in all_permissions]

    if request.method == "GET":
        form.permissions.data = [p.id for p in role.permissions]

    if form.validate_on_submit():
        role.name        = form.name.data.strip()
        role.description = form.description.data
        role.permissions = Permission.query.filter(
            Permission.id.in_(form.permissions.data or [])
        ).all()
        log_audit("updated", "role", resource_id=role.id, details=f"name={role.name}")
        db.session.commit()
        flash(Markup(f"Role <strong>{escape(role.name)}</strong> updated."), "success")
        return redirect(url_for("admin.roles"))

    return render_template(
        "admin/role_form.html", form=form, role=role,
        all_permissions=all_permissions, title="Edit Role", active_page="admin_roles",
    )


@admin_bp.route("/roles/<int:role_id>/delete", methods=["POST"])
def role_delete(role_id: int):
    role = db.get_or_404(Role, role_id)
    if role.users.count() > 0:
        flash(Markup(f"Cannot delete <strong>{escape(role.name)}</strong> — it has assigned users."), "danger")
        return redirect(url_for("admin.roles"))
    name = role.name
    log_audit("deleted", "role", resource_id=role.id, details=f"name={name}")
    db.session.delete(role)
    db.session.commit()
    flash(Markup(f"Role <strong>{escape(name)}</strong> deleted."), "success")
    return redirect(url_for("admin.roles"))


# ── Settings ──────────────────────────────────────────────────────────────────
@admin_bp.route("/settings", methods=["GET", "POST"])
def settings():
    categorized = get_settings_by_category()

    if request.method == "POST":
        # Update every setting that appears in the form
        for key in request.form:
            if key.startswith("csrf_"):
                continue
            s = Setting.query.filter_by(key=key).first()
            if s:
                s.value = request.form[key]

        # Unchecked booleans are absent from POST data – force them to 'false'
        for settings_list in categorized.values():
            for s in settings_list:
                if s.type == "boolean" and s.key not in request.form:
                    s.value = "false"

        log_audit("updated", "settings", details="bulk update")
        db.session.commit()
        flash("Settings saved successfully.", "success")
        return redirect(url_for("admin.settings"))

    return render_template("admin/settings.html", categorized=categorized, active_page="admin_settings")


# ── Login logo upload / remove ────────────────────────────────────────────────

@admin_bp.route("/settings/upload-logo", methods=["POST"])
def upload_login_logo():
    f = request.files.get("login_logo_file")
    if not f or not f.filename:
        flash("No file selected.", "danger")
        return redirect(url_for("admin.settings"))

    if not f.filename.lower().endswith(".png"):
        flash("Only PNG files are accepted.", "danger")
        return redirect(url_for("admin.settings"))

    # Validate PNG magic bytes
    header = f.read(8)
    if header[:8] != b"\x89PNG\r\n\x1a\n":
        flash("File does not appear to be a valid PNG image.", "danger")
        return redirect(url_for("admin.settings"))

    # Check size (2 MB limit)
    f.seek(0, 2)
    if f.tell() > 2 * 1024 * 1024:
        flash("Logo must be under 2 MB.", "danger")
        return redirect(url_for("admin.settings"))

    img_dir = os.path.join(current_app.root_path, "static", "img")
    os.makedirs(img_dir, exist_ok=True)
    f.seek(0)
    f.save(os.path.join(img_dir, "login_logo.png"))

    s = Setting.query.filter_by(key="login_logo").first()
    if s:
        s.value = "login_logo.png"
        db.session.commit()

    flash("Login logo uploaded.", "success")
    return redirect(url_for("admin.settings") + "#login")


@admin_bp.route("/settings/remove-logo", methods=["POST"])
def remove_login_logo():
    img_path = os.path.join(current_app.root_path, "static", "img", "login_logo.png")
    if os.path.exists(img_path):
        os.remove(img_path)

    s = Setting.query.filter_by(key="login_logo").first()
    if s:
        s.value = ""
        db.session.commit()

    flash("Login logo removed.", "info")
    return redirect(url_for("admin.settings") + "#login")


# ── App icon upload / remove ──────────────────────────────────────────────────

@admin_bp.route("/settings/upload-app-icon", methods=["POST"])
def upload_app_icon():
    f = request.files.get("app_icon_file")
    if not f or not f.filename:
        flash("No file selected.", "danger")
        return redirect(url_for("admin.settings"))

    if not f.filename.lower().endswith(".png"):
        flash("Only PNG files are accepted.", "danger")
        return redirect(url_for("admin.settings"))

    header = f.read(8)
    if header[:8] != b"\x89PNG\r\n\x1a\n":
        flash("File does not appear to be a valid PNG image.", "danger")
        return redirect(url_for("admin.settings"))

    f.seek(0, 2)
    if f.tell() > 2 * 1024 * 1024:
        flash("Icon must be under 2 MB.", "danger")
        return redirect(url_for("admin.settings"))

    img_dir = os.path.join(current_app.root_path, "static", "img")
    os.makedirs(img_dir, exist_ok=True)
    f.seek(0)
    f.save(os.path.join(img_dir, "app_icon.png"))

    s = Setting.query.filter_by(key="app_icon_img").first()
    if s:
        s.value = "app_icon.png"
        db.session.commit()

    flash("App icon uploaded.", "success")
    return redirect(url_for("admin.settings") + "#appearance")


@admin_bp.route("/settings/remove-app-icon", methods=["POST"])
def remove_app_icon():
    img_path = os.path.join(current_app.root_path, "static", "img", "app_icon.png")
    if os.path.exists(img_path):
        os.remove(img_path)

    s = Setting.query.filter_by(key="app_icon_img").first()
    if s:
        s.value = ""
        db.session.commit()

    flash("App icon removed.", "info")
    return redirect(url_for("admin.settings") + "#appearance")


# ── Audit Log ─────────────────────────────────────────────────────────────────
@admin_bp.route("/audit")
def audit():
    page = request.args.get("page", 1, type=int)
    logs = (
        AuditLog.query
        .order_by(AuditLog.created_at.desc())
        .paginate(page=page, per_page=50, error_out=False)
    )
    return render_template("admin/audit.html", logs=logs, active_page="admin_audit")
