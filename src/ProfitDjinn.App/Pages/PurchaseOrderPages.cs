using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ProfitDjinn.App.Controls;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.App.Shell;
using ProfitDjinn.Core;
using ProfitDjinn.Core.Data;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Pdf;
using ProfitDjinn.Core.Rules;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.App.Pages;

/// <summary>2.9. Shared bits of the purchase order screens.</summary>
internal static class PoUi
{
    public static string Badge(string status) => status switch
    {
        PurchaseOrderStatus.Received => "success",
        PurchaseOrderStatus.Cancelled => "secondary",
        _ => "info",
    };

    /// <summary>The PO table, used on the list and on a vendor's page.</summary>
    public static List<Column<PurchaseOrder>> Columns(MainWindow shell, bool showVendor) => new List<Column<PurchaseOrder>>
    {
        new("PO #", Ui.Auto, o => Ui.Link(o.PoNumber, () => shell.Navigate(Routes.PurchaseOrder(shell, o.Id)), mono: true)),
        showVendor
            ? new("Vendor", Ui.Star(), o => Ui.Link(o.VendorName, () => shell.Navigate(Routes.Vendor(shell, o.VendorId)), bold: false))
            : new("Items", Ui.Star(), o => Ui.Muted(Summary(o), 13.6).Also(t => t.TextTrimming = TextTrimming.CharacterEllipsis)),
        new("Date", Ui.Auto, o => Ui.Muted(Ui.Date(o.Date), 13.6)),
        new("Total", Ui.Auto, o => Ui.Text(Ui.Money(o.Total), "Money"), HorizontalAlignment.Right),
        new("Status", Ui.Auto, o => Ui.Badge(o.StatusLabel, Badge(o.Status)), HorizontalAlignment.Center),
        new("", Ui.Auto, o => Ui.Row(4,
            Ui.IconButton("eye", "Btn.OutlineSecondary", "View", () => shell.Navigate(Routes.PurchaseOrder(shell, o.Id))),
            Ui.IconButton("pencil", "Btn.OutlinePrimary", "Edit", () => shell.Navigate(Routes.EditPurchaseOrder(shell, o.Id)))), HorizontalAlignment.Right),
    };

    /// <summary>"Window glass, Glazing tape +2 more".</summary>
    private static string Summary(PurchaseOrder o)
    {
        var names = o.Lines.Select(l => l.Description.Split('\n')[0]).Take(2).ToList();
        string text = string.Join(", ", names);
        return o.Lines.Count > 2 ? $"{text} +{o.Lines.Count - 2} more" : text;
    }

    public static byte[] Render(Store store, PurchaseOrder o) => PurchaseOrderPdf.Render(o, store.Settings.Company());

    public static string FileName(PurchaseOrder o) => InvoiceOutput.SafeFileName("PO " + o.PoNumber) + ".pdf";
}

/// <summary>2.9. The purchase order list: status tabs, search, paged like the invoice list.</summary>
public sealed class PurchaseOrdersPage : AppPage
{
    private readonly string? _status;
    private readonly string _search;

    public override string NavKey => "purchaseorders";
    public override IReadOnlyList<Crumb> Crumbs => new[] { new Crumb("Purchase Orders") };

    public PurchaseOrdersPage(MainWindow shell, string? status, string search, int pageNumber = 1) : base(shell)
    {
        _status = status;
        _search = search;
        var orders = Store.PurchaseOrders.List(status, search);
        var slice = Paging.Slice(orders, pageNumber);
        var (openCount, openTotal) = Store.PurchaseOrders.OpenSummary();

        string lead = $"{orders.Count:N0} purchase order{(orders.Count != 1 ? "s" : "")} shown"
            + (openCount > 0 ? $"  ·  {openCount} open ({Ui.Money(openTotal)})" : "");
        var page = new StackPanel();
        page.Children.Add(Ui.PageHeader("Purchase Orders", lead, null,
            Ui.Button("New Purchase Order", "Btn.Primary", "plus-lg", () => Shell.Navigate(Routes.NewPurchaseOrder(Shell)))));

        var tabs = new StackPanel { Orientation = Orientation.Horizontal };
        tabs.Children.Add(Tab("All", null));
        tabs.Children.Add(Tab("Open", PurchaseOrderStatus.Open));
        tabs.Children.Add(Tab("Received", PurchaseOrderStatus.Received));
        tabs.Children.Add(Tab("Cancelled", PurchaseOrderStatus.Cancelled));
        var box = Ui.TextBox(search, "Search PO # or vendor…", 260).Also(t => { t.Style = Ui.Style("Input.Small"); Input.SetPrefixGlyph(t, "search"); });
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Run(_status, box.Text); };
        var searchRow = Ui.Row(8, box);
        if (search.Length > 0) searchRow.Children.Add(Ui.Button(null, "Btn.OutlineSecondary", "x-lg", () => Run(_status, ""), small: true).Margin(-8, 0, 0, 0));
        searchRow.Children.Add(Ui.Button("Search", "Btn.Secondary", null, () => Run(_status, box.Text), small: true));
        page.Children.Add(Ui.Card(Ui.Row(16, tabs, searchRow), bodyPadding: new Thickness(16)).Margin(0, 0, 0, 24));

