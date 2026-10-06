using Dapper;
using ProfitDjinn.Core;
using ProfitDjinn.Core.Data;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Rules;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.Tests;

/// <summary>2.7: the P&amp;L drill-down, and the Revenue and Items feature switches.</summary>
public class ProfitDetailTests
{
    private static readonly DateOnly Today = new(2026, 10, 5);

    private static long Cat(Store s, string name) => s.Categories.Active().Single(c => c.Name == name).Id;

    /// <summary>
    /// Every income and expense case the P&amp;L handles: payments, an overpayment then credit applied
    /// elsewhere, an invoice marked paid with no payments, part-paid and unpaid expenses, no vendor,
    /// a cost-of-revenue category, more than eight categories (so the doughnut has "Other"), and
    /// awkward cents.
    /// </summary>
    private static Store DrillStore()
    {
        var s = Fixture.FreshStore(Today);
        long cust = s.Customers.Create(new CustomerDraft("Client", "", "", "", "", "", "", "", "", true)).Id;
        InvoiceLineDraft[] Line(double price) => new[] { new InvoiceLineDraft("Work", 1, price) };
        long a = s.Invoices.Create(new InvoiceDraft(cust, s.Invoices.NextNumber(), new DateOnly(2026, 3, 10), "", "", "", false, Line(100))).Id;
        s.Invoices.RecordPayment(a, 150.10, "cash", null, new DateOnly(2026, 3, 12), null);          // $50.10 becomes credit
        long b = s.Invoices.Create(new InvoiceDraft(cust, s.Invoices.NextNumber(), new DateOnly(2026, 4, 1), "", "", "", false, Line(80.2))).Id;
        s.Invoices.RecordPayment(b, 50.1, "account_credit", null, new DateOnly(2026, 4, 2), null);
        s.Invoices.RecordPayment(b, 30.1, "check", "9", new DateOnly(2026, 4, 3), null);
        long old = s.Invoices.Create(new InvoiceDraft(cust, s.Invoices.NextNumber(), new DateOnly(2026, 2, 1), "", "", "", false, Line(33.33))).Id;
        s.Database.Run(db => db.Execute("UPDATE invoices SET paid = 1, paid_date = '2026-02-20' WHERE id = @old", new { old }));
        s.Invoices.Create(new InvoiceDraft(cust, s.Invoices.NextNumber(), new DateOnly(2025, 11, 1), "", "", "", false, Line(10)));

        long acme = s.Vendors.Create(new VendorDraft("Acme", "", "", "", "mt", "", "", "", null, "", true)).Id;
        s.Categories.ToggleCostOfRevenue(Cat(s, "Supplies"));
        string[] cats = { "Supplies", "Software & Subscriptions", "Travel", "Advertising", "Insurance", "Office Expenses",
            "Utilities", "Meals", "Interest & Bank Fees", "Car & Truck", "Phone & Internet" };
        double cents = 0.1;
        for (int i = 0; i < cats.Length; i++)
        {
            var date = new DateOnly(2026, 3 + i % 4, 5);
            long e = s.Expenses.Create(new ExpenseDraft(i % 3 == 0 ? null : acme, Cat(s, cats[i]), date, null, $"Thing {i}", "", 10 + i + cents, "")).Id;
            if (i % 4 != 3) s.Expenses.RecordPayment(e, i % 2 == 0 ? 10 + i + cents : 5.2, "cash", null, date.AddDays(i), null);
            cents += 0.1;
        }
        return s;
    }

    public static IEnumerable<object[]> Bases() => new[] { new object[] { ProfitBasis.Cash }, new object[] { ProfitBasis.Accrual } };

