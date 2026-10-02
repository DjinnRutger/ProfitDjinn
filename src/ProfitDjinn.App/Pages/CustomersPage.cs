using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ProfitDjinn.App.Controls;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.App.Shell;
using ProfitDjinn.Core.Model;

namespace ProfitDjinn.App.Pages;

/// <summary>The customer list with search and "Show inactive" (1.x customers/list.html).</summary>
public sealed class CustomersPage : AppPage
{
    private readonly string _search;
    private readonly bool _inactive;

    public override string NavKey => "customers";
    public override IReadOnlyList<Crumb> Crumbs => new[] { new Crumb("Customers") };

    public CustomersPage(MainWindow shell, string search, bool inactive) : base(shell)
    {
        _search = search;
        _inactive = inactive;
        var customers = Store.Customers.List(search, inactive);

        string lead = $"{customers.Count} customer{(customers.Count != 1 ? "s" : "")}" + (search.Length > 0 ? $" matching \"{search}\"" : "");
        var page = new StackPanel();
        page.Children.Add(Ui.PageHeader("Customers", lead, null,
            Ui.Button("New Customer", "Btn.Primary", "plus-lg", () => Shell.Navigate(Routes.NewCustomer(Shell)))));

        // search bar
        var box = Ui.TextBox(search, "Search by name…", 360);
        Input.SetPrefixGlyph(box, "search");
        var boxHost = box;
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Run(box.Text, _inactive); };
        var inactiveBox = new CheckBox { Content = "Show inactive", IsChecked = inactive, Margin = new Thickness(8, 0, 0, 0) };
        inactiveBox.Click += (_, _) => Run(box.Text, inactiveBox.IsChecked == true);
        var bar = Ui.Row(8, boxHost);
        if (search.Length > 0) bar.Children.Add(Ui.Button(null, "Btn.OutlineSecondary", "x-lg", () => Run("", _inactive)).Margin(-8, 0, 0, 0));
        bar.Children.Add(inactiveBox);
        bar.Children.Add(Ui.Button("Search", "Btn.Secondary", null, () => Run(box.Text, inactiveBox.IsChecked == true), small: true).Margin(8, 0, 0, 0));
        page.Children.Add(Ui.Card(bar, bodyPadding: new Thickness(16, 16, 16, 16)).Margin(0, 0, 0, 24));
        Loaded += (_, _) => { box.Focus(); box.CaretIndex = box.Text.Length; };

        // table
        var columns = new List<Column<Customer>>
        {
            new("Name", Ui.Star(2), c =>
            {
                var cell = new StackPanel();
                cell.Children.Add(Ui.Link(c.Name, () => Shell.Navigate(Routes.Customer(Shell, c.Id))).Also(l => l.HorizontalAlignment = HorizontalAlignment.Left));
                if (!string.IsNullOrEmpty(c.Attn)) cell.Children.Add(Ui.Muted(c.Attn, 12.8));
                return cell;
            }),
            new("Contact", Ui.Star(), c => Ui.Muted(string.IsNullOrEmpty(c.City) ? "—" : c.City + (string.IsNullOrEmpty(c.State) ? "" : ", " + c.State), 13.6)),
            new("Phone", Ui.Star(), c => Ui.Text(Ui.Dash(c.Phone), "Body", 13.6)),
            new("Email", Ui.Star(1.4), c => string.IsNullOrEmpty(c.Email)
                ? Ui.Text("—", "Body", 13.6)
                : Ui.Link(c.Email, () => MailTo(c.Email), bold: false).Also(l => l.FontSize = 13.6)),
            new("Outstanding", Ui.Auto, c =>
            {
                double o = c.TotalOutstanding;
                return o > 0 ? Ui.Text(Ui.Money(o), "Strong").WithResource(TextBlock.ForegroundProperty, "DangerText") : Ui.Muted("$0.00");
            }, HorizontalAlignment.Right),
            new("Status", Ui.Auto, c => c.IsActive ? Ui.Badge("Active", "success75") : Ui.Badge("Inactive", "secondary"), HorizontalAlignment.Center),
            new("", Ui.Auto, c => Ui.Row(4,
                Ui.IconButton("eye", "Btn.OutlineSecondary", "View", () => Shell.Navigate(Routes.Customer(Shell, c.Id))),
                Ui.IconButton("pencil", "Btn.OutlinePrimary", "Edit", () => Shell.Navigate(Routes.EditCustomer(Shell, c.Id))),
                Ui.IconButton("receipt", "Btn.OutlineSuccess", "New Invoice", () => Shell.Navigate(Routes.NewInvoice(Shell, c.Id)))), HorizontalAlignment.Right),
        };
        FrameworkElement body = customers.Count == 0
            ? Ui.Stack(0, Table.Build(columns, Array.Empty<Customer>()), Ui.Empty("people", "No customers found.", "Add one now.", () => Shell.Navigate(Routes.NewCustomer(Shell))))
            : Table.Build(columns, customers);
        page.Children.Add(Ui.Card(body, bodyPadding: new Thickness(0)));
        Content = page;
    }

    private void Run(string search, bool inactive) => Shell.Navigate(Routes.Customers(Shell, search.Trim(), inactive));

    /// <summary>Opens the default mail program, as a mailto: link did in 1.x.</summary>
    public static void MailTo(string address)
    {
        try { Process.Start(new ProcessStartInfo("mailto:" + address) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Clipboard.SetText(address);
            MessageBox.Show($"No mail program is set up on this PC, so the address was copied instead:\n\n{address}", "ProfitDjinn");
        }
    }
}
