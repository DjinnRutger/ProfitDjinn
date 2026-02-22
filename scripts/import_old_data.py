"""
Import old TV2 data into ProfitDjinn.

Usage (from project root, with venv active):
    python scripts/import_old_data.py

Idempotent: checks for existing records before inserting.
"""
import sys
import json
from pathlib import Path
from datetime import datetime, date

# Add project root to path
ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT))

from run import app  # noqa: E402 – loads Flask app


DATA_DIR = ROOT / "Old-Database-TV2"


def load_json(filename: str) -> list | dict:
    path = DATA_DIR / filename
    if not path.exists():
        print(f"  [SKIP] {filename} not found")
        return []
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def import_customers(session, customers_data: list) -> dict:
    """Import customers. Returns {old_uuid: new_int_id} mapping."""
    from app.models.customer import Customer

    id_map = {}
    created = 0
    skipped = 0

    for raw in customers_data:
        old_id = raw.get("id", "")
        name = raw.get("name", "").strip()
        if not name:
            continue

        # Check for existing by name (dedup)
        existing = Customer.query.filter_by(name=name).first()
        if existing:
            id_map[old_id] = existing.id
            skipped += 1
            continue

        c = Customer(
            name=name,
            attn=raw.get("attn", "") or "",
            address=raw.get("address", "") or "",
            city=raw.get("city", "") or "",
            state=(raw.get("state", "") or "").upper(),
            zip_code=raw.get("zip", "") or "",
            phone=raw.get("phone", "") or "",
            email=(raw.get("email", "") or "").lower().strip(),
            notes=raw.get("notes", "") or "",
            is_active=True,
        )
        session.add(c)
        session.flush()
        id_map[old_id] = c.id
        created += 1

    session.commit()
    print(f"  Customers: {created} created, {skipped} skipped (already exist)")
    return id_map


def import_service_items(session, items_data: list) -> None:
    from app.models.service_item import ServiceItem

    created = 0
    for raw in items_data:
        desc = raw.get("description", "").strip()
        if not desc:
            continue
        if ServiceItem.query.filter_by(description=desc).first():
            continue
        session.add(ServiceItem(
            description=desc,
            price=float(raw.get("price", 0)),
            is_active=True,
        ))
        created += 1

    session.commit()
    print(f"  Service items: {created} created")


def import_invoices(session, invoices_data: list, customer_id_map: dict) -> None:
    from app.models.invoice import Invoice, InvoiceLine
    from app.models.customer import Customer

    created = 0
    skipped = 0

    for raw in invoices_data:
        inv_num = raw.get("invoice_number", "").strip().upper()
        if not inv_num:
            continue

        if Invoice.query.filter_by(invoice_number=inv_num).first():
            skipped += 1
            continue

        # Resolve customer
        old_cust_id = raw.get("customer_id", "")
        cust_int_id = customer_id_map.get(old_cust_id)

        # Fallback: look up by embedded name
        if not cust_int_id:
            cust_data = raw.get("customer", {})
            cust_name = cust_data.get("name", "").strip()
            if cust_name:
                c = Customer.query.filter_by(name=cust_name).first()
                if c:
                    cust_int_id = c.id

        if not cust_int_id:
            print(f"  [WARN] Invoice {inv_num}: customer not found, skipping")
            skipped += 1
            continue

        raw_date = raw.get("date", "")
        try:
            inv_date = datetime.strptime(raw_date, "%Y-%m-%d").date()
        except (ValueError, TypeError):
            inv_date = date.today()

        invoice = Invoice(
            invoice_number=inv_num,
            customer_id=cust_int_id,
            date=inv_date,
            notes=raw.get("notes", "") or "",
            term1=raw.get("term1", "") or "",
            term2=raw.get("term2", "") or "",
            paid=bool(raw.get("paid", 0)),
        )
        session.add(invoice)
        session.flush()

        for item in raw.get("line_items", []):
            session.add(InvoiceLine(
                invoice_id=invoice.id,
                description=str(item.get("description", "")).strip(),
                quantity=float(item.get("quantity", 1.0)),
                amount=float(item.get("amount", 0.0)),
            ))

        created += 1

    session.commit()
    print(f"  Invoices: {created} created, {skipped} skipped (already exist)")


def main():
    with app.app_context():
        from app.extensions import db

        print("=== ProfitDjinn Data Import ===\n")

        customers_data = load_json("customers.json")
        items_data = load_json("items.json")
        invoices_data = load_json("invoices.json")

        print("Importing customers…")
        id_map = import_customers(db.session, customers_data if isinstance(customers_data, list) else [])

        print("Importing service items…")
        import_service_items(db.session, items_data if isinstance(items_data, list) else [])

        print("Importing invoices…")
        import_invoices(db.session, invoices_data if isinstance(invoices_data, list) else [], id_map)

        print("\nDone!")


if __name__ == "__main__":
    main()
