from flask_wtf import FlaskForm
from wtforms import StringField, DateField, TextAreaField, BooleanField, HiddenField, IntegerField
from wtforms.validators import DataRequired, Optional, Length


class InvoiceForm(FlaskForm):
    customer_id = IntegerField("Customer", validators=[DataRequired()])
    invoice_number = StringField("Invoice #", validators=[DataRequired(), Length(max=50)])
    date = DateField("Date", validators=[DataRequired()])
    notes = TextAreaField("Notes", validators=[Optional()])
    term1 = StringField("Payment Terms", validators=[Optional(), Length(max=300)])
    term2 = StringField("Additional Terms", validators=[Optional(), Length(max=300)])
    paid = BooleanField("Mark as Paid", default=False)
    # Populated by JavaScript before form submit
    line_items_json = HiddenField("Line Items JSON")
