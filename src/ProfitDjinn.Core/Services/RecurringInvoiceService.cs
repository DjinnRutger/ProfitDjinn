using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using ProfitDjinn.Core.Data;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Rules;

namespace ProfitDjinn.Core.Services;

/// <summary>What the recurring invoice form submits.</summary>
public sealed record RecurringInvoiceDraft(
    long? CustomerId,
    string Interval,
    DateOnly? StartDate,
    int? DayOfMonth,
    DateOnly? EndDate,
    string Notes,
    string Term1,
    string Term2,
    bool IsActive,
    IReadOnlyList<InvoiceLineDraft> Lines);

/// <summary>One invoice created from a schedule.</summary>
public sealed record IssuedInvoice(long InvoiceId, long RecurringId, string InvoiceNumber, string CustomerName, DateOnly Date, double Total);

/// <summary>The next invoice a schedule will create.</summary>
public sealed record UpcomingInvoice(RecurringInvoice Schedule, DateOnly Date)
{
    public double Total => Schedule.Total;
}

/// <summary>What a generation run did: the invoices made, and schedules held back with the reason.</summary>
public sealed record RecurringInvoiceRun(IReadOnlyList<IssuedInvoice> Created, IReadOnlyList<string> Problems);

/// <summary>
/// 2.4. Recurring invoices: a schedule per customer that creates the same invoice every month
/// or year, on its own date, with the next invoice number.
///
/// The rules that keep it from ever invoicing a date twice, the same as recurring expenses:
/// - <c>generated_through</c> is the last date handled. Nothing on or before it is created again,
///   so deleting a generated invoice does not bring it back, and editing never back-fills.
/// - <c>recurring_invoice_runs</c> has one row per schedule and date (unique index), kept even
///   when the invoice is deleted.
/// - Turning a paused schedule back on skips the dates it was paused for.
///
/// <see cref="Issue"/> is the only place a schedule becomes a real invoice. A future billing
/// provider (Stripe) hooks in there, keyed on <see cref="RecurringInvoice.CollectionMethod"/>.
/// </summary>
public sealed class RecurringInvoiceService
{
    private readonly Database _db;
    private readonly SettingsService _settings;
    private readonly InvoiceService _invoices;
    private readonly Func<DateOnly> _today;

    public RecurringInvoiceService(Database db, SettingsService settings, InvoiceService invoices, Func<DateOnly> today)
    {
        _db = db;
        _settings = settings;
        _invoices = invoices;
        _today = today;
    }

    // ------------------------------------------------------------------ reading

    public IReadOnlyList<RecurringInvoice> List(long? customerId = null) => _db.Run(db => Load(db, null, customerId));

    public RecurringInvoice Get(long id) => _db.Run(db => Load(db, null, null, id).SingleOrDefault()) ?? throw NotFound();

    /// <summary>A new schedule's starting values: monthly from today, the default terms.</summary>
    public RecurringInvoiceDraft NewDraft(long? customerId = null)
    {
        var today = _today();
        return new(customerId, BillingInterval.Month, today, today.Day, null, "",
            _settings.Get(SettingKeys.InvoiceTerm1, "Payment Terms: Due within 30 days"),
            _settings.Get(SettingKeys.InvoiceTerm2, "Make all checks payable to Your Name"),
            true, Array.Empty<InvoiceLineDraft>());
    }

