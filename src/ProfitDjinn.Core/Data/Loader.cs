using Dapper;
using Microsoft.Data.Sqlite;
using ProfitDjinn.Core.Model;

namespace ProfitDjinn.Core.Data;

/// <summary>
/// Loads whole aggregates (an invoice with its lines and payments, a work order with its
/// lines) in a few queries. Row order matches what 1.x got from SQLAlchemy: invoice lines by
/// id, payments by date, work order lines by id, a customer's invoices by id.
/// </summary>
internal static class Loader
{
    internal static List<Invoice> Invoices(SqliteConnection db, string where = "1=1", object? args = null,
        SqliteTransaction? tx = null, bool withCustomers = true)
    {
        var invoices = db.Query<Invoice>($"SELECT * FROM invoices WHERE {where} ORDER BY id", args, tx).ToList();
        if (invoices.Count == 0) return invoices;
        var ids = invoices.Select(i => i.Id).ToList();

        var lines = db.Query<InvoiceLine>("SELECT * FROM invoice_lines WHERE invoice_id IN @ids ORDER BY id", new { ids }, tx)
            .ToLookup(l => l.InvoiceId);
        var payments = db.Query<Payment>("SELECT * FROM payments WHERE invoice_id IN @ids ORDER BY date, id", new { ids }, tx)
            .ToLookup(p => p.InvoiceId);
        foreach (var inv in invoices)
        {
            inv.Lines = lines[inv.Id].ToList();
            inv.Payments = payments[inv.Id].ToList();
        }

        if (withCustomers)
        {
            var custIds = invoices.Select(i => i.CustomerId).Distinct().ToList();
            var customers = db.Query<Customer>("SELECT * FROM customers WHERE id IN @custIds", new { custIds }, tx)
                .ToDictionary(c => c.Id);
            foreach (var inv in invoices) inv.Customer = customers.GetValueOrDefault(inv.CustomerId);
        }
        return invoices;
    }

    internal static Invoice? Invoice(SqliteConnection db, long id, SqliteTransaction? tx = null) =>
        Invoices(db, "id = @id", new { id }, tx).SingleOrDefault();

    /// <summary>Customers with their invoices loaded, so the derived totals work.</summary>
    internal static List<Customer> Customers(SqliteConnection db, string where = "1=1", object? args = null,
        SqliteTransaction? tx = null)
    {
        var customers = db.Query<Customer>($"SELECT * FROM customers WHERE {where} ORDER BY id", args, tx).ToList();
        if (customers.Count == 0) return customers;
        var ids = customers.Select(c => c.Id).ToList();
        var invoices = Invoices(db, "customer_id IN @ids", new { ids }, tx, withCustomers: false).ToLookup(i => i.CustomerId);
        foreach (var c in customers)
        {
            c.Invoices = invoices[c.Id].ToList();
            foreach (var inv in c.Invoices) inv.Customer = c;
        }
        return customers;
    }

    internal static Customer? Customer(SqliteConnection db, long id, SqliteTransaction? tx = null) =>
        Customers(db, "id = @id", new { id }, tx).SingleOrDefault();

    internal static List<WorkOrder> WorkOrders(SqliteConnection db, string where = "1=1", object? args = null,
        SqliteTransaction? tx = null)
    {
        var orders = db.Query<WorkOrder>($"SELECT * FROM work_orders WHERE {where} ORDER BY id", args, tx).ToList();
        if (orders.Count == 0) return orders;
        var ids = orders.Select(o => o.Id).ToList();
        var lines = db.Query<WorkOrderLine>("SELECT * FROM work_order_lines WHERE work_order_id IN @ids ORDER BY id", new { ids }, tx)
            .ToLookup(l => l.WorkOrderId);
        var custIds = orders.Select(o => o.CustomerId).Distinct().ToList();
        var customers = db.Query<Customer>("SELECT * FROM customers WHERE id IN @custIds", new { custIds }, tx)
            .ToDictionary(c => c.Id);
        foreach (var o in orders)
        {
            o.Lines = lines[o.Id].ToList();
            o.Customer = customers.GetValueOrDefault(o.CustomerId);
        }
        return orders;
    }

    internal static WorkOrder? WorkOrder(SqliteConnection db, long id, SqliteTransaction? tx = null) =>
        WorkOrders(db, "id = @id", new { id }, tx).SingleOrDefault();
}
