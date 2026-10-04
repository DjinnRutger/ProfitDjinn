using System.Windows;
using System.Windows.Controls;
using ProfitDjinn.App.Controls;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.App.Shell;
using ProfitDjinn.Core.Data;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Rules;

namespace ProfitDjinn.App.Pages;

/// <summary>One invoice: lines, payments, notes, bill-to, details and the actions (1.x invoices/detail.html).</summary>
public sealed class InvoiceDetailPage : AppPage
{
    private readonly Invoice _inv;

    public override string NavKey => "invoices";
    public override IReadOnlyList<Crumb> Crumbs => new[] { new Crumb("Invoices", () => Shell.Navigate(Routes.Invoices(Shell))), new Crumb(_inv.InvoiceNumber) };

    public InvoiceDetailPage(MainWindow shell, long id) : base(shell)
    {
        _inv = Store.Invoices.Get(id);
        var inv = _inv;
        var c = inv.Customer ?? new Customer();

        // ---- header
        var lead = new WrapPanel();
        lead.Children.Add(Ui.Link(c.Name, () => Shell.Navigate(Routes.Customer(Shell, inv.CustomerId)), bold: false).Also(l => l.FontSize = 14));
        lead.Children.Add(Ui.Text($"  ·  {Ui.LongDate(inv.Date)}", "Lead"));
        var actions = new List<UIElement>
        {
            Ui.Button("Preview", "Btn.OutlineSecondary", "eye", () => Try(() => InvoiceOutput.Preview(inv, Store.Settings.Company, Shell))),
            Ui.Button("Print", "Btn.OutlineSecondary", "printer", () => Try(() => InvoiceOutput.Print(inv, Store.Settings.Company))),
            Ui.Button("PDF", "Btn.OutlineSecondary", "file-earmark-pdf", () => Try(() => InvoiceOutput.SavePdf(inv, Store.Settings.Company, Shell))),
            Ui.Button("Edit", "Btn.Primary", "pencil", () => Shell.Navigate(Routes.EditInvoice(Shell, inv.Id))),
        };
        if (inv.BalanceDue > 0) actions.Add(Ui.Button("Record Payment", "Btn.Success", "cash-coin", () => PaymentDialog.Open(Shell, inv)));
        if (inv.Paid || inv.IsPartial) actions.Add(Ui.Button("Mark Unpaid", "Btn.OutlineWarning", "arrow-counterclockwise", MarkUnpaid));
        actions.Add(Ui.Button("Delete", "Btn.OutlineDanger", "trash", Delete));

        var header = (DockPanel)Ui.PageHeader(inv.InvoiceNumber, " ", Ui.Badge(inv.StatusLabel, DashboardPage.InvoiceBadge(inv), big: true), actions.ToArray());
        var left = (StackPanel)header.Children[1];
        ((TextBlock)((StackPanel)left.Children[0]).Children[0]).SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        left.Children.RemoveAt(1);
        left.Children.Add(lead.Margin(0, 4, 0, 0));

        // ---- line items
        var lineCols = new List<Column<InvoiceLine>>
        {
            new("Description", Ui.Star(), InvoiceDetailPage.DescriptionCell),
            new("Qty", Ui.Auto, l => Ui.Muted(QtyText(l.Quantity)), HorizontalAlignment.Center),
            new("Unit Price", Ui.Auto, l => Ui.Muted(Ui.Money(l.UnitPrice)), HorizontalAlignment.Right),
            new("Amount", Ui.Auto, l => Ui.Text(Ui.Money(l.Amount), "Body"), HorizontalAlignment.Right),
        };
        var footer = new List<UIElement?[]>();
        TextBlock Right(string text, string style = "Body") => Ui.Text(text, style).Also(t => t.HorizontalAlignment = HorizontalAlignment.Right);
        if (inv.CreditApplied > 0)
        {
            footer.Add(new UIElement?[] { null, null, Right("Subtotal", "Muted"), Right(Ui.Money(inv.Total)) });
            footer.Add(new UIElement?[] { null, null, Right("Account Credit Applied", "Muted"), Right("−" + Ui.Money(inv.CreditApplied)).WithResource(TextBlock.ForegroundProperty, "Warning") });
            footer.Add(new UIElement?[] { null, null, Right("AMOUNT DUE").Also(t => t.FontWeight = FontWeights.Bold), Right(Ui.Money(inv.NetTotal)).Also(t => { t.FontWeight = FontWeights.Bold; t.FontSize = 20; }) });
        }
        else footer.Add(new UIElement?[] { null, null, Right("TOTAL").Also(t => t.FontWeight = FontWeights.Bold), Right(Ui.Money(inv.Total)).Also(t => { t.FontWeight = FontWeights.Bold; t.FontSize = 20; }) });
        var mainCol = new StackPanel();
        int lastLine = footer.Count - 1;
        mainCol.Children.Add(Ui.Card(Table.Build(lineCols, inv.Lines, footer: footer, footerShaded: f => f == lastLine), "Line Items", "list-ul", bodyPadding: new Thickness(0)));

        // ---- payments
        if (inv.Payments.Count > 0)
        {
            var payCols = new List<Column<Payment>>
            {
                new("Date", Ui.Auto, p => Ui.Text(Ui.Date(p.Date), "Body", 14.4)),
                new("Method", Ui.Auto, p => Ui.Text(p.MethodLabel, "Body", 14.4)),
                new("Check #", Ui.Auto, p => Ui.Muted(string.IsNullOrEmpty(p.CheckNumber) ? "—" : p.CheckNumber, 14.4).Also(t => t.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont"))),
                new("Notes", Ui.Star(), p => Ui.Muted(p.Notes, 14.4).Also(t => t.TextWrapping = TextWrapping.Wrap)),
                new("Amount", Ui.Auto, p => Ui.Text(Ui.Money(p.Amount), "Money", 14.4).WithResource(TextBlock.ForegroundProperty, "SuccessText"), HorizontalAlignment.Right),
                new("", Ui.Auto, p => Ui.IconButton("trash", "Btn.OutlineDanger", "Delete payment", () => DeletePayment(p)), HorizontalAlignment.Right),
            };
            var payFooter = new List<UIElement?[]>
            {
                new UIElement?[] { null, null, null, Right("Total Paid").Also(t => t.FontWeight = FontWeights.Bold), Right(Ui.Money(inv.AmountPaid)).Also(t => t.FontWeight = FontWeights.Bold).WithResource(TextBlock.ForegroundProperty, "SuccessText"), null },
            };
            if (inv.BalanceDue > 0)
                payFooter.Add(new UIElement?[] { null, null, null, Right("Balance Due", "Muted"), Right(Ui.Money(inv.BalanceDue), "Strong").WithResource(TextBlock.ForegroundProperty, "DangerText"), null });
            if (inv.CreditAmount > 0)
                payFooter.Add(new UIElement?[] { null, null, null, Right("Account Credit", "Muted"), Right(Ui.Money(inv.CreditAmount), "Strong").WithResource(TextBlock.ForegroundProperty, "Warning"), null });
            mainCol.Children.Add(Ui.Card(Table.Build(payCols, inv.Payments, footer: payFooter, footerShaded: f => f == 0), "Payment History", "cash-stack", "Success",
                headerRight: Ui.Badge(inv.Payments.Count.ToString(), "secondary"), bodyPadding: new Thickness(0)).Margin(0, 24, 0, 0));
        }

        // ---- notes and terms
        if (!string.IsNullOrEmpty(inv.Notes) || !string.IsNullOrEmpty(inv.Term1) || !string.IsNullOrEmpty(inv.Term2))
        {
            var p = new StackPanel();
            if (!string.IsNullOrEmpty(inv.Notes))
            {
                var t = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 15, Margin = new Thickness(0, 0, 0, 8) }.WithResource(TextBlock.ForegroundProperty, "Text");
                t.Inlines.Add(new System.Windows.Documents.Bold(new System.Windows.Documents.Run("Notes: ")));
                t.Inlines.Add(new System.Windows.Documents.Run(inv.Notes));
                p.Children.Add(t);
            }
            if (!string.IsNullOrEmpty(inv.Term1) || !string.IsNullOrEmpty(inv.Term2))
            {
                p.Children.Add(new Border { Style = Ui.Style("Rule"), Margin = new Thickness(0, 8, 0, 8) });
                p.Children.Add(Ui.Muted(inv.Term1, 13.6).Also(t => t.TextWrapping = TextWrapping.Wrap));
                p.Children.Add(Ui.Muted(inv.Term2, 13.6).Also(t => t.TextWrapping = TextWrapping.Wrap).Margin(0, 4, 0, 0));
            }
            mainCol.Children.Add(Ui.Card(p).Margin(0, 24, 0, 0));
        }

        // ---- bill to and details
        var bill = new StackPanel();
        bill.Children.Add(Ui.Text(c.Name, "Strong", 14.4));
        if (!string.IsNullOrEmpty(c.Attn)) bill.Children.Add(Ui.Muted($"Attn: {c.Attn}", 14.4));
        if (!string.IsNullOrEmpty(c.Address)) bill.Children.Add(Ui.Text(c.Address, "Body", 14.4));
        if (!string.IsNullOrEmpty(c.City) || !string.IsNullOrEmpty(c.State))
            bill.Children.Add(Ui.Text(string.Join(", ", new[] { c.City, c.State, c.ZipCode }.Where(x => !string.IsNullOrEmpty(x))), "Body", 14.4));
        if (!string.IsNullOrEmpty(c.Phone)) bill.Children.Add(Ui.Text(c.Phone, "Body", 14.4));
        if (!string.IsNullOrEmpty(c.Email)) bill.Children.Add(Ui.Link(c.Email, () => CustomersPage.MailTo(c.Email!), bold: false).Also(l => { l.FontSize = 14.4; l.HorizontalAlignment = HorizontalAlignment.Left; }));

        var details = new Grid();
        details.ColumnDefinitions.Add(new ColumnDefinition());
        details.ColumnDefinitions.Add(new ColumnDefinition());
        void Row(string label, UIElement value)
        {
            int r = details.RowDefinitions.Count;
            details.RowDefinitions.Add(new RowDefinition());
            var l = Ui.Muted(label, 14.4).Margin(0, 0, 8, 8);
            Grid.SetRow(l, r);
            if (value is FrameworkElement fe) { fe.Margin = new Thickness(0, 0, 0, 8); fe.HorizontalAlignment = HorizontalAlignment.Left; }
            Grid.SetRow(value, r);
            Grid.SetColumn(value, 1);
            details.Children.Add(l);
            details.Children.Add(value);
        }
        TextBlock Strong(string s, string? brush = null)
        {
            var t = Ui.Text(s, "Strong", 14.4);
            if (brush is not null) t.SetResourceReference(TextBlock.ForegroundProperty, brush);
            return t;
        }
        Row("Invoice #", Ui.Text(inv.InvoiceNumber, "Body", 14.4).WithResource(TextBlock.FontFamilyProperty, "MonoFont"));
        Row("Date", Ui.Text(Ui.Date(inv.Date), "Body", 14.4));
        Row("Invoice Total", Strong(Ui.Money(inv.Total)));
        if (inv.CreditApplied > 0)
        {
            Row("Credit Applied", Strong("−" + Ui.Money(inv.CreditApplied), "Warning"));
            Row("Amount Due", Strong(Ui.Money(inv.NetTotal)));
        }
        if (inv.AmountPaid > 0) Row("Amount Paid", Strong(Ui.Money(inv.AmountPaid), "SuccessText"));
        if (inv.BalanceDue > 0) Row("Balance Due", Strong(Ui.Money(inv.BalanceDue), "DangerText"));
        if (inv.CreditAmount > 0) Row("Account Credit", Strong(Ui.Money(inv.CreditAmount), "Warning"));
        Row("Status", Ui.Badge(inv.StatusLabel, DashboardPage.InvoiceBadge(inv)));
        if (inv.Payments.Count > 0) Row("Paid Via", Ui.Text(inv.PaidVia, "Body", 14.4).Also(t => t.TextWrapping = TextWrapping.Wrap));
        if (inv.PaidDate is { } pd) Row("Paid On", Ui.Text(Ui.Date(pd), "Body", 14.4));
        if (SqlFormat.ParseDateTime(inv.CreatedAt) is { } created)
            Row("Created", Ui.Muted(Ui.Date(DateOnly.FromDateTime(created)), 12.8));

        var side = Ui.Stack(16, Ui.Card(bill, "Bill To", "person-vcard"), Ui.Card(details, "Details", "info-circle"));

        var page = new StackPanel();
        page.Children.Add(header);
        page.Children.Add(Ui.Columns(24, (Ui.Star(2), mainCol), (Ui.Star(1), side)));
        Content = page;
    }

    /// <summary>A line's description, with its 2.5 service dates underneath when it has them.</summary>
    public static FrameworkElement DescriptionCell(InvoiceLine l) => l.ServiceText is { } service
        ? Ui.Stack(2, Ui.Text(l.Description, "Body", wrap: true), Ui.Muted(service, 12.8))
        : Ui.Text(l.Description, "Body", wrap: true);

    /// <summary>1.x showed a whole-number quantity without decimals, otherwise the raw number.</summary>
    public static string QtyText(double q) =>
        q == Math.Floor(q) && Math.Abs(q) < 1e15 ? ((long)q).ToString() : PyMath.Repr(q);

    private async void MarkUnpaid()
    {
        if (!await Shell.Confirm("Mark as Unpaid? This will delete ALL payment records for this invoice.", "Mark Unpaid", danger: true)) return;
        Try(() => Shell.Reload(Store.Invoices.MarkUnpaid(_inv.Id)));
    }

    private async void Delete()
    {
        if (!await Shell.Confirm($"Delete invoice {_inv.InvoiceNumber}?", "Delete", danger: true)) return;
        Try(() => Shell.Navigate(Routes.Invoices(Shell), Store.Invoices.Delete(_inv.Id)));
    }

    private async void DeletePayment(Payment p)
    {
        if (!await Shell.Confirm($"Delete this payment of {Ui.Money(p.Amount)}?", "Delete", danger: true)) return;
        Try(() => Shell.Reload(Store.Invoices.DeletePayment(_inv.Id, p.Id)));
    }
}
