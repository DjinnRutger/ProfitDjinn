using Dapper;
using Microsoft.Data.Sqlite;
using ProfitDjinn.Core.Data;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Rules;

namespace ProfitDjinn.Core.Services;

/// <remarks>2.6: OwnerPaid lists expenses with an owner-paid amount not yet paid back.</remarks>
public enum ExpenseFilter { All, Unpaid, Paid, OwnerPaid }

/// <summary>
/// "Already paid" on a new expense: one payment for the full amount. 2.6: who paid it
/// (<see cref="Model.PaidFrom"/>) and, with Bank Accounts, the account it came from.
/// </summary>
public sealed record PaidNow(string Method, DateOnly? Date, string? CheckNumber, string PaidFrom = Model.PaidFrom.Business, long? AccountId = null);

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
    PaidNow? Paid = null,
    double? Miles = null);

/// <summary>The figures on top of the expense list.</summary>
public sealed record ExpenseSummary(double YearTotal, int Year, int UnpaidCount, double UnpaidTotal, int OverdueCount, double OverdueTotal,
    int OwedToOwnerCount = 0, double OwedToOwnerTotal = 0);

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
    private readonly ReceiptStore _receipts;

    public ExpenseService(Database db, SettingsService settings, ReceiptStore receipts, Func<DateOnly> today)
    {
        _db = db;
        _settings = settings;
        _receipts = receipts;
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
                ExpenseFilter.OwnerPaid => e.Payments.Any(p => p.OwedToOwner),
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
        var toOwner = all.SelectMany(e => e.Payments).Where(p => p.OwedToOwner).ToList();
        return new ExpenseSummary(
            PyMath.Sum(all.Where(e => e.Date.Year == today.Year), e => e.Amount), today.Year,
            owed.Count, PyMath.Sum(owed, e => e.BalanceDue),
            overdue.Count, PyMath.Sum(overdue, e => e.BalanceDue),
            all.Count(e => e.Payments.Any(p => p.OwedToOwner)), PyMath.Sum(toOwner, p => p.Amount));
    }

    // ------------------------------------------------------------------ writing

    /// <summary>2.6. The dollars-per-mile setting.</summary>
    public double MileageRate => Setting.AsNumber(_settings.Get(SettingKeys.MileageRate, "0.70"));

    /// <summary>
    /// 2.6. A mileage draft: the amount is miles times the mileage rate, and it is paid in full
    /// with no cash (a deduction, not money spent) on the expense date.
    /// </summary>
    private ExpenseDraft Mileage(ExpenseDraft d, double rate)
    {
        if (d.Miles is not { } miles) return d;
        if (!(miles > 0) || double.IsNaN(miles))
            throw new ValidationException(new Dictionary<string, string> { ["miles"] = "Enter the miles driven, more than zero." });
        if (!(rate > 0))
            throw new ValidationException(new Dictionary<string, string> { ["miles"] = "Set the mileage rate in Settings > Expenses first." });
        return d with { Amount = PyMath.Round(miles * rate, 2), Paid = new PaidNow(PaymentMethods.Other, d.Date, null, PaidFrom.NoCash) };
    }

    public Created Create(ExpenseDraft draft) => _db.InTransaction((db, tx) =>
    {
        double rate = MileageRate;
        draft = Mileage(draft, rate);
        var e = Normalize(db, tx, draft, current: null);
        long id = db.ExecuteScalar<long>("""
            INSERT INTO expenses (vendor_id, category_id, date, due_date, description, reference, amount, notes, recurring_id, created_at)
            VALUES (@VendorId, @CategoryId, @Date, @DueDate, @Description, @Reference, @Amount, @Notes, NULL, @now);
            SELECT last_insert_rowid();
            """, new { e.VendorId, e.CategoryId, e.Date, e.DueDate, e.Description, e.Reference, e.Amount, e.Notes, now = SqlFormat.NowUtc() }, tx);
        if (draft.Miles is { } miles)
            db.Execute("INSERT INTO expense_mileage (expense_id, miles, rate) VALUES (@id, @miles, @rate)", new { id, miles, rate }, tx);
        if (draft.Paid is { } paid)
        {
            InsertPayment(db, tx, id, e.Amount, paid.Method, paid.CheckNumber, paid.Date ?? e.Date, "", paid.PaidFrom, paid.AccountId, e.Description);
            return new Created(id, Notice.Success(draft.Miles is not null
                ? $"Mileage '{e.Description}' saved: {Fmt.F2(draft.Miles.Value)} miles at ${Fmt.F2(rate)} = ${Fmt.F2(e.Amount)}."
                : $"Expense '{e.Description}' saved and marked paid."));
        }
        return new Created(id, Notice.Success($"Expense '{e.Description}' saved."));
    });

    public Notice Update(long id, ExpenseDraft draft) => _db.InTransaction((db, tx) =>
    {
        var current = Loader.Expense(db, id, tx) ?? throw NotFound();
        // 2.6: a mileage expense keeps its rate; new miles recompute the amount and its no-cash payment.
        bool mileage = current.Miles is not null;
        if (mileage && draft.Miles is { } miles)
        {
            double rate = current.MileageRate ?? MileageRate;
            draft = Mileage(draft, rate) with { Paid = null };
            var m = Normalize(db, tx, draft, current);
            db.Execute("""
                UPDATE expenses SET vendor_id = @VendorId, category_id = @CategoryId, date = @Date, due_date = @DueDate,
                    description = @Description, reference = @Reference, amount = @Amount, notes = @Notes
                WHERE id = @id
                """, new { m.VendorId, m.CategoryId, m.Date, m.DueDate, m.Description, m.Reference, m.Amount, m.Notes, id }, tx);
            db.Execute("UPDATE expense_mileage SET miles = @miles WHERE expense_id = @id", new { miles, id }, tx);
            BankService.UnlinkExpensePayments(db, tx, "expense_id = @id", new { id });
            db.Execute("DELETE FROM expense_payments WHERE expense_id = @id", new { id }, tx);
            InsertPayment(db, tx, id, m.Amount, PaymentMethods.Other, null, m.Date, "", PaidFrom.NoCash, null, m.Description);
            return Notice.Success($"Mileage '{m.Description}' updated: {Fmt.F2(miles)} miles = ${Fmt.F2(m.Amount)}.");
        }
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

    /// <summary>
    /// Deletes the expense with its payments and receipts. The receipt files are ProfitDjinn's
    /// own copies, so they go too, once the database change has been saved.
    /// </summary>
    public Notice Delete(long id)
    {
        var (desc, receipts) = _db.InTransaction((db, tx) =>
        {
            string d = db.ExecuteScalar<string?>("SELECT description FROM expenses WHERE id = @id", new { id }, tx) ?? throw NotFound();
            var r = db.Query<ExpenseReceipt>("SELECT * FROM expense_receipts WHERE expense_id = @id", new { id }, tx).ToList();
            BankService.UnlinkExpensePayments(db, tx, "expense_id = @id", new { id });
            BankService.ForgetExpense(db, tx, id);
            db.Execute("DELETE FROM expense_payments WHERE expense_id = @id", new { id }, tx);
            db.Execute("DELETE FROM expense_mileage WHERE expense_id = @id", new { id }, tx);
            db.Execute("DELETE FROM expense_receipts WHERE expense_id = @id", new { id }, tx);
            db.Execute("DELETE FROM expenses WHERE id = @id", new { id }, tx);
            return (d, r);
        });
        foreach (var r in receipts) _receipts.TryDelete(r);
        return Notice.Warning(receipts.Count == 0
            ? $"Expense '{desc}' deleted."
            : $"Expense '{desc}' and {receipts.Count} {Fmt.Plural(receipts.Count, "receipt")} deleted.");
    }

    // ------------------------------------------------------------------ receipts

    /// <summary>Copies the files into the receipts folder and attaches them. All are checked before any is copied.</summary>
    public Notice AddReceipts(long expenseId, IReadOnlyList<string> files)
    {
        if (files.Count == 0) throw new UserFacingException("Choose at least one file.");
        var problems = files.Select(ReceiptStore.Check).Where(p => p is not null).ToList();
        if (problems.Count > 0) throw new UserFacingException(string.Join("\n", problems) + "\n\nNothing was attached.");
        var e = Get(expenseId);
        var saved = new List<SavedReceipt>();
        try
        {
            foreach (string f in files) saved.Add(_receipts.Save(expenseId, f, e.Date));
            _db.InTransaction((db, tx) =>
            {
                foreach (var r in saved)
                    db.Execute("""
                        INSERT INTO expense_receipts (expense_id, file_name, rel_path, folder, size_bytes, created_at)
                        VALUES (@expenseId, @FileName, @RelPath, @Folder, @SizeBytes, @now)
                        """, new { expenseId, r.FileName, r.RelPath, r.Folder, r.SizeBytes, now = SqlFormat.NowUtc() }, tx);
            });
        }
        catch
        {
            foreach (var r in saved) _receipts.TryDelete(new ExpenseReceipt { RelPath = r.RelPath, Folder = r.Folder });
            throw;
        }
        return Notice.Success(saved.Count == 1 ? $"Receipt '{saved[0].FileName}' attached." : $"{saved.Count} receipts attached.");
    }

    public Notice RemoveReceipt(long receiptId)
    {
        var r = Receipt(receiptId);
        _db.Run(db => db.Execute("DELETE FROM expense_receipts WHERE id = @receiptId", new { receiptId }));
        _receipts.TryDelete(r);
        return Notice.Warning($"Receipt '{r.FileName}' removed.");
    }

    /// <summary>The file to open. Throws a message saying where it looked when the file is gone.</summary>
    public string ReceiptPath(long receiptId) => _receipts.PathFor(Receipt(receiptId));

    public int ReceiptCount() => _db.Run(db => db.ExecuteScalar<int>("SELECT COUNT(*) FROM expense_receipts"));

    /// <summary>Moves every receipt file into <paramref name="newFolder"/>, updating each row as its file moves.</summary>
    public MoveResult MoveReceipts(string newFolder)
    {
        string target = Path.GetFullPath(newFolder);
        var all = _db.Run(db => db.Query<ExpenseReceipt>("SELECT * FROM expense_receipts ORDER BY id").ToList());
        return _receipts.MoveAll(all, target, r =>
            _db.Run(db => db.Execute("UPDATE expense_receipts SET folder = @target WHERE id = @Id", new { target, r.Id })));
    }

    private ExpenseReceipt Receipt(long receiptId) =>
        _db.Run(db => db.QuerySingleOrDefault<ExpenseReceipt>("SELECT * FROM expense_receipts WHERE id = @receiptId", new { receiptId }))
        ?? throw new UserFacingException("That receipt is no longer attached. It may have been removed.");

    // ------------------------------------------------------------------ payments

    public Notice RecordPayment(long expenseId, double amount, string method, string? checkNumber, DateOnly? date, string? notes,
        string paidFrom = PaidFrom.Business, long? accountId = null)
    {
        if (!(amount > 0)) throw new UserFacingException("Payment amount must be greater than zero.");
        if (PyMath.Round(amount, 2) != amount) throw new UserFacingException("Enter the amount in dollars and cents, at most two decimal places.");
        return _db.InTransaction((db, tx) =>
        {
            var e = Loader.Expense(db, expenseId, tx) ?? throw NotFound();
            if (e.BalanceDue <= 0) throw new UserFacingException("This expense is already paid in full.");
            if (PyMath.Round(amount - e.BalanceDue, 2) > 0)
                throw new UserFacingException($"That is more than the ${Fmt.F2(e.BalanceDue)} balance due on this expense.");
            InsertPayment(db, tx, expenseId, amount, method, checkNumber, date ?? _today(), notes, paidFrom, accountId, e.Description);
            double left = PyMath.Round(e.BalanceDue - amount, 2);
            return left <= 0
                ? Notice.Success($"Payment of ${Fmt.F2(amount)} recorded. Expense paid in full.")
                : Notice.Info($"Partial payment of ${Fmt.F2(amount)} recorded. Balance remaining: ${Fmt.F2(left)}.");
        });
    }

    public Notice DeletePayment(long expenseId, long paymentId)
    {
        return _db.InTransaction((db, tx) =>
        {
            // The bank rows first: they are found through the payment.
            BankService.UnlinkExpensePayments(db, tx, "id = @paymentId AND expense_id = @expenseId", new { paymentId, expenseId });
            int n = db.Execute("DELETE FROM expense_payments WHERE id = @paymentId AND expense_id = @expenseId", new { paymentId, expenseId }, tx);
            if (n == 0) throw new UserFacingException("That payment is not on this expense. It may already have been deleted.");
            return Notice.Warning("Payment deleted.");
        });
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Inserts one expense payment. 2.6: <paramref name="paidFrom"/> says who funded it; only a
    /// business payment can name a bank account, which (with Bank Accounts) also records the
    /// money leaving that account.
    /// </summary>
    internal static long InsertPayment(SqliteConnection db, SqliteTransaction tx, long expenseId, double amount,
        string method, string? checkNumber, DateOnly date, string? notes,
        string paidFrom = PaidFrom.Business, long? accountId = null, string? description = null)
    {
        method = string.IsNullOrEmpty(method) ? "cash" : method;
        if (!PaymentMethods.All.Any(m => m.Value == method))
            throw new UserFacingException($"'{method}' is not a payment method ProfitDjinn knows.");
        if (!PaidFrom.IsValid(paidFrom)) throw new UserFacingException("Choose who paid: the business, personal funds, or no cash.");
        if (paidFrom != PaidFrom.Business) accountId = null;
        long id = db.ExecuteScalar<long>("""
            INSERT INTO expense_payments (expense_id, amount, method, check_number, date, notes, created_at, paid_from, account_id, reimbursed_on)
            VALUES (@expenseId, @amount, @method, @check, @date, @notes, @now, @paidFrom, @accountId, NULL);
            SELECT last_insert_rowid();
            """, new
        {
            expenseId, amount, method,
            check = method == PaymentMethods.Check ? (checkNumber ?? "").Trim() : "",
            date, notes = (notes ?? "").Trim(), now = SqlFormat.NowUtc(), paidFrom, accountId,
        }, tx);
        if (accountId is { } account)
            BankService.LinkExpensePayment(db, tx, account, id, date, amount, description ?? "Expense payment");
        return id;
    }

    // ------------------------------------------------------------------ 2.6 owner-paid

    /// <summary>
    /// Records that an owner-paid amount was paid back to the owner. With a bank account, the
    /// payback is also recorded as money leaving that account (an owner reimbursement).
    /// </summary>
    public Notice MarkReimbursed(long paymentId, DateOnly? date, long? fromAccountId = null) => _db.InTransaction((db, tx) =>
    {
        var p = db.QuerySingleOrDefault<ExpensePayment>("SELECT * FROM expense_payments WHERE id = @paymentId", new { paymentId }, tx)
            ?? throw new UserFacingException("That payment no longer exists.");
        if (p.PaidFrom != PaidFrom.Owner) throw new UserFacingException("Only an amount paid from personal funds can be paid back.");
        if (p.ReimbursedOn is not null) throw new UserFacingException("That amount is already marked as paid back.");
        DateOnly when = date ?? _today();
        db.Execute("UPDATE expense_payments SET reimbursed_on = @when WHERE id = @paymentId", new { when, paymentId }, tx);
        string desc = db.ExecuteScalar<string?>("SELECT description FROM expenses WHERE id = @ExpenseId", new { p.ExpenseId }, tx) ?? "expense";
        if (fromAccountId is { } account)
            BankService.LinkReimbursement(db, tx, account, paymentId, when, p.Amount, $"Paid back to owner: {desc}");
        return Notice.Success($"${Fmt.F2(p.Amount)} for '{desc}' marked as paid back to you.");
    });

    /// <summary>Undoes <see cref="MarkReimbursed"/>, removing any bank withdrawal it recorded.</summary>
    public Notice UndoReimbursed(long paymentId) => _db.InTransaction((db, tx) =>
    {
        int n = db.Execute("UPDATE expense_payments SET reimbursed_on = NULL WHERE id = @paymentId AND paid_from = 'owner'", new { paymentId }, tx);
        if (n == 0) throw new UserFacingException("That payment no longer exists.");
        BankService.UnlinkReimbursement(db, tx, paymentId);
        return Notice.Info("Marked as not paid back yet.");
    });

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
        if (d.Paid is { } p2 && !PaidFrom.IsValid(p2.PaidFrom)) checks.Add("paid_from", "Choose who paid.");
        checks.ThrowIfAny();

        return new Clean(d.VendorId, d.CategoryId!.Value, d.Date!.Value, d.DueDate, d.Description.Trim(),
            (d.Reference ?? "").Trim(), d.Amount!.Value, d.Notes ?? "");
    }

    private static UserFacingException NotFound() => new("That expense no longer exists. It may have been deleted.");
}
