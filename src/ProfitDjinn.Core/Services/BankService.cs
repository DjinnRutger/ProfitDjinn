using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using ProfitDjinn.Core.Data;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Rules;

namespace ProfitDjinn.Core.Services;

/// <summary>What the account form submits.</summary>
public sealed record AccountDraft(string Name, string Kind, double? OpeningBalance, DateOnly? OpeningDate, string Notes);

/// <summary>Money In or Money Out typed by hand. <paramref name="Amount"/> is positive; <paramref name="In"/> gives the sign.</summary>
public sealed record TxnDraft(long AccountId, bool In, string Kind, DateOnly? Date, string Description, double? Amount,
    string Reference, string Notes, bool Cleared, long? FeeExpenseCategoryId = null);

public sealed record TransferDraft(long FromAccountId, long ToAccountId, DateOnly? Date, double? Amount, string Description,
    string Reference, bool Cleared);

/// <summary>A processor payout and what it pays out: the charges and fees since the payout before it.</summary>
public sealed record PayoutBridge(BankTransaction Payout, IReadOnlyList<BankTransaction> Items, double Expected, double Paid)
{
    public double Difference => PyMath.Round(Paid - Expected, 2);
}

/// <summary>
/// 2.6. Bank Accounts (off by default): checking, savings, credit card, personal and payment-
/// processor accounts, their transactions, transfers, and reconciliation against a statement.
///
/// The rules that keep it from double counting:
/// - The P&amp;L never reads these tables. Income is still invoices and payments; expenses are
///   still expenses. Owner contributions, draws and transfers are therefore never revenue.
/// - A payment recorded with an account creates one linked row here. The row belongs to the
///   payment: deleting the payment deletes it, and it cannot be edited or deleted on its own.
/// - A bank fee can also be recorded as an expense; it is then one row, linked to that
///   expense's payment, so it reaches the P&amp;L once.
/// - Nothing is ever adjusted automatically. A reconciliation that does not balance cannot be
///   finished; a payout that differs from its charges is shown, not fixed.
/// </summary>
public sealed class BankService
{
    private readonly Database _db;
    private readonly SettingsService _settings;
    private readonly Func<DateOnly> _today;

    public BankService(Database db, SettingsService settings, Func<DateOnly> today)
    {
        _db = db;
        _settings = settings;
        _today = today;
    }

    public bool Enabled => _settings.GetBool(SettingKeys.BankingEnabled);

    // ------------------------------------------------------------------ accounts

    public IReadOnlyList<BankAccount> Accounts(bool includeInactive = false) => _db.Run(db =>
    {
        var accounts = db.Query<BankAccount>("SELECT * FROM bank_accounts ORDER BY is_active DESC, name COLLATE NOCASE").ToList();
        var sums = db.Query<(long AccountId, double Total, double Cleared, int Pending)>("""
            SELECT account_id, CAST(COALESCE(SUM(amount), 0.0) AS REAL),
                   CAST(COALESCE(SUM(CASE WHEN status != 'pending' THEN amount ELSE 0.0 END), 0.0) AS REAL),
                   SUM(CASE WHEN status = 'pending' THEN 1 ELSE 0 END)
            FROM bank_transactions GROUP BY account_id
            """).ToDictionary(r => r.AccountId);
        foreach (var a in accounts) Fill(db, a, sums.GetValueOrDefault(a.Id));
        return accounts.Where(a => includeInactive || a.IsActive).ToList();
    });

    public BankAccount Account(long id) => Accounts(includeInactive: true).FirstOrDefault(a => a.Id == id) ?? throw AccountGone();

    public Created CreateAccount(AccountDraft draft)
    {
        var d = Check(draft);
        long id = _db.Run(db => db.ExecuteScalar<long>("""
            INSERT INTO bank_accounts (name, kind, opening_balance, opening_date, is_active, notes, created_at)
            VALUES (@Name, @Kind, @balance, @date, 1, @Notes, @now);
            SELECT last_insert_rowid();
            """, new { d.Name, d.Kind, balance = d.OpeningBalance ?? 0, date = d.OpeningDate!.Value, d.Notes, now = SqlFormat.NowUtc() }));
        return new Created(id, Notice.Success($"Account '{d.Name}' added."));
    }

