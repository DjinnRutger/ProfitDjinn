using Dapper;
using ProfitDjinn.Core;
using ProfitDjinn.Core.Data;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Rules;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.Tests;

/// <summary>2.6 Bank Accounts: accounts, transactions, transfers, payouts, reconciliation, links.</summary>
public class BankTests
{
    private static readonly DateOnly Today = new(2026, 10, 4);

    private static Store Open()
    {
        var s = Fixture.FreshStore(Today);
        s.Settings.Set(SettingKeys.BankingEnabled, "true");
        s.Settings.Set(SettingKeys.ExpensesEnabled, "true");
        return s;
    }

    private static long Account(Store s, string name, string kind = AccountKind.Checking, double opening = 0) =>
        s.Banking.CreateAccount(new AccountDraft(name, kind, opening, new DateOnly(2026, 1, 1), "")).Id;

    private static (long Invoice, long Customer) Invoice(Store s, double amount)
    {
        long c = s.Customers.Create(new CustomerDraft("Client", "", "", "", "", "", "", "", "", true)).Id;
        long inv = s.Invoices.Create(new InvoiceDraft(c, s.Invoices.NextNumber(), new DateOnly(2026, 9, 1), "", "", "", false,
            new[] { new InvoiceLineDraft("Hosting", 1, amount) })).Id;
        return (inv, c);
    }

    [Fact]
    public void Starts_off_and_accounts_validate()
    {
        var s = Fixture.FreshStore(Today);
        Assert.False(s.Banking.Enabled);
        var bad = Assert.Throws<ValidationException>(() => s.Banking.CreateAccount(new AccountDraft(" ", "bogus", null, null, "")));
        Assert.Equal(new[] { "kind", "name", "opening_date" }, bad.Fields.Keys.OrderBy(k => k));
    }

    [Fact]
    public void Contributions_draws_fees_and_balances_with_pending_versus_cleared()
    {
        var s = Open();
        long chk = Account(s, "Checking", opening: 100);
        s.Banking.AddTransaction(new TxnDraft(chk, true, TxnKind.OwnerContribution, new DateOnly(2026, 9, 1), "", 500, "", "", Cleared: true));
        s.Banking.AddTransaction(new TxnDraft(chk, false, TxnKind.OwnerDraw, new DateOnly(2026, 9, 5), "Draw", 50, "", "", Cleared: false));
        var a = s.Banking.Account(chk);
        Assert.Equal((550.0, 600.0, 1), (a.Balance, a.ClearedBalance, a.PendingCount));
        var reg = s.Banking.Register(chk);
        Assert.Equal(new[] { 600.0, 550.0 }, reg.Select(r => r.Balance));
        Assert.Equal("Owner contribution", reg[0].Txn.Description);       // a blank description takes the kind

        // None of it is income or expense.
        Assert.Equal((0.0, 0.0), (s.Profit.Report(2026, ProfitBasis.Cash).Income, s.Profit.Report(2026, ProfitBasis.Cash).Expenses));

        s.Banking.ToggleCleared(reg[1].Txn.Id);
        Assert.Equal(550, s.Banking.Account(chk).ClearedBalance);
        Assert.Throws<ValidationException>(() => s.Banking.AddTransaction(new TxnDraft(chk, true, TxnKind.OwnerDraw, Today, "", 5, "", "", false)));  // a draw is not money in
    }

    [Fact]
    public void A_fee_can_also_be_an_expense_reaching_the_pnl_once()
    {
        var s = Open();
        long chk = Account(s, "Checking");
        long fees = s.Categories.Active().Single(c => c.Name == "Interest & Bank Fees").Id;
        var fee = s.Banking.AddTransaction(new TxnDraft(chk, false, TxnKind.Fee, new DateOnly(2026, 9, 30), "Monthly fee", 12, "", "", true, fees));
        var t = s.Banking.Transaction(fee.Id);
        Assert.Equal((TxnKind.Fee, -12.0, TxnStatus.Cleared), (t.Kind, t.Amount, t.Status));
        Assert.Single(s.Database.Run(db => db.Query<long>("SELECT id FROM bank_transactions")));
        Assert.Equal(12, s.Profit.Report(2026, ProfitBasis.Cash).Expenses);
        Assert.Throws<UserFacingException>(() => s.Banking.DeleteTransaction(fee.Id));   // it belongs to the expense
        s.Expenses.Delete(t.ExpenseId!.Value);
        Assert.Empty(s.Banking.Register(chk));
    }

