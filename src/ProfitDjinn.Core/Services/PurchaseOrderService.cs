using Dapper;
using Microsoft.Data.Sqlite;
using ProfitDjinn.Core.Data;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Rules;

namespace ProfitDjinn.Core.Services;

/// <summary>One line as the PO form submits it.</summary>
public sealed record PurchaseOrderLineDraft(string Description, double Quantity, double UnitPrice)
{
    /// <summary>A line builder row, read with the same number rules as invoice lines.</summary>
    public static PurchaseOrderLineDraft FromRow(InvoiceRowInput row) =>
        new((row.Description ?? "").Trim(), InvoiceRows.Quantity(row.Quantity), InvoiceRows.Price(row.UnitPrice));
}

/// <summary>What the PO form submits.</summary>
public sealed record PurchaseOrderDraft(
    long? VendorId,
    string PoNumber,
    DateOnly? Date,
    string Notes,
    IReadOnlyList<PurchaseOrderLineDraft> Lines);

/// <summary>
/// 2.9. Purchase orders: an order to a vendor with any number of lines, a number like invoices
/// have, and a status set by hand (open, received, cancelled). Off by default (Settings >
/// Features). Today a PO is a document only; nothing posts to expenses or stock.
/// </summary>
public sealed class PurchaseOrderService
{
    private readonly Database _db;
    private readonly SettingsService _settings;
    private readonly Func<DateOnly> _today;

    public PurchaseOrderService(Database db, SettingsService settings, Func<DateOnly> today)
    {
        _db = db;
        _settings = settings;
        _today = today;
    }

    public bool Enabled => _settings.GetBool(SettingKeys.PurchaseOrdersEnabled);

    // ------------------------------------------------------------------ reading

