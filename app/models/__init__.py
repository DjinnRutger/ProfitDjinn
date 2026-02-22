# Import all models so SQLAlchemy can discover them for db.create_all()
from app.models.permission import Permission
from app.models.role import Role, role_permissions
from app.models.user import User
from app.models.setting import Setting
from app.models.audit import AuditLog
from app.models.customer import Customer
from app.models.invoice import Invoice, InvoiceLine
from app.models.service_item import ServiceItem
from app.models.payment import Payment

__all__ = [
    "Permission", "Role", "role_permissions", "User", "Setting", "AuditLog",
    "Customer", "Invoice", "InvoiceLine", "ServiceItem", "Payment",
]