    [Fact]
    public void An_invoice_payment_without_an_account_is_written_exactly_as_before()
    {
        var s = Open();
        var (inv, _) = Invoice(s, 515);
        string before = s.Database.Run(db => db.ExecuteScalar<string>("SELECT group_concat(name) FROM pragma_table_info('payments')"));
        s.Invoices.RecordPayment(inv, 515, "ach", null, new DateOnly(2026, 9, 15), "");
        Assert.Equal(before, s.Database.Run(db => db.ExecuteScalar<string>("SELECT group_concat(name) FROM pragma_table_info('payments')")));
        Assert.Equal(0, s.Database.Run(db => db.ExecuteScalar<int>("SELECT COUNT(*) FROM bank_transactions")));
        Assert.Equal(0, s.Database.Run(db => db.ExecuteScalar<int>("SELECT COUNT(*) FROM payment_accounts")));
        Assert.True(s.Invoices.Get(inv).Paid);
    }

    [Fact]
    public void Payments_deposited_and_paid_from_accounts_are_linked_and_go_when_the_payment_goes()
    {
        var s = Open();
        long stripe = Account(s, "Stripe", AccountKind.Processor);
        long chk = Account(s, "Checking");
        var (inv, cust) = Invoice(s, 515);
        s.Invoices.RecordPayment(inv, 515, "credit_card", null, new DateOnly(2026, 9, 15), "", depositedTo: stripe);
        var row = Assert.Single(s.Banking.Register(stripe)).Txn;
        Assert.Equal((TxnKind.CustomerPayment, 515.0, inv), (row.Kind, row.Amount, row.InvoiceId!.Value));
        Assert.Throws<UserFacingException>(() => s.Banking.DeleteTransaction(row.Id));

        long cat = s.Categories.Active().First().Id;
        long exp = s.Expenses.Create(new ExpenseDraft(null, cat, new DateOnly(2026, 9, 20), null, "AWS", "", 30, "",
            new PaidNow("credit_card", null, null, PaidFrom.Business, chk))).Id;
        Assert.Equal(-30, Assert.Single(s.Banking.Register(chk)).Txn.Amount);
        // An owner-paid payment cannot name a business account.
        long exp2 = s.Expenses.Create(new ExpenseDraft(null, cat, Today, null, "Paper", "", 9, "", new PaidNow("cash", null, null, PaidFrom.Owner, chk))).Id;
        Assert.Single(s.Banking.Register(chk));
        Assert.Null(s.Expenses.Get(exp2).Payments.Single().AccountId);

        s.Invoices.MarkUnpaid(inv);
        Assert.Empty(s.Banking.Register(stripe));
        Assert.Equal(0, s.Database.Run(db => db.ExecuteScalar<int>("SELECT COUNT(*) FROM payment_accounts")));
        s.Expenses.DeletePayment(exp, s.Expenses.Get(exp).Payments.Single().Id);
        Assert.Empty(s.Banking.Register(chk));

        s.Invoices.RecordPayment(inv, 100, "cash", null, Today, "", depositedTo: chk);
        s.Customers.Delete(cust);
        Assert.Empty(s.Banking.Register(chk));
    }

    [Fact]
    public void A_processor_payout_is_a_transfer_with_its_charges_and_fees_bridged()
    {
        var s = Open();
        long stripe = Account(s, "Stripe", AccountKind.Processor);
        long chk = Account(s, "Checking");
        var (inv, _) = Invoice(s, 515);
        s.Invoices.RecordPayment(inv, 515, "credit_card", null, new DateOnly(2026, 10, 1), "", depositedTo: stripe);
        s.Banking.AddTransaction(new TxnDraft(stripe, false, TxnKind.Fee, new DateOnly(2026, 10, 1), "Processing fee", 15.24, "ch_123", "", true));
        s.Banking.AddTransaction(new TxnDraft(stripe, false, TxnKind.Fee, new DateOnly(2026, 10, 2), "Billing fee", 2.16, "", "", true));
        var payout = s.Banking.Transfer(new TransferDraft(stripe, chk, new DateOnly(2026, 10, 4), 497.60, "", "po_9", Cleared: false));

        var bridge = s.Banking.Bridge(payout.Id);
        Assert.Equal(3, bridge.Items.Count);
        Assert.Equal((497.60, 497.60, 0.0), (bridge.Expected, bridge.Paid, bridge.Difference));
        Assert.Equal(0, s.Banking.Account(stripe).Balance);
        var arrived = Assert.Single(s.Banking.Register(chk)).Txn;
        Assert.Equal((497.60, TxnStatus.Pending, "Stripe"), (arrived.Amount, arrived.Status, arrived.OtherAccount));
        Assert.Equal(0, s.Banking.Account(chk).ClearedBalance);              // not arrived yet

        // The transfer is not income: income is still only the invoice payment.
        Assert.Equal(515, s.Profit.Report(2026, ProfitBasis.Cash).Income);

        // A short payout shows a difference; nothing is created to cover it.
        s.Banking.DeleteTransaction(payout.Id);
        Assert.Empty(s.Banking.Register(chk));
        var shortPay = s.Banking.Transfer(new TransferDraft(stripe, chk, new DateOnly(2026, 10, 4), 495.54, "", "", true));
        Assert.Equal(-2.06, s.Banking.Bridge(shortPay.Id).Difference);
        Assert.Equal(4, s.Banking.Register(stripe).Count);
    }

