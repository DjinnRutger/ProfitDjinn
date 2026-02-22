import datetime

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
    from app.models.customer import Customer
    from app.models.invoice import Invoice

    current_year = datetime.date.today().year
    all_invoices = Invoice.query.all()
    unpaid_invoices = [inv for inv in all_invoices if inv.balance_due > 0]

    yearly_revenue = sum(
        inv.amount_paid for inv in all_invoices
        if inv.date.year == current_year and inv.amount_paid > 0
    )

    stats = {
        "total_users":      User.query.count(),
        "active_users":     User.query.filter_by(is_active=True).count(),
        "total_roles":      Role.query.count(),
        "total_settings":   Setting.query.count(),
        "total_customers":  Customer.query.filter_by(is_active=True).count(),
        "total_invoices":   len(all_invoices),
        "unpaid_invoices":  len(unpaid_invoices),
        "unpaid_total":     sum(inv.balance_due for inv in unpaid_invoices),
        "yearly_revenue":   yearly_revenue,
        "current_year":     current_year,
    }
    recent_invoices = (
        Invoice.query
        .order_by(Invoice.date.desc())
        .limit(5)
        .all()
    )
    return render_template(
        "main/dashboard.html",
        stats=stats,
        recent_invoices=recent_invoices,
        active_page="dashboard",
    )


@main_bp.route("/revenue")
@login_required
@permission_required("invoices.view")
def revenue():
    from app.models.invoice import Invoice

    today = datetime.date.today()
    current_year = today.year

    all_invoices = Invoice.query.all()

    # Build list of years that have any invoice activity
    invoice_years = sorted(set(inv.date.year for inv in all_invoices), reverse=True)
    if current_year not in invoice_years:
        invoice_years.insert(0, current_year)

    selected = request.args.get("year", str(current_year))

    # ── Filter invoices for the selected period ───────────────────────────────
    if selected == "all":
        filtered = [inv for inv in all_invoices if inv.amount_paid > 0]
    else:
        yr = int(selected)
        filtered = [inv for inv in all_invoices if inv.date.year == yr]

    # ── Monthly breakdown (for single-year view) ──────────────────────────────
    MONTH_NAMES = ["Jan", "Feb", "Mar", "Apr", "May", "Jun",
                   "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"]
    monthly_revenue  = {m: 0.0 for m in range(1, 13)}
    monthly_invoiced = {m: 0.0 for m in range(1, 13)}
    monthly_count    = {m: 0   for m in range(1, 13)}
    for inv in filtered:
        m = inv.date.month
        monthly_revenue[m]  += inv.amount_paid
        monthly_invoiced[m] += inv.total
        monthly_count[m]    += 1

    # ── Yearly breakdown (for "All Years" view) ───────────────────────────────
    yearly_totals = {}
    for yr in invoice_years:
        yearly_totals[yr] = sum(
            inv.amount_paid for inv in all_invoices
            if inv.date.year == yr and inv.amount_paid > 0
        )

    # ── Customer breakdown ────────────────────────────────────────────────────
    customer_map = {}
    for inv in filtered:
        if inv.amount_paid > 0:
            name = inv.customer.name
            customer_map[name] = customer_map.get(name, 0.0) + inv.amount_paid
    customer_revenue = sorted(customer_map.items(), key=lambda x: x[1], reverse=True)

    # ── Summary stats ─────────────────────────────────────────────────────────
    total_revenue    = sum(inv.amount_paid for inv in filtered)
    total_invoiced   = sum(inv.total       for inv in filtered)
    total_outstanding = sum(inv.balance_due for inv in filtered)
    paid_count       = sum(1 for inv in filtered if inv.paid)
    partial_count    = sum(1 for inv in filtered if inv.is_partial)

    # Months that have any revenue (for avg calculation)
    active_months = sum(1 for v in monthly_revenue.values() if v > 0) or 1
    avg_monthly = total_revenue / active_months if selected != "all" else None

    # Previous year comparison
    prev_year_revenue = None
    if selected != "all":
        yr = int(selected)
        prev_year_revenue = sum(
            inv.amount_paid for inv in all_invoices
            if inv.date.year == yr - 1 and inv.amount_paid > 0
        )

    # YTD progress (only for current year)
    ytd_pct = None
    if selected == str(current_year) and prev_year_revenue:
        ytd_pct = round((total_revenue / prev_year_revenue) * 100, 1) if prev_year_revenue else None

    return render_template(
        "main/revenue.html",
        selected_year=selected,
        current_year=current_year,
        invoice_years=invoice_years,
        # monthly
        monthly_revenue=monthly_revenue,
        monthly_invoiced=monthly_invoiced,
        monthly_count=monthly_count,
        month_names=MONTH_NAMES,
        # yearly
        yearly_totals=yearly_totals,
        # customers
        customer_revenue=customer_revenue,
        # summary
        total_revenue=total_revenue,
        total_invoiced=total_invoiced,
        total_outstanding=total_outstanding,
        paid_count=paid_count,
        partial_count=partial_count,
        avg_monthly=avg_monthly,
        prev_year_revenue=prev_year_revenue,
        ytd_pct=ytd_pct,
        invoice_count=len(filtered),
        active_page="revenue",
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
