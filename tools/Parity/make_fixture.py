"""Build the 2.0 parity fixture from the 1.x Flask app.

Runs every operation in tests/ProfitDjinn.Tests/Fixtures/parity_ops.json through the real
1.x routes (Flask test client, so the same validation and business code), then writes:

    Fixtures/parity.db             the database 1.x ended up with
    Fixtures/parity_expected.json  every figure 1.x computes from it (totals, statuses,
                                   groupings, dashboard, revenue, next numbers)

ParityTests replays the same operations against the 2.0 services and must end with the
same rows, and computes the same figures from parity.db.

Run from the project root with the 1.x venv:
    C:\\Dev\\venvs\\ProfitDjinn\\Scripts\\python tools\\Parity\\make_fixture.py

Everything here is fake data. Nothing reads or writes the real database.
"""
import json
import os
import shutil
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
FIXTURES = ROOT / "tests" / "ProfitDjinn.Tests" / "Fixtures"
WORK = Path(tempfile.mkdtemp(prefix="profitdjinn-parity-"))
DB_FILE = WORK / "parity.db"

# app.config reads these at import time.
os.environ["DATABASE_URI"] = "sqlite:///" + DB_FILE.as_posix()
os.environ["SECRET_KEY"] = "parity-fixture-only"
os.environ["FLASK_INSTANCE_PATH"] = str(WORK)
sys.path.insert(0, str(ROOT))

from app import create_app  # noqa: E402
from app.extensions import db  # noqa: E402

app = create_app("gui")
app.config["WTF_CSRF_ENABLED"] = False
app.config["RATELIMIT_ENABLED"] = False
client = app.test_client()

r = client.post("/auth/setup", data={"username": "parity", "email": "parity@example.com",
                                     "password": "parity-password", "confirm_password": "parity-password"})
assert r.status_code == 302, "setup failed"


def check(name, flag):
    """A ticked WTForms checkbox posts its name; an unticked one posts nothing."""
    return {name: "y"} if flag else {}


def customer_form(op):
    data = {k: op[k] for k in ("name", "attn", "address", "city", "state", "zip_code", "phone", "email", "notes")}
    data.update(check("is_active", op["is_active"]))
    return data


def invoice_form(op):
    data = {k: op[k] for k in ("customer_id", "invoice_number", "date", "notes", "term1", "term2")}
    data["line_items_json"] = json.dumps(op["lines"])
    data.update(check("paid", op["paid"]))
    return data


def line_form(op):
    data = {k: op[k] for k in ("description", "project_label", "line_type", "date_performed", "quantity", "rate", "internal_note")}
    if op["no_charge"]:
        data["no_charge"] = "1"
    return data


def js_parse_float(text):
    """The few inputs the fixture uses are plain numbers; parseFloat(x) || default."""
    try:
        return float(text)
    except ValueError:
        return float("nan")


def bill_rows(rows):
    """What bill.html's syncHidden() posts: qty = parseFloat||1, amount = qty * (parseFloat||0)."""
    items = []
    for row in rows:
        qty = js_parse_float(row["quantity"])
        qty = 1 if (qty != qty or qty == 0) else qty
        price = js_parse_float(row["unit_price"])
        price = 0 if price != price else price
        items.append({"description": row["description"].strip(), "quantity": qty, "amount": qty * price})
    return items


