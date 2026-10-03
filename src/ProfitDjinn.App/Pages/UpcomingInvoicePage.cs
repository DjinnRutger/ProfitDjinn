using System.Windows;
using System.Windows.Controls;
using ProfitDjinn.App.Controls;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.App.Shell;
using ProfitDjinn.Core;
using ProfitDjinn.Core.Model;

namespace ProfitDjinn.App.Pages;

/// <summary>
/// 2.4. A recurring invoice that has not been created yet, shown the way an invoice is.
/// Printing or saving the PDF issues it first (it keeps its scheduled date), so the copy the
/// customer gets always matches a real invoice with a real number.
/// </summary>
public sealed class UpcomingInvoicePage : AppPage
{
    private readonly RecurringInvoice _t;
    private readonly DateOnly _date;
    private readonly Invoice _preview;

    public override string NavKey => "invoices";
    public override IReadOnlyList<Crumb> Crumbs => new[] { new Crumb("Invoices", () => Shell.Navigate(Routes.Invoices(Shell))), new Crumb("Upcoming") };

    public UpcomingInvoicePage(MainWindow shell, long recurringId, DateOnly? date) : base(shell)
    {
        _t = Store.RecurringInvoices.Get(recurringId);
        _date = date ?? (_t.IsActive ? _t.NextDate : null)
            ?? throw new UserFacingException(_t.IsActive ? "This recurring invoice has no more invoices to create." : "This recurring invoice is paused. Turn it back on to see its next invoice.");
        _preview = Store.RecurringInvoices.Preview(recurringId, _date);
        var inv = _preview;
        var c = inv.Customer ?? new Customer();
        var today = DateOnly.FromDateTime(DateTime.Today);

        // ---- header
        var lead = new WrapPanel();
        lead.Children.Add(Ui.Link(c.Name, () => Shell.Navigate(Routes.Customer(Shell, inv.CustomerId)), bold: false).Also(l => l.FontSize = 14));
        lead.Children.Add(Ui.Text($"  ·  {Ui.LongDate(inv.Date)}  ·  {_t.ScheduleLabel}", "Lead"));
        var actions = new UIElement[]
        {
            Ui.Button("Print", "Btn.OutlineSecondary", "printer", () => IssueThen(i => InvoiceOutput.Print(i, Store.Settings.Company))),
            Ui.Button("PDF", "Btn.OutlineSecondary", "file-earmark-pdf", () => IssueThen(i => InvoiceOutput.SavePdf(i, Store.Settings.Company, Shell))),
            Ui.Button("Issue Now", "Btn.Success", "send", () => IssueThen(null)),
            Ui.Button("Skip", "Btn.OutlineWarning", "skip-forward", Skip),
            Ui.Button("Edit Schedule", "Btn.Primary", "pencil", () => Shell.Navigate(Routes.EditRecurringInvoice(Shell, _t.Id))),
        };
        string when = _date <= today ? "Due today" : $"Upcoming · {Ui.Date(_date)}";
        var header = (DockPanel)Ui.PageHeader("Upcoming Invoice", " ", Ui.Badge(when, "info", big: true), actions);
        var left = (StackPanel)header.Children[1];
        left.Children.RemoveAt(1);
        left.Children.Add(lead.Margin(0, 4, 0, 0));

        // ---- what happens next
        var explain = Ui.Text(
            $"ProfitDjinn creates this invoice on {Ui.LongDate(_date)}. Print, PDF or Issue Now creates it today instead, still dated {Ui.Date(_date)}, " +
            $"and the schedule moves on to the next date. The number shown is the next free one; it is assigned when the invoice is created.",
            "Body", 13.6, wrap: true).WithResource(TextBlock.ForegroundProperty, "Alert.Info.Fg");
        var note = new Border
        {
            Padding = new Thickness(14, 10, 14, 10), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 24),
            Child = explain,
        }.WithResource(Border.BackgroundProperty, "Alert.Info.Bg").WithResource(Border.BorderBrushProperty, "Alert.Info.Border").WithResource(Border.CornerRadiusProperty, "Radius");

