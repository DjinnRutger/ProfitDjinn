using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using ProfitDjinn.App.Controls;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.App.Shell;
using ProfitDjinn.Core;
using ProfitDjinn.Core.Pdf;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.App.Pages;

/// <summary>
/// 2.2 phase 2: Profit &amp; Loss for a year, cash or accrual. Laid out like the Revenue page:
/// year buttons, tiles, a chart beside a doughnut, breakdown tables, plus CSV and PDF export.
/// </summary>
public sealed class ProfitLossPage : AppPage
{
    private readonly ProfitReport _r;

    public override string NavKey => "profit";
    public override IReadOnlyList<Crumb> Crumbs => new[] { new Crumb("Profit & Loss") };

    public ProfitLossPage(MainWindow shell, int? year, ProfitBasis basis) : base(shell)
    {
        int current = DateTime.Today.Year;
        _r = Store.Profit.Report(year ?? current, basis);
        var r = _r;

        // ---- header with year buttons
        var years = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (int y in r.Years)
        {
            string style = r.Year == y ? "Btn.Primary" : y == current ? "Btn.OutlinePrimary" : "Btn.OutlineSecondary";
            var content = Ui.Row(0, new TextBlock { Text = y.ToString(CultureInfo.InvariantCulture) });
            if (y == current) content.Children.Add(new Border
            {
                Background = System.Windows.Media.Brushes.White, CornerRadius = new CornerRadius(6), Padding = new Thickness(5, 1, 5, 1), Margin = new Thickness(5, 0, 0, 0),
                Child = new TextBlock { Text = "YTD", FontSize = 10.4, FontWeight = FontWeights.SemiBold }.WithResource(TextBlock.ForegroundProperty, "BsPrimary"),
            });
            int captured = y;
            years.Children.Add(Ui.Button(null, style, null, () => Shell.Navigate(Routes.Profit(Shell, captured, basis)), small: true).Also(b => b.Content = content).Margin(0, 0, 4, 4));
        }
        var page = new StackPanel();
        page.Children.Add(Ui.PageHeaderWithGlyph("bar-chart-line", "Profit & Loss", $"{r.Year} · {(basis == ProfitBasis.Cash ? "cash basis" : "accrual basis")}", null, years));

        // ---- basis switch and exports
        var cash = Ui.Button("Cash", basis == ProfitBasis.Cash ? "Btn.Primary" : "Btn.OutlineSecondary", "cash-stack",
            () => Shell.Navigate(Routes.Profit(Shell, r.Year, ProfitBasis.Cash)), small: true);
        var accrual = Ui.Button("Accrual", basis == ProfitBasis.Accrual ? "Btn.Primary" : "Btn.OutlineSecondary", "receipt",
            () => Shell.Navigate(Routes.Profit(Shell, r.Year, ProfitBasis.Accrual)), small: true).Margin(4, 0, 0, 0);
        string explain = basis == ProfitBasis.Cash
            ? "Income is money received and expenses are money paid, on the day each happened. Most small businesses file this way."
            : "Income is invoiced and expenses are incurred on their own dates, whether or not they have been paid.";
        var toolbar = new DockPanel();
        var exports = Ui.Row(8,
            Ui.Button("P&L CSV", "Btn.OutlineSecondary", "file-earmark-spreadsheet", ExportSummary, small: true),
            Ui.Button("Expenses CSV", "Btn.OutlineSecondary", "file-earmark-spreadsheet", ExportExpenses, small: true),
            Ui.Button("Preview", "Btn.OutlineSecondary", "eye", PreviewPdf, small: true),
            Ui.Button("PDF", "Btn.OutlinePrimary", "file-earmark-pdf", ExportPdf, small: true));
        DockPanel.SetDock(exports, Dock.Right);
        toolbar.Children.Add(exports);
        toolbar.Children.Add(Ui.Row(0, cash, accrual));
        var explainLine = new Border { Padding = new Thickness(0, 10, 0, 0), Child = Ui.Muted(explain, 13).Also(t => t.TextWrapping = TextWrapping.Wrap) };
        page.Children.Add(Ui.Card(Ui.Stack(0, toolbar, explainLine), bodyPadding: new Thickness(16)).Margin(0, 0, 0, 24));

        // ---- tiles
        var tiles = new UniformGrid { Columns = 4, Margin = new Thickness(-8, 0, -8, 24) };
        void Tile(string value, string label, string glyph, string tone, object? badge = null) =>
            tiles.Children.Add(new StatTile { Value = value, Label = label, Glyph = glyph, Tone = tone, Accent = "left-" + tone, Badge = badge, Margin = new Thickness(8, 0, 8, 0), Focusable = false, Cursor = System.Windows.Input.Cursors.Arrow });
        Tile(Ui.Money(r.Income), "Income", "cash-stack", "success");
        Tile(Ui.Money(r.Expenses), "Expenses", "wallet2", "danger");
        Tile(Signed(r.Net), r.Net >= 0 ? "Net Profit" : "Net Loss", r.Net >= 0 ? "graph-up-arrow" : "graph-down-arrow", r.Net >= 0 ? "primary" : "danger");
        object? vs = r.PreviousNet is { } prev ? Ui.Muted($"{Signed(prev)} in {r.Year - 1}", 12) : null;
        Tile(r.Margin is { } m ? m.ToString("0.0", CultureInfo.InvariantCulture) + "%" : "—", "Profit Margin", "percent", "info", vs);
        page.Children.Add(tiles);

        // ---- charts
        var chart = new PairBarChart();
        chart.SetData(r.Months.Select(mo => (mo.Name, mo.Income, mo.Expenses)).ToList());
        var chartCard = Ui.Card(chart, $"Income vs Expenses — {r.Year}", "bar-chart-fill", headerRight: Ui.Muted("by month", 12.8));
        FrameworkElement donutBody;
        if (r.Categories.Count > 0)
        {
            var donut = new DoughnutChart();
            donut.SetData(Slices(r).ToList());
            donutBody = donut;
        }
        else donutBody = Ui.Empty("pie-chart", "No expenses in this year.");
        var donutCard = Ui.Card(donutBody, "Expenses by Category", "pie-chart-fill");
        page.Children.Add(Ui.Columns(24, (Ui.Star(7), chartCard), (Ui.Star(5), donutCard)).EqualHeight().Margin(0, 0, 0, 24));

        // ---- tables
        TextBlock R(string t, string style = "Body") => Ui.Text(t, style, 14.4).Also(x => x.HorizontalAlignment = HorizontalAlignment.Right);
        TextBlock Net(double v, bool bold = false) => R(Signed(v), bold ? "Strong" : "Body").WithResource(TextBlock.ForegroundProperty, v < 0 ? "DangerText" : "SuccessText")
            .Also(t => { if (bold) t.FontWeight = FontWeights.Bold; });
        var monthCols = new List<Column<ProfitMonth>>
        {
            new("Month", Ui.Star(), mo => Ui.Text(mo.Name, "Strong", 14.4)),
            new("Income", Ui.Auto, mo => mo.Income > 0 ? R(Ui.Money(mo.Income)) : Ui.Muted("—", 14.4), HorizontalAlignment.Right),
            new("Expenses", Ui.Auto, mo => mo.Expenses > 0 ? R(Ui.Money(mo.Expenses)) : Ui.Muted("—", 14.4), HorizontalAlignment.Right),
            new("Net", Ui.Auto, mo => mo.Income == 0 && mo.Expenses == 0 ? Ui.Muted("—", 14.4) : Net(mo.Net), HorizontalAlignment.Right),
        };
        var monthFooter = new List<UIElement?[]>
        {
            new UIElement?[] { Ui.Bold("Total", 14.4), R(Ui.Money(r.Income), "Strong").Also(t => t.FontWeight = FontWeights.Bold),
                R(Ui.Money(r.Expenses), "Strong").Also(t => t.FontWeight = FontWeights.Bold), Net(r.Net, bold: true) },
        };
        var monthCard = Ui.Card(Table.Build(monthCols, r.Months, footer: monthFooter), "Monthly Breakdown", "table", bodyPadding: new Thickness(0));

        var catCols = new List<Column<CategoryTotal>>
        {
            new("Category", Ui.Star(), c => Ui.Text(c.Name, "Body", 14.4)),
            new("Share", Ui.Auto, c => Ui.Muted(c.Share.ToString("0.0", CultureInfo.InvariantCulture) + "%", 13.6), HorizontalAlignment.Right),
            new("Amount", Ui.Auto, c => R(Ui.Money(c.Amount), "Strong"), HorizontalAlignment.Right),
        };
        FrameworkElement catTable = r.Categories.Count > 0
            ? Table.Build(catCols, r.Categories)
            : Ui.Stack(0, Table.Build(catCols, Array.Empty<CategoryTotal>()), Ui.Empty("tags", "No expenses in this year."));
        var catCard = Ui.Card(catTable, "Expense Categories", "tags", headerRight: Ui.Muted("for your tax return", 12.8), bodyPadding: new Thickness(0));

        var vendorCols = new List<Column<VendorTotal>>
        {
            new("Vendor", Ui.Star(), v => Ui.Text(v.Name, "Body", 14.4)),
            new("Entries", Ui.Auto, v => Ui.Muted(v.Count.ToString(CultureInfo.InvariantCulture), 13.6), HorizontalAlignment.Center),
            new("Amount", Ui.Auto, v => R(Ui.Money(v.Amount), "Strong"), HorizontalAlignment.Right),
        };
        var topVendors = r.Vendors.Take(10).ToList();
        FrameworkElement vendorTable = topVendors.Count > 0
            ? Table.Build(vendorCols, topVendors)
            : Ui.Stack(0, Table.Build(vendorCols, Array.Empty<VendorTotal>()), Ui.Empty("shop", "No expenses in this year."));
        var vendorCard = Ui.Card(vendorTable, "Top Vendors", "shop", bodyPadding: new Thickness(0)).Margin(0, 24, 0, 0);

        var yearCols = new List<Column<ProfitYear>>
        {
            new("Year", Ui.Star(), y => Ui.Row(4, Ui.Link(y.Year.ToString(CultureInfo.InvariantCulture), () => Shell.Navigate(Routes.Profit(Shell, y.Year, basis))).Also(l => l.FontSize = 14.4),
                y.IsCurrent ? Ui.Badge("YTD", "primary") : new TextBlock())),
            new("Income", Ui.Auto, y => R(Ui.Money(y.Income)), HorizontalAlignment.Right),
            new("Expenses", Ui.Auto, y => R(Ui.Money(y.Expenses)), HorizontalAlignment.Right),
            new("Net", Ui.Auto, y => Net(y.Net), HorizontalAlignment.Right),
        };
        var yearCard = Ui.Card(Table.Build(yearCols, r.YearRows.OrderByDescending(y => y.Year).ToList()), "Year by Year", "calendar3", bodyPadding: new Thickness(0)).Margin(0, 24, 0, 0);

        page.Children.Add(Ui.Columns(24,
            (Ui.Star(7), Ui.Stack(0, monthCard, yearCard)),
            (Ui.Star(5), Ui.Stack(0, catCard, vendorCard))));
        Content = page;
    }

