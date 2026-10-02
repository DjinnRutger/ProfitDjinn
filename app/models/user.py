from datetime import datetime, timezone
from flask_login import UserMixin
from argon2 import PasswordHasher
from argon2.exceptions import VerifyMismatchError, InvalidHashError, VerificationError
from app.extensions import db

_ph = PasswordHasher()


class User(db.Model, UserMixin):
    __tablename__ = "users"

    id            = db.Column(db.Integer, primary_key=True)
    username      = db.Column(db.String(64),  unique=True, nullable=False, index=True)
    email         = db.Column(db.String(120), unique=True, nullable=False, index=True)
    password_hash = db.Column(db.String(512), nullable=False)
    is_active     = db.Column(db.Boolean, default=True,  nullable=False)
    is_admin      = db.Column(db.Boolean, default=False, nullable=False)
    role_id       = db.Column(db.Integer, db.ForeignKey("roles.id"), nullable=True)
    theme         = db.Column(db.String(32), default="light", nullable=False)
    created_at    = db.Column(db.DateTime, default=lambda: datetime.now(timezone.utc))
    last_login    = db.Column(db.DateTime, nullable=True)

    audit_logs = db.relationship("AuditLog", backref="user", lazy="dynamic",
                                 foreign_keys="AuditLog.user_id")

    # ── Password helpers ────────────────────────────────────────────────────
    def set_password(self, password: str) -> None:
        self.password_hash = _ph.hash(password)

    def check_password(self, password: str) -> bool:
        try:
            return _ph.verify(self.password_hash, password)
        except (VerifyMismatchError, InvalidHashError, VerificationError):
            return False

    # ── Permission helpers ──────────────────────────────────────────────────
    def has_permission(self, perm_name: str) -> bool:
        if not self.is_active:
            return False
        if self.is_admin:
            return True
        if self.role is None:
            return False
        return self.role.has_permission(perm_name)

    # ── Backward-compat shim (templates may check dark_mode) ────────────────
    @property
    def dark_mode(self) -> bool:
        return self.theme in ("dark", "terminal")

    # ── Display helpers ─────────────────────────────────────────────────────
    def get_initials(self) -> str:
        parts = self.username.split()
        if len(parts) >= 2:
            return (parts[0][0] + parts[-1][0]).upper()
        return self.username[:2].upper()

    # Flask-Login requires this property
    @property
    def active(self) -> bool:
        return self.is_active

    def __repr__(self) -> str:
        return f"<User {self.username}>"
