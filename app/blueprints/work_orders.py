"""Work orders — a rolling tab of unbilled work per customer.

Every mutation here is a plain form POST + redirect (no AJAX), matching the
rest of the app. The only real JavaScript lives in the templates: dialog
prefill on the tab page, and the invoice line builder on the bill screen.
"""
import json
from datetime import date, datetime

from flask import (
    Blueprint, render_template, request, redirect, url_for, flash,
)
from flask_login import login_required
from sqlalchemy.exc import IntegrityError

from app.extensions import db
from app.models.customer import Customer
from app.models.invoice import Invoice, InvoiceLine
from app.models.service_item import ServiceItem
from app.models.work_order import (
    WorkOrder, WorkOrderLine,
    STATUS_PENDING, STATUS_COMPLETED, STATUS_BILLED,
    TYPE_LABOR, TYPE_PART, TYPE_SERVICE, TYPE_OTHER,
    LINE_TYPE_LABELS, GENERAL_LABEL,
)
from app.forms.work_order_form import WorkOrderBillForm
from app.utils.decorators import permission_required
from app.utils.settings import get_setting

work_orders_bp = Blueprint("work_orders", __name__, url_prefix="/work-orders")

VALID_TYPES = (TYPE_LABOR, TYPE_PART, TYPE_SERVICE, TYPE_OTHER)


# ── Helpers ───────────────────────────────────────────────────────────────────

def _next_wo_number() -> str:
    """Next work order number, same derivation as _next_invoice_number()."""
    prefix = get_setting("workorder_prefix", "WO")
    last = (
        WorkOrder.query
        .filter(WorkOrder.number.like(f"{prefix}%"))
        .order_by(WorkOrder.number.desc())
        .first()
    )
    if last:
        try:
            num = int(last.number[len(prefix):]) + 1
        except (ValueError, IndexError):
            num = int(get_setting("workorder_next_number", "1001"))
    else:
        num = int(get_setting("workorder_next_number", "1001"))
    return f"{prefix}{num:04d}"


def _get_or_create_work_order(customer_id: int) -> WorkOrder:
    """Fetch the customer's rolling tab, creating it on first use.

    `customer_id` is unique on work_orders, so a lost double-submit race
    surfaces as an IntegrityError — recover by reading the winner's row.
    """
    wo = WorkOrder.query.filter_by(customer_id=customer_id).first()
    if wo is None:
        Customer.query.get_or_404(customer_id)
        wo = WorkOrder(customer_id=customer_id, number=_next_wo_number())
        db.session.add(wo)
        try:
            db.session.commit()
        except IntegrityError:
            db.session.rollback()
            wo = WorkOrder.query.filter_by(customer_id=customer_id).first()
    return wo


def _parse_float(raw, default=0.0) -> float:
    try:
        value = float(str(raw).strip())
    except (TypeError, ValueError):
        return default
    return value


def _parse_date(raw):
    """Parse an <input type=date> value; None on anything unusable."""
    if not raw:
        return None
    try:
        return datetime.strptime(str(raw).strip(), "%Y-%m-%d").date()
    except (TypeError, ValueError):
        return None


def _apply_line_fields(line: WorkOrderLine, form) -> None:
    """Write the editable fields of a line from a submitted form.

    `amount` is always recomputed from quantity × rate — a client-supplied
    amount is never trusted.
    """
    line.description = (form.get("description") or "").strip()[:500]
    line.project_label = (form.get("project_label") or "").strip()[:120]
    line.internal_note = (form.get("internal_note") or "").strip()

    line_type = (form.get("line_type") or TYPE_LABOR).strip()
    line.line_type = line_type if line_type in VALID_TYPES else TYPE_LABOR

    line.quantity = max(0.0, _parse_float(form.get("quantity"), 1.0))
    line.rate = max(0.0, _parse_float(form.get("rate"), 0.0))
    line.no_charge = bool(form.get("no_charge"))
    line.recalc_amount()


def _line_or_404(line_id: int) -> WorkOrderLine:
    return WorkOrderLine.query.get_or_404(line_id)


def _reject_if_billed(line: WorkOrderLine) -> bool:
    """Billed lines are locked. Returns True if the caller should bail out."""
    if line.is_billed:
        flash(
            "That line has already been billed. Delete or edit the invoice "
            "instead — deleting the invoice returns the work to this tab.",
            "warning",
        )
        return True
    return False


