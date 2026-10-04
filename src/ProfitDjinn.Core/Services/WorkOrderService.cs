using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using ProfitDjinn.Core.Data;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Rules;

namespace ProfitDjinn.Core.Services;

/// <summary>
/// The Log Work / Complete / Edit dialog's fields, as typed. Quantity and rate stay text so
/// they are parsed the way 1.x parsed them (blank quantity means 1, blank rate means 0).
/// </summary>
public sealed record LineInput(
    string Description,
    string ProjectLabel,
    string LineType,
    DateOnly? DatePerformed,
    string Quantity,
    string Rate,
    bool NoCharge,
    string InternalNote);

/// <summary>Work order tabs and their lines. A port of 1.x app/blueprints/work_orders.py.</summary>
public sealed class WorkOrderService
{
    private readonly Database _db;
    private readonly SettingsService _settings;
    private readonly Func<DateOnly> _today;

    public WorkOrderService(Database db, SettingsService settings, Func<DateOnly> today)
    {
        _db = db;
        _settings = settings;
        _today = today;
    }

    /// <summary>2.5. Settings > Features. Off hides work orders in the app; nothing is deleted.</summary>
    public bool Enabled => _settings.GetBool(SettingKeys.WorkOrdersEnabled);

    /// <summary>
    /// The Work Orders page: one entry per customer, sorted by customer name. Unless
    /// <paramref name="includeIdle"/>, only tabs with pending or completed work. Search
    /// matches the customer, the number, or any line's description or project.
    /// </summary>
    public IReadOnlyList<WorkOrder> List(string? search = null, bool includeIdle = false)
    {
        string q = (search ?? "").Trim().ToLowerInvariant();
        return _db.Run(db => Loader.WorkOrders(db))
            .Where(w => w.Customer is not null)
            .OrderBy(w => w.Customer!.Name, StringComparer.Ordinal)
            .Where(w => includeIdle || w.HasOpenWork)
            .Where(w => q.Length == 0
                || (w.Customer!.Name ?? "").ToLowerInvariant().Contains(q)
                || (w.Number ?? "").ToLowerInvariant().Contains(q)
                || w.Lines.Any(l => (l.Description ?? "").ToLowerInvariant().Contains(q)
                                 || (l.ProjectLabel ?? "").ToLowerInvariant().Contains(q)))
            .ToList();
    }

    public double DefaultHourlyRate() => ParseFloat(_settings.Get(SettingKeys.DefaultHourlyRate, "0.00"), 0.0);

    /// <summary>The customer's tab, created (with the next number) the first time it is opened.</summary>
    public WorkOrder ForCustomer(long customerId) => _db.InTransaction((db, tx) =>
    {
        long? id = db.ExecuteScalar<long?>("SELECT id FROM work_orders WHERE customer_id = @customerId", new { customerId }, tx);
        if (id is null)
        {
            if (!db.ExecuteScalar<bool>("SELECT EXISTS (SELECT 1 FROM customers WHERE id = @customerId)", new { customerId }, tx))
                throw new UserFacingException("That customer no longer exists. They may have been deleted.");
            string number = Numbering.Next(
                _settings.Get(SettingKeys.WorkOrderPrefix, "WO"),
                db.Query<string>("SELECT number FROM work_orders", transaction: tx),
                _settings.Get(SettingKeys.WorkOrderNextNumber, "1001"));
            id = db.ExecuteScalar<long>(
                "INSERT INTO work_orders (customer_id, number, notes, is_active, created_at) VALUES (@customerId, @number, '', 1, @now); SELECT last_insert_rowid();",
                new { customerId, number, now = SqlFormat.NowUtc() }, tx);
        }
        return Loader.WorkOrder(db, id.Value, tx)!;
    });

    /// <summary>The customer's tab if it exists, without creating one (the customer page only shows it).</summary>
    public WorkOrder? FindForCustomer(long customerId) =>
        _db.Run(db => Loader.WorkOrders(db, "customer_id = @customerId", new { customerId }).SingleOrDefault());