    /// <summary>"$1234.50", or "-$1234.50" for a loss.</summary>
    private static string Signed(double x) => (x < 0 ? "-" : "") + Ui.Money(Math.Abs(x));

    /// <summary>Top 7 categories plus "Other", as the revenue doughnut does with customers.</summary>
    private static IEnumerable<(string, double)> Slices(ProfitReport r)
    {
        if (r.Categories.Count <= 8) return r.Categories.Select(c => (c.Name, c.Amount));
        return r.Categories.Take(7).Select(c => (c.Name, c.Amount))
            .Append(("Other", Core.Rules.PyMath.Sum(r.Categories.Skip(7), c => c.Amount)));
    }

    private string BasisWord => _r.Basis == ProfitBasis.Cash ? "cash" : "accrual";

    private void ExportSummary() => SaveText($"profit-loss-{_r.Year}-{BasisWord}.csv", "CSV file (*.csv)|*.csv",
        () => Store.Profit.SummaryCsv(_r.Year, _r.Basis));

    private void ExportExpenses() => SaveText($"expenses-{_r.Year}-{BasisWord}.csv", "CSV file (*.csv)|*.csv",
        () => Store.Profit.ExpensesCsv(_r.Year, _r.Basis));

    private byte[] RenderPdf() => ProfitPdf.Render(_r, Store.Settings.Company(), DateOnly.FromDateTime(DateTime.Today));