def _tab_url(work_order: WorkOrder):
    return url_for("work_orders.detail", customer_id=work_order.customer_id)


def _render_context(**extra):
    """Shared template context."""
    ctx = {
        "active_page": "workorders",
        "today": date.today(),
        "default_hourly_rate": _parse_float(get_setting("default_hourly_rate", "0.00")),
        "line_type_labels": LINE_TYPE_LABELS,
        "general_label": GENERAL_LABEL,
    }
    ctx.update(extra)
    return ctx


def _active_service_items():
    return (ServiceItem.query
            .filter_by(is_active=True)
            .order_by(ServiceItem.description)
            .all())


# ── Index: open work grouped by customer ──────────────────────────────────────

@work_orders_bp.route("/")
@login_required
@permission_required("workorders.view")
def index():
    q = request.args.get("q", "").strip()
    show = request.args.get("show", "open")

    # `amount`/`status` are real columns, but the roll-up totals are Python
    # properties, so filter in Python. selectinload keeps this to two queries.
    work_orders = (
        WorkOrder.query
        .join(Customer)
        .options(db.selectinload(WorkOrder.lines))
        .order_by(Customer.name)
        .all()
    )

    if show != "all":
        work_orders = [wo for wo in work_orders if wo.has_open_work]

    if q:
        needle = q.lower()
        work_orders = [
            wo for wo in work_orders
            if needle in (wo.customer.name or "").lower()
            or needle in (wo.number or "").lower()
            or any(needle in (ln.description or "").lower()
                   or needle in (ln.project_label or "").lower()
                   for ln in wo.lines)
        ]

    grand_ready = sum(wo.ready_to_bill_total for wo in work_orders)
    grand_pending = sum(wo.pending_count for wo in work_orders)

    return render_template(
        "work_orders/list.html",
        **_render_context(
            work_orders=work_orders,
            q=q,
            show=show,
            grand_ready=grand_ready,
            grand_pending=grand_pending,
        ),
    )


# ── The customer's rolling tab ────────────────────────────────────────────────

@work_orders_bp.route("/customer/<int:customer_id>")
@login_required
@permission_required("workorders.view")
def detail(customer_id):
    customer = Customer.query.get_or_404(customer_id)
    work_order = _get_or_create_work_order(customer.id)

    return render_template(
        "work_orders/detail.html",
        **_render_context(
            customer=customer,
            work_order=work_order,
            groups=work_order.grouped_open(),
            billed_groups=work_order.grouped_billed(),
            service_items=_active_service_items(),
        ),
    )


# ── Line: add ─────────────────────────────────────────────────────────────────

@work_orders_bp.route("/<int:wo_id>/lines/add", methods=["POST"])
@login_required
@permission_required("workorders.create")
def add_line(wo_id):
    work_order = WorkOrder.query.get_or_404(wo_id)

    description = (request.form.get("description") or "").strip()
    if not description:
        flash("A description is required.", "danger")
        return redirect(_tab_url(work_order))

    line = WorkOrderLine(work_order_id=work_order.id, description=description)

    # The quick-add bar posts mode=todo and nothing else; the Log Work dialog
    # posts mode=work with the full field set.
    if (request.form.get("mode") or "todo") == "todo":
        line.project_label = (request.form.get("project_label") or "").strip()[:120]
        line.status = STATUS_PENDING
        line.line_type = TYPE_LABOR
        line.rate = _parse_float(get_setting("default_hourly_rate", "0.00"))
        line.quantity = 1.0
        line.recalc_amount()
        # A to-do has no money attached until it's completed.
        line.amount = 0.0
        db.session.add(line)
        db.session.commit()
        flash("To-do added.", "success")
        return redirect(_tab_url(work_order))

    _apply_line_fields(line, request.form)
    line.status = STATUS_COMPLETED
    line.date_performed = _parse_date(request.form.get("date_performed")) or date.today()
    db.session.add(line)
    db.session.commit()
    flash(f"Logged work: {line.description}", "success")
    return redirect(_tab_url(work_order))


# ── Line: edit ────────────────────────────────────────────────────────────────