    public WorkOrder Get(long workOrderId) =>
        _db.Run(db => Loader.WorkOrder(db, workOrderId)) ?? throw new UserFacingException("That work order no longer exists.");

    /// <summary>Invoices that work on this tab was billed to, for the Billed History.</summary>
    public IReadOnlyDictionary<long, Invoice> BilledInvoices(WorkOrder wo)
    {
        var ids = wo.Lines.Where(l => l.InvoiceId is not null).Select(l => l.InvoiceId!.Value).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<long, Invoice>();
        return _db.Run(db => Loader.Invoices(db, "id IN @ids", new { ids }, withCustomers: false)).ToDictionary(i => i.Id);
    }

    // ------------------------------------------------------------------ lines

    /// <summary>The quick-add bar: a to-do with no money attached until it is completed.</summary>
    public Notice AddTodo(long workOrderId, string description, string? projectLabel)
    {
        description = (description ?? "").Trim();
        if (description.Length == 0) throw new UserFacingException("A description is required.");
        var line = new WorkOrderLine
        {
            WorkOrderId = workOrderId,
            Description = description,
            ProjectLabel = Cut((projectLabel ?? "").Trim(), 120),
            Status = "pending",
            LineType = LineTypes.Labor,
            Rate = DefaultHourlyRate(),
            Quantity = 1.0,
            Amount = 0.0,
        };
        _db.Run(db => { RequireWorkOrder(db, null, workOrderId); Insert(db, null, line); });
        return Notice.Success("To-do added.");
    }