def run(op):
    kind = op["op"]
    if kind == "set_setting":
        with app.app_context():
            from app.models.setting import Setting
            s = Setting.query.filter_by(key=op["key"]).first()
            s.value = op["value"]
            db.session.commit()
        return
    if kind == "create_customer":
        return client.post("/customers/new", data=customer_form(op))
    if kind == "edit_customer":
        return client.post(f"/customers/{op['id']}/edit", data=customer_form(op))
    if kind == "customer_notes":
        return client.post(f"/customers/{op['id']}/notes", data={"notes": op["notes"]})
    if kind == "delete_customer":
        return client.post(f"/customers/{op['id']}/delete")
    if kind == "create_item":
        return client.post("/items/new", data={"description": op["description"], "price": op["price"], **check("is_active", op["is_active"])})
    if kind == "edit_item":
        return client.post(f"/items/{op['id']}/edit", data={"description": op["description"], "price": op["price"], **check("is_active", op["is_active"])})
    if kind == "toggle_item":
        return client.post(f"/items/{op['id']}/toggle")
    if kind == "delete_item":
        return client.post(f"/items/{op['id']}/delete")
    if kind == "create_invoice":
        return client.post("/invoices/new", data=invoice_form(op))
    if kind == "edit_invoice":
        return client.post(f"/invoices/{op['id']}/edit", data=invoice_form(op))
    if kind == "delete_invoice":
        return client.post(f"/invoices/{op['id']}/delete")
    if kind == "record_payment":
        return client.post(f"/invoices/{op['invoice_id']}/record-payment",
                           data={k: op[k] for k in ("amount", "method", "check_number", "date", "notes")})
    if kind == "delete_payment":
        return client.post(f"/invoices/{op['invoice_id']}/payments/{op['payment_id']}/delete")
    if kind == "mark_unpaid":
        return client.post(f"/invoices/{op['invoice_id']}/mark-unpaid")
    if kind == "open_tab":
        return client.get(f"/work-orders/customer/{op['customer_id']}")
    if kind == "add_todo":
        return client.post(f"/work-orders/{op['wo_id']}/lines/add",
                           data={"mode": "todo", "description": op["description"], "project_label": op["project_label"]})
    if kind == "log_work":
        return client.post(f"/work-orders/{op['wo_id']}/lines/add", data={"mode": "work", **line_form(op)})
    if kind == "edit_line":
        return client.post(f"/work-orders/lines/{op['line_id']}/edit", data=line_form(op))
    if kind == "complete_line":
        return client.post(f"/work-orders/lines/{op['line_id']}/complete", data=line_form(op))
    if kind == "reopen_line":
        return client.post(f"/work-orders/lines/{op['line_id']}/reopen")
    if kind == "toggle_no_charge":
        return client.post(f"/work-orders/lines/{op['line_id']}/no-charge")
    if kind == "delete_line":
        return client.post(f"/work-orders/lines/{op['line_id']}/delete")
    if kind == "wo_notes":
        return client.post(f"/work-orders/{op['wo_id']}/notes", data={"notes": op["notes"]})
    if kind == "bill":
        return client.post(f"/work-orders/{op['wo_id']}/bill", data={
            "invoice_number": op["invoice_number"], "date": op["date"], "notes": op["notes"],
            "term1": op["term1"], "term2": op["term2"], "rollup": "one",
            "selected_line_ids": ",".join(str(i) for i in op["selected_line_ids"]),
            "line_items_json": json.dumps(bill_rows(op["rows"])),
        })
    raise ValueError(f"unknown op {kind}")


ops = json.loads((FIXTURES / "parity_ops.json").read_text(encoding="utf-8"))["ops"]
for i, op in enumerate(ops):
    response = run(op)
    if response is not None and response.status_code >= 500:
        raise SystemExit(f"op {i} ({op['op']}) crashed the 1.x app: HTTP {response.status_code}")


# ---------------------------------------------------------------------------- figures

sys.path.insert(0, str(Path(__file__).resolve().parent))
from figures import figures  # noqa: E402

with app.app_context():
    expected = figures(years=(2025, 2026))

out = json.dumps(expected, indent=1, ensure_ascii=False)
with app.app_context():
    db.engine.dispose()
shutil.copyfile(DB_FILE, FIXTURES / "parity.db")
(FIXTURES / "parity_expected.json").write_text(out + "\n", encoding="utf-8")
print(f"Wrote {FIXTURES / 'parity.db'} and parity_expected.json ({len(ops)} operations)")
