"""
Miscellaneous helpers used across blueprints.
"""
from flask import request
from app.models.audit import AuditLog
from app.extensions import db


def log_audit(
    action: str,
    resource: str = None,
    resource_id: int = None,
    details: str = None,
    user_id: int = None,
) -> None:
    """
    Append an audit log entry to the current session.
    Caller is responsible for db.session.commit().
    """
    from flask_login import current_user

    uid = user_id
    if uid is None:
        try:
            if current_user.is_authenticated:
                uid = current_user.id
        except RuntimeError:
            pass  # outside request context

    entry = AuditLog(
        user_id=uid,
        action=action,
        resource=resource,
        resource_id=resource_id,
        details=details,
        ip_address=request.remote_addr if request else None,
    )
    db.session.add(entry)