@work_orders_bp.route("/lines/<int:line_id>/edit", methods=["POST"])
@login_required
@permission_required("workorders.edit")
def edit_line(line_id):
    line = _line_or_404(line_id)
    work_order = line.work_order
    if _reject_if_billed(line):
        return redirect(_tab_url(work_order))

    if not (request.form.get("description") or "").strip():
        flash("A description is required.", "danger")
        return redirect(_tab_url(work_order))

    _apply_line_fields(line, request.form)

    performed = _parse_date(request.form.get("date_performed"))
    if performed:
        line.date_performed = performed
        line.status = STATUS_COMPLETED
    elif line.status == STATUS_PENDING:
        # Still a to-do — no money until it's completed.
        line.amount = 0.0

    db.session.commit()
    flash("Line updated.", "success")
    return redirect(_tab_url(work_order))


# ── Line: complete (pending → completed) ──────────────────────────────────────

@work_orders_bp.route("/lines/<int:line_id>/complete", methods=["POST"])
@login_required
@permission_required("workorders.edit")
def complete_line(line_id):
    line = _line_or_404(line_id)
    work_order = line.work_order
    if _reject_if_billed(line):
        return redirect(_tab_url(work_order))

    _apply_line_fields(line, request.form)
    line.status = STATUS_COMPLETED
    line.date_performed = _parse_date(request.form.get("date_performed")) or date.today()

    db.session.commit()
    flash(f"Marked complete: {line.description}", "success")
    return redirect(_tab_url(work_order))


# ── Line: reopen (completed → pending) ────────────────────────────────────────

@work_orders_bp.route("/lines/<int:line_id>/reopen", methods=["POST"])
@login_required
@permission_required("workorders.edit")
def reopen_line(line_id):
    line = _line_or_404(line_id)
    work_order = line.work_order
    if _reject_if_billed(line):
        return redirect(_tab_url(work_order))

    line.status = STATUS_PENDING
    line.date_performed = None
    line.amount = 0.0
    db.session.commit()
    flash("Line moved back to pending.", "info")
    return redirect(_tab_url(work_order))


# ── Line: toggle no-charge ────────────────────────────────────────────────────

@work_orders_bp.route("/lines/<int:line_id>/no-charge", methods=["POST"])
@login_required
@permission_required("workorders.edit")
def toggle_no_charge(line_id):
    line = _line_or_404(line_id)
    work_order = line.work_order
    if _reject_if_billed(line):
        return redirect(_tab_url(work_order))

    line.no_charge = not line.no_charge
    db.session.commit()
    flash(
        f"{line.description} marked "
        f"{'no charge' if line.no_charge else 'billable'}.",
        "info",
    )
    return redirect(_tab_url(work_order))


# ── Line: delete ──────────────────────────────────────────────────────────────

@work_orders_bp.route("/lines/<int:line_id>/delete", methods=["POST"])
@login_required
@permission_required("workorders.delete")
def delete_line(line_id):
    line = _line_or_404(line_id)
    work_order = line.work_order
    if _reject_if_billed(line):
        return redirect(_tab_url(work_order))

    description = line.description
    db.session.delete(line)
    db.session.commit()
    flash(f"Removed: {description}", "warning")
    return redirect(_tab_url(work_order))


# ── Tab notes ─────────────────────────────────────────────────────────────────

@work_orders_bp.route("/<int:wo_id>/notes", methods=["POST"])
@login_required
@permission_required("workorders.edit")
def update_notes(wo_id):
    work_order = WorkOrder.query.get_or_404(wo_id)
    work_order.notes = (request.form.get("notes") or "").strip()
    db.session.commit()
    flash("Notes saved.", "success")
    return redirect(_tab_url(work_order))


# ── Billing ───────────────────────────────────────────────────────────────────

def _sorted_billable(lines):
    """Completed lines ordered so the bill screen can emit label sub-headers
    with a simple running-value check: projects A-Z, General last, then date."""
    from app.models.work_order import GENERAL_LABEL as _general
    return sorted(
        lines,
        key=lambda ln: (
            ln.label_or_general == _general,
            ln.label_or_general.lower(),
            ln.date_performed or date.min,
            ln.id,
        ),
    )


def _selected_ids_from_request(work_order):
    """Line ids preselected via ?lines=1,2,3 — defaults to everything ready."""
    raw = request.args.get("lines", "").strip()
    if not raw:
        return [ln.id for ln in work_order.completed_lines if not ln.no_charge]
    ids = set()
    for chunk in raw.split(","):
        chunk = chunk.strip()
        if chunk.isdigit():
            ids.add(int(chunk))
    return [ln.id for ln in work_order.completed_lines if ln.id in ids]


