from flask import Blueprint, render_template, request, redirect, url_for, flash
from flask_login import login_required

from app.extensions import db
from app.models.customer import Customer
from app.forms.customer_form import CustomerForm
from app.utils.decorators import permission_required

customers_bp = Blueprint("customers", __name__, url_prefix="/customers")


@customers_bp.route("/")
@login_required
@permission_required("customers.view")
def list_customers():
    search = request.args.get("q", "").strip()
    show_inactive = request.args.get("inactive", "0") == "1"

    query = Customer.query
    if not show_inactive:
        query = query.filter_by(is_active=True)
    if search:
        query = query.filter(Customer.name.ilike(f"%{search}%"))

    customers = query.order_by(Customer.name).all()
    return render_template(
        "customers/list.html",
        customers=customers,
        search=search,
        show_inactive=show_inactive,
        active_page="customers",
    )


@customers_bp.route("/<int:customer_id>")
@login_required
@permission_required("customers.view")
def detail(customer_id):
    customer = Customer.query.get_or_404(customer_id)
    from app.models.invoice import Invoice
    invoices = (
        Invoice.query
        .filter_by(customer_id=customer_id)
        .order_by(Invoice.date.desc())
        .all()
    )
    return render_template(
        "customers/detail.html",
        customer=customer,
        invoices=invoices,
        active_page="customers",
    )


@customers_bp.route("/new", methods=["GET", "POST"])
@login_required
@permission_required("customers.create")
def create():
    form = CustomerForm()
    if form.validate_on_submit():
        customer = Customer(
            name=form.name.data.strip(),
            attn=(form.attn.data or "").strip(),
            address=(form.address.data or "").strip(),
            city=(form.city.data or "").strip(),
            state=(form.state.data or "").strip().upper(),
            zip_code=(form.zip_code.data or "").strip(),
            phone=(form.phone.data or "").strip(),
            email=(form.email.data or "").strip().lower(),
            notes=form.notes.data or "",
            is_active=form.is_active.data,
        )
        db.session.add(customer)
        db.session.commit()
        flash(f"Customer '{customer.name}' created.", "success")
        return redirect(url_for("customers.detail", customer_id=customer.id))
    return render_template(
        "customers/form.html",
        form=form,
        title="New Customer",
        active_page="customers",
    )


@customers_bp.route("/<int:customer_id>/edit", methods=["GET", "POST"])
@login_required
@permission_required("customers.edit")
def edit(customer_id):
    customer = Customer.query.get_or_404(customer_id)
    form = CustomerForm(obj=customer)
    if form.validate_on_submit():
        customer.name = form.name.data.strip()
        customer.attn = (form.attn.data or "").strip()
        customer.address = (form.address.data or "").strip()
        customer.city = (form.city.data or "").strip()
        customer.state = (form.state.data or "").strip().upper()
        customer.zip_code = (form.zip_code.data or "").strip()
        customer.phone = (form.phone.data or "").strip()
        customer.email = (form.email.data or "").strip().lower()
        customer.notes = form.notes.data or ""
        customer.is_active = form.is_active.data
        db.session.commit()
        flash(f"Customer '{customer.name}' updated.", "success")
        return redirect(url_for("customers.detail", customer_id=customer.id))
    return render_template(
        "customers/form.html",
        form=form,
        customer=customer,
        title="Edit Customer",
        active_page="customers",
    )


@customers_bp.route("/<int:customer_id>/delete", methods=["POST"])
@login_required
@permission_required("customers.delete")
def delete(customer_id):
    customer = Customer.query.get_or_404(customer_id)
    name = customer.name
    db.session.delete(customer)
    db.session.commit()
    flash(f"Customer '{name}' deleted.", "warning")
    return redirect(url_for("customers.list_customers"))