    private string PdfName => $"profit-loss-{_r.Year}-{BasisWord}.pdf";

    private void ExportPdf() => Try(() =>
    {
        if (InvoiceOutput.SavePdf(RenderPdf(), PdfName, Shell) is { } path) Shell.ShowNotice(Notice.Success($"Saved {path}"));
    });

    /// <summary>2.5. The P&amp;L PDF in the preview window, nothing written unless saved from there.</summary>
    private void PreviewPdf() => Try(() =>
        PdfPreviewWindow.Show(Shell, RenderPdf(), $"Profit & Loss {_r.Year}",
            save: (pdf, w) => { if (InvoiceOutput.SavePdf(pdf, PdfName, w) is { } path) Shell.ShowNotice(Notice.Success($"Saved {path}")); },
            print: (pdf, _) => InvoiceOutput.Print(pdf, $"Profit & Loss {_r.Year}")));

    /// <summary>Writes CSV as UTF-8 with a byte-order mark, so Excel reads accents and symbols correctly.</summary>
    private void SaveText(string name, string filter, Func<string> make)
    {
        var dialog = SaveDialog(name, filter);
        if (dialog.ShowDialog(Shell) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, make(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            Shell.ShowNotice(Notice.Success($"Saved {dialog.FileName}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Shell.ShowError($"The file could not be saved to\n{dialog.FileName}\n\n{ex.Message}\n\nIf it is open in Excel, close it and try again.");
        }
    }

    private static SaveFileDialog SaveDialog(string name, string filter) => new()
    {
        FileName = name,
        Filter = filter,
        InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
    };
}
