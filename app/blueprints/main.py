from flask import Blueprint, render_template, request, jsonify, flash, redirect, url_for
from flask_login import login_required, current_user

from app.extensions import db
from app.utils.decorators import permission_required

main_bp = Blueprint("main", __name__)

VALID_THEMES = {"light", "dark", "terminal"}


@main_bp.route("/")
@main_bp.route("/dashboard")
@login_required
@permission_required("dashboard.view")
def dashboard():
    from app.models.user import User
    from app.models.role import Role
    from app.models.setting import Setting
    from app.models.audit import AuditLog

    stats = {
        "total_users":  User.query.count(),
        "active_users": User.query.filter_by(is_active=True).count(),
        "total_roles":  Role.query.count(),
        "total_settings": Setting.query.count(),
    }
    recent_activity = (
        AuditLog.query
        .order_by(AuditLog.created_at.desc())
        .limit(10)
        .all()
    )
    return render_template(
        "main/dashboard.html",
        stats=stats,
        recent_activity=recent_activity,
        active_page="dashboard",
    )


@main_bp.route("/api/change-password", methods=["POST"])
@login_required
def change_password():
    """AJAX endpoint – change the current user's password."""
    data = request.get_json(silent=True) or {}
    current_pw = data.get("current_password", "")
    new_pw     = data.get("new_password", "")
    confirm_pw = data.get("confirm_password", "")

    if not current_pw:
        return jsonify(error="Current password is required."), 400
    if not current_user.check_password(current_pw):
        return jsonify(error="Current password is incorrect."), 400
    if not new_pw:
        return jsonify(error="New password is required."), 400
    if len(new_pw) < 8:
        return jsonify(error="New password must be at least 8 characters."), 400
    if new_pw != confirm_pw:
        return jsonify(error="Passwords do not match."), 400

    current_user.set_password(new_pw)
    db.session.commit()
    return jsonify(success=True, message="Password updated successfully.")


@main_bp.route("/api/set-theme", methods=["POST"])
@login_required
def set_theme():
    """AJAX endpoint – persist theme preference to the database."""
    data = request.get_json(silent=True) or {}
    theme = data.get("theme", "light")
    if theme not in VALID_THEMES:
        return jsonify(error="Invalid theme"), 400
    current_user.theme = theme
    db.session.commit()
    return jsonify(theme=current_user.theme)


@main_bp.route("/profile", methods=["GET", "POST"])
@login_required
def profile():
    from app.forms.profile import ChangePasswordForm
    form = ChangePasswordForm()
    if form.validate_on_submit():
        if not current_user.check_password(form.current_password.data):
            flash("Current password is incorrect.", "danger")
        elif form.new_password.data:
            current_user.set_password(form.new_password.data)
            db.session.commit()
            flash("Password updated successfully.", "success")
        return redirect(url_for("main.profile"))
    return render_template("main/profile.html", form=form, active_page="profile")
