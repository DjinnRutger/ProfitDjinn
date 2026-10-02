from flask_wtf import FlaskForm
from wtforms import StringField, DateField, TextAreaField, HiddenField
from wtforms.validators import DataRequired, Optional, Length


class WorkOrderBillForm(FlaskForm):
    """Header fields for the invoice produced from a work order.

    The work order lines being billed ride in `selected_line_ids` (CSV) and the
    resulting invoice lines in `line_items_json` — both populated by JS on the
    bill screen, and both re-validated server-side.
    """
    invoice_number = StringField("Invoice #", validators=[DataRequired(), Length(max=50)])
    date = DateField("Date", validators=[DataRequired()])
    notes = TextAreaField("Notes", validators=[Optional()])
    term1 = StringField("Payment Terms", validators=[Optional(), Length(max=300)])
    term2 = StringField("Additional Terms", validators=[Optional(), Length(max=300)])

    # Populated by JavaScript before submit
    line_items_json = HiddenField("Line Items JSON")
    selected_line_ids = HiddenField("Selected Work Order Lines")
    rollup = HiddenField("Roll-up Style")
