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

public sealed record ProfitMonth(int Month, string Name, double Income, double Expenses)
{
    public double Net => PyMath.Round(Income - Expenses, 2);
}

public sealed record CategoryTotal(string Name, double Amount, double Share);

public sealed record VendorTotal(string Name, double Amount, int Count);

public sealed record ProfitYear(int Year, double Income, double Expenses, bool IsCurrent)
{
    public double Net => PyMath.Round(Income - Expenses, 2);
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
    IReadOnlyList<ProfitYear> YearRows)
{
    public double Net => PyMath.Round(Income - Expenses, 2);

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

    /// <summary>One dated amount. Expense entries carry their category and vendor.</summary>
    public sealed record Entry(DateOnly Date, double Amount, string Category, string Vendor, string Description, string Reference);

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
            PyMath.Sum(outYear.Where(e => e.Date.Month == m), e => e.Amount))).ToList();

        double totalOut = PyMath.Sum(outYear, e => e.Amount);
        var categories = outYear.GroupBy(e => e.Category)
            .Select(g => (Name: g.Key, Amount: PyMath.Sum(g, e => e.Amount)))
            .OrderByDescending(c => c.Amount).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(c => new CategoryTotal(c.Name, c.Amount, totalOut > 0 ? c.Amount / totalOut * 100 : 0))
            .ToList();
        var vendors = outYear.GroupBy(e => e.Vendor)
            .Select(g => new VendorTotal(g.Key, PyMath.Sum(g, e => e.Amount), g.Count()))
            .OrderByDescending(v => v.Amount).ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var yearRows = years.OrderBy(y => y).Select(y => new ProfitYear(y,
            PyMath.Sum(income.Where(e => e.Date.Year == y), e => e.Amount),
            PyMath.Sum(expenses.Where(e => e.Date.Year == y), e => e.Amount), y == current)).ToList();

        return new ProfitReport(year, current, basis, years, PyMath.Sum(inYear, e => e.Amount), totalOut,
            months, categories, vendors, yearRows);
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
                income.Add(new Entry(inv.Date, inv.Total, "", who, $"Invoice {inv.InvoiceNumber}", inv.InvoiceNumber));
            else if (inv.Payments.Count > 0)
                income.AddRange(inv.Payments.Select(p => new Entry(p.Date, p.Amount, "", who, $"Payment on {inv.InvoiceNumber} ({p.MethodLabel})", inv.InvoiceNumber)));
            else if (inv.Paid && inv.NetTotal > 0)
                income.Add(new Entry(inv.PaidDate ?? inv.Date, inv.NetTotal, "", who, $"Invoice {inv.InvoiceNumber} marked paid", inv.InvoiceNumber));
        }

        var all = Loader.Expenses(db);
        var expenses = new List<Entry>();
        foreach (var e in all)
        {
            string cat = e.CategoryName.Length > 0 ? e.CategoryName : "Uncategorized";
            string vendor = e.VendorName.Length > 0 ? e.VendorName : "(No vendor)";
            if (basis == ProfitBasis.Accrual)
                expenses.Add(new Entry(e.Date, e.Amount, cat, vendor, e.Description, e.Reference ?? ""));
            else
                expenses.AddRange(e.Payments.Select(p => new Entry(p.Date, p.Amount, cat, vendor, e.Description, e.Reference ?? "")));
        }
        return (income, expenses);
    });

    // ------------------------------------------------------------------ CSV

    /// <summary>The P&amp;L for a year as CSV: months, totals, then expenses by category.</summary>
    public string SummaryCsv(int year, ProfitBasis basis)
    {
        var r = Report(year, basis);
        var csv = new Csv();
        csv.Row($"Profit and Loss {year}", basis == ProfitBasis.Cash ? "Cash basis" : "Accrual basis");
        csv.Row();
        csv.Row("Month", "Income", "Expenses", "Net");
        foreach (var m in r.Months) csv.Row(m.Name, Money(m.Income), Money(m.Expenses), Money(m.Net));
        csv.Row("Total", Money(r.Income), Money(r.Expenses), Money(r.Net));
        csv.Row();
        csv.Row("Expense category", "Amount", "Share %");
        foreach (var c in r.Categories) csv.Row(c.Name, Money(c.Amount), c.Share.ToString("0.0", CultureInfo.InvariantCulture));
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
