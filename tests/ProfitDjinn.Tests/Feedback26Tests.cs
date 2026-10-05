using Dapper;
using ProfitDjinn.Core;
using ProfitDjinn.Core.Data;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Pdf;
using ProfitDjinn.Core.Rules;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.Tests;

/// <summary>2.6 expense feedback: cost of revenue, MRR, who paid, mileage.</summary>
public class Feedback26Tests
{
    private static readonly DateOnly Today = new(2026, 10, 4);

    private static Store Open()
    {
        var s = Fixture.FreshStore(Today);
        s.Settings.Set(SettingKeys.ExpensesEnabled, "true");
        return s;
    }

    private static long Category(Store s, string name) => s.Categories.Active().Single(c => c.Name == name).Id;

    private static long Expense(Store s, string category, double amount, DateOnly date, PaidNow? paid = null) =>
        s.Expenses.Create(new ExpenseDraft(null, Category(s, category), date, null, $"{category} {amount}", "", amount, "", paid)).Id;

    private static void Income(Store s, double amount, DateOnly date)
    {
        long c = s.Customers.Create(new CustomerDraft("Client " + amount, "", "", "", "", "", "", "", "", true)).Id;
        long inv = s.Invoices.Create(new InvoiceDraft(c, s.Invoices.NextNumber(), date, "", "", "", false, new[] { new InvoiceLineDraft("Work", 1, amount) })).Id;
        s.Invoices.RecordPayment(inv, amount, "cash", null, date, null);
    }

    // ------------------------------------------------------------------ cost of revenue

    [Fact]
    public void Gross_profit_appears_only_once_a_category_counts_as_cost_of_revenue()
    {
        var s = Open();
        Income(s, 1000, new DateOnly(2026, 9, 10));
        Expense(s, "Software & Subscriptions", 100, new DateOnly(2026, 9, 12), new PaidNow("credit_card", null, null));
        Expense(s, "Advertising", 50, new DateOnly(2026, 9, 15), new PaidNow("cash", null, null));

        var before = s.Profit.Report(2026, ProfitBasis.Cash);
        Assert.False(before.ShowGross);
        Assert.DoesNotContain("Gross profit", s.Profit.SummaryCsv(2026, ProfitBasis.Cash));

        s.Categories.ToggleCostOfRevenue(Category(s, "Software & Subscriptions"));
        var r = s.Profit.Report(2026, ProfitBasis.Cash);
        Assert.True(r.ShowGross);
        Assert.Equal(100, r.CostOfRevenue);
        Assert.Equal(900, r.GrossProfit);
        Assert.Equal(90.0, r.GrossMargin);
        Assert.Equal(50, r.OperatingExpenses);
        Assert.Equal(850, r.Net);                                 // net is unchanged by the classification
        var sep = r.Months[8];
        Assert.Equal((100.0, 900.0, 50.0), (sep.CostOfRevenue, sep.GrossProfit, sep.OperatingExpenses));
        Assert.True(r.Categories.Single(c => c.Name == "Software & Subscriptions").CostOfRevenue);
        string csv = s.Profit.SummaryCsv(2026, ProfitBasis.Cash);
        Assert.Contains("Month,Income,Cost of revenue,Gross profit,Operating expenses,Net", csv);
        Assert.Contains("Software & Subscriptions,100.00,66.7,Cost of revenue", csv);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(ProfitPdf.Render(r, s.Settings.Company(), Today), 0, 4));

