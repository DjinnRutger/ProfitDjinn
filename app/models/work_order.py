"""Work orders — a rolling, never-closing tab of work per customer.

Lines move through three states:
    pending   → a to-do you still owe the customer (no money attached yet)
    completed → work performed, with a date and hours/qty × rate
    billed    → locked and linked to the invoice it went onto

The point of the module is that nothing billable is ever forgotten, so lines
are only ever *moved* to `billed` — never deleted as a side effect of billing.
"""
from collections import OrderedDict
from datetime import date, datetime

from app.extensions import db

# Line lifecycle
STATUS_PENDING = "pending"
STATUS_COMPLETED = "completed"
STATUS_BILLED = "billed"

# Line types. `labor` treats `quantity` as hours; everything else as a count.
TYPE_LABOR = "labor"
TYPE_PART = "part"
TYPE_SERVICE = "service"
TYPE_OTHER = "other"

LINE_TYPE_LABELS = {
    TYPE_LABOR: "Labor",
    TYPE_PART: "Parts",
    TYPE_SERVICE: "Services",
    TYPE_OTHER: "Other",
}

# Lines with no project label are grouped under this heading, sorted last.
GENERAL_LABEL = "General"


class WorkOrder(db.Model):
    __tablename__ = "work_orders"

    id = db.Column(db.Integer, primary_key=True)
    # One rolling tab per customer — the unique constraint is the enforcement.
    customer_id = db.Column(
        db.Integer, db.ForeignKey("customers.id"), nullable=False, unique=True
    )
    number = db.Column(db.String(50), unique=True, nullable=False, index=True)
    notes = db.Column(db.Text, default="")
    is_active = db.Column(db.Boolean, default=True, nullable=False)
    created_at = db.Column(db.DateTime, default=datetime.utcnow)

    customer = db.relationship("Customer", back_populates="work_order")
    lines = db.relationship(
        "WorkOrderLine",
        back_populates="work_order",
        cascade="all, delete-orphan",
        order_by="WorkOrderLine.id",
    )

    def __repr__(self):
        return f"<WorkOrder {self.number}>"

    # ── Line buckets ─────────────────────────────────────────────────────────

    @property
    def pending_lines(self):
        return [ln for ln in self.lines if ln.effective_status == STATUS_PENDING]

    @property
    def completed_lines(self):
        """Work performed but not yet billed — includes no-charge lines."""
        return [ln for ln in self.lines if ln.effective_status == STATUS_COMPLETED]

    @property
    def billed_lines(self):
        return [ln for ln in self.lines if ln.effective_status == STATUS_BILLED]

    @property
    def pending_count(self):
        return len(self.pending_lines)

    # ── Totals ───────────────────────────────────────────────────────────────

    @property
    def ready_to_bill_total(self):
        """Money sitting on the tab. No-charge lines contribute nothing."""
        return sum(ln.amount for ln in self.completed_lines if not ln.no_charge)

    @property
    def billed_total(self):
        return sum(ln.amount for ln in self.billed_lines if not ln.no_charge)

    @property
    def has_open_work(self):
        return bool(self.pending_lines or self.completed_lines)

    # ── Grouping ─────────────────────────────────────────────────────────────

    def grouped_open(self):
        """Open (pending + completed) lines grouped by project label.

        Returns an OrderedDict {label: [lines]} with labelled projects sorted
        alphabetically and the unlabelled "General" bucket last.
        """
        buckets = {}
        for line in self.lines:
            if line.effective_status == STATUS_BILLED:
                continue
            label = (line.project_label or "").strip() or GENERAL_LABEL
            buckets.setdefault(label, []).append(line)

        ordered = OrderedDict()
        for label in sorted(k for k in buckets if k != GENERAL_LABEL):
            ordered[label] = buckets[label]
        if GENERAL_LABEL in buckets:
            ordered[GENERAL_LABEL] = buckets[GENERAL_LABEL]
        return ordered

    def grouped_billed(self):
        """Billed lines grouped by the invoice they went onto, newest first.

        Returns a list of dicts so templates can render a history row even when
        the invoice has since been deleted (invoice will be None).
        """
        buckets = {}
        for line in self.billed_lines:
            buckets.setdefault(line.invoice_id, []).append(line)

        groups = []
        for invoice_id, lines in buckets.items():
            groups.append({
                "invoice_id": invoice_id,
                "invoice": lines[0].invoice,
                "billed_at": lines[0].billed_at,
                "lines": lines,
                "total": sum(ln.amount for ln in lines),
            })
        groups.sort(key=lambda g: (g["billed_at"] or date.min, g["invoice_id"] or 0),
                    reverse=True)
        return groups

    @property
    def open_labels(self):
        """Distinct project labels in use, for the quick-add datalist."""
        seen = []
        for line in self.lines:
            label = (line.project_label or "").strip()
            if label and label not in seen:
                seen.append(label)
        return sorted(seen)


