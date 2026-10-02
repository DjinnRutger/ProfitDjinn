using Dapper;
using Microsoft.Data.Sqlite;
using ProfitDjinn.Core.Data;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Rules;

namespace ProfitDjinn.Core.Services;

public enum ExpenseFilter { All, Unpaid, Paid }

/// <summary>"Already paid" on a new expense: one payment for the full amount.</summary>
public sealed record PaidNow(string Method, DateOnly? Date, string? CheckNumber);

/// <summary>What the expense form submits.</summary>
public sealed record ExpenseDraft(
    long? VendorId,
    long? CategoryId,
    DateOnly? Date,
    DateOnly? DueDate,
    string Description,
    string Reference,
    double? Amount,
    string Notes,
    PaidNow? Paid = null);

/// <summary>The figures on top of the expense list.</summary>
public sealed record ExpenseSummary(double YearTotal, int Year, int UnpaidCount, double UnpaidTotal, int OverdueCount, double OverdueTotal);

/// <summary>
/// 2.2. Expenses and their payments. Payments work like invoice payments: any number, each
/// with a date and method, and the status follows from them. Paying more than the balance is
/// refused (a vendor has no account credit).
/// </summary>
public sealed class ExpenseService
{
    private readonly Database _db;
    private readonly SettingsService _settings;
    private readonly Func<DateOnly> _today;

    public ExpenseService(Database db, SettingsService settings, Func<DateOnly> today)
    {
        _db = db;
        _settings = settings;
        _today = today;
    }

    /// <summary>The Settings switch. Off by default.</summary>
    public bool Enabled => _settings.GetBool(SettingKeys.ExpensesEnabled);

    // ------------------------------------------------------------------ reading