    /// <summary>Newest first. <paramref name="status"/> null means all; search matches the number or vendor.</summary>
    public IReadOnlyList<PurchaseOrder> List(string? status = null, string? search = null)
    {
        string q = (search ?? "").Trim();
        return _db.Run(db => Loader.PurchaseOrders(db))
            .Where(o => status is null || o.Status == status)
            .Where(o => q.Length == 0
                || o.PoNumber.Contains(q, StringComparison.OrdinalIgnoreCase)
                || o.VendorName.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(o => o.Date)
            .ThenByDescending(o => o.PoNumber, StringComparer.Ordinal)
            .ToList();
    }

    public PurchaseOrder Get(long id) => _db.Run(db => Loader.PurchaseOrder(db, id)) ?? throw NotFound();

    /// <summary>Open purchase orders and what they add up to, for the list header.</summary>
    public (int Count, double Total) OpenSummary()
    {
        var open = _db.Run(db => Loader.PurchaseOrders(db, "status = @s", new { s = PurchaseOrderStatus.Open }, withVendors: false));
        return (open.Count, PyMath.Sum(open, o => o.Total));
    }

    public string NextNumber() => _db.Run(db => NextNumber(db, null));

    private string NextNumber(SqliteConnection db, SqliteTransaction? tx) => Numbering.Next(
        _settings.Get(SettingKeys.PurchaseOrderPrefix, "PO"),
        db.Query<string>("SELECT po_number FROM purchase_orders", transaction: tx),
        _settings.Get(SettingKeys.PurchaseOrderNextNumber, "1001"));

    public PurchaseOrderDraft NewDraft(long? vendorId = null) =>
        new(vendorId, NextNumber(), _today(), "", Array.Empty<PurchaseOrderLineDraft>());

    // ------------------------------------------------------------------ create / edit

    public Created Create(PurchaseOrderDraft draft)
    {
        var (number, lines) = Validate(draft);
        return _db.InTransaction((db, tx) =>
        {
            EnsureUniqueNumber(db, tx, number, exceptId: null);
            RequireVendor(db, tx, draft.VendorId!.Value, mustBeActive: true);
            long id = db.ExecuteScalar<long>("""
                INSERT INTO purchase_orders (po_number, vendor_id, date, status, received_date, notes, created_at)
                VALUES (@number, @vendor, @date, @status, NULL, @notes, @now);
                SELECT last_insert_rowid();
                """, new { number, vendor = draft.VendorId, date = draft.Date!.Value, status = PurchaseOrderStatus.Open, notes = draft.Notes ?? "", now = SqlFormat.NowUtc() }, tx);
            InsertLines(db, tx, id, lines);
            return new Created(id, Notice.Success($"Purchase order {number} created."));
        });
    }

    public Notice Update(long id, PurchaseOrderDraft draft)
    {
        var (number, lines) = Validate(draft);
        return _db.InTransaction((db, tx) =>
        {
            var order = Loader.PurchaseOrder(db, id, tx) ?? throw NotFound();
            EnsureUniqueNumber(db, tx, number, exceptId: id);
            long vendor = draft.VendorId!.Value;
            // The PO's own vendor stays allowed if it has since been made inactive.
            RequireVendor(db, tx, vendor, mustBeActive: vendor != order.VendorId);
            db.Execute("UPDATE purchase_orders SET po_number = @number, vendor_id = @vendor, date = @date, notes = @notes WHERE id = @id",
                new { number, vendor, date = draft.Date!.Value, notes = draft.Notes ?? "", id }, tx);
            db.Execute("DELETE FROM purchase_order_lines WHERE purchase_order_id = @id", new { id }, tx);
            InsertLines(db, tx, id, lines);
            return Notice.Success($"Purchase order {number} updated.");
        });
    }

    /// <summary>Open, received (dated today) or cancelled. Reopening clears the received date.</summary>
    public Notice SetStatus(long id, string status)
    {
        if (!PurchaseOrderStatus.IsValid(status)) throw new ArgumentException($"Unknown purchase order status '{status}'.", nameof(status));
        return _db.InTransaction((db, tx) =>
        {
            var order = Loader.PurchaseOrder(db, id, tx) ?? throw NotFound();
            DateOnly? received = status == PurchaseOrderStatus.Received ? _today() : null;
            db.Execute("UPDATE purchase_orders SET status = @status, received_date = @received WHERE id = @id", new { status, received, id }, tx);
            return status switch
            {
                PurchaseOrderStatus.Received => Notice.Success($"Purchase order {order.PoNumber} marked received."),
                PurchaseOrderStatus.Cancelled => Notice.Warning($"Purchase order {order.PoNumber} cancelled."),
                _ => Notice.Success($"Purchase order {order.PoNumber} reopened."),
            };
        });
    }

    public Notice Delete(long id) => _db.InTransaction((db, tx) =>
    {
        string number = db.ExecuteScalar<string?>("SELECT po_number FROM purchase_orders WHERE id = @id", new { id }, tx) ?? throw NotFound();
        db.Execute("DELETE FROM purchase_order_lines WHERE purchase_order_id = @id", new { id }, tx);
        db.Execute("DELETE FROM purchase_orders WHERE id = @id", new { id }, tx);
        return Notice.Warning($"Purchase order {number} deleted.");
    });

    // ------------------------------------------------------------------ rules

    /// <summary>
    /// The number is required and upper-cased (as invoice numbers are). A line with no description
    /// and no amount is dropped as an unused row; any other line needs a description.
    /// </summary>
    private static (string Number, List<PurchaseOrderLineDraft> Lines) Validate(PurchaseOrderDraft d)
    {
        var checks = new Checks();
        if (d.VendorId is null) checks.Add("vendor", "Choose a vendor.");
        checks.RequireText("po_number", d.PoNumber, 50);
        if (d.Date is null) checks.Add("date", Checks.Required);
        var lines = new List<PurchaseOrderLineDraft>();
        int n = 0;
        foreach (var l in d.Lines)
        {
            n++;
            string description = (l.Description ?? "").Trim();
            if (description.Length == 0 && l.Quantity * l.UnitPrice == 0) continue;
            if (description.Length == 0) checks.Add("lines", $"Line {n} has a price but no description.");
            else if (description.Length > 500) checks.Add("lines", $"Line {n}: the description is longer than 500 characters.");
            if (double.IsInfinity(l.Quantity) || double.IsInfinity(l.UnitPrice)) checks.Add("lines", $"Line {n}: that number is too large.");
            lines.Add(l with { Description = description });
        }
        checks.ThrowIfAny();
        if (lines.Count == 0) throw new UserFacingException("Add at least one line before saving the purchase order.");
        return (d.PoNumber.Trim().ToUpperInvariant(), lines);
    }

    private static void InsertLines(SqliteConnection db, SqliteTransaction tx, long id, IReadOnlyList<PurchaseOrderLineDraft> lines)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            db.Execute("""
                INSERT INTO purchase_order_lines (purchase_order_id, position, description, quantity, unit_price, amount)
                VALUES (@id, @position, @Description, @Quantity, @UnitPrice, @amount)
                """, new { id, position = i + 1, l.Description, l.Quantity, l.UnitPrice, amount = l.Quantity * l.UnitPrice }, tx);
        }
    }

    private static void EnsureUniqueNumber(SqliteConnection db, SqliteTransaction tx, string number, long? exceptId)
    {
        bool taken = db.ExecuteScalar<bool>(
            "SELECT EXISTS (SELECT 1 FROM purchase_orders WHERE po_number = @number AND id != @except)",
            new { number, except = exceptId ?? -1 }, tx);
        if (taken)
            throw new ValidationException(
                new Dictionary<string, string> { ["po_number"] = "That number is already used." },
                $"Purchase order number {number} already exists.");
    }

    private static void RequireVendor(SqliteConnection db, SqliteTransaction tx, long vendorId, bool mustBeActive)
    {
        bool? active = db.ExecuteScalar<bool?>("SELECT is_active FROM vendors WHERE id = @vendorId", new { vendorId }, tx);
        if (active is null || (mustBeActive && active == false))
            throw new ValidationException(new Dictionary<string, string> { ["vendor"] = "Choose a vendor." });
    }

    private static UserFacingException NotFound() => new("That purchase order no longer exists. It may have been deleted.");
}