    [Fact]
    public void Reconciling_needs_a_zero_difference_and_then_locks_the_rows()
    {
        var s = Open();
        long chk = Account(s, "Checking", opening: 1000);
        var a = s.Banking.AddTransaction(new TxnDraft(chk, true, TxnKind.Deposit, new DateOnly(2026, 9, 3), "Deposit", 200, "", "", false)).Id;
        var b = s.Banking.AddTransaction(new TxnDraft(chk, false, TxnKind.Withdrawal, new DateOnly(2026, 9, 10), "Rent", 300, "", "", false)).Id;
        var later = s.Banking.AddTransaction(new TxnDraft(chk, false, TxnKind.Withdrawal, new DateOnly(2026, 10, 2), "Later", 5, "", "", false)).Id;

        Assert.Equal(2, s.Banking.Unreconciled(chk, new DateOnly(2026, 9, 30)).Count);
        var off = Assert.Throws<UserFacingException>(() => s.Banking.FinishReconcile(chk, new DateOnly(2026, 9, 30), 900, new[] { a }));
        Assert.Contains("$1200.00", off.Message);
        Assert.Contains("$300.00 more than the statement", off.Message);
        Assert.Equal(0, s.Database.Run(db => db.ExecuteScalar<int>("SELECT COUNT(*) FROM bank_transactions WHERE status = 'reconciled'")));

        s.Banking.FinishReconcile(chk, new DateOnly(2026, 9, 30), 900, new[] { a, b });
        var acct = s.Banking.Account(chk);
        Assert.Equal((new DateOnly(2026, 9, 30), 900.0), (acct.ReconciledThrough!.Value, acct.ReconciledBalance!.Value));
        Assert.Equal(900, s.Banking.ReconciledStart(chk));
        Assert.Throws<UserFacingException>(() => s.Banking.DeleteTransaction(a));
        Assert.Throws<UserFacingException>(() => s.Banking.ToggleCleared(b));
        Assert.Single(s.Banking.Unreconciled(chk, new DateOnly(2026, 10, 31)));
        Assert.Throws<ValidationException>(() => s.Banking.UpdateAccount(chk, new AccountDraft("Checking", AccountKind.Checking, 5, new DateOnly(2026, 1, 1), "")));
        Assert.Equal(later, s.Banking.Unreconciled(chk, new DateOnly(2026, 10, 31)).Single().Id);
    }

    [Fact]
    public void Reimbursing_the_owner_from_an_account_records_the_withdrawal_and_undo_removes_it()
    {
        var s = Open();
        long chk = Account(s, "Checking");
        long cat = s.Categories.Active().First().Id;
        long exp = s.Expenses.Create(new ExpenseDraft(null, cat, Today, null, "Mileage-free item", "", 40, "", new PaidNow("cash", null, null, PaidFrom.Owner))).Id;
        long pay = s.Expenses.Get(exp).Payments.Single().Id;
        s.Expenses.MarkReimbursed(pay, Today, chk);
        var t = Assert.Single(s.Banking.Register(chk)).Txn;
        Assert.Equal((TxnKind.Reimbursement, -40.0), (t.Kind, t.Amount));
        s.Expenses.UndoReimbursed(pay);
        Assert.Empty(s.Banking.Register(chk));
    }

    [Fact]
    public void Accounts_with_history_close_instead_of_deleting()
    {
        var s = Open();
        long chk = Account(s, "Checking");
        long empty = Account(s, "Unused");
        s.Banking.AddTransaction(new TxnDraft(chk, true, TxnKind.Deposit, Today, "x", 1, "", "", true));
        Assert.Throws<UserFacingException>(() => s.Banking.DeleteAccount(chk));
        s.Banking.DeleteAccount(empty);
        s.Banking.ToggleActive(chk);
        Assert.Empty(s.Banking.Accounts());
        Assert.Throws<UserFacingException>(() => s.Banking.AddTransaction(new TxnDraft(chk, true, TxnKind.Deposit, Today, "x", 1, "", "", true)));
    }
}