    /// <summary>
    /// The expense list, newest first. Search matches the description, reference or vendor,
    /// ignoring case. Unpaid includes partly paid expenses.
    /// </summary>
    public IReadOnlyList<Expense> List(ExpenseFilter filter = ExpenseFilter.All, string? search = null, long? categoryId = null)
    {
        string q = (search ?? "").Trim();
        return _db.Run(db => Loader.Expenses(db))
            .Where(e => filter switch
            {
                ExpenseFilter.Paid => e.Status == InvoiceStatus.Paid,
                ExpenseFilter.Unpaid => e.Status != InvoiceStatus.Paid,
                _ => true,
            })
            .Where(e => categoryId is null || e.CategoryId == categoryId)
            .Where(e => q.Length == 0
                || e.Description.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (e.Reference ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                || e.VendorName.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.Date)
            .ThenByDescending(e => e.Id)
            .ToList();
    }

    public Expense Get(long id) => _db.Run(db => Loader.Expense(db, id)) ?? throw NotFound();

    /// <summary>This year's spending (by expense date), and what is still owed and overdue.</summary>
    public ExpenseSummary Summary()
    {
        DateOnly today = _today();
        var all = _db.Run(db => Loader.Expenses(db, withVendors: false));
        var owed = all.Where(e => e.BalanceDue > 0).ToList();
        var overdue = owed.Where(e => e.IsOverdue(today)).ToList();
        return new ExpenseSummary(
            PyMath.Sum(all.Where(e => e.Date.Year == today.Year), e => e.Amount), today.Year,
            owed.Count, PyMath.Sum(owed, e => e.BalanceDue),
            overdue.Count, PyMath.Sum(overdue, e => e.BalanceDue));
    }

    // ------------------------------------------------------------------ writing

    public Created Create(ExpenseDraft draft) => _db.InTransaction((db, tx) =>
    {
        var e = Normalize(db, tx, draft, current: null);
        long id = db.ExecuteScalar<long>("""
            INSERT INTO expenses (vendor_id, category_id, date, due_date, description, reference, amount, notes, recurring_id, created_at)
            VALUES (@VendorId, @CategoryId, @Date, @DueDate, @Description, @Reference, @Amount, @Notes, NULL, @now);
            SELECT last_insert_rowid();
            """, new { e.VendorId, e.CategoryId, e.Date, e.DueDate, e.Description, e.Reference, e.Amount, e.Notes, now = SqlFormat.NowUtc() }, tx);
        if (draft.Paid is { } paid)
        {
            InsertPayment(db, tx, id, e.Amount, paid.Method, paid.CheckNumber, paid.Date ?? e.Date, "");
            return new Created(id, Notice.Success($"Expense '{e.Description}' saved and marked paid."));
        }
        return new Created(id, Notice.Success($"Expense '{e.Description}' saved."));
    });

    public Notice Update(long id, ExpenseDraft draft) => _db.InTransaction((db, tx) =>
    {
        var current = Loader.Expense(db, id, tx) ?? throw NotFound();
        var e = Normalize(db, tx, draft with { Paid = null }, current);
        if (PyMath.Round(current.AmountPaid - e.Amount, 2) > 0)
            throw new ValidationException(new Dictionary<string, string>
            {
                ["amount"] = $"Payments on this expense already total ${Fmt.F2(current.AmountPaid)}. Delete a payment first, or enter at least that much.",
            });
        db.Execute("""
            UPDATE expenses SET vendor_id = @VendorId, category_id = @CategoryId, date = @Date, due_date = @DueDate,
                description = @Description, reference = @Reference, amount = @Amount, notes = @Notes
            WHERE id = @id
            """, new { e.VendorId, e.CategoryId, e.Date, e.DueDate, e.Description, e.Reference, e.Amount, e.Notes, id }, tx);
        return Notice.Success($"Expense '{e.Description}' updated.");
    });

    /// <summary>Deletes the expense with its payments and receipt records.</summary>
    public Notice Delete(long id) => _db.InTransaction((db, tx) =>
    {
        string desc = db.ExecuteScalar<string?>("SELECT description FROM expenses WHERE id = @id", new { id }, tx) ?? throw NotFound();
        db.Execute("DELETE FROM expense_payments WHERE expense_id = @id", new { id }, tx);
        db.Execute("DELETE FROM expense_receipts WHERE expense_id = @id", new { id }, tx);
        db.Execute("DELETE FROM expenses WHERE id = @id", new { id }, tx);
        return Notice.Warning($"Expense '{desc}' deleted.");
    });

    // ------------------------------------------------------------------ payments

    public Notice RecordPayment(long expenseId, double amount, string method, string? checkNumber, DateOnly? date, string? notes)
    {
        if (!(amount > 0)) throw new UserFacingException("Payment amount must be greater than zero.");
        if (PyMath.Round(amount, 2) != amount) throw new UserFacingException("Enter the amount in dollars and cents, at most two decimal places.");
        return _db.InTransaction((db, tx) =>
        {
            var e = Loader.Expense(db, expenseId, tx) ?? throw NotFound();
            if (e.BalanceDue <= 0) throw new UserFacingException("This expense is already paid in full.");
            if (PyMath.Round(amount - e.BalanceDue, 2) > 0)
                throw new UserFacingException($"That is more than the ${Fmt.F2(e.BalanceDue)} balance due on this expense.");
            InsertPayment(db, tx, expenseId, amount, method, checkNumber, date ?? _today(), notes);
            double left = PyMath.Round(e.BalanceDue - amount, 2);
            return left <= 0
                ? Notice.Success($"Payment of ${Fmt.F2(amount)} recorded. Expense paid in full.")
                : Notice.Info($"Partial payment of ${Fmt.F2(amount)} recorded. Balance remaining: ${Fmt.F2(left)}.");
        });
    }

    public Notice DeletePayment(long expenseId, long paymentId)
    {
        int n = _db.Run(db => db.Execute("DELETE FROM expense_payments WHERE id = @paymentId AND expense_id = @expenseId", new { paymentId, expenseId }));
        if (n == 0) throw new UserFacingException("That payment is not on this expense. It may already have been deleted.");
        return Notice.Warning("Payment deleted.");
    }

    // ------------------------------------------------------------------ helpers

    internal static void InsertPayment(SqliteConnection db, SqliteTransaction tx, long expenseId, double amount,
        string method, string? checkNumber, DateOnly date, string? notes)
    {
        method = string.IsNullOrEmpty(method) ? "cash" : method;
        if (!PaymentMethods.All.Any(m => m.Value == method))
            throw new UserFacingException($"'{method}' is not a payment method ProfitDjinn knows.");
        db.Execute("""
            INSERT INTO expense_payments (expense_id, amount, method, check_number, date, notes, created_at)
            VALUES (@expenseId, @amount, @method, @check, @date, @notes, @now)
            """, new
        {
            expenseId, amount, method,
            check = method == PaymentMethods.Check ? (checkNumber ?? "").Trim() : "",
            date, notes = (notes ?? "").Trim(), now = SqlFormat.NowUtc(),
        }, tx);
    }

    private sealed record Clean(long? VendorId, long CategoryId, DateOnly Date, DateOnly? DueDate, string Description, string Reference, double Amount, string Notes);

    /// <summary>
    /// Field rules. The vendor and category must exist and be in use; an expense that already
    /// has an inactive vendor or a hidden category may keep it.
    /// </summary>
    private static Clean Normalize(SqliteConnection db, SqliteTransaction tx, ExpenseDraft d, Expense? current)
    {
        var checks = new Checks();
        if (d.VendorId is { } vid)
        {
            bool? active = db.ExecuteScalar<bool?>("SELECT is_active FROM vendors WHERE id = @vid", new { vid }, tx);
            if (active is null) checks.Add("vendor_id", "That vendor no longer exists. Choose another.");
            else if (active == false && current?.VendorId != vid) checks.Add("vendor_id", "That vendor is inactive. Make it active again, or choose another.");
        }
        if (d.CategoryId is not { } cid) checks.Add("category_id", Checks.Required);
        else
        {
            bool? active = db.ExecuteScalar<bool?>("SELECT is_active FROM expense_categories WHERE id = @cid", new { cid }, tx);
            if (active is null) checks.Add("category_id", "That category no longer exists. Choose another.");
            else if (active == false && current?.CategoryId != cid) checks.Add("category_id", "That category is hidden. Show it again in Categories, or choose another.");
        }
        if (d.Date is null) checks.Add("date", Checks.Required);
        checks.RequireText("description", d.Description, 500);
        checks.MaxLength("reference", d.Reference, 100);
        if (d.Amount is not { } amount || double.IsNaN(amount)) checks.Add("amount", Checks.Required);
        else if (!(amount > 0)) checks.Add("amount", "Amount must be greater than zero.");
        else if (PyMath.Round(amount, 2) != amount) checks.Add("amount", "Enter dollars and cents, at most two decimal places.");
        if (d.DueDate is { } due && d.Date is { } date && due < date) checks.Add("due_date", "The due date cannot be before the expense date.");
        if (d.Paid is { } paid && !PaymentMethods.All.Any(m => m.Value == paid.Method)) checks.Add("paid_method", "Choose how it was paid.");
        checks.ThrowIfAny();

        return new Clean(d.VendorId, d.CategoryId!.Value, d.Date!.Value, d.DueDate, d.Description.Trim(),
            (d.Reference ?? "").Trim(), d.Amount!.Value, d.Notes ?? "");
    }

    private static UserFacingException NotFound() => new("That expense no longer exists. It may have been deleted.");
}
