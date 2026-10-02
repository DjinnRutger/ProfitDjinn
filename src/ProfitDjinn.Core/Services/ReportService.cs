using Dapper;
using ProfitDjinn.Core.Data;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Rules;

namespace ProfitDjinn.Core.Services;

public sealed record DashboardStats(
    int ActiveCustomers,
    int TotalInvoices,
    int UnpaidInvoices,
    double UnpaidTotal,
    double UnbilledWork,
    int OpenTodos,
    int Year,
    double YearRevenue,
    IReadOnlyList<Invoice> RecentInvoices);

public sealed record MonthRow(int Month, string Name, int Count, double Invoiced, double Collected);

public sealed record YearRow(int Year, double Collected, double? Change, bool IsCurrent);

public sealed record CustomerRevenue(string Name, double Collected, double Share);

/// <summary>The Revenue page for one year (Year set) or all years (Year null).</summary>
public sealed record RevenueReport(
    int? Year,
    int CurrentYear,
    IReadOnlyList<int> Years,
    double TotalRevenue,
    double TotalInvoiced,
    double TotalOutstanding,
    int InvoiceCount,
    int PaidCount,
    int PartialCount,
    double? AverageActiveMonth,
    double? PreviousYearRevenue,
    double? YtdPercent,
    IReadOnlyList<MonthRow> Months,
    IReadOnlyList<YearRow> YearRows,
    IReadOnlyList<CustomerRevenue> Customers);

/// <summary>Dashboard and Revenue figures. A port of 1.x app/blueprints/main.py.</summary>
public sealed class ReportService
{
    private static readonly string[] MonthNames = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

    private readonly Database _db;
    private readonly Func<DateOnly> _today;

    public ReportService(Database db, Func<DateOnly> today)
    {
        _db = db;
        _today = today;
    }

    public DashboardStats Dashboard()
    {
        int year = _today().Year;
        return _db.Run(db =>
        {
            var all = Loader.Invoices(db);
            var unpaid = all.Where(i => i.BalanceDue > 0).ToList();
            double yearRevenue = PyMath.Sum(all.Where(i => i.Date.Year == year && i.AmountPaid > 0), i => i.AmountPaid);

            // "Not billed" means no invoice link, whatever the status says, which also catches
            // lines orphaned by a deleted invoice. SQLite's SUM, as 1.x used.
            double unbilled = db.ExecuteScalar<double>(
                "SELECT COALESCE(SUM(amount), 0.0) FROM work_order_lines WHERE invoice_id IS NULL AND status != 'pending' AND no_charge = 0");
            int todos = db.ExecuteScalar<int>("SELECT COUNT(*) FROM work_order_lines WHERE status = 'pending'");
            int activeCustomers = db.ExecuteScalar<int>("SELECT COUNT(*) FROM customers WHERE is_active = 1");

            var recent = all.OrderByDescending(i => i.Date).ThenByDescending(i => i.Id).Take(5).ToList();
            return new DashboardStats(activeCustomers, all.Count, unpaid.Count, PyMath.Sum(unpaid, i => i.BalanceDue),
                unbilled, todos, year, yearRevenue, recent);
        });
    }

    /// <summary>
    /// The Revenue page. <paramref name="year"/> null means All Years.
    /// Fixed in 2.0: All Years counts every invoice. 1.x left out invoices with nothing paid,
    /// so its Invoiced, Outstanding and invoice count were too low.
    /// </summary>
    public RevenueReport Revenue(int? year)
    {
        int currentYear = _today().Year;
        var all = _db.Run(db => Loader.Invoices(db));

        var years = all.Select(i => i.Date.Year).Distinct().OrderByDescending(y => y).ToList();
        if (!years.Contains(currentYear)) years.Insert(0, currentYear);

        var filtered = year is { } y ? all.Where(i => i.Date.Year == y).ToList() : all;

        // Plain += per month, as 1.x accumulated them.
        var revenue = new double[13];
        var invoiced = new double[13];
        var count = new int[13];
        foreach (var inv in filtered)
        {
            int m = inv.Date.Month;
            revenue[m] += inv.AmountPaid;
            invoiced[m] += inv.Total;
            count[m] += 1;
        }
        var months = Enumerable.Range(1, 12)
            .Select(m => new MonthRow(m, MonthNames[m - 1], count[m], invoiced[m], revenue[m])).ToList();

        double YearTotal(int yr) => PyMath.Sum(all.Where(i => i.Date.Year == yr && i.AmountPaid > 0), i => i.AmountPaid);

        // Year by Year: ascending. Change shows only when this year and the previous row are both above zero.
        var yearRows = new List<YearRow>();
        double? previous = null;
        foreach (int yr in years.OrderBy(v => v))
        {
            double total = YearTotal(yr);
            double? change = previous is > 0 && total > 0 ? total - previous.Value : null;
            yearRows.Add(new YearRow(yr, total, change, yr == currentYear));
            previous = total;
        }

        // By customer name (customers with the same name merge), largest first, ties in first-seen order.
        var byName = new Dictionary<string, double>();
        var order = new List<string>();
        foreach (var inv in filtered.Where(i => i.AmountPaid > 0))
        {
            string name = inv.Customer?.Name ?? "";
            if (!byName.ContainsKey(name)) { byName[name] = 0.0; order.Add(name); }
            byName[name] += inv.AmountPaid;
        }

        double totalRevenue = PyMath.Sum(filtered, i => i.AmountPaid);
        var customers = order.OrderByDescending(n => byName[n])
            .Select(n => new CustomerRevenue(n, byName[n], totalRevenue > 0 ? byName[n] / totalRevenue * 100 : 0))
            .ToList();

        double? average = null, prev = null, ytd = null;
        if (year is { } sel)
        {
            int activeMonths = revenue.Skip(1).Count(v => v > 0);
            average = totalRevenue / (activeMonths == 0 ? 1 : activeMonths);
            prev = YearTotal(sel - 1);
            if (sel == currentYear && prev > 0) ytd = PyMath.Round(totalRevenue / prev.Value * 100, 1);
        }

        return new RevenueReport(year, currentYear, years,
            totalRevenue, PyMath.Sum(filtered, i => i.Total), PyMath.Sum(filtered, i => i.BalanceDue),
            filtered.Count, filtered.Count(i => i.Paid), filtered.Count(i => i.IsPartial),
            average, prev, ytd, months, yearRows, customers);
    }

    /// <summary>
    /// The doughnut's slices: every customer when there are 8 or fewer, otherwise the top 7
    /// plus "Other".
    /// </summary>
    public static IReadOnlyList<CustomerRevenue> DoughnutSlices(IReadOnlyList<CustomerRevenue> customers, double total)
    {
        if (customers.Count <= 8) return customers;
        var top = customers.Take(7).ToList();
        double other = PyMath.JsSum(customers.Skip(7).Select(c => c.Collected));
        top.Add(new CustomerRevenue("Other", other, total > 0 ? other / total * 100 : 0));
        return top;
    }
}
