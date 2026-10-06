using System.Globalization;
using System.Text;
using Dapper;
using ProfitDjinn.Core.Data;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Rules;

namespace ProfitDjinn.Core.Services;

/// <summary>
/// Cash: money in and out on the day it moved (payments received, payments made).
/// Accrual: income on the invoice date, expenses on the expense date, paid or not.
/// </summary>
public enum ProfitBasis { Cash, Accrual }

/// <remarks>2.6: <paramref name="CostOfRevenue"/> is the part of Expenses in cost-of-revenue categories.</remarks>
public sealed record ProfitMonth(int Month, string Name, double Income, double Expenses, double CostOfRevenue = 0)
{
    public double Net => PyMath.Round(Income - Expenses, 2);
    public double GrossProfit => PyMath.Round(Income - CostOfRevenue, 2);
    public double OperatingExpenses => PyMath.Round(Expenses - CostOfRevenue, 2);
}

public sealed record CategoryTotal(string Name, double Amount, double Share, bool CostOfRevenue = false);

public sealed record VendorTotal(string Name, double Amount, int Count);

public sealed record ProfitYear(int Year, double Income, double Expenses, bool IsCurrent)
{
    public double Net => PyMath.Round(Income - Expenses, 2);
}

/// <summary>2.7. Which P&amp;L figure a drill-down explains.</summary>
public enum ProfitPart { Income, Expenses, CostOfRevenue, Operating, Gross, Net }

/// <summary>
/// 2.7. Where a drill-down looks: a year, optionally one month, some expense categories (one,
/// or the doughnut's "Other" group) or one vendor. Categories and vendor narrow expenses only.
/// </summary>
public sealed record ProfitScope(int Year, int? Month = null, IReadOnlyList<string>? Categories = null, string? Vendor = null);

/// <summary>
/// 2.7. A P&amp;L figure and the entries behind it. <see cref="Figure"/> is read from the report,
/// so it is exactly the number that was clicked; the entry lists are its breakdown.
/// </summary>
public sealed record ProfitDetail(
    ProfitPart Part,
    ProfitScope Scope,
    ProfitBasis Basis,
    string Title,
    string Explanation,
    IReadOnlyList<ProfitService.Entry> Income,
    IReadOnlyList<ProfitService.Entry> Expenses,
    double IncomeTotal,
    double ExpenseTotal,
    double Figure,
    bool ShowGross)
{
    public bool HasIncome => Part is ProfitPart.Income or ProfitPart.Gross or ProfitPart.Net;
    public bool HasExpenses => Part != ProfitPart.Income;
}

/// <summary>The Profit &amp; Loss page for one year.</summary>
public sealed record ProfitReport(
    int Year,
    int CurrentYear,
    ProfitBasis Basis,
    IReadOnlyList<int> Years,
    double Income,
    double Expenses,
    IReadOnlyList<ProfitMonth> Months,
    IReadOnlyList<CategoryTotal> Categories,
    IReadOnlyList<VendorTotal> Vendors,
    IReadOnlyList<ProfitYear> YearRows,
    double CostOfRevenue = 0,
    bool ShowGross = false)
{
    public double Net => PyMath.Round(Income - Expenses, 2);

    /// <summary>2.6. Income less cost of revenue.</summary>
    public double GrossProfit => PyMath.Round(Income - CostOfRevenue, 2);

    /// <summary>2.6. Gross profit as a share of income, or null with no income.</summary>
    public double? GrossMargin => Income > 0 ? PyMath.Round(GrossProfit / Income * 100, 1) : null;

    /// <summary>2.6. Expenses that are not cost of revenue.</summary>
    public double OperatingExpenses => PyMath.Round(Expenses - CostOfRevenue, 2);

    /// <summary>Net as a share of income, or null with no income.</summary>
    public double? Margin => Income > 0 ? PyMath.Round(Net / Income * 100, 1) : null;

    public double? PreviousNet => YearRows.FirstOrDefault(y => y.Year == Year - 1)?.Net;
}

/// <summary>
/// 2.2 phase 2: profit and loss. Income comes from the invoices, expenses from the Expenses
/// feature. Every figure is a sum of dated entries, so a month, a year and the CSV always
/// agree with each other.
///
/// Cash income is every payment received, on its date, including overpayments (money that
/// came in). Applying account credit is not new money, so it is not income again. An
/// invoice marked paid before payments were tracked (paid, with no payment rows) counts its
/// net total on its paid date, or its invoice date when that is missing.
/// Accrual income is each invoice's total on the invoice date.
/// </summary>
public sealed class ProfitService
{
    private static readonly string[] MonthNames = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

    private readonly Database _db;
    private readonly Func<DateOnly> _today;

