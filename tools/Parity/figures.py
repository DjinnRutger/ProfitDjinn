"""Every figure 1.x computes from a database, as JSON-ready data.

Used by make_fixture.py (for the committed fixture) and dump_figures.py (for a copy of a
real database, kept out of the repo). ParityTests computes the same figures in 2.0 and
compares them exactly, to the last bit of every double.
"""
from datetime import date


def figures(years=None):
    """Call inside an app context. `years`: the Revenue years to include (default: all)."""
    from app.models.customer import Customer
    from app.models.invoice import Invoice
    from app.models.work_order import WorkOrder
    from app.blueprints.invoices import _next_invoice_number
    from app.blueprints.work_orders import _next_wo_number

    out = {"today": date.today().isoformat()}

    out["invoices"] = {
        str(inv.id): {
            "total": inv.total, "net_total": inv.net_total, "amount_paid": inv.amount_paid,
            "balance_due": inv.balance_due, "credit_amount": inv.credit_amount,
            "is_partial": inv.is_partial, "status": inv.status_label,
            "unit_prices": [ln.unit_price for ln in inv.line_items],
        }
        for inv in Invoice.query.order_by(Invoice.id).all()
    }
    out["customers"] = {
        str(c.id): {
            "total_invoiced": c.total_invoiced, "total_outstanding": c.total_outstanding,
            "total_paid": c.total_paid, "account_credit": c.account_credit, "full_address": c.full_address,
        }
        for c in Customer.query.order_by(Customer.id).all()
    }
    work, lines = {}, {}
    for wo in WorkOrder.query.order_by(WorkOrder.id).all():
        work[str(wo.id)] = {
            "ready_to_bill_total": wo.ready_to_bill_total, "billed_total": wo.billed_total,
            "pending_count": wo.pending_count, "has_open_work": wo.has_open_work,
            "grouped_open": [[label, [ln.id for ln in ls]] for label, ls in wo.grouped_open().items()],
            "grouped_billed": [[g["invoice_id"], g["total"], [ln.id for ln in g["lines"]]] for g in wo.grouped_billed()],
            "open_labels": wo.open_labels,
        }
        for ln in wo.lines:
            lines[str(ln.id)] = {
                "effective_status": ln.effective_status, "status_label": ln.status_label,
                "quantity_label": ln.quantity_label, "type_label": ln.type_label,
            }
    out["work_orders"] = work
    out["lines"] = lines
    out["next_invoice_number"] = _next_invoice_number()
    out["next_work_order_number"] = _next_wo_number()

    # Dashboard: the same expressions as main.dashboard().
    all_invoices = Invoice.query.all()
    year = date.today().year
    unpaid = [inv for inv in all_invoices if inv.balance_due > 0]
    out["dashboard"] = {
        "unpaid_invoices": len(unpaid),
        "unpaid_total": sum(inv.balance_due for inv in unpaid),
        "year_revenue": sum(inv.amount_paid for inv in all_invoices if inv.date.year == year and inv.amount_paid > 0),
        "active_customers": Customer.query.filter_by(is_active=True).count(),
        "total_invoices": len(all_invoices),
    }

    # Revenue for single years: the same expressions as main.revenue().
    def revenue_for(yr):
        filtered = [inv for inv in all_invoices if inv.date.year == yr]
        monthly = {m: 0.0 for m in range(1, 13)}
        invoiced = {m: 0.0 for m in range(1, 13)}
        for inv in filtered:
            monthly[inv.date.month] += inv.amount_paid
            invoiced[inv.date.month] += inv.total
        cmap = {}
        for inv in filtered:
            if inv.amount_paid > 0:
                cmap[inv.customer.name] = cmap.get(inv.customer.name, 0.0) + inv.amount_paid
        return {
            "total_revenue": sum(inv.amount_paid for inv in filtered),
            "total_invoiced": sum(inv.total for inv in filtered),
            "total_outstanding": sum(inv.balance_due for inv in filtered),
            "paid_count": sum(1 for inv in filtered if inv.paid),
            "partial_count": sum(1 for inv in filtered if inv.is_partial),
            "monthly_revenue": [monthly[m] for m in range(1, 13)],
            "monthly_invoiced": [invoiced[m] for m in range(1, 13)],
            "customers": sorted(cmap.items(), key=lambda x: x[1], reverse=True),
        }

    if years is None:
        years = sorted({inv.date.year for inv in all_invoices})
    out["revenue"] = {str(y): revenue_for(y) for y in years}
    return out
