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
