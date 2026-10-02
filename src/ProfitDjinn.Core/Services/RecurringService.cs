using Dapper;
using Microsoft.Data.Sqlite;
using ProfitDjinn.Core.Data;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Rules;

namespace ProfitDjinn.Core.Services;

/// <summary>What the recurring expense form submits.</summary>
public sealed record RecurringDraft(
    long? VendorId,
    long? CategoryId,
    string Description,
    double? Amount,
    string Frequency,
    DateOnly? StartDate,
    int? DayOfMonth,
    DateOnly? EndDate,
    string Mode,
    string? Method,
    string Notes,
    bool IsActive);

/// <summary>One expense created from a recurring expense.</summary>
public sealed record GeneratedExpense(long ExpenseId, string Description, DateOnly Date, double Amount, bool Paid);

/// <summary>
/// 2.2. Recurring expenses: a template that creates an expense every month or year.
///
/// The rules that keep it from ever double-counting:
/// - Each template remembers <c>generated_through</c>, the last date it has created
///   expenses up to. Nothing on or before that date is created again, so deleting a
///   generated expense does not bring it back, and editing a template never back-fills.
/// - An occurrence is skipped if an expense from the same template already has that date
///   (also enforced by a unique index).
/// - Turning a paused template back on skips the dates it was paused for.
/// </summary>
public sealed class RecurringService
{
    private readonly Database _db;
    private readonly SettingsService _settings;
    private readonly Func<DateOnly> _today;

    public RecurringService(Database db, SettingsService settings, Func<DateOnly> today)
    {
        _db = db;
        _settings = settings;
        _today = today;
    }

    // ------------------------------------------------------------------ the schedule

    /// <summary>
    /// The dates a template falls on between <paramref name="from"/> and <paramref name="to"/>,
    /// inclusive. Every date is worked out from the start month, never from the previous date,
    /// so day 31 gives Jan 31, Feb 28 (29 in a leap year), Mar 31; a yearly Feb 29 gives Feb 28
    /// in other years.
    /// </summary>
    public static IEnumerable<DateOnly> Occurrences(RecurringExpense t, DateOnly from, DateOnly to)
    {
        var anchor = new DateOnly(t.StartDate.Year, t.StartDate.Month, 1);
        int step = t.Frequency == RecurringFrequency.Yearly ? 12 : 1;
        for (int k = 0; ; k++)
        {
            var month = anchor.AddMonths(k * step);
            var d = new DateOnly(month.Year, month.Month, Math.Min(t.DayOfMonth, DateTime.DaysInMonth(month.Year, month.Month)));
            if (d > to) yield break;
            if (t.EndDate is { } end && d > end) yield break;
            if (d < t.StartDate || d < from) continue;
            yield return d;
        }
    }

    /// <summary>The first date not yet created, or null when the template has ended.</summary>
    public DateOnly? NextDate(RecurringExpense t)
    {
        DateOnly from = t.GeneratedThrough is { } g ? g.AddDays(1) : t.StartDate;
        foreach (var d in Occurrences(t, from, DateOnly.MaxValue.AddYears(-1))) return d;
        return null;
    }

    // ------------------------------------------------------------------ reading

    public IReadOnlyList<RecurringExpense> List() => _db.Run(db =>
    {
        var all = db.Query<RecurringExpense>("SELECT * FROM recurring_expenses ORDER BY description COLLATE NOCASE, id").ToList();
        var vendors = db.Query<Vendor>("SELECT * FROM vendors").ToDictionary(v => v.Id);
        var categories = db.Query<ExpenseCategory>("SELECT * FROM expense_categories").ToDictionary(c => c.Id);
        foreach (var t in all)
        {
            if (t.VendorId is { } v) t.Vendor = vendors.GetValueOrDefault(v);
            t.Category = categories.GetValueOrDefault(t.CategoryId);
        }
        return all;
    });

    public RecurringExpense Get(long id) => List().FirstOrDefault(t => t.Id == id) ?? throw NotFound();

    /// <summary>
    /// How many past expenses saving this draft would create now. For a new template that is
    /// every date from the start through today; an edited template only counts dates after
    /// what it has already created.
    /// </summary>
    public int PreviewCount(RecurringDraft draft, long? id = null)
    {
        var t = ToTemplate(Validate(draft, current: id is { } i ? Get(i) : null));
        if (!t.IsActive) return 0;
        DateOnly? generated = id is { } existing ? Get(existing).GeneratedThrough : null;
        DateOnly from = generated is { } g ? g.AddDays(1) : t.StartDate;
        return Occurrences(t, from, _today()).Count();
    }

    // ------------------------------------------------------------------ writing