    /// <summary>
    /// The next invoice of every active schedule, soonest first. With <paramref name="through"/>,
    /// only those on or before it.
    /// </summary>
    public IReadOnlyList<UpcomingInvoice> Upcoming(DateOnly? through = null) => List()
        .Where(t => t.IsActive && t.NextDate is { } d && (through is null || d <= through))
        .Select(t => new UpcomingInvoice(t, t.NextDate!.Value))
        .OrderBy(u => u.Date).ThenBy(u => u.Schedule.Customer?.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>
    /// For the dashboard: every invoice the active schedules will create from now through
    /// <paramref name="days"/> days ahead (a schedule can fall twice), and their total.
    /// </summary>
    public (int Count, double Total) DueWithin(int days = 30)
    {
        var through = _today().AddDays(days);
        var amounts = new List<double>();
        foreach (var t in List().Where(t => t.IsActive))
        {
            DateOnly from = t.GeneratedThrough is { } g ? g.AddDays(1) : t.StartDate;
            foreach (var _ in t.Schedule.Occurrences(from, through)) amounts.Add(t.Total);
        }
        return (amounts.Count, PyMath.Sum(amounts, a => a));
    }

    /// <summary>
    /// How many past invoices saving this draft would create now. A new schedule counts every
    /// date from the start through today; an edited one only dates after what it already made.
    /// </summary>
    public int PreviewCount(RecurringInvoiceDraft draft, long? id = null)
    {
        var current = id is { } i ? Get(i) : null;
        var c = Validate(draft, current);
        if (!c.IsActive) return 0;
        var t = ToSchedule(c);
        DateOnly from = current?.GeneratedThrough is { } g ? g.AddDays(1) : t.StartDate;
        return t.Schedule.Occurrences(from, _today()).Count();
    }

    /// <summary>
    /// The invoice the schedule will create on <paramref name="date"/>, built in memory and not
    /// saved: placeholders filled, and the number it would get if it were created now.
    /// </summary>
    public Invoice Preview(long id, DateOnly date) => _db.Run(db =>
    {
        var t = Load(db, null, null, id).SingleOrDefault() ?? throw NotFound();
        RequireNext(t, date);
        var invoice = Build(t, date, _invoices.NextNumber(db, null));
        invoice.Customer = Loader.Customer(db, t.CustomerId);
        return invoice;
    });

    // ------------------------------------------------------------------ writing

    public Created Create(RecurringInvoiceDraft draft)
    {
        var c = Validate(draft, current: null);
        long id = _db.InTransaction((db, tx) =>
        {
            long newId = db.ExecuteScalar<long>("""
                INSERT INTO recurring_invoices (customer_id, interval, interval_count, start_date, day_of_month, end_date,
                    notes, term1, term2, collection_method, is_active, generated_through, created_at)
                VALUES (@CustomerId, @Interval, 1, @StartDate, @DayOfMonth, @EndDate,
                    @Notes, @Term1, @Term2, @method, @IsActive, NULL, @now);
                SELECT last_insert_rowid();
                """, new { c.CustomerId, c.Interval, c.StartDate, c.DayOfMonth, c.EndDate, c.Notes, c.Term1, c.Term2,
                    method = CollectionMethod.SendInvoice, c.IsActive, now = SqlFormat.NowUtc() }, tx);
            SaveLines(db, tx, newId, c.Lines);
            return newId;
        });
        return new Created(id, Notice.Success($"Recurring invoice for {CustomerName(c.CustomerId!.Value)} saved."));
    }

    public Notice Update(long id, RecurringInvoiceDraft draft)
    {
        var current = Get(id);
        var c = Validate(draft, current);
        // Turning it back on here skips the paused dates, as ToggleActive does.
        DateOnly? generated = !current.IsActive && c.IsActive ? Resume(current.GeneratedThrough) : current.GeneratedThrough;
        _db.InTransaction((db, tx) =>
        {
            db.Execute("""
                UPDATE recurring_invoices SET customer_id = @CustomerId, interval = @Interval, start_date = @StartDate,
                    day_of_month = @DayOfMonth, end_date = @EndDate, notes = @Notes, term1 = @Term1, term2 = @Term2,
                    is_active = @IsActive, generated_through = @generated
                WHERE id = @id
                """, new { c.CustomerId, c.Interval, c.StartDate, c.DayOfMonth, c.EndDate, c.Notes, c.Term1, c.Term2, c.IsActive, generated, id }, tx);
            db.Execute("DELETE FROM recurring_invoice_lines WHERE recurring_id = @id", new { id }, tx);
            SaveLines(db, tx, id, c.Lines);
        });
        return Notice.Success($"Recurring invoice for {CustomerName(c.CustomerId!.Value)} updated.");
    }

    public Notice ToggleActive(long id)
    {
        var t = Get(id);
        DateOnly? generated = t.IsActive ? t.GeneratedThrough : Resume(t.GeneratedThrough);
        _db.Run(db => db.Execute("UPDATE recurring_invoices SET is_active = @active, generated_through = @generated WHERE id = @id",
            new { active = !t.IsActive, generated, id }));
        string who = t.Customer?.Name ?? "this customer";
        return Notice.Info(t.IsActive
            ? $"Recurring invoice for {who} paused. It creates nothing until you turn it back on."
            : $"Recurring invoice for {who} turned back on. Dates while it was paused are skipped.");
    }

    /// <summary>Deletes the schedule. Invoices it already created stay.</summary>
    public Notice Delete(long id) => _db.InTransaction((db, tx) =>
    {
        long customerId = db.ExecuteScalar<long?>("SELECT customer_id FROM recurring_invoices WHERE id = @id", new { id }, tx) ?? throw NotFound();
        int kept = db.ExecuteScalar<int>("SELECT COUNT(*) FROM recurring_invoice_runs WHERE recurring_id = @id AND invoice_id IS NOT NULL", new { id }, tx);
        DeleteSchedules(db, tx, "id = @id", new { id });
        string who = db.ExecuteScalar<string?>("SELECT name FROM customers WHERE id = @customerId", new { customerId }, tx) ?? "a deleted customer";
        return Notice.Warning(kept == 0
            ? $"Recurring invoice for {who} deleted."
            : $"Recurring invoice for {who} deleted. {(kept == 1 ? "The invoice it already created is" : $"The {kept} invoices it already created are")} kept.");
    });

    /// <summary>
    /// Creates the invoice for <paramref name="date"/>, which must be the schedule's next date.
    /// Used on the date (by <see cref="GenerateDue"/>) and early, when the user issues or prints
    /// an upcoming invoice before its date: it keeps its scheduled date either way, and the
    /// schedule moves on so that date is never created again.
    /// </summary>
    public IssuedInvoice Issue(long id, DateOnly date) => _db.InTransaction((db, tx) =>
    {
        var t = Load(db, tx, null, id).SingleOrDefault() ?? throw NotFound();
        RequireNext(t, date);
        return IssueIn(db, tx, t, date);
    });

    /// <summary>Passes on the schedule's next date without creating an invoice for it.</summary>
    public Notice Skip(long id, DateOnly date) => _db.InTransaction((db, tx) =>
    {
        var t = Load(db, tx, null, id).SingleOrDefault() ?? throw NotFound();
        RequireNext(t, date);
        db.Execute("UPDATE recurring_invoices SET generated_through = @date WHERE id = @id", new { date, id }, tx);
        string next = t.Schedule.FirstFrom(date.AddDays(1)) is { } n ? $" The next one is {Day(n)}." : " That was the last one.";
        return Notice.Info($"Skipped the {Day(date)} invoice for {t.Customer?.Name ?? "this customer"}.{next}");
    });

    /// <summary>
    /// Creates every invoice that has come due, up to today, for each active schedule. Safe to
    /// run any number of times. A schedule whose customer is inactive is held back, and named.
    /// </summary>
    public RecurringInvoiceRun GenerateDue()
    {
        DateOnly today = _today();
        var created = new List<IssuedInvoice>();
        var problems = new List<string>();
        foreach (var t in List().Where(t => t.IsActive))
        {
            DateOnly from = t.GeneratedThrough is { } g ? g.AddDays(1) : t.StartDate;
            DateOnly to = t.EndDate is { } end && end < today ? end : today;
            if (from > to) continue;
            var dates = t.Schedule.Occurrences(from, to).ToList();
            if (dates.Count == 0) continue;
            if (t.Customer is not { IsActive: true })
            {
                problems.Add($"{t.Customer?.Name ?? "A deleted customer"} is inactive, so its recurring invoice was not created. Make the customer active, or pause the schedule.");
                continue;
            }
            try
            {
                _db.InTransaction((db, tx) =>
                {
                    foreach (var d in dates)
                    {
                        if (db.ExecuteScalar<bool>("SELECT EXISTS (SELECT 1 FROM recurring_invoice_runs WHERE recurring_id = @Id AND date = @d)", new { t.Id, d }, tx))
                            continue;
                        created.Add(IssueIn(db, tx, t, d));
                    }
                });
            }
            catch (Exception e) when (e is SqliteException or UserFacingException)
            {
                problems.Add($"The recurring invoice for {t.Customer.Name} could not be created: {e.Message}");
            }
        }
        return new RecurringInvoiceRun(created, problems);
    }

    /// <summary>"2 recurring invoices created: INV1043 Maple Street HOA (Nov 01), ..." for the notice at start.</summary>
    public static Notice? Summarize(RecurringInvoiceRun run)
    {
        var parts = new List<string>();
        if (run.Created.Count > 0)
        {
            var names = run.Created.Take(3).Select(i => $"{i.InvoiceNumber} {i.CustomerName} ({i.Date.ToString("MMM dd", CultureInfo.InvariantCulture)})");
            string more = run.Created.Count > 3 ? $", and {run.Created.Count - 3} more" : "";
            parts.Add($"{run.Created.Count} recurring {Fmt.Plural(run.Created.Count, "invoice")} created: {string.Join(", ", names)}{more}.");
        }
        parts.AddRange(run.Problems);
        if (parts.Count == 0) return null;
        string text = string.Join(" ", parts);
        return run.Problems.Count > 0 ? Notice.Warning(text) : Notice.Info(text);
    }

    // ------------------------------------------------------------------ cascades (foreign keys are off)

    /// <summary>Removes schedules, their lines and their runs. The invoices they made are not touched.</summary>
    internal static void DeleteSchedules(SqliteConnection db, SqliteTransaction tx, string where, object args)
    {
        db.Execute($"DELETE FROM recurring_invoice_lines WHERE recurring_id IN (SELECT id FROM recurring_invoices WHERE {where})", args, tx);
        db.Execute($"DELETE FROM recurring_invoice_runs WHERE recurring_id IN (SELECT id FROM recurring_invoices WHERE {where})", args, tx);
        db.Execute($"DELETE FROM recurring_invoices WHERE {where}", args, tx);
    }

    /// <summary>An invoice was deleted: its run row stays, so the date is not invoiced again, but loses the link.</summary>
    internal static void ForgetInvoices(SqliteConnection db, SqliteTransaction tx, string invoiceWhere, object args) =>
        db.Execute($"UPDATE recurring_invoice_runs SET invoice_id = NULL WHERE invoice_id IN (SELECT id FROM invoices WHERE {invoiceWhere})", args, tx);

    // ------------------------------------------------------------------ helpers

    private IssuedInvoice IssueIn(SqliteConnection db, SqliteTransaction tx, RecurringInvoice t, DateOnly d)
    {
        if (db.ExecuteScalar<bool>("SELECT EXISTS (SELECT 1 FROM recurring_invoice_runs WHERE recurring_id = @Id AND date = @d)", new { t.Id, d }, tx))
            throw new UserFacingException($"The {Day(d)} invoice for this schedule was already created.");
        bool active = db.ExecuteScalar<bool?>("SELECT is_active FROM customers WHERE id = @CustomerId", new { t.CustomerId }, tx) ?? false;
        if (!active) throw new UserFacingException($"{t.Customer?.Name ?? "This customer"} is inactive or deleted. Make the customer active before creating the invoice.");

        // Stripe hook (docs/stripe-readiness.md): a schedule whose collection_method is handled
        // by a provider would hand off here. Today every schedule is send_invoice.
        var invoice = Build(t, d, _invoices.NextNumber(db, tx));
        long invoiceId = db.ExecuteScalar<long>("""
            INSERT INTO invoices (invoice_number, customer_id, date, notes, term1, term2, paid, paid_date, credit_applied, created_at)
            VALUES (@InvoiceNumber, @CustomerId, @Date, @Notes, @Term1, @Term2, 0, NULL, 0, @now);
            SELECT last_insert_rowid();
            """, new { invoice.InvoiceNumber, invoice.CustomerId, invoice.Date, invoice.Notes, invoice.Term1, invoice.Term2, now = SqlFormat.NowUtc() }, tx);
        InvoiceService.InsertLines(db, tx, invoiceId,
            invoice.Lines.Select(l => new InvoiceLineDraft(l.Description, l.Quantity, l.Amount)), skipBlank: false, roundAmounts: true);
        db.Execute("INSERT INTO recurring_invoice_runs (recurring_id, date, invoice_id, created_at) VALUES (@Id, @d, @invoiceId, @now)",
            new { t.Id, d, invoiceId, now = SqlFormat.NowUtc() }, tx);
        db.Execute("UPDATE recurring_invoices SET generated_through = @d WHERE id = @Id AND (generated_through IS NULL OR generated_through < @d)",
            new { d, t.Id }, tx);
        t.GeneratedThrough = t.GeneratedThrough is { } g && g > d ? g : d;
        return new IssuedInvoice(invoiceId, t.Id, invoice.InvoiceNumber, t.Customer?.Name ?? "", d, PyMath.Sum(invoice.Lines, l => l.Amount));
    }

    /// <summary>The invoice a schedule makes on a date. Amounts are rounded to cents as saved.</summary>
    private static Invoice Build(RecurringInvoice t, DateOnly d, string number) => new()
    {
        InvoiceNumber = number,
        CustomerId = t.CustomerId,
        Date = d,
        Notes = PeriodText.Fill(t.Notes, d),
        Term1 = t.Term1 ?? "",
        Term2 = t.Term2 ?? "",
        Customer = t.Customer,
        Lines = t.Lines.OrderBy(l => l.Position).Select(l => new InvoiceLine
        {
            Description = PeriodText.Fill(l.Description, d),
            Quantity = l.Quantity,
            Amount = PyMath.Round(l.Amount, 2),
        }).ToList(),
    };

    private static void RequireNext(RecurringInvoice t, DateOnly date)
    {
        if (t.NextDate != date)
            throw new UserFacingException(t.NextDate is { } n
                ? $"That date is no longer upcoming for this schedule. Its next invoice is {Day(n)}."
                : "This schedule has no more invoices to create.");
    }

    private static List<RecurringInvoice> Load(SqliteConnection db, SqliteTransaction? tx, long? customerId, long? id = null)
    {
        string where = id is not null ? "id = @id" : customerId is not null ? "customer_id = @customerId" : "1=1";
        var all = db.Query<RecurringInvoice>($"SELECT * FROM recurring_invoices WHERE {where} ORDER BY id", new { id, customerId }, tx).ToList();
        if (all.Count == 0) return all;
        var ids = all.Select(t => t.Id).ToList();
        var lines = db.Query<RecurringInvoiceLine>("SELECT * FROM recurring_invoice_lines WHERE recurring_id IN @ids ORDER BY position, id", new { ids }, tx)
            .ToLookup(l => l.RecurringId);
        var customerIds = all.Select(t => t.CustomerId).Distinct().ToList();
        var customers = db.Query<Customer>("SELECT * FROM customers WHERE id IN @customerIds", new { customerIds }, tx).ToDictionary(c => c.Id);
        foreach (var t in all)
        {
            t.Lines = lines[t.Id].ToList();
            t.Customer = customers.GetValueOrDefault(t.CustomerId);
        }
        return all
            .OrderBy(t => t.Customer?.Name ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Id)
            .ToList();
    }

    private static void SaveLines(SqliteConnection db, SqliteTransaction tx, long id, IReadOnlyList<InvoiceLineDraft> lines)
    {
        for (int i = 0; i < lines.Count; i++)
            db.Execute("INSERT INTO recurring_invoice_lines (recurring_id, position, description, quantity, amount) VALUES (@id, @i, @Description, @Quantity, @Amount)",
                new { id, i, lines[i].Description, lines[i].Quantity, lines[i].Amount }, tx);
    }

    /// <summary>On resuming, dates before today are treated as already handled.</summary>
    private DateOnly? Resume(DateOnly? generated)
    {
        DateOnly yesterday = _today().AddDays(-1);
        return generated is { } g && g > yesterday ? g : yesterday;
    }

    private static RecurringInvoice ToSchedule(RecurringInvoiceDraft c) => new()
    {
        CustomerId = c.CustomerId!.Value, Interval = c.Interval, StartDate = c.StartDate!.Value,
        DayOfMonth = c.DayOfMonth!.Value, EndDate = c.EndDate, IsActive = c.IsActive,
    };

    private RecurringInvoiceDraft Validate(RecurringInvoiceDraft d, RecurringInvoice? current)
    {
        var checks = new Checks();
        if (d.CustomerId is not { } cid) checks.Add("customer", Checks.Required);
        else
        {
            bool? active = _db.Run(db => db.ExecuteScalar<bool?>("SELECT is_active FROM customers WHERE id = @cid", new { cid }));
            if (active is null) checks.Add("customer", "That customer no longer exists. Choose another.");
            else if (active == false && current?.CustomerId != cid) checks.Add("customer", "That customer is inactive. Make them active again, or choose another.");
        }
        if (!BillingInterval.IsValid(d.Interval)) checks.Add("interval", "Choose monthly or yearly.");
        if (d.StartDate is null) checks.Add("start_date", Checks.Required);
        if (d.DayOfMonth is not { } day) checks.Add("day_of_month", Checks.Required);
        else if (day is < 1 or > 31) checks.Add("day_of_month", "Enter a day from 1 to 31.");
        if (d.EndDate is { } end && d.StartDate is { } start && end < start) checks.Add("end_date", "The end date cannot be before the start date.");
        checks.MaxLength("term1", d.Term1, 300);
        checks.MaxLength("term2", d.Term2, 300);

        // A row with no description and no amount is an empty row left in the line builder.
        var lines = (d.Lines ?? Array.Empty<InvoiceLineDraft>())
            .Select(l => l with { Description = (l.Description ?? "").Trim() })
            .Where(l => l.Description.Length > 0 || l.Amount != 0)
            .ToList();
        if (lines.Any(l => l.Description.Length > 500)) checks.Add("lines", "A line description cannot be longer than 500 characters.");
        checks.ThrowIfAny();
        if (lines.Count == 0) throw new UserFacingException("At least one line item is required.");
        return d with { Notes = d.Notes ?? "", Term1 = d.Term1 ?? "", Term2 = d.Term2 ?? "", Lines = lines };
    }

    private string CustomerName(long id) =>
        _db.Run(db => db.ExecuteScalar<string?>("SELECT name FROM customers WHERE id = @id", new { id })) ?? "this customer";

    private static string Day(DateOnly d) => d.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

    private static UserFacingException NotFound() => new("That recurring invoice no longer exists. It may have been deleted.");
}