        // ---- line items
        var lineCols = new List<Column<InvoiceLine>>
        {
            new("Description", Ui.Star(), l => Ui.Text(l.Description, "Body", wrap: true)),
            new("Qty", Ui.Auto, l => Ui.Muted(InvoiceDetailPage.QtyText(l.Quantity)), HorizontalAlignment.Center),
            new("Unit Price", Ui.Auto, l => Ui.Muted(Ui.Money(l.UnitPrice)), HorizontalAlignment.Right),
            new("Amount", Ui.Auto, l => Ui.Text(Ui.Money(l.Amount), "Body"), HorizontalAlignment.Right),
        };
        TextBlock Right(string text, string style = "Body") => Ui.Text(text, style).Also(t => t.HorizontalAlignment = HorizontalAlignment.Right);
        var footer = new List<UIElement?[]>
        {
            new UIElement?[] { null, null, Right("TOTAL").Also(t => t.FontWeight = FontWeights.Bold), Right(Ui.Money(inv.Total)).Also(t => { t.FontWeight = FontWeights.Bold; t.FontSize = 20; }) },
        };
        var mainCol = new StackPanel();
        mainCol.Children.Add(note);
        mainCol.Children.Add(Ui.Card(Controls.Table.Build(lineCols, inv.Lines, footer: footer, footerShaded: _ => true), "Line Items", "list-ul", bodyPadding: new Thickness(0)));

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
        if (!string.IsNullOrEmpty(c.Email)) bill.Children.Add(Ui.Text(c.Email, "Body", 14.4));

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
        Row("Invoice #", Ui.Text(inv.InvoiceNumber, "Body", 14.4).WithResource(TextBlock.FontFamilyProperty, "MonoFont").Also(t => t.ToolTip = "The next free number. Assigned when the invoice is created."));
        Row("Date", Ui.Text(Ui.Date(inv.Date), "Body", 14.4));
        Row("Invoice Total", Ui.Text(Ui.Money(inv.Total), "Strong", 14.4));
        Row("Repeats", Ui.Text(_t.ScheduleLabel, "Body", 14.4).Also(t => t.TextWrapping = TextWrapping.Wrap));
        if (_t.Schedule.FirstFrom(_date.AddDays(1)) is { } after) Row("After That", Ui.Text(Ui.Date(after), "Body", 14.4));
        if (_t.EndDate is { } end) Row("Ends", Ui.Text(Ui.Date(end), "Body", 14.4));
        Row("Status", Ui.Badge("Upcoming", "info"));

        var side = Ui.Stack(16, Ui.Card(bill, "Bill To", "person-vcard"), Ui.Card(details, "Details", "info-circle"));

        var page = new StackPanel();
        page.Children.Add(header);
        page.Children.Add(Ui.Columns(24, (Ui.Star(2), mainCol), (Ui.Star(1), side)));
        Content = page;
    }

    /// <summary>Creates the invoice, opens it, then prints or saves it if asked.</summary>
    private async void IssueThen(Action<Invoice>? output)
    {
        string what = output is null ? "" : " so the copy you send matches your records";
        if (!await Shell.Confirm(
                $"Issue this invoice now{what}? It is created today with the next invoice number, dated {Ui.Date(_date)}, and the schedule moves on to its next date.",
                "Issue Invoice", title: "Issue this invoice now?"))
            return;
        Try(() =>
        {
            var issued = Store.RecurringInvoices.Issue(_t.Id, _date);
            Shell.Navigate(Routes.Invoice(Shell, issued.InvoiceId),
                Core.Services.Notice.Success($"Invoice {issued.InvoiceNumber} created from the recurring invoice, dated {Ui.Date(issued.Date)}."));
            if (output is not null)
            {
                var invoice = Store.Invoices.Get(issued.InvoiceId);
                // After the page has changed, so the dialog sits over the real invoice.
                Dispatcher.BeginInvoke(() =>
                {
                    try { output(invoice); }
                    catch (UserFacingException ex) { Shell.ShowError(ex.Message); }
                }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
        });
    }

    private async void Skip()
    {
        if (!await Shell.Confirm($"Skip the {Ui.Date(_date)} invoice for {_t.Customer?.Name}? It will not be created.", "Skip It", title: "Skip this invoice?")) return;
        Try(() => Shell.Navigate(Routes.Invoices(Shell), Store.RecurringInvoices.Skip(_t.Id, _date)));
    }
}