    public Notice UpdateAccount(long id, AccountDraft draft)
    {
        var current = Account(id);
        var d = Check(draft);
        if (current.ReconciledThrough is not null && (d.OpeningBalance ?? 0) != current.OpeningBalance)
            throw new ValidationException(new Dictionary<string, string> { ["opening_balance"] = "This account has been reconciled, so its opening balance can no longer change." });
        _db.Run(db => db.Execute("UPDATE bank_accounts SET name = @Name, kind = @Kind, opening_balance = @balance, opening_date = @date, notes = @Notes WHERE id = @id",
            new { d.Name, d.Kind, balance = d.OpeningBalance ?? 0, date = d.OpeningDate!.Value, d.Notes, id }));
        return Notice.Success($"Account '{d.Name}' updated.");
    }

    public Notice ToggleActive(long id)
    {
        var a = Account(id);
        _db.Run(db => db.Execute("UPDATE bank_accounts SET is_active = @on WHERE id = @id", new { on = !a.IsActive, id }));
        return Notice.Info(a.IsActive ? $"Account '{a.Name}' closed. Its history is kept." : $"Account '{a.Name}' reopened.");
    }

    /// <summary>Only an account with no transactions can be deleted; otherwise close it.</summary>
    public Notice DeleteAccount(long id) => _db.InTransaction((db, tx) =>
    {
        string name = db.ExecuteScalar<string?>("SELECT name FROM bank_accounts WHERE id = @id", new { id }, tx) ?? throw AccountGone();
        int n = db.ExecuteScalar<int>("SELECT COUNT(*) FROM bank_transactions WHERE account_id = @id", new { id }, tx);
        if (n > 0) throw new UserFacingException($"'{name}' has {n} {Fmt.Plural(n, "transaction")}. Close the account instead; its history is kept.");
        db.Execute("UPDATE expense_payments SET account_id = NULL WHERE account_id = @id", new { id }, tx);
        db.Execute("DELETE FROM bank_accounts WHERE id = @id", new { id }, tx);
        return Notice.Warning($"Account '{name}' deleted.");
    });

    // ------------------------------------------------------------------ the register

    /// <summary>Every transaction of the account, oldest first, with the running balance after each.</summary>
    public IReadOnlyList<RegisterRow> Register(long accountId) => _db.Run(db =>
    {
        var a = db.QuerySingleOrDefault<BankAccount>("SELECT * FROM bank_accounts WHERE id = @accountId", new { accountId }) ?? throw AccountGone();
        var rows = Transactions(db, null, "t.account_id = @accountId", new { accountId });
        double running = a.OpeningBalance;
        var result = new List<RegisterRow>();
        foreach (var t in rows)
        {
            running = PyMath.Round(running + t.Amount, 2);
            result.Add(new RegisterRow(t, running));
        }
        return result;
    });

    public BankTransaction Transaction(long id) =>
        _db.Run(db => Transactions(db, null, "t.id = @id", new { id }).SingleOrDefault()) ?? throw TxnGone();

