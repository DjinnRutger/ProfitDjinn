import json
from datetime import date, datetime

from flask import (
    Blueprint, render_template, request, redirect, url_for,
    flash, Response, abort,
)
from flask_login import login_required

from app.extensions import db
from app.models.invoice import Invoice, InvoiceLine
from app.models.customer import Customer
from app.models.payment import Payment
from app.forms.invoice_form import InvoiceForm
from app.utils.decorators import permission_required
from app.utils.settings import get_setting

invoices_bp = Blueprint("invoices", __name__, url_prefix="/invoices")


def _next_invoice_number() -> str:
    prefix = get_setting("invoice_prefix", "JQ")
    last = (
        Invoice.query
        .filter(Invoice.invoice_number.like(f"{prefix}%"))
        .order_by(Invoice.invoice_number.desc())
        .first()
    )
    if last:
        try:
            num = int(last.invoice_number[len(prefix):]) + 1
        except (ValueError, IndexError):
            num = int(get_setting("invoice_next_number", "2018"))
    else:
        num = int(get_setting("invoice_next_number", "2018"))
    return f"{prefix}{num:04d}"


# ── List ─────────────────────────────────────────────────────────────────────

@invoices_bp.route("/")
@login_required
@permission_required("invoices.view")
def list_invoices():
    status = request.args.get("status", "all")
    q = request.args.get("q", "").strip()

    query = Invoice.query.join(Customer)
    if status == "paid":
        query = query.filter(Invoice.paid == True)
    elif status == "unpaid":
        query = query.filter(Invoice.paid == False)
    if q:
        query = query.filter(
            db.or_(
                Invoice.invoice_number.ilike(f"%{q}%"),
                Customer.name.ilike(f"%{q}%"),
            )
        )

    invoices = query.order_by(Invoice.date.desc(), Invoice.invoice_number.desc()).all()
    # "unpaid" here includes partial — anything with a balance_due > 0
    unpaid_invoices = [inv for inv in Invoice.query.all() if inv.balance_due > 0]
    unpaid_count = len(unpaid_invoices)
    unpaid_total = sum(inv.balance_due for inv in unpaid_invoices)

    return render_template(
        "invoices/list.html",
        invoices=invoices,
        status=status,
        q=q,
        unpaid_count=unpaid_count,
        unpaid_total=unpaid_total,
        active_page="invoices",
    )


# ── Detail ────────────────────────────────────────────────────────────────────

@invoices_bp.route("/<int:invoice_id>")
@login_required
@permission_required("invoices.view")
def detail(invoice_id):
    invoice = Invoice.query.get_or_404(invoice_id)
    return render_template(
        "invoices/detail.html",
        invoice=invoice,
        today=date.today(),
        active_page="invoices",
    )


# ── Create ────────────────────────────────────────────────────────────────────

@invoices_bp.route("/new", methods=["GET", "POST"])
@login_required
@permission_required("invoices.create")
def create():
    form = InvoiceForm()
    customers = Customer.query.filter_by(is_active=True).order_by(Customer.name).all()
    from app.models.service_item import ServiceItem
    service_items = ServiceItem.query.filter_by(is_active=True).order_by(ServiceItem.description).all()

    preselect_customer_id = request.args.get("customer_id", type=int)

    if request.method == "GET":
        form.invoice_number.data = _next_invoice_number()
        form.date.data = date.today()
        form.term1.data = get_setting("invoice_term1", "Payment Terms: Due within 30 days")
        form.term2.data = get_setting("invoice_term2", "Make all checks payable to Jon Quincy")
        if preselect_customer_id:
            form.customer_id.data = preselect_customer_id

    if form.validate_on_submit():
        raw = request.form.get("line_items_json", "[]")
        try:
            items_data = json.loads(raw)
        except (json.JSONDecodeError, TypeError):
            items_data = []

        if not items_data:
            flash("At least one line item is required.", "danger")
            return render_template(
                "invoices/form.html", form=form, customers=customers,
                service_items=service_items, title="New Invoice",
                active_page="invoices",
            )

        if Invoice.query.filter_by(invoice_number=form.invoice_number.data.strip().upper()).first():
            flash(f"Invoice number {form.invoice_number.data} already exists.", "danger")
            return render_template(
                "invoices/form.html", form=form, customers=customers,
                service_items=service_items, title="New Invoice",
                active_page="invoices",
            )

        invoice = Invoice(
            invoice_number=form.invoice_number.data.strip().upper(),
            customer_id=form.customer_id.data,
            date=form.date.data,
            notes=form.notes.data or "",
            term1=form.term1.data or "",
            term2=form.term2.data or "",
            paid=form.paid.data,
            paid_date=date.today() if form.paid.data else None,
        )
        db.session.add(invoice)
        db.session.flush()

        for item in items_data:
            db.session.add(InvoiceLine(
                invoice_id=invoice.id,
                description=str(item.get("description", "")).strip(),
                quantity=float(item.get("quantity", 1.0)),
                amount=float(item.get("amount", 0.0)),
            ))

        db.session.commit()
        flash(f"Invoice {invoice.invoice_number} created.", "success")
        return redirect(url_for("invoices.detail", invoice_id=invoice.id))

    return render_template(
        "invoices/form.html", form=form, customers=customers,
        service_items=service_items, title="New Invoice",
        active_page="invoices",
        preselect_customer_id=preselect_customer_id,
    )


