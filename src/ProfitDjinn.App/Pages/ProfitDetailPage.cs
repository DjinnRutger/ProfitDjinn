using System.Windows;
using System.Windows.Controls;
using ProfitDjinn.App.Controls;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.App.Shell;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.App.Pages;

/// <summary>
/// 2.7. What makes up one Profit &amp; Loss figure: the figure as the P&amp;L shows it, how it is
/// worked out, and every payment, invoice or expense behind it (each opens its record).
/// </summary>
public sealed class ProfitDetailPage : AppPage
{
    private readonly ProfitDetail _d;

    public override string NavKey => "profit";
    public override IReadOnlyList<Crumb> Crumbs => new[]
    {
        new Crumb("Profit & Loss", () => Shell.Navigate(Routes.Profit(Shell, _d.Scope.Year, _d.Basis))),
        new Crumb(_d.Title),
    };

    public ProfitDetailPage(MainWindow shell, ProfitBasis basis, ProfitPart part, ProfitScope scope) : base(shell)
    {
        _d = Store.Profit.Detail(basis, part, scope);
        var d = _d;
        string basisText = basis == ProfitBasis.Cash ? "Cash basis" : "Accrual basis";

        var page = new StackPanel();
        page.Children.Add(Ui.PageHeaderWithGlyph("bar-chart-line", d.Title, basisText, null,
            Ui.Button("Back to Profit & Loss", "Btn.OutlineSecondary", "arrow-left", () => Shell.Navigate(Routes.Profit(Shell, d.Scope.Year, d.Basis)))));

        // ---- the figure and how it is worked out
        var summary = Ui.Stack(0,
            Ui.Text(Signed(d.Figure), "H1").WithResource(TextBlock.ForegroundProperty, d.Figure < 0 ? "DangerText" : "Text"),
            new Border { Padding = new Thickness(0, 8, 0, 0), Child = Ui.Muted(d.Explanation, 13.6).Also(t => t.TextWrapping = TextWrapping.Wrap) });
        if (d.Part is ProfitPart.Gross or ProfitPart.Net)
        {
            string minus = d.Part == ProfitPart.Gross ? "Cost of revenue" : "Expenses";
            summary.Children.Add(new Border
            {
                Padding = new Thickness(0, 10, 0, 0),
                Child = Ui.Text($"Income {Ui.Money(d.IncomeTotal)}  −  {minus} {Ui.Money(d.ExpenseTotal)}  =  {Signed(d.Figure)}", "Strong", 14.4),
            });
        }
        page.Children.Add(Ui.Card(summary, bodyPadding: new Thickness(16)).Margin(0, 0, 0, 24));

        // ---- the entries
        TextBlock Amount(double v, bool bold = false) => Ui.Text(Ui.Money(v), bold ? "Strong" : "Body", 14.4)
            .Also(t => { t.HorizontalAlignment = HorizontalAlignment.Right; if (bold) t.FontWeight = FontWeights.Bold; });

        if (d.HasIncome)
        {
            var cols = new List<Column<ProfitService.Entry>>
            {
                new("Date", Ui.Px(130), e => Ui.Muted(Ui.Date(e.Date), 13.6)),
                new("Customer", Ui.Star(), e => Ui.Text(e.Vendor, "Body", 14.4)),
                new("What", Ui.Star(), e => Ui.Text(e.Description, "Body", 14.4)),
                new("Amount", Ui.Auto, e => Amount(e.Amount), HorizontalAlignment.Right),
            };
            var footer = new List<UIElement?[]> { new UIElement?[] { Ui.Bold("Total", 14.4), null, null, Amount(d.IncomeTotal, bold: true) } };
            FrameworkElement body = d.Income.Count > 0
                ? Table.Build(cols, d.Income, onRowClick: e => { if (e.InvoiceId is { } id) Shell.Navigate(Routes.Invoice(Shell, id)); }, footer: footer)
                : Ui.Stack(0, Table.Build(cols, Array.Empty<ProfitService.Entry>()), Ui.Empty("cash-stack", "No income in this period."));
            page.Children.Add(Ui.Card(body, "Income", "cash-stack", headerRight: Ui.Muted(Count(d.Income.Count), 12.8),
                bodyPadding: new Thickness(0)).Margin(0, 0, 0, 24));
        }

        if (d.HasExpenses)
        {
            var cols = new List<Column<ProfitService.Entry>>
            {
                new("Date", Ui.Px(130), e => Ui.Muted(Ui.Date(e.Date), 13.6)),
                new("Vendor", Ui.Star(), e => Ui.Text(e.Vendor, "Body", 14.4)),
                new("Category", Ui.Star(), e => d.ShowGross && e.CostOfRevenue
                    ? Ui.Row(8, Ui.Text(e.Category, "Body", 14.4), Ui.Badge("Cost of revenue", "info75"))
                    : Ui.Text(e.Category, "Body", 14.4)),
                new("Description", Ui.Star(), e => Ui.Text(e.Description, "Body", 14.4)),
                new("Amount", Ui.Auto, e => Amount(e.Amount), HorizontalAlignment.Right),
            };
            var footer = new List<UIElement?[]> { new UIElement?[] { Ui.Bold("Total", 14.4), null, null, null, Amount(d.ExpenseTotal, bold: true) } };
            string title = d.Part switch { ProfitPart.CostOfRevenue or ProfitPart.Gross => "Cost of Revenue", ProfitPart.Operating => "Operating Expenses", _ => "Expenses" };
            FrameworkElement body = d.Expenses.Count > 0
                ? Table.Build(cols, d.Expenses, onRowClick: e => { if (e.ExpenseId is { } id) Shell.Navigate(Routes.Expense(Shell, id)); }, footer: footer)
                : Ui.Stack(0, Table.Build(cols, Array.Empty<ProfitService.Entry>()), Ui.Empty("wallet2", "No expenses in this period."));
            page.Children.Add(Ui.Card(body, title, "wallet2", headerRight: Ui.Muted(Count(d.Expenses.Count), 12.8),
                bodyPadding: new Thickness(0)));
        }
        Content = page;
    }

    private static string Count(int n) => n == 0 ? "0 entries" : $"{n} {(n == 1 ? "entry" : "entries")} · click one to open it";

    /// <summary>"$1234.50", or "-$1234.50" for a loss.</summary>
    private static string Signed(double x) => (x < 0 ? "-" : "") + Ui.Money(Math.Abs(x));
}