    /// <summary>Money In or Money Out typed by hand. A fee can also become an expense (one linked row).</summary>
    public Created AddTransaction(TxnDraft draft) => _db.InTransaction((db, tx) =>
    {
        var (d, account) = CheckTxn(db, tx, draft);
        double signed = d.In ? d.Amount!.Value : -d.Amount!.Value;
        string status = d.Cleared ? TxnStatus.Cleared : TxnStatus.Pending;
        if (!d.In && d.Kind == TxnKind.Fee && d.FeeExpenseCategoryId is { } cat)
        {
            // One row: the expense's payment from this account, labelled as the fee.
            long expenseId = db.ExecuteScalar<long>("""
                INSERT INTO expenses (vendor_id, category_id, date, due_date, description, reference, amount, notes, recurring_id, created_at)
                VALUES (NULL, @cat, @date, NULL, @desc, @ref, @amount, @notes, NULL, @now);
                SELECT last_insert_rowid();
                """, new { cat, date = d.Date!.Value, desc = d.Description, @ref = d.Reference, amount = d.Amount!.Value, notes = d.Notes, now = SqlFormat.NowUtc() }, tx);
            long paymentId = ExpenseService.InsertPayment(db, tx, expenseId, d.Amount!.Value, PaymentMethods.Other, null, d.Date!.Value, "", PaidFrom.Business, account.Id, d.Description);
            long txnId = db.ExecuteScalar<long>("SELECT id FROM bank_transactions WHERE expense_payment_id = @paymentId", new { paymentId }, tx);
            db.Execute("UPDATE bank_transactions SET kind = 'fee', status = @status, reference = @ref, notes = @notes, expense_id = @expenseId WHERE id = @txnId",
                new { status, @ref = d.Reference, notes = d.Notes, expenseId, txnId }, tx);
            return new Created(txnId, Notice.Success($"Fee of ${Fmt.F2(d.Amount!.Value)} recorded in '{account.Name}' and as an expense."));
        }
        long id = Insert(db, tx, account.Id, d.Date!.Value, d.Description, signed, d.Kind, status, null, d.Reference, d.Notes);
        return new Created(id, Notice.Success($"{TxnKind.Label(d.Kind)} of ${Fmt.F2(d.Amount!.Value)} recorded in '{account.Name}'."));
    });

    /// <summary>Edits a row typed by hand. A linked row (a payment's) only takes reference and notes here.</summary>
    public Notice UpdateTransaction(long id, TxnDraft draft) => _db.InTransaction((db, tx) =>
    {
        var t = Transactions(db, tx, "t.id = @id", new { id }).SingleOrDefault() ?? throw TxnGone();
        if (t.IsReconciled) throw new UserFacingException("This transaction is reconciled, so it can no longer change.");
        if (t.TransferId is not null) throw new UserFacingException("Edit a transfer by deleting it and entering it again.");
        if (t.IsLinked || !TxnKind.IsManual(t.Kind))
        {
            db.Execute("UPDATE bank_transactions SET reference = @Reference, notes = @Notes WHERE id = @id", new { draft.Reference, draft.Notes, id }, tx);
            return Notice.Success("Transaction updated. Its amount and date come from the payment; change them there.");
        }
        var (d, _) = CheckTxn(db, tx, draft with { AccountId = t.AccountId });
        db.Execute("""
            UPDATE bank_transactions SET date = @date, description = @Description, amount = @amount, kind = @Kind,
                reference = @Reference, notes = @Notes, status = @status
            WHERE id = @id
            """, new { date = d.Date!.Value, d.Description, amount = d.In ? d.Amount!.Value : -d.Amount!.Value, d.Kind, d.Reference, d.Notes,
                status = d.Cleared ? TxnStatus.Cleared : TxnStatus.Pending, id }, tx);
        return Notice.Success("Transaction updated.");
    });

    /// <summary>Deletes a row typed by hand, or both halves of a transfer. A payment's row goes with its payment.</summary>
    public Notice DeleteTransaction(long id) => _db.InTransaction((db, tx) =>
    {
        var t = Transactions(db, tx, "t.id = @id", new { id }).SingleOrDefault() ?? throw TxnGone();
        if (t.IsLinked)
            throw new UserFacingException(t.InvoicePaymentId is not null
                ? "This is a customer payment. Delete the payment on its invoice; this row goes with it."
                : "This belongs to an expense payment. Delete the payment on the expense; this row goes with it.");
        if (t.IsReconciled) throw new UserFacingException("This transaction is reconciled, so it cannot be deleted.");
        if (t.TransferId is { } pair)
        {
            if (db.ExecuteScalar<bool>("SELECT EXISTS (SELECT 1 FROM bank_transactions WHERE transfer_id = @pair AND status = 'reconciled')", new { pair }, tx))
                throw new UserFacingException("One side of this transfer is reconciled, so it cannot be deleted.");
            db.Execute("DELETE FROM bank_transactions WHERE transfer_id = @pair", new { pair }, tx);
            return Notice.Warning("Transfer deleted from both accounts.");
        }
        db.Execute("DELETE FROM bank_transactions WHERE id = @id", new { id }, tx);
        return Notice.Warning("Transaction deleted.");
    });