    public Created Create(RecurringDraft draft)
    {
        var c = Validate(draft, current: null);
        long id = _db.Run(db => db.ExecuteScalar<long>("""
            INSERT INTO recurring_expenses (vendor_id, category_id, description, amount, frequency, start_date, day_of_month,
                end_date, mode, method, notes, is_active, generated_through, created_at)
            VALUES (@VendorId, @CategoryId, @Description, @Amount, @Frequency, @StartDate, @DayOfMonth,
                @EndDate, @Mode, @Method, @Notes, @IsActive, NULL, @now);
            SELECT last_insert_rowid();
            """, new { c.VendorId, c.CategoryId, c.Description, c.Amount, c.Frequency, c.StartDate, c.DayOfMonth, c.EndDate, c.Mode, c.Method, c.Notes, c.IsActive, now = SqlFormat.NowUtc() }));
        return new Created(id, Notice.Success($"Recurring expense '{c.Description}' saved."));
    }

    public Notice Update(long id, RecurringDraft draft)
    {
        var current = Get(id);
        var c = Validate(draft, current);
        // Turning it back on here skips the paused dates, as ToggleActive does.
        DateOnly? generated = !current.IsActive && c.IsActive ? Resume(current.GeneratedThrough) : current.GeneratedThrough;
        _db.Run(db => db.Execute("""
            UPDATE recurring_expenses SET vendor_id = @VendorId, category_id = @CategoryId, description = @Description,
                amount = @Amount, frequency = @Frequency, start_date = @StartDate, day_of_month = @DayOfMonth,
                end_date = @EndDate, mode = @Mode, method = @Method, notes = @Notes, is_active = @IsActive,
                generated_through = @generated
            WHERE id = @id
            """, new { c.VendorId, c.CategoryId, c.Description, c.Amount, c.Frequency, c.StartDate, c.DayOfMonth, c.EndDate, c.Mode, c.Method, c.Notes, c.IsActive, generated, id }));
        return Notice.Success($"Recurring expense '{c.Description}' updated.");
    }

    public Notice ToggleActive(long id)
    {
        var t = Get(id);
        DateOnly? generated = t.IsActive ? t.GeneratedThrough : Resume(t.GeneratedThrough);
        _db.Run(db => db.Execute("UPDATE recurring_expenses SET is_active = @active, generated_through = @generated WHERE id = @id",
            new { active = !t.IsActive, generated, id }));
        return Notice.Info(t.IsActive
            ? $"Recurring expense '{t.Description}' paused. It creates nothing until you turn it back on."
            : $"Recurring expense '{t.Description}' turned back on. Dates while it was paused are skipped.");
    }

    /// <summary>Deletes the template. Expenses it already created stay, no longer linked to it.</summary>
    public Notice Delete(long id) => _db.InTransaction((db, tx) =>
    {
        string desc = db.ExecuteScalar<string?>("SELECT description FROM recurring_expenses WHERE id = @id", new { id }, tx) ?? throw NotFound();
        int kept = db.Execute("UPDATE expenses SET recurring_id = NULL WHERE recurring_id = @id", new { id }, tx);
        db.Execute("DELETE FROM recurring_expenses WHERE id = @id", new { id }, tx);
        return Notice.Warning(kept == 0
            ? $"Recurring expense '{desc}' deleted."
            : $"Recurring expense '{desc}' deleted. {(kept == 1 ? "The expense it already created is" : $"The {kept} expenses it already created are")} kept.");
    });

    /// <summary>
    /// Creates every expense that has come due, up to today, for each active template. Does
    /// nothing while Expenses is turned off. Safe to run any number of times.
    /// </summary>
    public IReadOnlyList<GeneratedExpense> GenerateDue()
    {
        if (!_settings.GetBool(SettingKeys.ExpensesEnabled)) return Array.Empty<GeneratedExpense>();
        DateOnly today = _today();
        var created = new List<GeneratedExpense>();
        foreach (var t in List().Where(t => t.IsActive))
        {
            DateOnly from = t.GeneratedThrough is { } g ? g.AddDays(1) : t.StartDate;
            DateOnly to = t.EndDate is { } end && end < today ? end : today;
            if (from > to) continue;
            _db.InTransaction((db, tx) =>
            {
                foreach (var d in Occurrences(t, from, to))
                {
                    if (db.ExecuteScalar<bool>("SELECT EXISTS (SELECT 1 FROM expenses WHERE recurring_id = @Id AND date = @d)", new { t.Id, d }, tx))
                        continue;
                    created.Add(Insert(db, tx, t, d));
                }
                db.Execute("UPDATE recurring_expenses SET generated_through = @to WHERE id = @Id", new { to, t.Id }, tx);
            });
        }
        return created;
    }

