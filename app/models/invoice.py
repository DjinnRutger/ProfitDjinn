from datetime import date, datetime
from app.extensions import db


class Invoice(db.Model):
    __tablename__ = "invoices"

    id = db.Column(db.Integer, primary_key=True)
    invoice_number = db.Column(db.String(50), unique=True, nullable=False, index=True)
    customer_id = db.Column(db.Integer, db.ForeignKey("customers.id"), nullable=False)
    date = db.Column(db.Date, nullable=False, default=date.today)
    notes = db.Column(db.Text, default="")
    term1 = db.Column(db.String(300), default="")
    term2 = db.Column(db.String(300), default="")
    paid = db.Column(db.Boolean, default=False, nullable=False)
    paid_date = db.Column(db.Date)
    created_at = db.Column(db.DateTime, default=datetime.utcnow)

    customer = db.relationship("Customer", back_populates="invoices")
    line_items = db.relationship(
        "InvoiceLine",
        back_populates="invoice",
        cascade="all, delete-orphan",
        order_by="InvoiceLine.id",
    )
    payments = db.relationship(
        "Payment",
        back_populates="invoice",
        cascade="all, delete-orphan",
        order_by="Payment.date",
    )

    def __repr__(self):
        return f"<Invoice {self.invoice_number}>"

    @property
    def total(self):
        return sum(item.amount for item in self.line_items)

    @property
    def amount_paid(self):
        """Sum of all payment records. Falls back to total for legacy paid invoices."""
        if self.payments:
            return sum(p.amount for p in self.payments)
        # Legacy: if marked paid with no payment records, treat as fully paid
        if self.paid:
            return self.total
        return 0.0

    @property
    def balance_due(self):
        return max(0.0, self.total - self.amount_paid)

    @property
    def credit_amount(self):
        """Overpayment that becomes account credit."""
        return max(0.0, self.amount_paid - self.total)

    @property
    def is_partial(self):
        paid_amt = self.amount_paid
        return 0 < paid_amt < self.total

    @property
    def status_label(self):
        if self.paid:
            return "Paid"
        if self.is_partial:
            return "Partial"
        return "Unpaid"

    @property
    def status_badge_class(self):
        if self.paid:
            return "success"
        if self.is_partial:
            return "info"
        return "warning"


class InvoiceLine(db.Model):
    __tablename__ = "invoice_lines"

    id = db.Column(db.Integer, primary_key=True)
    invoice_id = db.Column(db.Integer, db.ForeignKey("invoices.id"), nullable=False)
    description = db.Column(db.String(500), nullable=False)
    quantity = db.Column(db.Float, default=1.0, nullable=False)
    amount = db.Column(db.Float, nullable=False)

    invoice = db.relationship("Invoice", back_populates="line_items")

    @property
    def unit_price(self):
        if self.quantity and self.quantity != 0:
            return self.amount / self.quantity
        return self.amount