    /// <summary>Pending ⇄ Cleared. Reconciled rows are fixed.</summary>
    public Notice ToggleCleared(long id) => _db.InTransaction((db, tx) =>
    {
        string status = db.ExecuteScalar<string?>("SELECT status FROM bank_transactions WHERE id = @id", new { id }, tx) ?? throw TxnGone();
        if (status == TxnStatus.Reconciled) throw new UserFacingException("This transaction is reconciled, so it stays cleared.");
        string next = status == TxnStatus.Pending ? TxnStatus.Cleared : TxnStatus.Pending;
        db.Execute("UPDATE bank_transactions SET status = @next WHERE id = @id", new { next, id }, tx);
        return Notice.Info(next == TxnStatus.Cleared ? "Marked cleared." : "Marked pending.");
    });

    /// <summary>
    /// Money moved between two of your accounts (including a processor payout to the bank).
    /// Two rows sharing a transfer id; neither is income or expense.
    /// </summary>
    public Created Transfer(TransferDraft draft) => _db.InTransaction((db, tx) =>
    {
        var checks = new Checks();
        if (draft.FromAccountId == draft.ToAccountId) checks.Add("to_account", "Choose two different accounts.");
        if (draft.Date is null) checks.Add("date", Checks.Required);
        Amount(checks, draft.Amount);
        checks.ThrowIfAny();
        var from = ActiveAccount(db, tx, draft.FromAccountId);
        var to = ActiveAccount(db, tx, draft.ToAccountId);
        string status = draft.Cleared ? TxnStatus.Cleared : TxnStatus.Pending;
        string desc = string.IsNullOrWhiteSpace(draft.Description)
            ? (from.IsProcessor ? $"Payout from {from.Name}" : "Transfer")
            : draft.Description.Trim();
        double amount = draft.Amount!.Value;
        long outId = Insert(db, tx, from.Id, draft.Date!.Value, $"{desc} to {to.Name}", -amount, TxnKind.Transfer, status, null, draft.Reference, "");
        long inId = Insert(db, tx, to.Id, draft.Date!.Value, $"{desc} from {from.Name}", amount, TxnKind.Transfer, status, null, draft.Reference, "");
        db.Execute("UPDATE bank_transactions SET transfer_id = @outId WHERE id IN (@outId, @inId)", new { outId, inId }, tx);
        return new Created(outId, Notice.Success($"${Fmt.F2(amount)} moved from '{from.Name}' to '{to.Name}'."));
    });

    // ------------------------------------------------------------------ processor payouts

    /// <summary>
    /// For a payout out of a processor account: the charges and fees since the payout before it,
    /// what they add up to, and what was actually paid out. A difference is shown, never fixed.
    /// </summary>
    public PayoutBridge Bridge(long payoutTxnId) => _db.Run(db =>
    {
        var payout = Transactions(db, null, "t.id = @payoutTxnId", new { payoutTxnId }).SingleOrDefault() ?? throw TxnGone();
        if (payout.Kind != TxnKind.Transfer || payout.Amount >= 0)
            throw new UserFacingException("Choose the payout (the transfer out of the processor account).");
        var account = db.QuerySingle<BankAccount>("SELECT * FROM bank_accounts WHERE id = @AccountId", new { payout.AccountId });
        var rows = Transactions(db, null, "t.account_id = @AccountId", new { payout.AccountId });
        int at = rows.FindIndex(r => r.Id == payout.Id);
        // The payout before this one; everything between the two is what this payout pays out.
        int previous = rows.FindLastIndex(Math.Max(0, at - 1), at, r => r.Kind == TxnKind.Transfer && r.Amount < 0);
        if (at == 0) previous = -1;
        var items = rows.Skip(previous + 1).Take(at - previous - 1).ToList();
        // Expected = the account balance just before the payout (left over from before, plus these).
        double expected = PyMath.Round(account.OpeningBalance + PyMath.Sum(rows.Take(at), r => r.Amount), 2);
        return new PayoutBridge(payout, items, expected, PyMath.Round(-payout.Amount, 2));
    });