        var columns = PoUi.Columns(Shell, showVendor: true);
        FrameworkElement body;
        if (orders.Count == 0)
            body = Ui.Stack(0, Table.Build(columns, Array.Empty<PurchaseOrder>()),
                Ui.Empty("cart3", "No purchase orders found.", "Create one now.", () => Shell.Navigate(Routes.NewPurchaseOrder(Shell))));
        else
        {
            var footer = new List<UIElement?[]>
            {
                new UIElement?[]
                {
                    Ui.Bold(slice.PageCount > 1 ? $"Total, all {orders.Count:N0}" : "Total shown"), null, null,
                    Ui.Text(Ui.Money(PyMath.Sum(orders, o => o.Total)), "Money").Also(t => t.FontWeight = FontWeights.Bold),
                    null, null,
                },
            };
            body = Table.Build(columns, slice.Items, onRowClick: o => Shell.Navigate(Routes.PurchaseOrder(Shell, o.Id)), footer: footer);
            if (Pager.Build(slice, "purchase orders", p => Shell.Navigate(Routes.PurchaseOrders(Shell, _status, _search, p))) is { } pager)
                body = Ui.Stack(0, body, pager);
        }
        page.Children.Add(Ui.Card(body, bodyPadding: new Thickness(0)));
        Content = page;
    }

    private Button Tab(string text, string? status)
    {
        var b = Ui.Button(text, status == _status ? "Btn.Secondary" : "Btn.OutlineSecondary", null, () => Run(status, _search), small: true);
        b.Margin = new Thickness(0, 0, 4, 0);
        return b;
    }

    private void Run(string? status, string search) => Shell.Navigate(Routes.PurchaseOrders(Shell, status, search.Trim()));
}

/// <summary>2.9. One purchase order: lines, vendor, details, and print / status actions.</summary>
public sealed class PurchaseOrderDetailPage : AppPage
{
    private readonly PurchaseOrder _o;

    public override string NavKey => "purchaseorders";
    public override IReadOnlyList<Crumb> Crumbs => new[] { new Crumb("Purchase Orders", () => Shell.Navigate(Routes.PurchaseOrders(Shell))), new Crumb(_o.PoNumber) };

