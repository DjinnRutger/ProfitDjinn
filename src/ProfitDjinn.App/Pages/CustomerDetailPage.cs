using System.Windows;
using System.Windows.Controls;
using ProfitDjinn.App.Controls;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.App.Shell;
using ProfitDjinn.Core.Model;

namespace ProfitDjinn.App.Pages;

/// <summary>One customer: contact, money totals, work order, notes, invoice history (1.x customers/detail.html).</summary>
public sealed class CustomerDetailPage : AppPage
{
    private readonly Customer _c;

    public override string NavKey => "customers";
    public override IReadOnlyList<Crumb> Crumbs => new[] { new Crumb("Customers", () => Shell.Navigate(Routes.Customers(Shell))), new Crumb(_c.Name) };

    public CustomerDetailPage(MainWindow shell, long id) : base(shell)
    {
        _c = Store.Customers.Get(id);
        var c = _c;

        var page = new StackPanel();
        bool workOrders = Store.WorkOrders.Enabled;
        page.Children.Add(Ui.PageHeader(c.Name, string.IsNullOrEmpty(c.Attn) ? null : $"Attn: {c.Attn}", null, new UIElement?[]
        {
            workOrders ? Ui.Button("Work Order", "Btn.OutlinePrimary", "clipboard-check", () => Shell.Navigate(Routes.WorkOrder(Shell, c.Id))) : null,
            Ui.Button("New Invoice", "Btn.Success", "receipt", () => Shell.Navigate(Routes.NewInvoice(Shell, c.Id))),
            Ui.Button("Recurring Invoice", "Btn.OutlineSuccess", "arrow-repeat", () => Shell.Navigate(Routes.NewRecurringInvoice(Shell, c.Id))),
            Ui.Button("Edit", "Btn.Primary", "pencil", () => Shell.Navigate(Routes.EditCustomer(Shell, c.Id))),
            Ui.Button("Delete", "Btn.OutlineDanger", "trash", Delete),
        }.OfType<UIElement>().ToArray()));

        // ---- left column
        var left = new StackPanel();
        var info = new Grid();
        info.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star(1) });
        info.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star(2) });
        void Pair(string label, UIElement value)
        {
            int row = info.RowDefinitions.Count;
            info.RowDefinitions.Add(new RowDefinition());
            var l = Ui.Muted(label, 14.4).Margin(0, 0, 8, 8);
            Grid.SetRow(l, row);
            if (value is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 0, 8);
            Grid.SetRow(value, row);
            Grid.SetColumn(value, 1);
            info.Children.Add(l);
            info.Children.Add(value);
        }
        if (!string.IsNullOrEmpty(c.Address))
        {
            string cityLine = string.Join(", ", new[] { c.City, c.State, c.ZipCode }.Where(p => !string.IsNullOrEmpty(p)));
            Pair("Address", Ui.Text(cityLine.Length > 0 ? $"{c.Address}\n{cityLine}" : c.Address, "Body", 14.4, wrap: true));
        }
        if (!string.IsNullOrEmpty(c.Phone)) Pair("Phone", Ui.Text(c.Phone, "Body", 14.4));
        if (!string.IsNullOrEmpty(c.Email)) Pair("Email", Ui.Link(c.Email, () => CustomersPage.MailTo(c.Email!), bold: false).Also(l => { l.FontSize = 14.4; l.TextWrapping = TextWrapping.Wrap; }));
        Pair("Status", c.IsActive ? Ui.Badge("Active", "success75") : Ui.Badge("Inactive", "secondary"));
        left.Children.Add(Ui.Card(info, "Contact Info", "person-vcard").Margin(0, 0, 0, 24));

        double credit = c.AccountCredit, outstanding = c.TotalOutstanding;
        var money = new Grid();
        money.ColumnDefinitions.Add(new ColumnDefinition());
        money.ColumnDefinitions.Add(new ColumnDefinition());
        money.RowDefinitions.Add(new RowDefinition());
        money.RowDefinitions.Add(new RowDefinition());
        void Place(UIElement e, int row, int col, int span = 1) { Grid.SetRow(e, row); Grid.SetColumn(e, col); Grid.SetColumnSpan(e, span); money.Children.Add(e); }
        Place(Small(Ui.Money(c.TotalInvoiced), "Total Invoiced", "primary", 22.4).Margin(0, 0, 8, 16), 0, 0);
        Place(Small(Ui.Money(c.TotalPaid), "Total Paid", "success", 22.4).Margin(8, 0, 0, 16), 0, 1);
        var owe = Small(Ui.Money(outstanding), "Outstanding Balance", "danger", 25.6);
        owe.Muted = outstanding <= 0;
        if (credit > 0)
        {
            Place(owe.Margin(0, 0, 8, 0), 1, 0);
            var cr = Small(Ui.Money(credit), "Account Credit", "warning", 22.4);
            cr.Accent = "leftwarning";
            Place(cr.Margin(8, 0, 0, 0), 1, 1);
        }
        else Place(owe, 1, 0, 2);
        left.Children.Add(money);

        var wo = Store.WorkOrders.FindForCustomer(c.Id);
        var woBody = new StackPanel();
        if (wo is { HasOpenWork: true })
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition());
            g.RowDefinitions.Add(new RowDefinition());
            var readyLabel = Ui.Muted("Ready to bill", 14.4);
            var ready = Ui.Text(Ui.Money(wo.ReadyToBillTotal), "Strong", 14.4);
            if (wo.ReadyToBillTotal > 0) ready.SetResourceReference(TextBlock.ForegroundProperty, "Warning");
            var todoLabel = Ui.Muted("Open to-dos", 14.4).Margin(0, 8, 0, 0);
            var todos = Ui.Text(wo.PendingCount.ToString(), "Strong", 14.4).Margin(0, 8, 0, 0);
            Grid.SetColumn(ready, 1); Grid.SetRow(todoLabel, 1); Grid.SetRow(todos, 1); Grid.SetColumn(todos, 1);
            g.Children.Add(readyLabel); g.Children.Add(ready); g.Children.Add(todoLabel); g.Children.Add(todos);
            woBody.Children.Add(g);
        }
        else woBody.Children.Add(Ui.Muted("Nothing unbilled. Open the tab to jot down work as you do it.", 14).Also(t => t.TextWrapping = TextWrapping.Wrap).Margin(0, 0, 0, 16));
        woBody.Children.Add(Ui.Button("Open Work Order", "Btn.OutlinePrimary", "box-arrow-up-right", () => Shell.Navigate(Routes.WorkOrder(Shell, c.Id)), small: true)
            .Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch));
        object? woBadge = wo is { ReadyToBillTotal: > 0 } ? Ui.Badge($"{Ui.Money(wo.ReadyToBillTotal)} ready", "warningdark") : null;
        if (workOrders) left.Children.Add(Ui.Card(woBody, "Work Order", "clipboard-check", headerRight: woBadge).Margin(0, 24, 0, 0));

        var notes = Ui.TextArea(c.Notes, 100, "Add notes about this customer…").Also(t => t.FontSize = 14);
        var notesBody = Ui.Stack(8, notes, Ui.Button("Save Notes", "Btn.Primary", "save", () =>
            Try(() => Shell.Reload(Store.Customers.UpdateNotes(c.Id, notes.Text))), small: true).Also(b => b.HorizontalAlignment = HorizontalAlignment.Left));
        left.Children.Add(Ui.Card(notesBody, "Account Notes", "sticky", "Warning").Margin(0, 24, 0, 0));

        // ---- right column: invoice history
        var invoices = Store.Customers.InvoiceHistory(c);
        var columns = new List<Column<Invoice>>
        {
            new("Invoice #", Ui.Auto, i => Ui.Link(i.InvoiceNumber, () => Shell.Navigate(Routes.Invoice(Shell, i.Id)), mono: true)),
            new("Date", Ui.Star(), i => Ui.Muted(Ui.Date(i.Date), 13.6)),
            new("Amount", Ui.Auto, i => Ui.Text(Ui.Money(i.Total), "Money"), HorizontalAlignment.Right),
            new("Balance", Ui.Auto, i => i.BalanceDue > 0
                ? Ui.Text(Ui.Money(i.BalanceDue), "Body").WithResource(TextBlock.ForegroundProperty, "DangerText")
                : Ui.Muted("—"), HorizontalAlignment.Right),
            new("Status", Ui.Auto, i => Ui.Badge(i.StatusLabel, DashboardPage.InvoiceBadge(i)), HorizontalAlignment.Center),
            new("", Ui.Auto, i => Ui.IconButton("eye", "Btn.OutlineSecondary", "View", () => Shell.Navigate(Routes.Invoice(Shell, i.Id))), HorizontalAlignment.Right),
        };
        FrameworkElement table = invoices.Count == 0
            ? Ui.Stack(0, Table.Build(columns, Array.Empty<Invoice>()), Ui.Empty("receipt", "No invoices yet.", "Create the first one.", () => Shell.Navigate(Routes.NewInvoice(Shell, c.Id))))
            : Table.Build(columns, invoices, onRowClick: i => Shell.Navigate(Routes.Invoice(Shell, i.Id)));
        var history = Ui.Card(table, "Invoice History", "receipt", headerRight: Ui.Badge(invoices.Count.ToString(), "secondary"), bodyPadding: new Thickness(0));

        // ---- 2.4: recurring invoices, above the history, only once there is one
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        var schedules = Store.RecurringInvoices.List(c.Id);
        if (schedules.Count > 0)
            right.Children.Add(Ui.Card(RecurringInvoicesPage.Table(Shell, schedules, showCustomer: false), "Recurring Invoices", "arrow-repeat", "Success",
                headerRight: Ui.Badge(schedules.Count.ToString(), "secondary"), bodyPadding: new Thickness(0)).Margin(0, 0, 0, 24));
        right.Children.Add(history);

        page.Children.Add(Ui.Columns(24, (Ui.Star(1), left), (Ui.Star(2), right)));
        Content = page;
    }

    private static StatTile Small(string value, string label, string tone, double size) => new()
    {
        Value = value, Label = label, Tone = tone, Glyph = "", ValueSize = size, Cursor = System.Windows.Input.Cursors.Arrow, Focusable = false,
    };

    private async void Delete()
    {
        if (!await Shell.Confirm($"Delete {_c.Name}? This also deletes all their invoices, their work order and any recurring invoices.", "Delete", danger: true)) return;
        Try(() => Shell.Navigate(Routes.Customers(Shell), Store.Customers.Delete(_c.Id)));
    }
}