# ── Edit ──────────────────────────────────────────────────────────────────────

@invoices_bp.route("/<int:invoice_id>/edit", methods=["GET", "POST"])
@login_required
@permission_required("invoices.edit")
def edit(invoice_id):
    invoice = Invoice.query.get_or_404(invoice_id)
    form = InvoiceForm(obj=invoice)
    customers = Customer.query.filter_by(is_active=True).order_by(Customer.name).all()
    from app.models.service_item import ServiceItem
    service_items = ServiceItem.query.filter_by(is_active=True).order_by(ServiceItem.description).all()

    if request.method == "GET":
        form.customer_id.data = invoice.customer_id

    if form.validate_on_submit():
        raw = request.form.get("line_items_json", "[]")
        try:
            items_data = json.loads(raw)
        except (json.JSONDecodeError, TypeError):
            items_data = []

        if not items_data:
            flash("At least one line item is required.", "danger")
            return render_template(
                "invoices/form.html", form=form, customers=customers,
                service_items=service_items, invoice=invoice,
                title="Edit Invoice", active_page="invoices",
            )

        dup = Invoice.query.filter(
            Invoice.invoice_number == form.invoice_number.data.strip().upper(),
            Invoice.id != invoice_id,
        ).first()
        if dup:
            flash(f"Invoice number {form.invoice_number.data} already exists.", "danger")
            return render_template(
                "invoices/form.html", form=form, customers=customers,
                service_items=service_items, invoice=invoice,
                title="Edit Invoice", active_page="invoices",
            )

        invoice.invoice_number = form.invoice_number.data.strip().upper()
        invoice.customer_id = form.customer_id.data
        invoice.date = form.date.data
        invoice.notes = form.notes.data or ""
        invoice.term1 = form.term1.data or ""
        invoice.term2 = form.term2.data or ""
        if form.paid.data and not invoice.paid:
            invoice.paid_date = date.today()
        elif not form.paid.data:
            invoice.paid_date = None
        invoice.paid = form.paid.data

        # Replace line items
        for line in list(invoice.line_items):
            db.session.delete(line)
        db.session.flush()

        for item in items_data:
            db.session.add(InvoiceLine(
                invoice_id=invoice.id,
                description=str(item.get("description", "")).strip(),
                quantity=float(item.get("quantity", 1.0)),
                amount=float(item.get("amount", 0.0)),
            ))

        db.session.commit()
        flash(f"Invoice {invoice.invoice_number} updated.", "success")
        return redirect(url_for("invoices.detail", invoice_id=invoice.id))

    return render_template(
        "invoices/form.html", form=form, customers=customers,
        service_items=service_items, invoice=invoice,
        title="Edit Invoice", active_page="invoices",
    )


# ── Delete ────────────────────────────────────────────────────────────────────

@invoices_bp.route("/<int:invoice_id>/delete", methods=["POST"])
@login_required
@permission_required("invoices.delete")
def delete(invoice_id):
    invoice = Invoice.query.get_or_404(invoice_id)
    num = invoice.invoice_number
    db.session.delete(invoice)
    db.session.commit()
    flash(f"Invoice {num} deleted.", "warning")
    return redirect(url_for("invoices.list_invoices"))


# ── Payment recording ─────────────────────────────────────────────────────────