    public PurchaseOrderDetailPage(MainWindow shell, long id) : base(shell)
    {
        _o = Store.PurchaseOrders.Get(id);
        var o = _o;
        var v = o.Vendor ?? new Vendor();

        var lead = new WrapPanel();
        lead.Children.Add(Ui.Link(v.Name, () => Shell.Navigate(Routes.Vendor(Shell, o.VendorId)), bold: false).Also(l => l.FontSize = 14));
        lead.Children.Add(Ui.Text($"  ·  {Ui.LongDate(o.Date)}", "Lead"));
        var actions = new List<UIElement>
        {
            Ui.Button("Preview", "Btn.OutlineSecondary", "eye", () => Try(() => PdfPreviewWindow.Show(Shell, PoUi.Render(Store, o), $"Purchase order {o.PoNumber}",
                save: (pdf, w) => InvoiceOutput.SavePdf(pdf, PoUi.FileName(o), w),
                print: (pdf, _) => InvoiceOutput.Print(pdf, $"Purchase order {o.PoNumber}")))),
            Ui.Button("Print", "Btn.OutlineSecondary", "printer", () => Try(() => InvoiceOutput.Print(PoUi.Render(Store, o), $"Purchase order {o.PoNumber}"))),
            Ui.Button("PDF", "Btn.OutlineSecondary", "file-earmark-pdf", () => Try(() => InvoiceOutput.SavePdf(PoUi.Render(Store, o), PoUi.FileName(o), Shell))),
            Ui.Button("Edit", "Btn.Primary", "pencil", () => Shell.Navigate(Routes.EditPurchaseOrder(Shell, o.Id))),
        };
        if (o.Status == PurchaseOrderStatus.Open)
        {
            actions.Add(Ui.Button("Mark Received", "Btn.Success", "box-arrow-in-down", () => SetStatus(PurchaseOrderStatus.Received)));
            actions.Add(Ui.Button("Cancel PO", "Btn.OutlineWarning", "x-circle", CancelOrder));
        }
        else actions.Add(Ui.Button("Reopen", "Btn.OutlineSecondary", "arrow-counterclockwise", () => SetStatus(PurchaseOrderStatus.Open)));
        actions.Add(Ui.Button("Delete", "Btn.OutlineDanger", "trash", Delete));

        var header = (DockPanel)Ui.PageHeader(o.PoNumber, " ", Ui.Badge(o.StatusLabel, PoUi.Badge(o.Status), big: true), actions.ToArray());
        var left = (StackPanel)header.Children[1];
        ((TextBlock)((StackPanel)left.Children[0]).Children[0]).SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        left.Children.RemoveAt(1);
        left.Children.Add(lead.Margin(0, 4, 0, 0));

        TextBlock Right(string text, string style = "Body") => Ui.Text(text, style).Also(t => t.HorizontalAlignment = HorizontalAlignment.Right);
        var lineCols = new List<Column<PurchaseOrderLine>>
        {
            new("Qty", Ui.Auto, l => Ui.Muted(InvoiceDetailPage.QtyText(l.Quantity)), HorizontalAlignment.Center),
            new("Description", Ui.Star(), l => Ui.Text(l.Description, "Body", wrap: true)),
            new("Price", Ui.Auto, l => Ui.Muted(Ui.Money(l.UnitPrice)), HorizontalAlignment.Right),
            new("Amount", Ui.Auto, l => Ui.Text(Ui.Money(l.Amount), "Body"), HorizontalAlignment.Right),
        };
        var footer = new List<UIElement?[]>
        {
            new UIElement?[] { null, null, Right("TOTAL").Also(t => t.FontWeight = FontWeights.Bold), Right(Ui.Money(o.Total)).Also(t => { t.FontWeight = FontWeights.Bold; t.FontSize = 20; }) },
        };
        var mainCol = new StackPanel();
        mainCol.Children.Add(Ui.Card(Table.Build(lineCols, o.Lines, footer: footer), "Line Items", "list-ul",
            headerRight: Ui.Badge(o.Lines.Count.ToString(), "secondary"), bodyPadding: new Thickness(0)));
        if (!string.IsNullOrWhiteSpace(o.Notes))
            mainCol.Children.Add(Ui.Card(Ui.Text(o.Notes, "Body", 14.4, wrap: true), "Notes", "sticky", "Warning").Margin(0, 24, 0, 0));

        var vendor = new StackPanel();
        vendor.Children.Add(Ui.Link(v.Name, () => Shell.Navigate(Routes.Vendor(Shell, o.VendorId))).Also(l => { l.FontSize = 14.4; l.HorizontalAlignment = HorizontalAlignment.Left; }));
        if (!string.IsNullOrEmpty(v.Contact)) vendor.Children.Add(Ui.Muted($"Contact: {v.Contact}", 14.4));
        if (!string.IsNullOrEmpty(v.Address)) vendor.Children.Add(Ui.Text(v.Address, "Body", 14.4));
        string cityLine = string.Join(", ", new[] { v.City, v.State, v.ZipCode }.Where(x => !string.IsNullOrEmpty(x)));
        if (cityLine.Length > 0) vendor.Children.Add(Ui.Text(cityLine, "Body", 14.4));
        if (!string.IsNullOrEmpty(v.Phone)) vendor.Children.Add(Ui.Text(v.Phone, "Body", 14.4));
        if (!string.IsNullOrEmpty(v.Email)) vendor.Children.Add(Ui.Link(v.Email, () => CustomersPage.MailTo(v.Email!), bold: false).Also(l => { l.FontSize = 14.4; l.HorizontalAlignment = HorizontalAlignment.Left; }));

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
        Row("PO #", Ui.Text(o.PoNumber, "Body", 14.4).WithResource(TextBlock.FontFamilyProperty, "MonoFont"));
        Row("Date", Ui.Text(Ui.Date(o.Date), "Body", 14.4));
        Row("Total", Ui.Text(Ui.Money(o.Total), "Strong", 14.4));
        Row("Status", Ui.Badge(o.StatusLabel, PoUi.Badge(o.Status)));
        if (o.ReceivedDate is { } received) Row("Received", Ui.Text(Ui.Date(received), "Body", 14.4));
        if (SqlFormat.ParseDateTime(o.CreatedAt) is { } created)
            Row("Created", Ui.Muted(Ui.Date(DateOnly.FromDateTime(created)), 12.8));

        var side = Ui.Stack(16, Ui.Card(vendor, "Vendor", "shop"), Ui.Card(details, "Details", "info-circle"));
        var page = new StackPanel();
        page.Children.Add(header);
        page.Children.Add(Ui.Columns(24, (Ui.Star(2), mainCol), (Ui.Star(1), side)));
        Content = page;
    }

