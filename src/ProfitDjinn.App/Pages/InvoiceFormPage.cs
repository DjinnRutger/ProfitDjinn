using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ProfitDjinn.App.Controls;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.App.Shell;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Rules;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.App.Pages;

/// <summary>New Invoice and Edit Invoice, with the line builder (1.x invoices/form.html).</summary>
public sealed class InvoiceFormPage : AppPage
{
    private sealed record CustomerChoice(long Id, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly long? _id;
    private readonly string _number;
    private readonly ComboBox _customer;
    private readonly TextBox _invoiceNumber, _notes, _term1, _term2;
    private readonly DateBox _date;
    private readonly CheckBox _paid;
    private readonly LineBuilder _lines;
    private readonly TextBlock _summaryTotal = new() { FontWeight = FontWeights.Bold, FontSize = 24 };
    private readonly Field _customerField, _numberField, _dateField;

    public override string NavKey => "invoices";

    public override IReadOnlyList<Crumb> Crumbs
    {
        get
        {
            var list = new List<Crumb> { new("Invoices", () => Shell.Navigate(Routes.Invoices(Shell))) };
            if (_id is { } id) list.Add(new Crumb(_number, () => Shell.Navigate(Routes.Invoice(Shell, id))));
            list.Add(new Crumb(_id is null ? "New Invoice" : "Edit"));
            return list;
        }
    }

    public InvoiceFormPage(MainWindow shell, long? id, long? customerId) : base(shell)
    {
        _id = id;
        Invoice? existing = id is { } iid ? Store.Invoices.Get(iid) : null;
        var draft = existing is null ? Store.Invoices.NewDraft(customerId) : null;
        _number = existing?.InvoiceNumber ?? "";

        // customers: active ones, plus the invoice's own customer if it has since been made inactive (fixed in 2.0)
        var choices = Store.Customers.ActiveForPicker().Select(c => new CustomerChoice(c.Id, string.IsNullOrEmpty(c.Attn) ? c.Name : $"{c.Name} ({c.Attn})")).ToList();
        if (existing?.Customer is { } current && choices.All(c => c.Id != current.Id))
            choices.Add(new CustomerChoice(current.Id, current.Name + " (inactive)"));
        _customer = new ComboBox { ItemsSource = choices, MaxDropDownHeight = 400 };
        // existing?.X ?? draft!.X would reach the null draft when a saved value is null (old rows
        // can hold NULL notes and terms), so pick the source first.
        long? selected = existing is not null ? existing.CustomerId : draft!.CustomerId;
        _customer.SelectedItem = choices.FirstOrDefault(c => c.Id == selected);
        Input.SetPlaceholder(_customer, "— Select customer —");

        _invoiceNumber = Ui.TextBox(existing is not null ? existing.InvoiceNumber : draft!.InvoiceNumber).Also(t => { t.CharacterCasing = CharacterCasing.Upper; t.SetResourceReference(TextBox.FontFamilyProperty, "MonoFont"); });
        _date = Ui.DateBox(existing is not null ? existing.Date : draft!.Date);
        _paid = new CheckBox { Content = Ui.Bold("Mark as Paid", 14), IsChecked = existing?.Paid ?? false, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 24) };
        _notes = Ui.TextArea((existing is not null ? existing.Notes ?? "" : draft!.Notes), 56);
        _term1 = Ui.TextBox((existing is not null ? existing.Term1 ?? "" : draft!.Term1)).Also(t => t.Style = Ui.Style("Input.Small"));
        _term2 = Ui.TextBox((existing is not null ? existing.Term2 ?? "" : draft!.Term2)).Also(t => t.Style = Ui.Style("Input.Small"));

        _customerField = Ui.Field("Customer", _customer, required: true);
        _numberField = Ui.Field("Invoice #", _invoiceNumber, required: true);
        _dateField = Ui.Field("Date", _date, required: true);
        var details = Ui.Stack(0,
            Ui.Columns(16, (Ui.Star(2), _customerField), (Ui.Star(1), _numberField)),
            Ui.Columns(16, (Ui.Star(1), _dateField), (Ui.Star(1), _paid), (Ui.Star(1), new Border())));

        // line items
        _lines = new LineBuilder("No line items yet. Click Add Line to get started.", LineDates.Dates);
        _lines.Changed += () => _summaryTotal.Text = Ui.MoneyGrouped(_lines.Total);
        if (existing is not null)
            _lines.SetRows(existing.Lines.Select(l => new InvoiceRowInput(l.Description, InvoiceDetailPage.QtyText(l.Quantity), PyMath.JsToFixedText(l.UnitPrice, 2), l.ServiceStart, l.ServiceEnd)));

        var headerTools = Ui.Row(8);
        var items = Store.Items.Active();
        if (items.Count > 0)
        {
            var picker = new ComboBox { Width = 240, ItemsSource = items.Select(i => $"{i.Description} — {Ui.Money(i.Price)}").ToList(), MinHeight = 31, FontSize = 13.6 };
            Input.SetPlaceholder(picker, "Quick-add service…");
            picker.SelectionChanged += (_, _) =>
            {
                if (picker.SelectedIndex < 0) return;
                var item = items[picker.SelectedIndex];
                _lines.AddRow(item.Description, "1", PyMath.JsToFixedText(item.Price, 2));
                picker.SelectedIndex = -1;
            };
            headerTools.Children.Add(picker);
        }
        headerTools.Children.Add(Ui.Button("Add Line", "Btn.OutlinePrimary", "plus-lg", () => _lines.AddRow("", "1", "", focus: true), small: true).Margin(8, 0, 0, 0));

        var terms = Ui.Stack(0,
            Ui.Field("Notes", _notes),
            Ui.Columns(16, (Ui.Star(), Ui.Field("Payment Terms", _term1).Margin(0, 0, 0, 0)), (Ui.Star(), Ui.Field("Additional Terms", _term2).Margin(0, 0, 0, 0))));

        var left = Ui.Stack(24,
            Ui.Card(details, "Invoice Details"),
            Ui.Card(_lines, "Line Items", "list-ul", headerRight: headerTools, bodyPadding: new Thickness(0)),
            Ui.Card(terms));

        // summary
        var summary = new StackPanel();
        var totalRow = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        DockPanel.SetDock(_summaryTotal, Dock.Right);
        _summaryTotal.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        totalRow.Children.Add(_summaryTotal);
        totalRow.Children.Add(Ui.Muted("Invoice Total", 15).Also(t => t.VerticalAlignment = VerticalAlignment.Center));
        summary.Children.Add(totalRow);
        if (existing is { CreditApplied: > 0 })
            summary.Children.Add(new Border
            {
                Padding = new Thickness(12, 8, 12, 8), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 16),
                Child = Ui.Text($"{Ui.Money(existing.CreditApplied)} account credit applied — manage it from Record Payment on the invoice page.", "Body", 13.6, wrap: true)
                    .WithResource(TextBlock.ForegroundProperty, "Alert.Warning.Fg"),
            }.WithResource(Border.BackgroundProperty, "Alert.Warning.Bg").WithResource(Border.BorderBrushProperty, "Alert.Warning.Border").WithResource(Border.CornerRadiusProperty, "Radius"));
        summary.Children.Add(new Border { Style = Ui.Style("Rule"), Margin = new Thickness(0, 0, 0, 16) });
        summary.Children.Add(Ui.Button("Save Invoice", "Btn.Primary", "check-lg", Save).Also(b => { b.HorizontalAlignment = HorizontalAlignment.Stretch; b.Padding = new Thickness(16, 9, 16, 9); b.FontSize = 17.6; }));
        summary.Children.Add(Ui.Button("Cancel", "Btn.OutlineSecondary", null, () => Shell.Navigate(_id is { } i ? Routes.Invoice(Shell, i) : Routes.Invoices(Shell)))
            .Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch).Margin(0, 8, 0, 0));
        var right = Ui.Card(summary, "Summary");
        _summaryTotal.Text = Ui.MoneyGrouped(_lines.Total);

        var page = new StackPanel();
        page.Children.Add(Ui.PageHeader(_id is null ? "New Invoice" : $"Edit Invoice {_number}"));
        page.Children.Add(Ui.Columns(24, (Ui.Star(2), left), (Ui.Star(1), right)));
        Content = page;
    }

    public override void OnShown()
    {
        if (_customer.SelectedItem is null) _customer.Focus();
        else _invoiceNumber.Focus();
    }

    private void Save()
    {
        _customerField.Error = _numberField.Error = _dateField.Error = null;
        Input.SetInvalid(_customer, false); Input.SetInvalid(_invoiceNumber, false); _date.Invalid = false;
        if (_lines.Count == 0)
        {
            Shell.ShowError("Please add at least one line item before saving.");
            return;
        }
        if (!_date.IsBlank && _date.Date is null)
        {
            _dateField.Error = "Not a valid date value.";
            _date.Invalid = true;
            return;
        }
        var draft = new InvoiceDraft(
            (_customer.SelectedItem as CustomerChoice)?.Id, _invoiceNumber.Text, _date.Date, _notes.Text,
            _term1.Text, _term2.Text, _paid.IsChecked == true, _lines.Rows.Select(InvoiceRows.ToDraft).ToList());
        Try(() =>
        {
            if (_id is { } id)
            {
                var notice = Store.Invoices.Update(id, draft);
                Shell.Navigate(Routes.Invoice(Shell, id), notice);
            }
            else
            {
                var created = Store.Invoices.Create(draft);
                Shell.Navigate(Routes.Invoice(Shell, created.Id), created.Notice);
            }
        });
    }

    protected override void ShowFieldErrors(IReadOnlyDictionary<string, string> errors)
    {
        if (errors.TryGetValue("customer", out var c)) { _customerField.Error = c; Input.SetInvalid(_customer, true); }
        if (errors.TryGetValue("invoice_number", out var n)) { _numberField.Error = n; Input.SetInvalid(_invoiceNumber, true); }
        if (errors.TryGetValue("date", out var d)) { _dateField.Error = d; _date.Invalid = true; }
    }
}