    public ProfitService(Database db, Func<DateOnly> today)
    {
        _db = db;
        _today = today;
    }

    /// <summary>
    /// One dated amount. Expense entries carry their category and vendor; income entries carry
    /// the customer in <see cref="Vendor"/>. 2.7: the invoice or expense it came from.
    /// </summary>
    public sealed record Entry(DateOnly Date, double Amount, string Category, string Vendor, string Description, string Reference, bool CostOfRevenue = false,
        long? InvoiceId = null, long? ExpenseId = null);

    public ProfitReport Report(int year, ProfitBasis basis)
    {
        int current = _today().Year;
        var (income, expenses) = Entries(basis);

        var years = income.Select(e => e.Date.Year).Concat(expenses.Select(e => e.Date.Year)).Append(current)
            .Distinct().OrderByDescending(y => y).ToList();
        if (!years.Contains(year)) years.Add(year);
        years = years.OrderByDescending(y => y).ToList();

        var inYear = income.Where(e => e.Date.Year == year).ToList();
        var outYear = expenses.Where(e => e.Date.Year == year).ToList();

        var months = Enumerable.Range(1, 12).Select(m => new ProfitMonth(m, MonthNames[m - 1],
            PyMath.Sum(inYear.Where(e => e.Date.Month == m), e => e.Amount),
            PyMath.Sum(outYear.Where(e => e.Date.Month == m), e => e.Amount),
            PyMath.Sum(outYear.Where(e => e.Date.Month == m && e.CostOfRevenue), e => e.Amount))).ToList();

        double totalOut = PyMath.Sum(outYear, e => e.Amount);
        var categories = outYear.GroupBy(e => e.Category)
            .Select(g => (Name: g.Key, Amount: PyMath.Sum(g, e => e.Amount), Cogs: g.Any(e => e.CostOfRevenue)))
            .OrderByDescending(c => c.Amount).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(c => new CategoryTotal(c.Name, c.Amount, totalOut > 0 ? c.Amount / totalOut * 100 : 0, c.Cogs))
            .ToList();
        var vendors = outYear.GroupBy(e => e.Vendor)
            .Select(g => new VendorTotal(g.Key, PyMath.Sum(g, e => e.Amount), g.Count()))
            .OrderByDescending(v => v.Amount).ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var yearRows = years.OrderBy(y => y).Select(y => new ProfitYear(y,
            PyMath.Sum(income.Where(e => e.Date.Year == y), e => e.Amount),
            PyMath.Sum(expenses.Where(e => e.Date.Year == y), e => e.Amount), y == current)).ToList();

        // Gross profit only shows once some category is marked cost of revenue, so reports for
        // people who never use it look exactly as before.
        bool showGross = _db.Run(db => db.ExecuteScalar<bool>("SELECT EXISTS (SELECT 1 FROM expense_categories WHERE cost_of_revenue = 1)"));
        return new ProfitReport(year, current, basis, years, PyMath.Sum(inYear, e => e.Amount), totalOut,
            months, categories, vendors, yearRows, PyMath.Sum(outYear.Where(e => e.CostOfRevenue), e => e.Amount), showGross);
    }

    /// <summary>This year's net profit, cash basis, for the dashboard tile.</summary>
    public double YearNet(int year)
    {
        var (income, expenses) = Entries(ProfitBasis.Cash);
        return PyMath.Round(PyMath.Sum(income.Where(e => e.Date.Year == year), e => e.Amount)
            - PyMath.Sum(expenses.Where(e => e.Date.Year == year), e => e.Amount), 2);
    }

    /// <summary>Every income and expense entry, on the basis asked for.</summary>
    public (List<Entry> Income, List<Entry> Expenses) Entries(ProfitBasis basis) => _db.Run(db =>
    {
        var invoices = Loader.Invoices(db);
        var income = new List<Entry>();
        foreach (var inv in invoices)
        {
            string who = inv.Customer?.Name ?? "";
            if (basis == ProfitBasis.Accrual)
                income.Add(new Entry(inv.Date, inv.Total, "", who, $"Invoice {inv.InvoiceNumber}", inv.InvoiceNumber, InvoiceId: inv.Id));
            else if (inv.Payments.Count > 0)
                income.AddRange(inv.Payments.Select(p => new Entry(p.Date, p.Amount, "", who, $"Payment on {inv.InvoiceNumber} ({p.MethodLabel})", inv.InvoiceNumber, InvoiceId: inv.Id)));
            else if (inv.Paid && inv.NetTotal > 0)
                income.Add(new Entry(inv.PaidDate ?? inv.Date, inv.NetTotal, "", who, $"Invoice {inv.InvoiceNumber} marked paid", inv.InvoiceNumber, InvoiceId: inv.Id));
        }

        var all = Loader.Expenses(db);
        var expenses = new List<Entry>();
        foreach (var e in all)
        {
            string cat = e.CategoryName.Length > 0 ? e.CategoryName : "Uncategorized";
            string vendor = e.VendorName.Length > 0 ? e.VendorName : "(No vendor)";
            bool cogs = e.Category?.CostOfRevenue == true;
            if (basis == ProfitBasis.Accrual)
                expenses.Add(new Entry(e.Date, e.Amount, cat, vendor, e.Description, e.Reference ?? "", cogs, ExpenseId: e.Id));
            else
                expenses.AddRange(e.Payments.Select(p => new Entry(p.Date, p.Amount, cat, vendor, e.Description, e.Reference ?? "", cogs, ExpenseId: e.Id)));
        }
        return (income, expenses);
    });

