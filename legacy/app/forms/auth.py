from flask_wtf import FlaskForm
from wtforms import StringField, PasswordField, BooleanField, SubmitField, EmailField
from wtforms.validators import DataRequired, Length, Email, EqualTo


class LoginForm(FlaskForm):
    username   = StringField("Username",  validators=[DataRequired(), Length(1, 64)])
    password   = PasswordField("Password", validators=[DataRequired()])
    remember_me = BooleanField("Keep me signed in", default=True)
    submit     = SubmitField("Sign In")


class SetupForm(FlaskForm):
    """First start: create the administrator account. Same rules as Admin > Users."""
    username         = StringField("Username", validators=[DataRequired(), Length(3, 64)])
    email            = EmailField("Email",     validators=[DataRequired(), Email(), Length(1, 120)])
    password         = PasswordField("Password", validators=[DataRequired(), Length(8, 128)])
    confirm_password = PasswordField("Confirm Password", validators=[
        DataRequired(), EqualTo("password", message="Passwords must match.")
    ])
    submit           = SubmitField("Create Administrator")