class WorkOrderLine(db.Model):
    __tablename__ = "work_order_lines"

    id = db.Column(db.Integer, primary_key=True)
    work_order_id = db.Column(
        db.Integer, db.ForeignKey("work_orders.id"), nullable=False
    )
    project_label = db.Column(db.String(120), default="")
    description = db.Column(db.String(500), nullable=False)
    line_type = db.Column(db.String(20), default=TYPE_LABOR, nullable=False)
    status = db.Column(db.String(20), default=STATUS_PENDING, nullable=False, index=True)
    date_performed = db.Column(db.Date)
    # For `labor` this is hours; otherwise a count.
    quantity = db.Column(db.Float, default=1.0, nullable=False)
    # Hourly rate or unit price.
    rate = db.Column(db.Float, default=0.0, nullable=False)
    # Extended total — always recomputed server-side as quantity * rate.
    amount = db.Column(db.Float, default=0.0, nullable=False)
    no_charge = db.Column(db.Boolean, default=False, nullable=False)
    # Private to you — never rendered on an invoice or print view.
    internal_note = db.Column(db.Text, default="")
    # Linked to the invoice, not an invoice line: invoice edits replace all
    # their lines, and roll-up billing collapses many work lines into one.
    invoice_id = db.Column(
        db.Integer, db.ForeignKey("invoices.id", ondelete="SET NULL")
    )
    billed_at = db.Column(db.Date)
    created_at = db.Column(db.DateTime, default=datetime.utcnow)

    work_order = db.relationship("WorkOrder", back_populates="lines")
    invoice = db.relationship("Invoice", back_populates="work_order_lines")

    def __repr__(self):
        return f"<WorkOrderLine {self.id} {self.status} {self.description[:30]!r}>"

    def recalc_amount(self):
        """Single source of truth for the extended total."""
        self.amount = round((self.quantity or 0.0) * (self.rate or 0.0), 2)
        return self.amount

    @property
    def effective_status(self):
        """Status corrected for a vanished invoice.

        SQLite never enforces foreign keys here, so if an invoice row
        disappears by any path other than the un-billing in invoices.delete(),
        the line would otherwise be stranded as "billed" forever with nothing
        to point at. Treat it as billable again — losing work is the one
        outcome this module exists to prevent.
        """
        if self.status == STATUS_BILLED and self.invoice_id is None:
            return STATUS_COMPLETED
        return self.status

    @property
    def is_billed(self):
        return self.effective_status == STATUS_BILLED

    @property
    def billable_amount(self):
        """What this line contributes to an invoice."""
        return 0.0 if self.no_charge else self.amount

    @property
    def type_label(self):
        return LINE_TYPE_LABELS.get(self.line_type, "Other")

    @property
    def is_hourly(self):
        return self.line_type == TYPE_LABOR

    @property
    def quantity_label(self):
        """Human-readable qty, e.g. '1.5 h' or '2'."""
        qty = self.quantity or 0.0
        if self.is_hourly:
            return f"{qty:g} h"
        return f"{qty:g}"

    @property
    def label_or_general(self):
        return (self.project_label or "").strip() or GENERAL_LABEL

    @property
    def status_label(self):
        status = self.effective_status
        if status == STATUS_BILLED:
            return "Billed"
        if status == STATUS_COMPLETED:
            return "No Charge" if self.no_charge else "Ready to Bill"
        return "Pending"

    @property
    def status_badge_class(self):
        status = self.effective_status
        if status == STATUS_BILLED:
            return "success"
        if status == STATUS_COMPLETED:
            return "info" if self.no_charge else "warning"
        return "secondary"