        s.Categories.ToggleCostOfRevenue(Category(s, "Software & Subscriptions"));
        Assert.False(s.Profit.Report(2026, ProfitBasis.Cash).ShowGross);
    }

    [Fact]
    public void Accrual_basis_classifies_unpaid_cost_of_revenue_too()
    {
        var s = Open();
        s.Categories.ToggleCostOfRevenue(Category(s, "Software & Subscriptions"));
        Expense(s, "Software & Subscriptions", 40, new DateOnly(2026, 3, 1));
        var r = s.Profit.Report(2026, ProfitBasis.Accrual);
        Assert.Equal(40, r.CostOfRevenue);
        Assert.Equal(0, s.Profit.Report(2026, ProfitBasis.Cash).CostOfRevenue);
    }

    // ------------------------------------------------------------------ MRR / ARR

    [Fact]
    public void Recurring_revenue_counts_active_schedules_with_yearly_ones_as_a_twelfth()
    {
        DateOnly day = new(2026, 10, 4);
        var s = new Store(Fixture.TempPaths(), () => day);
        long a = s.Customers.Create(new CustomerDraft("A", "", "", "", "", "", "", "", "", true)).Id;
        long b = s.Customers.Create(new CustomerDraft("B", "", "", "", "", "", "", "", "", true)).Id;
        RecurringInvoiceDraft D(long c, string interval, double amount, DateOnly? end = null) =>
            new(c, interval, new DateOnly(2026, 11, 1), 1, end, "", "", "", true, new[] { new InvoiceLineDraft("Plan", 1, amount) });
        s.RecurringInvoices.Create(D(a, BillingInterval.Month, 515));
        s.RecurringInvoices.Create(D(a, BillingInterval.Year, 120));         // 10 a month
        long paused = s.RecurringInvoices.Create(D(b, BillingInterval.Month, 99)).Id;
        s.RecurringInvoices.ToggleActive(paused);
        var r = s.RecurringInvoices.Revenue();
        Assert.Equal(525, r.Monthly);
        Assert.Equal(6300, r.Yearly);
        Assert.Equal(1, r.ActiveClients);
        Assert.Equal(2, r.Schedules);
        Assert.Empty(s.Invoices.List());                                     // read only
    }

    // ------------------------------------------------------------------ who paid

    [Fact]
    public void Owner_paid_amounts_are_owed_back_until_marked_reimbursed()
    {
        var s = Open();
        long a = Expense(s, "Supplies", 80, new DateOnly(2026, 9, 1), new PaidNow("credit_card", null, null, PaidFrom.Owner));
        long b = Expense(s, "Supplies", 20, new DateOnly(2026, 9, 2));
        s.Expenses.RecordPayment(b, 20, "cash", null, new DateOnly(2026, 9, 3), null, PaidFrom.Owner);
        Expense(s, "Advertising", 15, new DateOnly(2026, 9, 4), new PaidNow("cash", null, null));   // business

        var sum = s.Expenses.Summary();
        Assert.Equal((2, 100.0), (sum.OwedToOwnerCount, sum.OwedToOwnerTotal));
        Assert.Equal(2, s.Expenses.List(ExpenseFilter.OwnerPaid).Count);

        long pay = s.Expenses.Get(a).Payments.Single().Id;
        Assert.Contains("paid back", s.Expenses.MarkReimbursed(pay, new DateOnly(2026, 9, 30)).Message);
        Assert.Throws<UserFacingException>(() => s.Expenses.MarkReimbursed(pay, null));
        Assert.Equal(20, s.Expenses.Summary().OwedToOwnerTotal);
        s.Expenses.UndoReimbursed(pay);
        Assert.Equal(100, s.Expenses.Summary().OwedToOwnerTotal);

        // Who paid never changes what the expense costs on the P&L.
        Assert.Equal(115, s.Profit.Report(2026, ProfitBasis.Cash).Expenses);
        var business = s.Expenses.List().Single(e => e.Category!.Name == "Advertising").Payments.Single();
        Assert.Throws<UserFacingException>(() => s.Expenses.MarkReimbursed(business.Id, null));
    }

    // ------------------------------------------------------------------ mileage

    [Fact]
    public void Mileage_is_miles_times_the_rate_paid_with_no_cash_and_recomputes_on_edit()
    {
        var s = Open();
        s.Settings.Set(SettingKeys.MileageRate, "0.70");
        long car = Category(s, "Car & Truck");
        var made = s.Expenses.Create(new ExpenseDraft(null, car, new DateOnly(2026, 10, 1), null, "Client visits", "", null, "", Miles: 120));
        Assert.Contains("120.00 miles at $0.70 = $84.00", made.Notice.Message);
        var e = s.Expenses.Get(made.Id);
        Assert.Equal((84.0, 120.0, 0.70), (e.Amount, e.Miles!.Value, e.MileageRate!.Value));
        var p = Assert.Single(e.Payments);
        Assert.Equal((PaidFrom.NoCash, 84.0, "No cash"), (p.PaidFrom, p.Amount, p.MethodLabel));
        Assert.Equal(InvoiceStatus.Paid, e.Status);

        s.Settings.Set(SettingKeys.MileageRate, "0.75");                      // the saved rate stays with the trip
        s.Expenses.Update(made.Id, new ExpenseDraft(null, car, new DateOnly(2026, 10, 1), null, "Client visits", "", null, "", Miles: 10));
        e = s.Expenses.Get(made.Id);
        Assert.Equal(7, e.Amount);
        Assert.Equal(7, Assert.Single(e.Payments).Amount);
        Assert.Throws<ValidationException>(() => s.Expenses.Create(new ExpenseDraft(null, car, Today, null, "x", "", null, "", Miles: 0)));

        s.Expenses.Delete(made.Id);
        Assert.Equal(0, s.Database.Run(db => db.ExecuteScalar<int>("SELECT COUNT(*) FROM expense_mileage")));
    }

    [Fact]
    public void New_settings_rows_have_their_defaults()
    {
        var s = Fixture.FreshStore(Today);
        Assert.Equal("0.70", s.Settings.Get(SettingKeys.MileageRate));
        Assert.False(s.Settings.GetBool(SettingKeys.BankingEnabled));
        s.Database.Run(db =>
        {
            foreach (string t in new[] { "expense_mileage", "bank_accounts", "bank_transactions", "payment_accounts" })
                Assert.True(db.ExecuteScalar<bool>("SELECT EXISTS (SELECT 1 FROM sqlite_master WHERE type='table' AND name=@t)", new { t }), t);
        });
    }
}
