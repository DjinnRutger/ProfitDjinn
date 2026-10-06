using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ProfitDjinn.App.Controls;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.App.Shell;
using ProfitDjinn.Core.Data;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.App.Pages;

/// <summary>The home page: five stat tiles and the five newest invoices (1.x main/dashboard.html).</summary>
public sealed class DashboardPage : AppPage
{
    public override string NavKey => "dashboard";
    public override IReadOnlyList<Crumb> Crumbs => Array.Empty<Crumb>();

    public DashboardPage(MainWindow shell) : base(shell)
    {
        var stats = Store.Reports.Dashboard();
        string company = Store.Settings.Get(SettingKeys.CompanyName);
        string welcome = company.Length > 0 && company != "Your Name" ? $"Welcome back, {company}!" : "Welcome back!";

        var tiles = new UniformGrid { Columns = 5, Margin = new Thickness(-8, 0, -8, 24) };
        // 2.4: recurring invoices due in the next 30 days (replaced the Customers count).
        var (dueCount, dueTotal) = Store.RecurringInvoices.DueWithin(30);
        var upcoming = Tile(dueCount.ToString(), "Next 30 Days", "calendar-event", "primary",
            dueCount > 0 ? Ui.Badge(Ui.Money(dueTotal), "secondary") : null, Routes.Invoices(shell));
        upcoming.Muted = dueCount == 0;
        tiles.Children.Add(upcoming);
        tiles.Children.Add(Tile(stats.TotalInvoices.ToString(), "Total Invoices", "receipt", "info", null, Routes.Invoices(shell)));

        var outstanding = Tile(Ui.Money(stats.UnpaidTotal), "Outstanding", "exclamation-circle-fill", "warning",
            stats.UnpaidInvoices > 0 ? Ui.Badge(stats.UnpaidInvoices.ToString(), "warningdark") : null,
            Routes.Invoices(shell, InvoiceFilter.Unpaid));
        outstanding.Muted = stats.UnpaidInvoices == 0;
        if (stats.UnpaidInvoices > 0) outstanding.Accent = "warning";
        tiles.Children.Add(outstanding);

        var unbilled = Tile(Ui.Money(stats.UnbilledWork), "Unbilled Work", "clipboard-check", "danger",
            stats.OpenTodos > 0 ? Ui.Badge($"{stats.OpenTodos} to-do", "secondary") : null, Routes.WorkOrders(shell));
        unbilled.Muted = stats.UnbilledWork <= 0;
        if (stats.UnbilledWork > 0) unbilled.Accent = "danger";
        if (Store.WorkOrders.Enabled) tiles.Children.Add(unbilled);

        // 2.7: with the Revenue page off the figure stays, but the tile opens nothing.
        var revenue = Tile(Ui.Money(stats.YearRevenue), $"{stats.Year} Revenue", "graph-up-arrow", "success", null, Store.Reports.RevenueEnabled ? Routes.Revenue(shell) : null);
        revenue.Accent = "bottom";
        tiles.Children.Add(revenue);

        // 2.2: with Expenses on, this year's net profit (cash basis) opens the Profit & Loss page.
        if (Store.Expenses.Enabled)
        {
            double net = Store.Profit.YearNet(stats.Year);
            var profit = Tile((net < 0 ? "-" : "") + Ui.Money(Math.Abs(net)), net < 0 ? $"{stats.Year} Net Loss" : $"{stats.Year} Net Profit", "bar-chart-line", net < 0 ? "danger" : "primary", null,
                Routes.Profit(shell, stats.Year));
            profit.Accent = "bottom";
            tiles.Children.Add(profit);
        }
        // Up to five tiles fit one row; six do not without breaking the labels: two rows of three.
        tiles.Columns = tiles.Children.Count <= 5 ? tiles.Children.Count : 3;
        if (tiles.Children.Count > 5)
        {
            foreach (var t in tiles.Children.OfType<StatTile>()) t.Margin = new Thickness(8, 0, 8, 16);
            tiles.Margin = new Thickness(-8, 0, -8, 8);
        }

        var page = new StackPanel();
        page.Children.Add(Ui.PageHeader("Dashboard", welcome));
        page.Children.Add(tiles);

        if (stats.RecentInvoices.Count > 0)
        {
            var columns = new List<Column<Invoice>>
            {
                new("Invoice #", Ui.Auto, i => Ui.Link(i.InvoiceNumber, () => shell.Navigate(Routes.Invoice(shell, i.Id)), mono: true)),
                new("Customer", Ui.Star(), i => Ui.Link(i.Customer?.Name ?? "", () => shell.Navigate(Routes.Customer(shell, i.CustomerId)), bold: false)),
                new("Date", Ui.Auto, i => Ui.Muted(Ui.Date(i.Date), 13.6)),
                new("Amount", Ui.Auto, i => Ui.Text(Ui.Money(i.Total), "Money"), HorizontalAlignment.Right),
                new("Status", Ui.Auto, i => Ui.Badge(i.StatusLabel, InvoiceBadge(i)), HorizontalAlignment.Center),
            };
            var viewAll = Ui.Button("View All", "Btn.OutlineSecondary", null, () => shell.Navigate(Routes.Invoices(shell)), small: true);
            page.Children.Add(Ui.Card(Table.Build(columns, stats.RecentInvoices), "Recent Invoices", "receipt", headerRight: viewAll, bodyPadding: new Thickness(0)));
        }
        Content = page;
    }

    private StatTile Tile(string value, string label, string glyph, string tone, object? badge, Func<AppPage>? open)
    {
        var t = new StatTile { Value = value, Label = label, Glyph = glyph, Tone = tone, Badge = badge, Margin = new Thickness(8, 0, 8, 0) };
        if (open is not null) t.Click += (_, _) => Shell.Navigate(open);
        else { t.Focusable = false; t.Cursor = System.Windows.Input.Cursors.Arrow; }
        return t;
    }

    public static string InvoiceBadge(Invoice i) => i.Status switch
    {
        InvoiceStatus.Paid => "success",
        InvoiceStatus.Partial => "info",
        _ => "warning",
    };
}
