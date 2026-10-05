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
    private sealed class ServiceRow
    {
        public long LineId { get; set; }
        public long InvoiceId { get; set; }
        public string Description { get; set; } = "";
        public DateOnly? ServiceStart { get; set; }
        public DateOnly? ServiceEnd { get; set; }
    }

    internal static List<Invoice> Invoices(SqliteConnection db, string where = "1=1", object? args = null,
        SqliteTransaction? tx = null, bool withCustomers = true)
    {
        var invoices = db.Query<Invoice>($"SELECT * FROM invoices WHERE {where} ORDER BY id", args, tx).ToList();
        if (invoices.Count == 0) return invoices;
        var ids = invoices.Select(i => i.Id).ToList();

        var lineList = db.Query<InvoiceLine>("SELECT * FROM invoice_lines WHERE invoice_id IN @ids ORDER BY id", new { ids }, tx).ToList();
        // 2.5 service dates, matched on line and invoice so a reused line id never picks up another's dates.
        var service = db.Query<ServiceRow>(
                "SELECT line_id, invoice_id, description, service_start, service_end FROM invoice_line_service WHERE invoice_id IN @ids", new { ids }, tx)
            .ToDictionary(r => (r.LineId, r.InvoiceId));
        foreach (var l in lineList)
            if (service.TryGetValue((l.Id, l.InvoiceId), out var sd) && sd.Description == l.Description) { l.ServiceStart = sd.ServiceStart; l.ServiceEnd = sd.ServiceEnd; }
        var lines = lineList.ToLookup(l => l.InvoiceId);
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

    /// <summary>2.2. Expenses with payments (by date), receipts, vendor and category.</summary>
    internal static List<Expense> Expenses(SqliteConnection db, string where = "1=1", object? args = null,
        SqliteTransaction? tx = null, bool withVendors = true)
    {
        var expenses = db.Query<Expense>($"SELECT * FROM expenses WHERE {where} ORDER BY id", args, tx).ToList();
        if (expenses.Count == 0) return expenses;
        var ids = expenses.Select(e => e.Id).ToList();
        var payments = db.Query<ExpensePayment>("SELECT * FROM expense_payments WHERE expense_id IN @ids ORDER BY date, id", new { ids }, tx)
            .ToLookup(p => p.ExpenseId);
        var receipts = db.Query<ExpenseReceipt>("SELECT * FROM expense_receipts WHERE expense_id IN @ids ORDER BY id", new { ids }, tx)
            .ToLookup(r => r.ExpenseId);
        var mileage = db.Query<(long ExpenseId, double Miles, double Rate)>("SELECT expense_id, CAST(miles AS REAL), CAST(rate AS REAL) FROM expense_mileage WHERE expense_id IN @ids", new { ids }, tx)
            .ToDictionary(m => m.ExpenseId);
        foreach (var e in expenses)
            if (mileage.TryGetValue(e.Id, out var m)) { e.Miles = m.Miles; e.MileageRate = m.Rate; }
        var categories = db.Query<ExpenseCategory>("SELECT * FROM expense_categories", transaction: tx).ToDictionary(c => c.Id);
        Dictionary<long, Vendor> vendors = new();
        if (withVendors)
        {
            var vendorIds = expenses.Where(e => e.VendorId is not null).Select(e => e.VendorId!.Value).Distinct().ToList();
            if (vendorIds.Count > 0)
                vendors = db.Query<Vendor>("SELECT * FROM vendors WHERE id IN @vendorIds", new { vendorIds }, tx).ToDictionary(v => v.Id);
        }
        foreach (var e in expenses)
        {
            e.Payments = payments[e.Id].ToList();
            e.Receipts = receipts[e.Id].ToList();
            e.Category = categories.GetValueOrDefault(e.CategoryId);
            if (e.VendorId is { } v) e.Vendor = vendors.GetValueOrDefault(v);
        }
        return expenses;
    }

    internal static Expense? Expense(SqliteConnection db, long id, SqliteTransaction? tx = null) =>
        Expenses(db, "id = @id", new { id }, tx).SingleOrDefault();

    /// <summary>2.2. Vendors with their expenses loaded, so the totals work.</summary>
    internal static List<Vendor> Vendors(SqliteConnection db, string where = "1=1", object? args = null,
        SqliteTransaction? tx = null)
    {
        var vendors = db.Query<Vendor>($"SELECT * FROM vendors WHERE {where} ORDER BY id", args, tx).ToList();
        if (vendors.Count == 0) return vendors;
        var ids = vendors.Select(v => v.Id).ToList();
        var expenses = Expenses(db, "vendor_id IN @ids", new { ids }, tx, withVendors: false).ToLookup(e => e.VendorId);
        foreach (var v in vendors)
        {
            v.Expenses = expenses[v.Id].ToList();
            foreach (var e in v.Expenses) e.Vendor = v;
        }
        return vendors;
    }

    internal static Vendor? Vendor(SqliteConnection db, long id, SqliteTransaction? tx = null) =>
        Vendors(db, "id = @id", new { id }, tx).SingleOrDefault();

    internal static WorkOrder? WorkOrder(SqliteConnection db, long id, SqliteTransaction? tx = null) =>
        WorkOrders(db, "id = @id", new { id }, tx).SingleOrDefault();
}