    [Theory]
    [MemberData(nameof(Bases))]
    public void Every_figure_drills_down_to_exactly_the_number_on_the_page(ProfitBasis basis)
    {
        var s = DrillStore();
        var r = s.Profit.Report(2026, basis);
        Assert.True(r.ShowGross);
        Assert.True(r.Categories.Count > 8);
        ProfitDetail D(ProfitPart p, int? month = null, IReadOnlyList<string>? cats = null, string? vendor = null, int year = 2026) =>
            s.Profit.Detail(basis, p, new ProfitScope(year, month, cats, vendor));
        void Reconciles(ProfitDetail d)
        {
            double sum = d.Part switch
            {
                ProfitPart.Income => d.IncomeTotal,
                ProfitPart.Gross or ProfitPart.Net => d.IncomeTotal - d.ExpenseTotal,
                _ => d.ExpenseTotal,
            };
            Assert.Equal(PyMath.Round(d.Figure, 2), PyMath.Round(sum, 2));
        }

        var tiles = new (ProfitPart, double)[]
        {
            (ProfitPart.Income, r.Income), (ProfitPart.Expenses, r.Expenses), (ProfitPart.CostOfRevenue, r.CostOfRevenue),
            (ProfitPart.Operating, r.OperatingExpenses), (ProfitPart.Gross, r.GrossProfit), (ProfitPart.Net, r.Net),
        };
        foreach (var (part, value) in tiles)
        {
            var d = D(part);
            Assert.Equal(value, d.Figure);
            Reconciles(d);
        }
        foreach (var m in r.Months)
        {
            foreach (var (part, value) in new (ProfitPart, double)[]
            {
                (ProfitPart.Income, m.Income), (ProfitPart.Expenses, m.Expenses), (ProfitPart.CostOfRevenue, m.CostOfRevenue),
                (ProfitPart.Operating, m.OperatingExpenses), (ProfitPart.Gross, m.GrossProfit), (ProfitPart.Net, m.Net),
            })
            {
                var d = D(part, m.Month);
                Assert.Equal(value, d.Figure);
                Reconciles(d);
            }
        }
        foreach (var c in r.Categories)
        {
            var d = D(ProfitPart.Expenses, cats: new[] { c.Name });
            Assert.Equal(c.Amount, d.Figure);
            Reconciles(d);
            Assert.All(d.Expenses, e => Assert.Equal(c.Name, e.Category));
        }
        var other = r.Categories.Skip(7).ToList();
        var od = D(ProfitPart.Expenses, cats: other.Select(c => c.Name).ToList());
        Assert.Equal(PyMath.Sum(other, c => c.Amount), od.Figure);      // what the doughnut's "Other" slice shows
        Reconciles(od);
        foreach (var v in r.Vendors)
        {
            var d = D(ProfitPart.Expenses, vendor: v.Name);
            Assert.Equal(v.Amount, d.Figure);
            Assert.Equal(v.Count, d.Expenses.Count);
            Reconciles(d);
        }
        foreach (var y in r.YearRows)
        {
            Assert.Equal(y.Income, D(ProfitPart.Income, year: y.Year).Figure);
            Assert.Equal(y.Expenses, D(ProfitPart.Expenses, year: y.Year).Figure);
            Assert.Equal(y.Net, D(ProfitPart.Net, year: y.Year).Figure);
        }
    }