    // ------------------------------------------------------------------ drill-down (2.7)

    /// <summary>
    /// The entries behind one P&amp;L figure. The figure comes from <see cref="Report"/> (a tile,
    /// a month, a category, a vendor), never re-added here, so it always matches the page.
    /// </summary>
    public ProfitDetail Detail(ProfitBasis basis, ProfitPart part, ProfitScope scope)
    {
        var r = Report(scope.Year, basis);
        var (income, expenses) = Entries(basis);
        bool InScope(Entry e) => e.Date.Year == scope.Year && (scope.Month is null || e.Date.Month == scope.Month);
        bool narrowed = scope.Categories is not null || scope.Vendor is not null;
        if (narrowed) part = ProfitPart.Expenses;

        var inc = part is ProfitPart.Income or ProfitPart.Gross or ProfitPart.Net
            ? income.Where(InScope).OrderBy(e => e.Date).ThenBy(e => e.Description, StringComparer.OrdinalIgnoreCase).ToList()
            : new List<Entry>();
        var exp = part == ProfitPart.Income ? new List<Entry>() : expenses.Where(InScope)
            .Where(e => part switch
            {
                ProfitPart.CostOfRevenue or ProfitPart.Gross => e.CostOfRevenue,
                ProfitPart.Operating => !e.CostOfRevenue,
                _ => true,
            })
            .Where(e => scope.Categories is null || scope.Categories.Contains(e.Category))
            .Where(e => scope.Vendor is null || e.Vendor == scope.Vendor)
            .OrderBy(e => e.Date).ThenBy(e => e.Vendor, StringComparer.OrdinalIgnoreCase).ToList();

        double figure;
        if (scope.Vendor is { } vendor) figure = r.Vendors.FirstOrDefault(v => v.Name == vendor)?.Amount ?? 0;
        else if (scope.Categories is { } cats) figure = PyMath.Sum(r.Categories.Where(c => cats.Contains(c.Name)), c => c.Amount);
        else if (scope.Month is { } m)
        {
            var mo = r.Months[m - 1];
            figure = part switch
            {
                ProfitPart.Income => mo.Income,
                ProfitPart.Expenses => mo.Expenses,
                ProfitPart.CostOfRevenue => mo.CostOfRevenue,
                ProfitPart.Operating => mo.OperatingExpenses,
                ProfitPart.Gross => mo.GrossProfit,
                _ => mo.Net,
            };
        }
        else figure = part switch
        {
            ProfitPart.Income => r.Income,
            ProfitPart.Expenses => r.Expenses,
            ProfitPart.CostOfRevenue => r.CostOfRevenue,
            ProfitPart.Operating => r.OperatingExpenses,
            ProfitPart.Gross => r.GrossProfit,
            _ => r.Net,
        };

        string period = scope.Month is { } mm ? $"{MonthNames[mm - 1]} {scope.Year}" : scope.Year.ToString(CultureInfo.InvariantCulture);
        string what = part switch
        {
            ProfitPart.Income => "Income",
            ProfitPart.Expenses => "Expenses",
            ProfitPart.CostOfRevenue => "Cost of Revenue",
            ProfitPart.Operating => "Operating Expenses",
            ProfitPart.Gross => "Gross Profit",
            _ => figure < 0 ? "Net Loss" : "Net Profit",
        };
        string? narrow = scope.Vendor ?? (scope.Categories is { Count: 1 } one ? one[0] : scope.Categories is not null ? "Other categories" : null);
        string title = narrow is null ? $"{what} · {period}" : $"{what} · {narrow} · {period}";

        return new ProfitDetail(part, scope, basis, title, Explain(basis, part, scope, period),
            inc, exp, PyMath.Sum(inc, e => e.Amount), PyMath.Sum(exp, e => e.Amount), figure, r.ShowGross);
    }