    // ------------------------------------------------------------------ reconciliation

    /// <summary>The rows a statement through <paramref name="through"/> can include: not yet reconciled, on or before the date.</summary>
    public IReadOnlyList<BankTransaction> Unreconciled(long accountId, DateOnly through) =>
        _db.Run(db => Transactions(db, null, "t.account_id = @accountId AND t.status != 'reconciled' AND t.date <= @through", new { accountId, through }));

    /// <summary>The account's opening balance plus everything already reconciled: where a new statement starts.</summary>
    public double ReconciledStart(long accountId) => _db.Run(db =>
        PyMath.Round(db.ExecuteScalar<double>("SELECT opening_balance FROM bank_accounts WHERE id = @accountId", new { accountId })
            + db.ExecuteScalar<double>("SELECT COALESCE(SUM(amount), 0) FROM bank_transactions WHERE account_id = @accountId AND status = 'reconciled'", new { accountId }), 2));

    /// <summary>
    /// Finishes a reconciliation: the ticked rows must bring the reconciled balance to the
    /// statement's ending balance exactly. They become Reconciled; nothing else changes.
    /// </summary>
    public Notice FinishReconcile(long accountId, DateOnly through, double statementBalance, IReadOnlyCollection<long> ticked) => _db.InTransaction((db, tx) =>
    {
        var a = db.QuerySingleOrDefault<BankAccount>("SELECT * FROM bank_accounts WHERE id = @accountId", new { accountId }, tx) ?? throw AccountGone();
        var rows = Transactions(db, tx, "t.account_id = @accountId AND t.status != 'reconciled' AND t.date <= @through", new { accountId, through })
            .Where(t => ticked.Contains(t.Id)).ToList();
        if (rows.Count != ticked.Count) throw new UserFacingException("Some ticked transactions changed meanwhile. Open the reconciliation again.");
        double start = a.OpeningBalance + db.ExecuteScalar<double>("SELECT COALESCE(SUM(amount), 0) FROM bank_transactions WHERE account_id = @accountId AND status = 'reconciled'", new { accountId }, tx);
        double cleared = PyMath.Round(start + PyMath.Sum(rows, r => r.Amount), 2);
        double diff = PyMath.Round(statementBalance - cleared, 2);
        if (diff != 0)
            throw new UserFacingException($"The ticked transactions come to ${Fmt.F2(cleared)}, ${Fmt.F2(Math.Abs(diff))} {(diff > 0 ? "less" : "more")} than the statement. Nothing was changed.");
        var ids = rows.Select(r => r.Id).ToList();
        if (ids.Count > 0) db.Execute("UPDATE bank_transactions SET status = 'reconciled' WHERE id IN @ids", new { ids }, tx);
        db.Execute("UPDATE bank_accounts SET reconciled_through = @through, reconciled_balance = @statementBalance WHERE id = @accountId",
            new { through, statementBalance, accountId }, tx);
        return Notice.Success($"'{a.Name}' reconciled through {through.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}: {ids.Count} {Fmt.Plural(ids.Count, "transaction")}, balance ${Fmt.F2(statementBalance)}.");
    });

    // ------------------------------------------------------------------ links from payments (called inside their transactions)

    internal static void LinkExpensePayment(SqliteConnection db, SqliteTransaction tx, long accountId, long expensePaymentId, DateOnly date, double amount, string description)
    {
        ActiveAccount(db, tx, accountId);
        long id = Insert(db, tx, accountId, date, description, -amount, TxnKind.ExpensePayment, TxnStatus.Pending, null, "", "");
        db.Execute("UPDATE bank_transactions SET expense_payment_id = @expensePaymentId WHERE id = @id", new { expensePaymentId, id }, tx);
    }