    private void SetStatus(string status) => Try(() => Shell.Reload(Store.PurchaseOrders.SetStatus(_o.Id, status)));

    private async void CancelOrder()
    {
        if (!await Shell.Confirm($"Cancel purchase order {_o.PoNumber}? It is kept, marked Cancelled; Reopen undoes it.", "Cancel PO", title: "Cancel this purchase order?")) return;
        SetStatus(PurchaseOrderStatus.Cancelled);
    }

    private async void Delete()
    {
        if (!await Shell.Confirm($"Delete purchase order {_o.PoNumber}? This cannot be undone.", "Delete", danger: true)) return;
        Try(() => Shell.Navigate(Routes.PurchaseOrders(Shell), Store.PurchaseOrders.Delete(_o.Id)));
    }
}

/// <summary>2.9. New and Edit Purchase Order: vendor, number, date, lines and notes.</summary>
public sealed class PurchaseOrderFormPage : AppPage
{
    private readonly long? _id;
    private readonly string _number;
    private readonly RecordPicker _vendor;
    private readonly TextBox _poNumber, _notes;
    private readonly DateBox _date;
    private readonly LineBuilder _lines;
    private readonly TextBlock _summaryTotal = new() { FontWeight = FontWeights.Bold, FontSize = 24 };
    private readonly Field _vendorField, _numberField, _dateField;

    public override string NavKey => "purchaseorders";

    public override IReadOnlyList<Crumb> Crumbs
    {
        get
        {
            var list = new List<Crumb> { new("Purchase Orders", () => Shell.Navigate(Routes.PurchaseOrders(Shell))) };
            if (_id is { } id) list.Add(new Crumb(_number, () => Shell.Navigate(Routes.PurchaseOrder(Shell, id))));
            list.Add(new Crumb(_id is null ? "New Purchase Order" : "Edit"));
            return list;
        }
    }