@work_orders_bp.route("/<int:wo_id>/bill", methods=["GET"])
@login_required
@permission_required("invoices.create")
def bill(wo_id):
    work_order = WorkOrder.query.get_or_404(wo_id)
    if not work_order.completed_lines:
        flash("There is no completed work on this tab to bill yet.", "info")
        return redirect(_tab_url(work_order))

    # Import here to avoid a circular import at module load.
    from app.blueprints.invoices import _next_invoice_number

    label_filter = request.args.get("label", "").strip()
    billable = work_order.completed_lines
    if label_filter:
        billable = [ln for ln in billable if ln.label_or_general == label_filter]
        if not billable:
            flash(f"No completed work found under '{label_filter}'.", "warning")
            return redirect(_tab_url(work_order))

    form = WorkOrderBillForm()
    form.invoice_number.data = _next_invoice_number()
    form.date.data = date.today()
    form.term1.data = get_setting("invoice_term1", "Payment Terms: Due within 30 days")
    form.term2.data = get_setting("invoice_term2", "Make all checks payable to Jon Quincy")

    return render_template(
        "work_orders/bill.html",
        **_render_context(
            form=form,
            work_order=work_order,
            customer=work_order.customer,
            billable_lines=_sorted_billable(billable),
            selected_ids=_selected_ids_from_request(work_order),
            label_filter=label_filter,
        ),
    )


@work_orders_bp.route("/<int:wo_id>/bill", methods=["POST"])
@login_required
@permission_required("invoices.create")
def bill_submit(wo_id):
    work_order = WorkOrder.query.get_or_404(wo_id)
    form = WorkOrderBillForm()

    def _rerender(message, category="danger"):
        flash(message, category)
        return render_template(
            "work_orders/bill.html",
            **_render_context(
                form=form,
                work_order=work_order,
                customer=work_order.customer,
                billable_lines=_sorted_billable(work_order.completed_lines),
                selected_ids=selected_ids,
                label_filter="",
            ),
        )

    # Which work order lines are being billed.
    raw_ids = form.selected_line_ids.data or ""
    selected_ids = [int(c) for c in raw_ids.split(",") if c.strip().isdigit()]

    if not form.validate_on_submit():
        return _rerender("Please correct the highlighted fields.")

    # Re-validate server-side: every id must belong to this tab and still be
    # billable. Never trust the posted list — this is what stops a stale second
    # browser tab from billing the same work twice.
    wanted = set(selected_ids)
    valid_lines = [
        ln for ln in work_order.lines
        if ln.id in wanted and ln.effective_status == STATUS_COMPLETED
    ]
    if not valid_lines:
        return _rerender("Select at least one completed line to bill.")
    if len(valid_lines) != len(wanted):
        flash(
            "Some of the selected work was billed or changed since this page "
            "was opened. Check the selection and try again.",
            "warning",
        )
        return redirect(url_for("work_orders.bill", wo_id=work_order.id))

    try:
        items_data = json.loads(form.line_items_json.data or "[]")
    except (json.JSONDecodeError, TypeError):
        items_data = []
    if not items_data:
        return _rerender("The invoice needs at least one line item.")

    invoice_number = (form.invoice_number.data or "").strip().upper()
    if Invoice.query.filter_by(invoice_number=invoice_number).first():
        return _rerender(f"Invoice number {invoice_number} already exists.")

    invoice = Invoice(
        invoice_number=invoice_number,
        customer_id=work_order.customer_id,
        date=form.date.data,
        notes=form.notes.data or "",
        term1=form.term1.data or "",
        term2=form.term2.data or "",
        paid=False,
        paid_date=None,
    )
    db.session.add(invoice)
    db.session.flush()

    for item in items_data:
        description = str(item.get("description", "")).strip()
        if not description:
            continue
        quantity = _parse_float(item.get("quantity"), 1.0)
        db.session.add(InvoiceLine(
            invoice_id=invoice.id,
            description=description[:500],
            quantity=quantity,
            amount=round(_parse_float(item.get("amount"), 0.0), 2),
        ))

    billed_on = date.today()
    for line in valid_lines:
        line.status = STATUS_BILLED
        line.invoice_id = invoice.id
        line.billed_at = billed_on

    db.session.commit()
    flash(
        f"Invoice {invoice.invoice_number} created from "
        f"{len(valid_lines)} work order line{'s' if len(valid_lines) != 1 else ''}.",
        "success",
    )
    return redirect(url_for("invoices.detail", invoice_id=invoice.id))