    internal static void LinkInvoicePayment(SqliteConnection db, SqliteTransaction tx, long accountId, long paymentId, DateOnly date, double amount, string description)
    {
        ActiveAccount(db, tx, accountId);
        long id = Insert(db, tx, accountId, date, description, amount, TxnKind.CustomerPayment, TxnStatus.Pending, null, "", "");
        db.Execute("UPDATE bank_transactions SET invoice_payment_id = @paymentId WHERE id = @id", new { paymentId, id }, tx);
        db.Execute("INSERT OR REPLACE INTO payment_accounts (payment_id, account_id, bank_transaction_id) VALUES (@paymentId, @accountId, @id)",
            new { paymentId, accountId, id }, tx);
    }

    internal static void LinkReimbursement(SqliteConnection db, SqliteTransaction tx, long accountId, long expensePaymentId, DateOnly date, double amount, string description)
    {
        ActiveAccount(db, tx, accountId);
        long id = Insert(db, tx, accountId, date, description, -amount, TxnKind.Reimbursement, TxnStatus.Pending, null, "", "");
        db.Execute("UPDATE bank_transactions SET expense_payment_id = @expensePaymentId WHERE id = @id", new { expensePaymentId, id }, tx);
    }

    internal static void UnlinkReimbursement(SqliteConnection db, SqliteTransaction tx, long expensePaymentId) =>
        db.Execute("DELETE FROM bank_transactions WHERE expense_payment_id = @expensePaymentId AND kind = 'reimbursement'", new { expensePaymentId }, tx);

    /// <summary>Before expense payments are deleted: their bank rows go too. <paramref name="where"/> is on expense_payments.</summary>
    internal static void UnlinkExpensePayments(SqliteConnection db, SqliteTransaction tx, string where, object args) =>
        db.Execute($"DELETE FROM bank_transactions WHERE expense_payment_id IN (SELECT id FROM expense_payments WHERE {where})", args, tx);

    internal static void ForgetExpense(SqliteConnection db, SqliteTransaction tx, long expenseId) =>
        db.Execute("DELETE FROM bank_transactions WHERE expense_id = @expenseId", new { expenseId }, tx);

    /// <summary>Before invoice payments are deleted: their bank rows and links go too. <paramref name="where"/> is on payments.</summary>
    internal static void UnlinkInvoicePayments(SqliteConnection db, SqliteTransaction tx, string where, object args)
    {
        db.Execute($"DELETE FROM bank_transactions WHERE invoice_payment_id IN (SELECT id FROM payments WHERE {where})", args, tx);
        db.Execute($"DELETE FROM payment_accounts WHERE payment_id IN (SELECT id FROM payments WHERE {where})", args, tx);
    }

    // ------------------------------------------------------------------ helpers

    private static long Insert(SqliteConnection db, SqliteTransaction tx, long accountId, DateOnly date, string description, double amount,
        string kind, string status, long? transferId, string? reference, string? notes) =>
        db.ExecuteScalar<long>("""
            INSERT INTO bank_transactions (account_id, date, description, amount, kind, status, transfer_id, reference, notes, created_at)
            VALUES (@accountId, @date, @description, @amount, @kind, @status, @transferId, @reference, @notes, @now);
            SELECT last_insert_rowid();
            """, new { accountId, date, description = Trim(description, 300), amount = PyMath.Round(amount, 2), kind, status, transferId,
                reference = Trim(reference, 100), notes = (notes ?? "").Trim(), now = SqlFormat.NowUtc() }, tx);

