using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ProfitDjinn.App.Controls;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.App.Shell;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.App.Pages;

/// <summary>Revenue for a year or all years: tiles, two charts, breakdown tables (1.x main/revenue.html).</summary>
public sealed class RevenuePage : AppPage
{
    public override string NavKey => "revenue";
    public override IReadOnlyList<Crumb> Crumbs => new[] { new Crumb("Revenue") };

    public RevenuePage(MainWindow shell, int? year, bool all) : base(shell)
    {
        int current = DateTime.Today.Year;
        int? selected = all ? null : year ?? current;
        var r = Store.Reports.Revenue(selected);

        // ---- header with year buttons
        var years = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        years.Children.Add(Ui.Button("All Years", selected is null ? "Btn.Secondary" : "Btn.OutlineSecondary", null,
            () => Shell.Navigate(Routes.Revenue(Shell, null, all: true)), small: true).Margin(0, 0, 4, 4));
        foreach (int y in r.Years)
        {
            string style = selected == y ? "Btn.Primary" : y == current ? "Btn.OutlinePrimary" : "Btn.OutlineSecondary";
            var content = Ui.Row(0, new TextBlock { Text = y.ToString(CultureInfo.InvariantCulture) });
            if (y == current) content.Children.Add(new Border
            {
                Background = System.Windows.Media.Brushes.White, CornerRadius = new CornerRadius(6), Padding = new Thickness(5, 1, 5, 1), Margin = new Thickness(5, 0, 0, 0),
                Child = new TextBlock { Text = "YTD", FontSize = 10.4, FontWeight = FontWeights.SemiBold }.WithResource(TextBlock.ForegroundProperty, "BsPrimary"),
            });
            int captured = y;
            years.Children.Add(Ui.Button(null, style, null, () => Shell.Navigate(Routes.Revenue(Shell, captured)), small: true).Also(b => b.Content = content).Margin(0, 0, 4, 4));
        }
        var page = new StackPanel();
        page.Children.Add(Ui.PageHeaderWithGlyph("graph-up-arrow", "Revenue",
            selected is null ? "All-time revenue overview" : $"{selected} fiscal year overview", null, years).WithGlyphBrush("Success"));

        // ---- tiles
        var tiles = new UniformGrid { Columns = 4, Margin = new Thickness(-8, 0, -8, 24) };
        StatTile Tile(string value, string label, string glyph, string tone, object? badge = null)
        {
            var t = new StatTile { Value = value, Label = label, Glyph = glyph, Tone = tone, Accent = "left-" + tone, Badge = badge, Margin = new Thickness(8, 0, 8, 16), Focusable = false, Cursor = System.Windows.Input.Cursors.Arrow };
            tiles.Children.Add(t);
            return t;
        }
        Tile(Ui.Money(r.TotalRevenue), "Revenue Collected", "cash-stack", "success");
        var badges = Ui.Row(4);
        if (r.PaidCount > 0) badges.Children.Add(Ui.Badge($"{r.PaidCount} paid", "success75"));
        if (r.PartialCount > 0) badges.Children.Add(Ui.Badge($"{r.PartialCount} partial", "info75").Margin(4, 0, 0, 0));
        Tile(r.InvoiceCount.ToString(), "Invoices", "receipt", "primary", badges);
        if (r.TotalOutstanding > 0) Tile(Ui.Money(r.TotalOutstanding), "Still Outstanding", "hourglass-split", "warning");
        if (r.AverageActiveMonth is { } avg) Tile(Ui.Money(avg), "Avg / Active Month", "calendar3", "info");
        if (r.PreviousYearRevenue is { } prev)
        {
            double diff = r.TotalRevenue - prev;
            string tone = diff >= 0 ? "success" : "danger";
            object? pct = prev > 0 && r.YtdPercent is { } p ? Ui.Muted($"({p.ToString("0.0", CultureInfo.InvariantCulture)}%)", 12) : null;
            Tile((diff >= 0 ? "+" : "") + Ui.Money(diff), $"vs {selected - 1}", diff >= 0 ? "arrow-up-circle" : "arrow-down-circle", tone, pct);
        }
        page.Children.Add(tiles);

        // ---- charts
        var bar = new BarChart();
        if (selected is null) bar.SetData(r.YearRows.Select(y => (y.Year.ToString(CultureInfo.InvariantCulture), y.Collected)).ToList());
        else bar.SetData(r.Months.Select(m => (m.Name, m.Collected)).ToList());
        var barCard = Ui.Card(bar, selected is null ? "Revenue by Year" : $"Monthly Revenue — {selected}", "bar-chart-fill",
            headerRight: Ui.Muted("collected", 12.8));

        FrameworkElement donutBody;
        if (r.Customers.Count > 0)
        {
            var donut = new DoughnutChart();
            donut.SetData(ReportService.DoughnutSlices(r.Customers, r.TotalRevenue).Select(c => (c.Name, c.Collected)).ToList());
            donutBody = donut;
        }
        else donutBody = Ui.Empty("pie-chart", "No data for this period.");
        var donutCard = Ui.Card(donutBody, "Revenue by Customer", "pie-chart-fill");
        page.Children.Add(Ui.Columns(24, (Ui.Star(7), barCard), (Ui.Star(5), donutCard)).Margin(0, 0, 0, 24));

        // ---- tables
        FrameworkElement leftTable;
        TextBlock R(string t, string style = "Body") => Ui.Text(t, style, 14.4).Also(x => x.HorizontalAlignment = HorizontalAlignment.Right);
        if (selected is not null)
        {
            var months = r.Months.Where(m => m.Count > 0).ToList();
            var cols = new List<Column<MonthRow>>
            {
                new("Month", Ui.Star(), m => Ui.Text(m.Name, "Strong", 14.4)),
                new("Invoices", Ui.Auto, m => Ui.Muted(m.Count.ToString(), 14.4), HorizontalAlignment.Center),
                new("Invoiced", Ui.Auto, m => Ui.Muted(Ui.Money(m.Invoiced), 14.4), HorizontalAlignment.Right),
                new("Collected", Ui.Auto, m => R(Ui.Money(m.Collected), "Strong").WithResource(TextBlock.ForegroundProperty, "SuccessText"), HorizontalAlignment.Right),
            };
            List<UIElement?[]>? footer = r.TotalRevenue > 0
                ? new() { new UIElement?[] { Ui.Bold("Total", 14.4), Ui.Bold(r.InvoiceCount.ToString(), 14.4).Also(t => t.HorizontalAlignment = HorizontalAlignment.Center),
                    R(Ui.Money(r.TotalInvoiced), "Strong").Also(t => t.FontWeight = FontWeights.Bold),
                    R(Ui.Money(r.TotalRevenue), "Strong").Also(t => t.FontWeight = FontWeights.Bold).WithResource(TextBlock.ForegroundProperty, "SuccessText") } }
                : null;
            FrameworkElement table = months.Count > 0
                ? Table.Build(cols, months, footer: footer)
                : Ui.Stack(0, Table.Build(cols, Array.Empty<MonthRow>()), Ui.Empty("calendar-x", "No invoices for this year."));
            leftTable = Ui.Card(table, "Monthly Breakdown", "table", bodyPadding: new Thickness(0));
        }
        else
        {
            var cols = new List<Column<YearRow>>
            {
                new("Year", Ui.Star(), y => Ui.Row(4, Ui.Link(y.Year.ToString(CultureInfo.InvariantCulture), () => Shell.Navigate(Routes.Revenue(Shell, y.Year))).Also(l => l.FontSize = 14.4),
                    y.IsCurrent ? Ui.Badge("YTD", "primary") : new TextBlock())),
                new("Collected", Ui.Auto, y => R(Ui.Money(y.Collected), "Strong").WithResource(TextBlock.ForegroundProperty, "SuccessText"), HorizontalAlignment.Right),
                new("Change", Ui.Auto, y => y.Change is { } c
                    ? R((c >= 0 ? "+" : "") + Ui.Money(c)).WithResource(TextBlock.ForegroundProperty, c >= 0 ? "SuccessText" : "DangerText")
                    : Ui.Muted("—", 14.4), HorizontalAlignment.Right),
            };
            var footer = new List<UIElement?[]> { new UIElement?[] { Ui.Bold("All Time", 14.4), R(Ui.Money(r.TotalRevenue), "Strong").Also(t => t.FontWeight = FontWeights.Bold).WithResource(TextBlock.ForegroundProperty, "SuccessText"), null } };
            leftTable = Ui.Card(Table.Build(cols, r.YearRows, rowBrush: y => y.IsCurrent ? "PrimaryTint" : null, footer: footer), "Year by Year", "table", bodyPadding: new Thickness(0));
        }

        var custCols = new List<Column<(int Rank, CustomerRevenue C)>>
        {
            new("#", Ui.Px(40), x => Ui.Muted(x.Rank.ToString(), 14.4)),
            new("Customer", Ui.Star(), x => Ui.Text(x.C.Name, "Body", 14.4)),
            new("Collected", Ui.Auto, x => R(Ui.Money(x.C.Collected), "Strong").WithResource(TextBlock.ForegroundProperty, "SuccessText"), HorizontalAlignment.Right),
            new("Share", Ui.Auto, x => Ui.Muted(x.C.Share.ToString("0.0", CultureInfo.InvariantCulture) + "%", 14.4), HorizontalAlignment.Right),
        };
        var ranked = r.Customers.Select((c, i) => (i + 1, c)).ToList();
        FrameworkElement custTable = ranked.Count > 0
            ? Table.Build(custCols, ranked)
            : Ui.Stack(0, Table.Build(custCols, Array.Empty<(int, CustomerRevenue)>()), Ui.Empty("people", "No payments in this period."));
        var rightTable = Ui.Card(custTable, "Top Customers", "trophy", "Warning", bodyPadding: new Thickness(0));

        page.Children.Add(Ui.Columns(24, (Ui.Star(), leftTable), (Ui.Star(), rightTable)));
        Content = page;
    }
}
