from datetime import datetime, timezone

from flask import Blueprint, current_app, render_template, redirect, url_for, flash, request, session
from flask_login import login_user, logout_user, login_required, current_user

from app.extensions import db, limiter
from app.models.user import User
from app.forms.auth import LoginForm, SetupForm
from app.utils.helpers import log_audit

auth_bp = Blueprint("auth", __name__, url_prefix="/auth")

# Releases before 1.0.0-beta seeded an "admin" account with this password, and
# it is in the public source history. It is kept only to recognise it: anyone
# who signs in with it is made to change it before doing anything else.
LEGACY_DEFAULT_PASSWORD = "Admin@1234!"

# Reachable while a password change is being forced.
_PASSWORD_CHANGE_ENDPOINTS = {"main.profile", "main.change_password", "auth.logout", "static"}

_DEFAULT_PASSWORD_WARNING = ("Your password is the old built-in default, which is public. "
                             "Choose a new one to continue.")


@auth_bp.before_app_request
def require_setup_and_safe_password():
    """Two gates in front of every request.

    1. No user exists yet (fresh install): everything goes to /auth/setup.
    2. Signed in with the old public default password: everything goes to the
       profile page until it is changed.
    """
    endpoint = request.endpoint or ""

    # Once a user exists it never goes back to zero (an admin cannot delete
    # their own account), so stop querying after the first hit.
    if not current_app.config.get("SETUP_COMPLETE"):
        if User.query.first() is None:
            if endpoint in ("auth.setup", "static"):
                return None
            return redirect(url_for("auth.setup"))
        current_app.config["SETUP_COMPLETE"] = True

    if session.get("must_change_password") and current_user.is_authenticated:
        if endpoint not in _PASSWORD_CHANGE_ENDPOINTS:
            flash(_DEFAULT_PASSWORD_WARNING, "warning")
            return redirect(url_for("main.profile"))
    return None


@auth_bp.route("/setup", methods=["GET", "POST"])
@limiter.limit("15 per minute")
def setup():
    """First start only: create the administrator account."""
    if User.query.first() is not None:
        return redirect(url_for("auth.login"))

    form = SetupForm()
    if form.validate_on_submit():
        from app.models.role import Role

        admin_role = Role.query.filter_by(name="Administrator").first()
        if admin_role is None:
            raise RuntimeError(
                "The Administrator role is missing, so the first account cannot be "
                "created. The database seed did not run; delete app.db and start again."
            )

        user = User(
            username=form.username.data.strip(),
            email=form.email.data.strip(),
            is_admin=True,
            is_active=True,
            role_id=admin_role.id,
        )
        user.set_password(form.password.data)
        user.last_login = datetime.now(timezone.utc)
        db.session.add(user)
        db.session.flush()
        log_audit("created", "user", resource_id=user.id, details="first-start administrator")
        db.session.commit()

        login_user(user, remember=True)
        flash("Administrator created. Next, put your business details on your invoices "
              "under Admin > Settings.", "success")
        return redirect(url_for("main.dashboard"))

    return render_template("auth/setup.html", form=form)


@auth_bp.route("/login", methods=["GET", "POST"])
@limiter.limit("15 per minute")
def login():
    if current_user.is_authenticated:
        return redirect(url_for("main.dashboard"))

    form = LoginForm()
    if form.validate_on_submit():
        user = User.query.filter_by(username=form.username.data.strip()).first()

        if user is None or not user.check_password(form.password.data):
            flash("Invalid username or password.", "danger")
            log_audit("login_failed", "user", details=f"username={form.username.data}")
            db.session.commit()
            return render_template("auth/login.html", form=form)

        if not user.is_active:
            flash("Your account is disabled. Contact an administrator.", "danger")
            return render_template("auth/login.html", form=form)

        login_user(user, remember=form.remember_me.data)
        user.last_login = datetime.now(timezone.utc)
        log_audit("login", "user", resource_id=user.id)
        db.session.commit()

        if form.password.data == LEGACY_DEFAULT_PASSWORD:
            session["must_change_password"] = True
            flash(_DEFAULT_PASSWORD_WARNING, "warning")
            return redirect(url_for("main.profile"))

        # Safe open redirect – only allow local paths
        next_page = request.args.get("next")
        if next_page and next_page.startswith("/") and not next_page.startswith("//"):
            return redirect(next_page)
        return redirect(url_for("main.dashboard"))

    return render_template("auth/login.html", form=form)


@auth_bp.route("/logout")
@login_required
def logout():
    log_audit("logout", "user", resource_id=current_user.id)
    db.session.commit()
    logout_user()
    flash("You have been signed out.", "info")
    return redirect(url_for("auth.login"))