    private static List<BankTransaction> Transactions(SqliteConnection db, SqliteTransaction? tx, string where, object args)
    {
        var rows = db.Query<BankTransaction>($"""
            SELECT t.*, p.invoice_id AS InvoiceId,
                   (SELECT a.name FROM bank_transactions o JOIN bank_accounts a ON a.id = o.account_id
                    WHERE o.transfer_id = t.transfer_id AND o.id != t.id) AS OtherAccount
            FROM bank_transactions t LEFT JOIN payments p ON p.id = t.invoice_payment_id
            WHERE {where} ORDER BY t.date, t.id
            """, args, tx).ToList();
        foreach (var r in rows.Where(r => r.ExpensePaymentId is not null && r.ExpenseId is null))
            r.ExpenseId = db.ExecuteScalar<long?>("SELECT expense_id FROM expense_payments WHERE id = @ExpensePaymentId", new { r.ExpensePaymentId }, tx);
        return rows;
    }

    private static void Fill(SqliteConnection db, BankAccount a, (long AccountId, double Total, double Cleared, int Pending) sums)
    {
        a.Balance = PyMath.Round(a.OpeningBalance + sums.Total, 2);
        a.ClearedBalance = PyMath.Round(a.OpeningBalance + sums.Cleared, 2);
        a.PendingCount = sums.Pending;
    }

    private static BankAccount ActiveAccount(SqliteConnection db, SqliteTransaction tx, long id)
    {
        var a = db.QuerySingleOrDefault<BankAccount>("SELECT * FROM bank_accounts WHERE id = @id", new { id }, tx) ?? throw AccountGone();
        if (!a.IsActive) throw new UserFacingException($"'{a.Name}' is closed. Reopen it on the Banking page, or choose another account.");
        return a;
    }

    private (TxnDraft, BankAccount) CheckTxn(SqliteConnection db, SqliteTransaction tx, TxnDraft d)
    {
        var checks = new Checks();
        var allowed = d.In ? TxnKind.MoneyIn : TxnKind.MoneyOut;
        if (!allowed.Any(k => k.Value == d.Kind)) checks.Add("kind", "Choose what it is.");
        if (d.Date is null) checks.Add("date", Checks.Required);
        Amount(checks, d.Amount);
        checks.MaxLength("reference", d.Reference, 100);
        if (d.FeeExpenseCategoryId is { } cat && !db.ExecuteScalar<bool>("SELECT EXISTS (SELECT 1 FROM expense_categories WHERE id = @cat)", new { cat }, tx))
            checks.Add("category", "That category no longer exists.");
        checks.ThrowIfAny();
        var account = ActiveAccount(db, tx, d.AccountId);
        string desc = string.IsNullOrWhiteSpace(d.Description) ? TxnKind.Label(d.Kind) : d.Description.Trim();
        return (d with { Description = desc, Reference = (d.Reference ?? "").Trim(), Notes = d.Notes ?? "" }, account);
    }

    private static void Amount(Checks checks, double? amount)
    {
        if (amount is not { } a || double.IsNaN(a)) checks.Add("amount", Checks.Required);
        else if (!(a > 0)) checks.Add("amount", "Amount must be greater than zero.");
        else if (PyMath.Round(a, 2) != a) checks.Add("amount", "Enter dollars and cents, at most two decimal places.");
    }

    private static AccountDraft Check(AccountDraft d)
    {
        var checks = new Checks();
        checks.RequireText("name", d.Name, 100);
        if (!AccountKind.IsValid(d.Kind)) checks.Add("kind", "Choose the kind of account.");
        if (d.OpeningDate is null) checks.Add("opening_date", Checks.Required);
        if (d.OpeningBalance is { } b && (double.IsNaN(b) || PyMath.Round(b, 2) != b)) checks.Add("opening_balance", "Enter dollars and cents.");
        checks.ThrowIfAny();
        return d with { Name = d.Name.Trim(), Notes = d.Notes ?? "" };
    }

    private static string Trim(string? s, int max) { s = (s ?? "").Trim(); return s.Length > max ? s[..max] : s; }

    private static UserFacingException AccountGone() => new("That account no longer exists. It may have been deleted.");
    private static UserFacingException TxnGone() => new("That transaction no longer exists. It may have been deleted.");
}
