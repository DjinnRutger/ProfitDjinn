from datetime import date, datetime
from app.extensions import db

METHOD_LABELS = {
    "cash": "Cash",
    "check": "Check",
    "credit_card": "Credit Card",
    "ach": "ACH",
    "venmo": "Venmo",
    "other": "Other",
}


class Payment(db.Model):
    __tablename__ = "payments"

    id = db.Column(db.Integer, primary_key=True)
    invoice_id = db.Column(db.Integer, db.ForeignKey("invoices.id"), nullable=False)
    customer_id = db.Column(db.Integer, db.ForeignKey("customers.id"), nullable=False)
    amount = db.Column(db.Float, nullable=False)
    method = db.Column(db.String(30), nullable=False, default="cash")
    check_number = db.Column(db.String(50), default="")
    date = db.Column(db.Date, nullable=False, default=date.today)
    notes = db.Column(db.Text, default="")
    created_at = db.Column(db.DateTime, default=datetime.utcnow)

    invoice = db.relationship("Invoice", back_populates="payments")
    customer = db.relationship("Customer", back_populates="payments")

    def __repr__(self):
        return f"<Payment {self.id} ${self.amount:.2f} {self.method}>"

    @property
    def method_label(self):
        return METHOD_LABELS.get(self.method, self.method.title())