@invoices_bp.route("/<int:invoice_id>/record-payment", methods=["POST"])
@login_required
@permission_required("invoices.edit")
def record_payment(invoice_id):
    invoice = Invoice.query.get_or_404(invoice_id)

    try:
        amount = float(request.form.get("amount", 0))
    except (ValueError, TypeError):
        amount = 0.0

    if amount <= 0:
        flash("Payment amount must be greater than zero.", "danger")
        return redirect(url_for("invoices.detail", invoice_id=invoice_id))

    method = request.form.get("method", "cash")
    check_number = request.form.get("check_number", "").strip()
    notes = request.form.get("notes", "").strip()

    date_str = request.form.get("date", "")
    try:
        payment_date = date.fromisoformat(date_str)
    except (ValueError, TypeError):
        payment_date = date.today()

    payment = Payment(
        invoice_id=invoice_id,
        customer_id=invoice.customer_id,
        amount=amount,
        method=method,
        check_number=check_number if method == "check" else "",
        date=payment_date,
        notes=notes,
    )
    db.session.add(payment)
    db.session.flush()

    # Recalculate paid status
    new_amount_paid = sum(p.amount for p in invoice.payments)
    if new_amount_paid >= invoice.total:
        invoice.paid = True
        if not invoice.paid_date:
            invoice.paid_date = payment_date
        credit = new_amount_paid - invoice.total
        if credit > 0:
            flash(
                f"Payment of ${amount:.2f} recorded. Invoice paid in full. "
                f"${credit:.2f} credit applied to account.",
                "success",
            )
        else:
            flash(f"Payment of ${amount:.2f} recorded. Invoice paid in full.", "success")
    else:
        invoice.paid = False
        invoice.paid_date = None
        balance = invoice.total - new_amount_paid
        flash(
            f"Partial payment of ${amount:.2f} recorded. Balance remaining: ${balance:.2f}.",
            "info",
        )

    db.session.commit()
    return redirect(url_for("invoices.detail", invoice_id=invoice_id))


@invoices_bp.route("/<int:invoice_id>/payments/<int:payment_id>/delete", methods=["POST"])
@login_required
@permission_required("invoices.edit")
def delete_payment(invoice_id, payment_id):
    invoice = Invoice.query.get_or_404(invoice_id)
    payment = Payment.query.filter_by(id=payment_id, invoice_id=invoice_id).first_or_404()

    db.session.delete(payment)
    db.session.flush()

    # Recalculate paid status after deletion
    remaining_paid = sum(p.amount for p in invoice.payments if p.id != payment_id)
    if remaining_paid >= invoice.total:
        invoice.paid = True
    else:
        invoice.paid = False
        invoice.paid_date = None

    db.session.commit()
    flash("Payment deleted.", "warning")
    return redirect(url_for("invoices.detail", invoice_id=invoice_id))


@invoices_bp.route("/<int:invoice_id>/mark-unpaid", methods=["POST"])
@login_required
@permission_required("invoices.edit")
def mark_unpaid(invoice_id):
    invoice = Invoice.query.get_or_404(invoice_id)
    # Delete all payment records
    for p in list(invoice.payments):
        db.session.delete(p)
    invoice.paid = False
    invoice.paid_date = None
    db.session.commit()
    flash(f"Invoice {invoice.invoice_number} marked as unpaid. All payments removed.", "warning")
    return redirect(url_for("invoices.detail", invoice_id=invoice_id))


# ── Print / PDF ───────────────────────────────────────────────────────────────

@invoices_bp.route("/<int:invoice_id>/print")
@login_required
@permission_required("invoices.view")
def print_view(invoice_id):
    invoice = Invoice.query.get_or_404(invoice_id)
    return render_template(
        "invoices/pdf_print.html",
        invoice=invoice,
        company_name=get_setting("company_name", ""),
        company_address=get_setting("company_address", ""),
        company_city=get_setting("company_city", ""),
        company_state=get_setting("company_state", ""),
        company_zip=get_setting("company_zip", ""),
        company_email=get_setting("company_email", ""),
        company_phone=get_setting("company_phone", ""),
    )


@invoices_bp.route("/<int:invoice_id>/pdf")
@login_required
@permission_required("invoices.view")
def pdf_download(invoice_id):
    invoice = Invoice.query.get_or_404(invoice_id)
    try:
        from app.utils.pdf_generator import generate_invoice_pdf
        pdf_bytes = generate_invoice_pdf(invoice, {
            "company_name": get_setting("company_name", ""),
            "company_address": get_setting("company_address", ""),
            "company_city": get_setting("company_city", ""),
            "company_state": get_setting("company_state", ""),
            "company_zip": get_setting("company_zip", ""),
            "company_email": get_setting("company_email", ""),
            "company_phone": get_setting("company_phone", ""),
        })
        return Response(
            pdf_bytes,
            mimetype="application/pdf",
            headers={
                "Content-Disposition": f'attachment; filename="{invoice.invoice_number}.pdf"'
            },
        )
    except ImportError:
        flash("PDF generation requires fpdf2. Run: pip install fpdf2", "danger")
        return redirect(url_for("invoices.print_view", invoice_id=invoice_id))