    [Fact]
    public void Drill_down_rows_point_at_their_invoice_or_expense_and_cash_income_skips_applied_credit()
    {
        var s = DrillStore();
        var d = s.Profit.Detail(ProfitBasis.Cash, ProfitPart.Net, new ProfitScope(2026));
        var invoiceIds = s.Invoices.List().Select(i => i.Id).ToHashSet();
        var expenseIds = s.Database.Run(db => db.Query<long>("SELECT id FROM expenses")).ToHashSet();
        Assert.All(d.Income, e => Assert.Contains(e.InvoiceId!.Value, invoiceIds));
        Assert.All(d.Expenses, e => Assert.Contains(e.ExpenseId!.Value, expenseIds));
        Assert.DoesNotContain(d.Income, e => e.Description.Contains("Account Credit", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(d.Income, e => e.Description.EndsWith("marked paid") && e.Date == new DateOnly(2026, 2, 20));
        Assert.Equal(PyMath.Round(150.10 + 30.1 + 33.33, 2), PyMath.Round(d.IncomeTotal, 2));
        Assert.StartsWith("Net Profit · 2026", d.Title);
        Assert.Contains("Applying account credit is not counted again", d.Explanation);
        Assert.Equal("Expenses · Acme · 2026", s.Profit.Detail(ProfitBasis.Cash, ProfitPart.Expenses, new ProfitScope(2026, Vendor: "Acme")).Title);
        Assert.Equal("Cost of Revenue · Mar 2026", s.Profit.Detail(ProfitBasis.Accrual, ProfitPart.CostOfRevenue, new ProfitScope(2026, 3)).Title);
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void A_month_breaks_down_by_customer_category_and_vendor_and_each_line_drills_to_its_own_total(ProfitBasis basis)
    {
        var s = DrillStore();
        foreach (var m in s.Profit.Report(2026, basis).Months.Where(m => m.Income != 0 || m.Expenses != 0))
        {
            var month = s.Profit.Detail(basis, ProfitPart.Net, new ProfitScope(2026, m.Month));
            Assert.Equal(m.Net, month.Figure);
            Assert.Equal(PyMath.Round(m.Income, 2), PyMath.Round(PyMath.Sum(month.ByCustomer, g => g.Amount), 2));
            Assert.Equal(PyMath.Round(m.Expenses, 2), PyMath.Round(PyMath.Sum(month.ByCategory, g => g.Amount), 2));
            Assert.Equal(PyMath.Round(m.Expenses, 2), PyMath.Round(PyMath.Sum(month.ByVendor, g => g.Amount), 2));
            foreach (var g in month.ByCustomer)
            {
                var d = s.Profit.Detail(basis, ProfitPart.Income, month.Scope with { Customer = g.Name });
                Assert.Equal(ProfitPart.Income, d.Part);
                Assert.Equal(g.Amount, d.Figure);
                Assert.Equal(g.Count, d.Income.Count);
                Assert.Empty(d.ByCustomer);
            }
            foreach (var g in month.ByCategory)
            {
                var d = s.Profit.Detail(basis, ProfitPart.Expenses, month.Scope with { Categories = new[] { g.Name } });
                Assert.Equal(g.Amount, d.Figure);
                Assert.Equal(g.Count, d.Expenses.Count);
                Assert.Empty(d.ByCategory);
                foreach (var v in d.ByVendor)       // and on again, to a vendor within that category
                    Assert.Equal(v.Amount, s.Profit.Detail(basis, ProfitPart.Expenses, d.Scope with { Vendor = v.Name }).Figure);
            }
            foreach (var g in month.ByVendor)
                Assert.Equal(g.Amount, s.Profit.Detail(basis, ProfitPart.Expenses, month.Scope with { Vendor = g.Name }).Figure);
        }
        // Cost of revenue stays cost of revenue when narrowed to a vendor.
        var cogs = s.Profit.Detail(basis, ProfitPart.CostOfRevenue, new ProfitScope(2026));
        foreach (var v in cogs.ByVendor)
        {
            var d = s.Profit.Detail(basis, ProfitPart.CostOfRevenue, cogs.Scope with { Vendor = v.Name });
            Assert.Equal(v.Amount, d.Figure);
            Assert.All(d.Expenses, e => Assert.True(e.CostOfRevenue));
        }
    }

    [Fact]
    public void Revenue_and_items_are_on_by_default_and_turning_items_off_keeps_them()
    {
        var s = Fixture.FreshStore(Today);
        Assert.True(s.Reports.RevenueEnabled);
        Assert.True(s.Items.Enabled);
        s.Items.Create("Hourly", 50, true);
        s.Settings.Set(SettingKeys.ItemsEnabled, "false");
        s.Settings.Set(SettingKeys.RevenueEnabled, "false");
        Assert.False(s.Items.Enabled);
        Assert.False(s.Reports.RevenueEnabled);
        Assert.Single(s.Items.Active());
    }
}
