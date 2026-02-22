from datetime import datetime
from app.extensions import db


class Customer(db.Model):
    __tablename__ = "customers"

    id = db.Column(db.Integer, primary_key=True)
    name = db.Column(db.String(200), nullable=False, index=True)
    attn = db.Column(db.String(200), default="")
    address = db.Column(db.String(300), default="")
    city = db.Column(db.String(100), default="")
    state = db.Column(db.String(50), default="")
    zip_code = db.Column(db.String(20), default="")
    phone = db.Column(db.String(50), default="")
    email = db.Column(db.String(200), default="")
    notes = db.Column(db.Text, default="")
    is_active = db.Column(db.Boolean, default=True, nullable=False)
    created_at = db.Column(db.DateTime, default=datetime.utcnow)

    invoices = db.relationship(
        "Invoice",
        back_populates="customer",
        lazy="dynamic",
        cascade="all, delete-orphan",
    )
    payments = db.relationship(
        "Payment",
        back_populates="customer",
        lazy="dynamic",
    )

    def __repr__(self):
        return f"<Customer {self.name}>"

    @property
    def full_address(self):
        parts = [p for p in [self.address, self.city, self.state, self.zip_code] if p]
        return ", ".join(parts)

    @property
    def total_invoiced(self):
        return sum(inv.total for inv in self.invoices)

    @property
    def total_outstanding(self):
        """Sum of balance_due across all invoices (accurate with partial payments)."""
        return sum(inv.balance_due for inv in self.invoices)

    @property
    def total_paid(self):
        return self.total_invoiced - self.total_outstanding

    @property
    def account_credit(self):
        """Sum of overpayments across all invoices."""
        return sum(inv.credit_amount for inv in self.invoices)