    /// <summary>"3 recurring expenses added: Rent (Oct 01), ..." for the notice at start.</summary>
    public static Notice? Summarize(IReadOnlyList<GeneratedExpense> created)
    {
        if (created.Count == 0) return null;
        var names = created.Take(3).Select(g => $"{g.Description} ({g.Date.ToString("MMM dd", System.Globalization.CultureInfo.InvariantCulture)})");
        string more = created.Count > 3 ? $", and {created.Count - 3} more" : "";
        return Notice.Info($"{created.Count} recurring {Fmt.Plural(created.Count, "expense")} added: {string.Join(", ", names)}{more}.");
    }

    // ------------------------------------------------------------------ helpers

    private GeneratedExpense Insert(SqliteConnection db, SqliteTransaction tx, RecurringExpense t, DateOnly d)
    {
        bool paid = t.Mode == RecurringMode.Paid;
        long id = db.ExecuteScalar<long>("""
            INSERT INTO expenses (vendor_id, category_id, date, due_date, description, reference, amount, notes, recurring_id, created_at)
            VALUES (@VendorId, @CategoryId, @d, @due, @Description, '', @Amount, '', @Id, @now);
            SELECT last_insert_rowid();
            """, new { t.VendorId, t.CategoryId, d, due = paid ? (DateOnly?)null : d, t.Description, t.Amount, t.Id, now = SqlFormat.NowUtc() }, tx);
        if (paid) ExpenseService.InsertPayment(db, tx, id, t.Amount, t.Method ?? "cash", null, d, "Recurring");
        return new GeneratedExpense(id, t.Description, d, t.Amount, paid);
    }

    /// <summary>On resuming, dates before today are treated as already handled.</summary>
    private DateOnly? Resume(DateOnly? generated)
    {
        DateOnly yesterday = _today().AddDays(-1);
        return generated is { } g && g > yesterday ? g : yesterday;
    }

    private static RecurringExpense ToTemplate(RecurringDraft c) => new()
    {
        VendorId = c.VendorId, CategoryId = c.CategoryId!.Value, Description = c.Description, Amount = c.Amount!.Value,
        Frequency = c.Frequency, StartDate = c.StartDate!.Value, DayOfMonth = c.DayOfMonth!.Value, EndDate = c.EndDate,
        Mode = c.Mode, Method = c.Method, Notes = c.Notes, IsActive = c.IsActive,
    };

    private RecurringDraft Validate(RecurringDraft d, RecurringExpense? current)
    {
        var checks = new Checks();
        _db.Run(db =>
        {
            if (d.VendorId is { } vid)
            {
                bool? active = db.ExecuteScalar<bool?>("SELECT is_active FROM vendors WHERE id = @vid", new { vid });
                if (active is null) checks.Add("vendor_id", "That vendor no longer exists. Choose another.");
                else if (active == false && current?.VendorId != vid) checks.Add("vendor_id", "That vendor is inactive. Make it active again, or choose another.");
            }
            if (d.CategoryId is not { } cid) checks.Add("category_id", Checks.Required);
            else
            {
                bool? active = db.ExecuteScalar<bool?>("SELECT is_active FROM expense_categories WHERE id = @cid", new { cid });
                if (active is null) checks.Add("category_id", "That category no longer exists. Choose another.");
                else if (active == false && current?.CategoryId != cid) checks.Add("category_id", "That category is hidden. Show it again in Categories, or choose another.");
            }
        });
        checks.RequireText("description", d.Description, 500);
        if (d.Amount is not { } amount || double.IsNaN(amount)) checks.Add("amount", Checks.Required);
        else if (!(amount > 0)) checks.Add("amount", "Amount must be greater than zero.");
        else if (PyMath.Round(amount, 2) != amount) checks.Add("amount", "Enter dollars and cents, at most two decimal places.");
        if (!RecurringFrequency.IsValid(d.Frequency)) checks.Add("frequency", "Choose monthly or yearly.");
        if (d.StartDate is null) checks.Add("start_date", Checks.Required);
        if (d.DayOfMonth is not { } day) checks.Add("day_of_month", Checks.Required);
        else if (day is < 1 or > 31) checks.Add("day_of_month", "Enter a day from 1 to 31.");
        if (d.EndDate is { } end && d.StartDate is { } start && end < start) checks.Add("end_date", "The end date cannot be before the start date.");
        if (!RecurringMode.IsValid(d.Mode)) checks.Add("mode", "Choose auto-paid or bill.");
        else if (d.Mode == RecurringMode.Paid && !PaymentMethods.All.Any(m => m.Value == d.Method)) checks.Add("method", "Choose how it is paid.");
        checks.ThrowIfAny();
        return d with
        {
            Description = d.Description.Trim(),
            Method = d.Mode == RecurringMode.Paid ? d.Method : null,
            Notes = d.Notes ?? "",
        };
    }

    private static UserFacingException NotFound() => new("That recurring expense no longer exists. It may have been deleted.");
}