    private static string Explain(ProfitBasis basis, ProfitPart part, ProfitScope scope, string period)
    {
        bool cash = basis == ProfitBasis.Cash;
        string income = cash
            ? $"Income is the payments received in {period}, on the day each came in. An invoice marked paid without payment records counts its total on the day it was marked paid. Applying account credit is not counted again."
            : $"Income is each invoice's total on its invoice date in {period}, paid or not.";
        string expenses = cash
            ? $"Expenses are the payments made in {period}, on the day each was paid."
            : $"Expenses are each expense's amount on its expense date in {period}, paid or not.";
        const string cogs = "Cost of revenue counts only categories marked Cost of revenue (Expenses > Categories).";
        string narrow = scope.Vendor is { } v ? $" Only expenses from {v}."
            : scope.Categories is { Count: 1 } one ? $" Only the {one[0]} category."
            : scope.Categories is { } many ? $" Only the categories grouped as Other: {string.Join(", ", many)}." : "";
        return part switch
        {
            ProfitPart.Income => income,
            ProfitPart.Expenses => expenses + narrow,
            ProfitPart.CostOfRevenue => expenses + " " + cogs,
            ProfitPart.Operating => expenses + " Operating expenses are every category not marked Cost of revenue.",
            ProfitPart.Gross => "Gross profit is income minus cost of revenue. " + income + " " + cogs,
            _ => "Net is income minus expenses. " + income + " " + expenses,
        };
    }

    // ------------------------------------------------------------------ CSV

    /// <summary>The P&amp;L for a year as CSV: months, totals, then expenses by category.</summary>
    public string SummaryCsv(int year, ProfitBasis basis)
    {
        var r = Report(year, basis);
        var csv = new Csv();
        csv.Row($"Profit and Loss {year}", basis == ProfitBasis.Cash ? "Cash basis" : "Accrual basis");
        csv.Row();
        if (r.ShowGross)
        {
            csv.Row("Month", "Income", "Cost of revenue", "Gross profit", "Operating expenses", "Net");
            foreach (var m in r.Months) csv.Row(m.Name, Money(m.Income), Money(m.CostOfRevenue), Money(m.GrossProfit), Money(m.OperatingExpenses), Money(m.Net));
            csv.Row("Total", Money(r.Income), Money(r.CostOfRevenue), Money(r.GrossProfit), Money(r.OperatingExpenses), Money(r.Net));
        }
        else
        {
            csv.Row("Month", "Income", "Expenses", "Net");
            foreach (var m in r.Months) csv.Row(m.Name, Money(m.Income), Money(m.Expenses), Money(m.Net));
            csv.Row("Total", Money(r.Income), Money(r.Expenses), Money(r.Net));
        }
        csv.Row();
        if (r.ShowGross)
        {
            csv.Row("Expense category", "Amount", "Share %", "Class");
            foreach (var c in r.Categories) csv.Row(c.Name, Money(c.Amount), c.Share.ToString("0.0", CultureInfo.InvariantCulture), c.CostOfRevenue ? "Cost of revenue" : "Operating");
        }
        else
        {
            csv.Row("Expense category", "Amount", "Share %");
            foreach (var c in r.Categories) csv.Row(c.Name, Money(c.Amount), c.Share.ToString("0.0", CultureInfo.InvariantCulture));
        }
        return csv.ToString();
    }

    /// <summary>Every expense entry of the year as CSV, for an accountant or a spreadsheet.</summary>
    public string ExpensesCsv(int year, ProfitBasis basis)
    {
        var (_, expenses) = Entries(basis);
        var csv = new Csv();
        csv.Row(basis == ProfitBasis.Cash ? "Payment date" : "Expense date", "Vendor", "Category", "Description", "Reference", "Amount");
        foreach (var e in expenses.Where(e => e.Date.Year == year).OrderBy(e => e.Date).ThenBy(e => e.Vendor, StringComparer.OrdinalIgnoreCase))
            csv.Row(e.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), e.Vendor, e.Category, e.Description, e.Reference, Money(e.Amount));
        return csv.ToString();
    }

    private static string Money(double x) => PyMath.Round(x, 2).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>RFC 4180 CSV: fields with a comma, quote or line break are quoted; quotes doubled.</summary>
    private sealed class Csv
    {
        private readonly StringBuilder _sb = new();

        public void Row(params string[] fields)
        {
            _sb.Append(string.Join(",", fields.Select(Field)));
            _sb.Append("\r\n");
        }

        private static string Field(string f)
        {
            f ??= "";
            // A leading = + - @ would run as a formula in Excel; prefix it so it stays text.
            if (f.Length > 0 && "=+@".Contains(f[0])) f = "'" + f;
            return f.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + f.Replace("\"", "\"\"") + "\"" : f;
        }

        public override string ToString() => _sb.ToString();
    }
}
