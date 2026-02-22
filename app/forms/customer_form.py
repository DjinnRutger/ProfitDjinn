from flask_wtf import FlaskForm
from wtforms import StringField, TextAreaField, BooleanField
from wtforms.validators import DataRequired, Optional, Email, Length, ValidationError


class CustomerForm(FlaskForm):
    name = StringField("Company / Name", validators=[DataRequired(), Length(max=200)])
    attn = StringField("Attn / Contact", validators=[Optional(), Length(max=200)])
    address = StringField("Address", validators=[Optional(), Length(max=300)])
    city = StringField("City", validators=[Optional(), Length(max=100)])
    state = StringField("State", validators=[Optional(), Length(max=50)])
    zip_code = StringField("ZIP", validators=[Optional(), Length(max=20)])
    phone = StringField("Phone", validators=[Optional(), Length(max=50)])
    email = StringField("Email", validators=[Optional(), Length(max=200)])
    notes = TextAreaField("Notes", validators=[Optional()])
    is_active = BooleanField("Active", default=True)

    def validate_email(self, field):
        if field.data and field.data.strip():
            # Basic email check — only enforce if something was entered
            if "@" not in field.data or "." not in field.data.split("@")[-1]:
                raise ValidationError("Enter a valid email address.")
