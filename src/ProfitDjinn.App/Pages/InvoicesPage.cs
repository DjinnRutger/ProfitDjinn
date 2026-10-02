using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ProfitDjinn.App.Controls;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.App.Shell;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Rules;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.App.Pages;

/// <summary>The invoice list: All / Unpaid / Paid tabs, search, totals (1.x invoices/list.html).</summary>
public sealed class InvoicesPage : AppPage
{
    private readonly InvoiceFilter _filter;
    private readonly string _search;

    public override string NavKey => "invoices";
    public override IReadOnlyList<Crumb> Crumbs => new[] { new Crumb("Invoices") };

    public InvoicesPage(MainWindow shell, InvoiceFilter filter, string search) : base(shell)
    {
        _filter = filter;
        _search = search;
        var invoices = Store.Invoices.List(filter, search);
        var (unpaidCount, unpaidTotal) = Store.Invoices.UnpaidSummary();

        var lead = new WrapPanel();
        lead.Children.Add(Ui.Text($"{invoices.Count} invoice{(invoices.Count != 1 ? "s" : "")} shown", "Lead"));
        if (unpaidCount > 0)
        {
            lead.Children.Add(Ui.Text("  ·  ", "Lead"));
            lead.Children.Add(Ui.Text($"{unpaidCount} with balance ({Ui.Money(unpaidTotal)})", "Strong", 14).WithResource(TextBlock.ForegroundProperty, "DangerText"));
        }
        var header = (DockPanel)Ui.PageHeader("Invoices", " ", null,
            Ui.Button("New Invoice", "Btn.Primary", "plus-lg", () => Shell.Navigate(Routes.NewInvoice(Shell))));
        // Swap the plain lead line for the one with the red "with balance" part.
        var left = (StackPanel)header.Children[1];
        left.Children.RemoveAt(1);
        left.Children.Add(lead.Margin(0, 4, 0, 0));

        // status tabs (Bootstrap btn-group) + search
        var tabs = new StackPanel { Orientation = Orientation.Horizontal };
        tabs.Children.Add(Tab("All", null, "Btn.OutlineSecondary", InvoiceFilter.All, null));
        tabs.Children.Add(Tab("Unpaid / Partial", "exclamation-circle", "Btn.OutlineWarning", InvoiceFilter.Unpaid,
            unpaidCount > 0 ? Ui.Badge(unpaidCount.ToString(), "warningdark").Margin(6, 0, 0, 0) : null));
        tabs.Children.Add(Tab("Paid", "check-circle", "Btn.OutlineSuccess", InvoiceFilter.Paid, null));

        var box = Ui.TextBox(search, "Search invoice # or customer…", 260).Also(t => { t.Style = Ui.Style("Input.Small"); Input.SetPrefixGlyph(t, "search"); });
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Run(_filter, box.Text); };
        var searchRow = Ui.Row(8, box);
        if (search.Length > 0) searchRow.Children.Add(Ui.Button(null, "Btn.OutlineSecondary", "x-lg", () => Run(_filter, ""), small: true).Margin(-8, 0, 0, 0));
        searchRow.Children.Add(Ui.Button("Search", "Btn.Secondary", null, () => Run(_filter, box.Text), small: true));
        var bar = Ui.Row(16, tabs, searchRow);

        var page = new StackPanel();
        page.Children.Add(header);
        page.Children.Add(Ui.Card(bar, bodyPadding: new Thickness(16)).Margin(0, 0, 0, 24));

        var columns = new List<Column<Invoice>>
        {
            new("Invoice #", Ui.Auto, i => Ui.Link(i.InvoiceNumber, () => Shell.Navigate(Routes.Invoice(Shell, i.Id)), mono: true)),
            new("Customer", Ui.Star(), i => Ui.Link(i.Customer?.Name ?? "", () => Shell.Navigate(Routes.Customer(Shell, i.CustomerId)), bold: false)),
            new("Date", Ui.Auto, i => Ui.Muted(Ui.Date(i.Date), 13.6)),
            new("Amount", Ui.Auto, i => Ui.Text(Ui.Money(i.Total), "Money"), HorizontalAlignment.Right),
            new("Balance", Ui.Auto, i => i.BalanceDue > 0
                ? Ui.Text(Ui.Money(i.BalanceDue), "Strong").WithResource(TextBlock.ForegroundProperty, "DangerText")
                : Ui.Muted("—"), HorizontalAlignment.Right),
            new("Status", Ui.Auto, i => Ui.Badge(i.StatusLabel, DashboardPage.InvoiceBadge(i)), HorizontalAlignment.Center),
            new("", Ui.Auto, i =>
            {
                var actions = Ui.Row(4,
                    Ui.IconButton("eye", "Btn.OutlineSecondary", "View", () => Shell.Navigate(Routes.Invoice(Shell, i.Id))),
                    Ui.IconButton("pencil", "Btn.OutlinePrimary", "Edit", () => Shell.Navigate(Routes.EditInvoice(Shell, i.Id))));
                if (i.BalanceDue > 0) actions.Children.Add(Ui.IconButton("cash-coin", "Btn.OutlineSuccess", "Record Payment", () => PaymentDialog.Open(Shell, i)).Margin(4, 0, 0, 0));
                return actions;
            }, HorizontalAlignment.Right),
        };

        FrameworkElement body;
        if (invoices.Count == 0)
            body = Ui.Stack(0, Table.Build(columns, Array.Empty<Invoice>()), Ui.Empty("receipt", "No invoices found.", "Create one now.", () => Shell.Navigate(Routes.NewInvoice(Shell))));
        else
        {
            double balance = PyMath.Sum(invoices, i => i.BalanceDue);
            var footer = new List<UIElement?[]>
            {
                new UIElement?[]
                {
                    Ui.Bold("Total shown"), null, null,
                    Ui.Text(Ui.Money(PyMath.Sum(invoices, i => i.Total)), "Money").Also(t => t.FontWeight = FontWeights.Bold),
                    balance > 0 ? Ui.Text("-" + Ui.Money(balance), "Money").Also(t => t.FontWeight = FontWeights.Bold).WithResource(TextBlock.ForegroundProperty, "DangerText") : null,
                    null, null,
                },
            };
            body = Table.Build(columns, invoices, footer: footer);
        }
        page.Children.Add(Ui.Card(body, bodyPadding: new Thickness(0)));
        Content = page;
    }

    private Button Tab(string text, string? glyph, string style, InvoiceFilter filter, UIElement? badge)
    {
        var content = Ui.Row(0, Ui.Text(text, "Body", 12.8).Also(t => t.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1) })));
        if (badge is not null) content.Children.Add(badge);
        var b = new Button { Content = content, Style = Ui.Style(style) };
        Btn.SetSmall(b, true);
        if (glyph is not null) Btn.SetIcon(b, glyph);
        if (filter == _filter)
        {
            // Bootstrap .active: the outline button shown filled.
            b.SetResourceReference(Button.BackgroundProperty, style switch
            {
                "Btn.OutlineWarning" => "Warning",
                "Btn.OutlineSuccess" => "Success",
                _ => "Secondary",
            });
            b.Foreground = style == "Btn.OutlineWarning" ? System.Windows.Media.Brushes.Black : System.Windows.Media.Brushes.White;
        }
        b.Click += (_, _) => Run(filter, _search);
        return b;
    }

    private void Run(InvoiceFilter filter, string search) => Shell.Navigate(Routes.Invoices(Shell, filter, search.Trim()));
}
