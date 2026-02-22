from app.extensions import db


class ServiceItem(db.Model):
    __tablename__ = "service_items"

    id = db.Column(db.Integer, primary_key=True)
    description = db.Column(db.String(500), nullable=False)
    price = db.Column(db.Float, nullable=False, default=0.0)
    is_active = db.Column(db.Boolean, default=True, nullable=False)

    def __repr__(self):
        return f"<ServiceItem {self.description}>"