    public PurchaseOrderFormPage(MainWindow shell, long? id, long? vendorId) : base(shell)
    {
        _id = id;
        PurchaseOrder? existing = id is { } oid ? Store.PurchaseOrders.Get(oid) : null;
        var draft = existing is null ? Store.PurchaseOrders.NewDraft(vendorId) : null;
        _number = existing?.PoNumber ?? "";

        _vendor = QuickAdd.VendorPicker(Shell, existing is not null ? existing.VendorId : draft!.VendorId, (_, _) => { }, "Type a vendor name");
        _poNumber = Ui.TextBox(existing is not null ? existing.PoNumber : draft!.PoNumber).Also(t => { t.CharacterCasing = CharacterCasing.Upper; t.SetResourceReference(TextBox.FontFamilyProperty, "MonoFont"); });
        _date = Ui.DateBox(existing is not null ? existing.Date : draft!.Date);
        _notes = Ui.TextArea(existing is not null ? existing.Notes ?? "" : "", 56);

        _vendorField = Ui.Field("Vendor", _vendor, required: true);
        _numberField = Ui.Field("PO #", _poNumber, required: true);
        _dateField = Ui.Field("Date", _date, required: true);
        var details = Ui.Stack(0,
            Ui.Columns(16, (Ui.Star(2), _vendorField), (Ui.Star(1), _numberField)),
            Ui.Columns(16, (Ui.Star(1), _dateField), (Ui.Star(2), new Border())));

        _lines = new LineBuilder("No lines yet. Click Add Line to get started.");
        _lines.Changed += () => _summaryTotal.Text = Ui.MoneyGrouped(_lines.Total);
        if (existing is not null)
            _lines.SetRows(existing.Lines.Select(l => new InvoiceRowInput(l.Description, InvoiceDetailPage.QtyText(l.Quantity), PyMath.JsToFixedText(l.UnitPrice, 2))));
        else _lines.AddRow("", "1", "", byUser: false);
        var addLine = Ui.Button("Add Line", "Btn.OutlinePrimary", "plus-lg", () => _lines.AddRow("", "1", "", focus: true), small: true);

        var left = Ui.Stack(24,
            Ui.Card(details, "Purchase Order Details"),
            Ui.Card(_lines, "Line Items", "list-ul", headerRight: addLine, bodyPadding: new Thickness(0)),
            Ui.Card(Ui.Field("Notes", _notes, hint: "Printed under the lines, e.g. delivery instructions.")));

        var summary = new StackPanel();
        var totalRow = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        DockPanel.SetDock(_summaryTotal, Dock.Right);
        _summaryTotal.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        totalRow.Children.Add(_summaryTotal);
        totalRow.Children.Add(Ui.Muted("PO Total", 15).Also(t => t.VerticalAlignment = VerticalAlignment.Center));
        summary.Children.Add(totalRow);
        summary.Children.Add(new Border { Style = Ui.Style("Rule"), Margin = new Thickness(0, 0, 0, 16) });
        summary.Children.Add(Ui.Button("Save Purchase Order", "Btn.Primary", "check-lg", Save).Also(b => { b.HorizontalAlignment = HorizontalAlignment.Stretch; b.Padding = new Thickness(16, 9, 16, 9); b.FontSize = 17.6; }));
        summary.Children.Add(Ui.Button("Cancel", "Btn.OutlineSecondary", null, () => Shell.Navigate(_id is { } i ? Routes.PurchaseOrder(Shell, i) : Routes.PurchaseOrders(Shell)))
            .Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch).Margin(0, 8, 0, 0));
        _summaryTotal.Text = Ui.MoneyGrouped(_lines.Total);

        var page = new StackPanel();
        page.Children.Add(Ui.PageHeader(_id is null ? "New Purchase Order" : $"Edit Purchase Order {_number}"));
        page.Children.Add(Ui.Columns(24, (Ui.Star(2), left), (Ui.Star(1), Ui.Card(summary, "Summary"))));
        Content = page;
    }

    public override void OnShown()
    {
        if (_vendor.SelectedId is null) _vendor.FocusBox();
        else _poNumber.Focus();
    }

    private void Save()
    {
        _vendorField.Error = _numberField.Error = _dateField.Error = null;
        _vendor.Invalid = false; Input.SetInvalid(_poNumber, false); _date.Invalid = false;
        if (_vendor.HasUnmatchedText)
        {
            ShowFieldErrors(new Dictionary<string, string> { ["vendor"] = "No vendor has that name. Click Add Vendor, or pick one from the list." });
            return;
        }
        if (!_date.IsBlank && _date.Date is null)
        {
            _dateField.Error = "Not a valid date value.";
            _date.Invalid = true;
            return;
        }
        var draft = new PurchaseOrderDraft(_vendor.SelectedId, _poNumber.Text, _date.Date, _notes.Text,
            _lines.Rows.Select(PurchaseOrderLineDraft.FromRow).ToList());
        Try(() =>
        {
            if (_id is { } id) Shell.Navigate(Routes.PurchaseOrder(Shell, id), Store.PurchaseOrders.Update(id, draft));
            else
            {
                var created = Store.PurchaseOrders.Create(draft);
                Shell.Navigate(Routes.PurchaseOrder(Shell, created.Id), created.Notice);
            }
        });
    }

    protected override void ShowFieldErrors(IReadOnlyDictionary<string, string> errors)
    {
        if (errors.TryGetValue("vendor", out var v)) { _vendorField.Error = v; _vendor.Invalid = true; }
        if (errors.TryGetValue("po_number", out var n)) { _numberField.Error = n; Input.SetInvalid(_poNumber, true); }
        if (errors.TryGetValue("date", out var d)) { _dateField.Error = d; _date.Invalid = true; }
        if (errors.TryGetValue("lines", out var l)) Shell.ShowError(l);
    }
}