    /// <summary>The Log Work dialog: finished work, dated today unless a date is given.</summary>
    public Notice LogWork(long workOrderId, LineInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Description)) throw new UserFacingException("A description is required.");
        var line = new WorkOrderLine { WorkOrderId = workOrderId };
        Apply(line, input);
        line.Status = "completed";
        line.DatePerformed = input.DatePerformed ?? _today();
        _db.Run(db => { RequireWorkOrder(db, null, workOrderId); Insert(db, null, line); });
        return Notice.Success($"Logged work: {line.Description}");
    }

    public Notice EditLine(long lineId, LineInput input) => WithUnbilledLine(lineId, (db, tx, line) =>
    {
        if (string.IsNullOrWhiteSpace(input.Description)) throw new UserFacingException("A description is required.");
        Apply(line, input);
        if (input.DatePerformed is { } performed)
        {
            line.DatePerformed = performed;
            line.Status = "completed";
        }
        else if (line.Status == "pending")
        {
            line.Amount = 0.0;   // still a to-do: no money until it is completed
        }
        Save(db, tx, line);
        return Notice.Success("Line updated.");
    });

    public Notice CompleteLine(long lineId, LineInput input) => WithUnbilledLine(lineId, (db, tx, line) =>
    {
        Apply(line, input);
        line.Status = "completed";
        line.DatePerformed = input.DatePerformed ?? _today();
        Save(db, tx, line);
        return Notice.Success($"Marked complete: {line.Description}");
    });

    /// <summary>Back to a pending to-do. The hours and rate are kept; the date and amount are cleared.</summary>
    public Notice ReopenLine(long lineId) => WithUnbilledLine(lineId, (db, tx, line) =>
    {
        line.Status = "pending";
        line.DatePerformed = null;
        line.Amount = 0.0;
        Save(db, tx, line);
        return Notice.Info("Line moved back to pending.");
    });

    public Notice ToggleNoCharge(long lineId) => WithUnbilledLine(lineId, (db, tx, line) =>
    {
        line.NoCharge = !line.NoCharge;
        Save(db, tx, line);
        return Notice.Info($"{line.Description} marked {(line.NoCharge ? "no charge" : "billable")}.");
    });

    public Notice DeleteLine(long lineId) => WithUnbilledLine(lineId, (db, tx, line) =>
    {
        db.Execute("DELETE FROM work_order_lines WHERE id = @lineId", new { lineId }, tx);
        return Notice.Warning($"Removed: {line.Description}");
    });

    public Notice UpdateNotes(long workOrderId, string notes)
    {
        int n = _db.Run(db => db.Execute("UPDATE work_orders SET notes = @notes WHERE id = @workOrderId",
            new { notes = (notes ?? "").Trim(), workOrderId }));
        if (n == 0) throw new UserFacingException("That work order no longer exists.");
        return Notice.Success("Notes saved.");
    }

    // ------------------------------------------------------------------ helpers

    public const string BilledLineMessage =
        "That line has already been billed. Delete or edit the invoice instead — deleting the invoice returns the work to this tab.";

    private Notice WithUnbilledLine(long lineId, Func<SqliteConnection, SqliteTransaction, WorkOrderLine, Notice> work) =>
        _db.InTransaction((db, tx) =>
        {
            var line = db.QuerySingleOrDefault<WorkOrderLine>("SELECT * FROM work_order_lines WHERE id = @lineId", new { lineId }, tx)
                ?? throw new UserFacingException("That line no longer exists. It may have been deleted.");
            if (line.IsBilled) return Notice.Warning(BilledLineMessage);
            return work(db, tx, line);
        });

    /// <summary>1.x <c>_apply_line_fields</c>. The amount is always recomputed, never taken from the form.</summary>
    private static void Apply(WorkOrderLine line, LineInput input)
    {
        line.Description = Cut((input.Description ?? "").Trim(), 500);
        line.ProjectLabel = Cut((input.ProjectLabel ?? "").Trim(), 120);
        line.InternalNote = (input.InternalNote ?? "").Trim();
        string type = (input.LineType ?? LineTypes.Labor).Trim();
        line.LineType = LineTypes.IsValid(type) ? type : LineTypes.Labor;
        line.Quantity = Math.Max(0.0, ParseFloat(input.Quantity, 1.0));
        line.Rate = Math.Max(0.0, ParseFloat(input.Rate, 0.0));
        line.NoCharge = input.NoCharge;
        line.RecalcAmount();
    }

    /// <summary>Python float(text.strip()), with the default when it does not parse. Infinity and NaN count as not parsing.</summary>
    internal static double ParseFloat(string? text, double fallback)
    {
        string s = (text ?? "").Trim().Replace("_", "");
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v) ? v : fallback;
    }

    private static string Cut(string s, int max) => s.Length > max ? s[..max] : s;

    private static void RequireWorkOrder(SqliteConnection db, SqliteTransaction? tx, long id)
    {
        if (!db.ExecuteScalar<bool>("SELECT EXISTS (SELECT 1 FROM work_orders WHERE id = @id)", new { id }, tx))
            throw new UserFacingException("That work order no longer exists.");
    }

    private static void Insert(SqliteConnection db, SqliteTransaction? tx, WorkOrderLine l) => db.Execute("""
        INSERT INTO work_order_lines (work_order_id, project_label, description, line_type, status, date_performed,
            quantity, rate, amount, no_charge, internal_note, invoice_id, billed_at, created_at)
        VALUES (@WorkOrderId, @ProjectLabel, @Description, @LineType, @Status, @DatePerformed,
            @Quantity, @Rate, @Amount, @NoCharge, @InternalNote, NULL, NULL, @now)
        """, new { l.WorkOrderId, l.ProjectLabel, l.Description, l.LineType, l.Status, l.DatePerformed, l.Quantity, l.Rate, l.Amount, l.NoCharge, l.InternalNote, now = SqlFormat.NowUtc() }, tx);

    private static void Save(SqliteConnection db, SqliteTransaction tx, WorkOrderLine l) => db.Execute("""
        UPDATE work_order_lines SET project_label = @ProjectLabel, description = @Description, line_type = @LineType,
            status = @Status, date_performed = @DatePerformed, quantity = @Quantity, rate = @Rate, amount = @Amount,
            no_charge = @NoCharge, internal_note = @InternalNote
        WHERE id = @Id
        """, new { l.ProjectLabel, l.Description, l.LineType, l.Status, l.DatePerformed, l.Quantity, l.Rate, l.Amount, l.NoCharge, l.InternalNote, l.Id }, tx);
}
