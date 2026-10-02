from flask_wtf import FlaskForm
from wtforms import StringField, DecimalField, BooleanField
from wtforms.validators import DataRequired, Length, NumberRange, Optional


class ItemForm(FlaskForm):
    description = StringField(
        "Description",
        validators=[DataRequired(), Length(max=500)],
    )
    price = DecimalField(
        "Default Price ($)",
        validators=[DataRequired(), NumberRange(min=0)],
        places=2,
    )
    is_active = BooleanField("Active", default=True)
