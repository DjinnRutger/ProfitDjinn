using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Win32;
using ProfitDjinn.App.Controls;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.App.Shell;
using ProfitDjinn.Core;
using ProfitDjinn.Core.Data;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Rules;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.App.Pages;

/// <summary>2.2. The expense list: figures, All / Unpaid / Paid tabs, search and category filter. Laid out like the invoice list.</summary>
public sealed class ExpensesPage : AppPage
{
    private readonly ExpenseFilter _filter;
    private readonly string _search;
    private readonly long? _category;

    public override string NavKey => "expenses";
    public override IReadOnlyList<Crumb> Crumbs => new[] { new Crumb("Expenses") };

    public ExpensesPage(MainWindow shell, ExpenseFilter filter, string search, long? categoryId) : base(shell)
    {
        _filter = filter;
        _search = search;
        _category = categoryId;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var expenses = Store.Expenses.List(filter, search, categoryId);
        var sum = Store.Expenses.Summary();

        var page = new StackPanel();
        page.Children.Add(Ui.PageHeader("Expenses", $"{expenses.Count} expense{(expenses.Count != 1 ? "s" : "")} shown", null,
            Ui.Button("Recurring", "Btn.OutlineSecondary", "arrow-repeat", () => Shell.Navigate(Routes.Recurring(Shell))),
            Ui.Button("Categories", "Btn.OutlineSecondary", "tags", () => Shell.Navigate(Routes.ExpenseCategories(Shell))),
            Ui.Button("New Expense", "Btn.Primary", "plus-lg", () => Shell.Navigate(Routes.NewExpense(Shell)))));

        // ---- figures
        var tiles = new UniformGrid { Columns = 3, Margin = new Thickness(-8, 0, -8, 24) };
        tiles.Children.Add(Tile(Ui.Money(sum.YearTotal), $"{sum.Year} Expenses", "wallet2", "primary", null, muted: sum.YearTotal <= 0, accent: "bottom",
            () => Run(ExpenseFilter.All, "", null)));
        tiles.Children.Add(Tile(Ui.Money(sum.UnpaidTotal), "Unpaid", "hourglass-split", "warning",
            sum.UnpaidCount > 0 ? Ui.Badge(sum.UnpaidCount.ToString(), "warningdark") : null, muted: sum.UnpaidCount == 0, accent: sum.UnpaidCount > 0 ? "warning" : "",
            () => Run(ExpenseFilter.Unpaid, "", null)));
        tiles.Children.Add(Tile(Ui.Money(sum.OverdueTotal), "Overdue", "exclamation-triangle", "danger",
            sum.OverdueCount > 0 ? Ui.Badge(sum.OverdueCount.ToString(), "danger") : null, muted: sum.OverdueCount == 0, accent: sum.OverdueCount > 0 ? "danger" : "",
            () => Run(ExpenseFilter.Unpaid, "", null)));
        page.Children.Add(tiles);

        // ---- tabs, search, category
        var tabs = new StackPanel { Orientation = Orientation.Horizontal };
        tabs.Children.Add(Tab("All", null, "Btn.OutlineSecondary", ExpenseFilter.All, null));
        tabs.Children.Add(Tab("Unpaid / Partial", "exclamation-circle", "Btn.OutlineWarning", ExpenseFilter.Unpaid,
            sum.UnpaidCount > 0 ? Ui.Badge(sum.UnpaidCount.ToString(), "warningdark").Margin(6, 0, 0, 0) : null));
        tabs.Children.Add(Tab("Paid", "check-circle", "Btn.OutlineSuccess", ExpenseFilter.Paid, null));

        var box = Ui.TextBox(search, "Search vendor, description, ref…", 300).Also(t => { t.Style = Ui.Style("Input.Small"); Input.SetPrefixGlyph(t, "search"); });
        var cat = ExpenseUi.CategoryPicker(Store, categoryId, allowNone: true).Also(c => { c.MinWidth = 180; ((List<Choice>)c.ItemsSource)[0] = new Choice(null, "All categories"); c.Items.Refresh(); c.SelectedIndex = Math.Max(0, c.SelectedIndex); });
        cat.SelectionChanged += (_, _) => Run(_filter, box.Text, ExpenseUi.SelectedId(cat));
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Run(_filter, box.Text, _category); };
        var searchRow = Ui.Row(8, box);
        if (search.Length > 0) searchRow.Children.Add(Ui.Button(null, "Btn.OutlineSecondary", "x-lg", () => Run(_filter, "", _category), small: true).Margin(-8, 0, 0, 0));
        searchRow.Children.Add(Ui.Button("Search", "Btn.Secondary", null, () => Run(_filter, box.Text, _category), small: true));
        searchRow.Children.Add(cat);
        page.Children.Add(Ui.Card(Ui.Row(16, tabs, searchRow), bodyPadding: new Thickness(16)).Margin(0, 0, 0, 24));

        // ---- table
        var columns = ExpenseUi.Columns(Shell, today, showVendor: true);
        FrameworkElement body;
        if (expenses.Count == 0)
            body = Ui.Stack(0, Table.Build(columns, Array.Empty<Expense>()), Ui.Empty("wallet2", "No expenses found.", "Add one now.", () => Shell.Navigate(Routes.NewExpense(Shell))));
        else
        {
            double balance = PyMath.Sum(expenses, e => e.BalanceDue);
            var footer = new List<UIElement?[]>
            {
                new UIElement?[]
                {
                    Ui.Bold("Total shown"), null, null, null,
                    Ui.Text(Ui.Money(PyMath.Sum(expenses, e => e.Amount)), "Money").Also(t => t.FontWeight = FontWeights.Bold),
                    balance > 0 ? Ui.Text(Ui.Money(balance), "Money").Also(t => t.FontWeight = FontWeights.Bold).WithResource(TextBlock.ForegroundProperty, "DangerText") : null,
                    null, null,
                },
            };
            body = Table.Build(columns, expenses, onRowClick: e => Shell.Navigate(Routes.Expense(Shell, e.Id)), footer: footer);
        }
        page.Children.Add(Ui.Card(body, bodyPadding: new Thickness(0)));
        Content = page;
    }

    private static StatTile Tile(string value, string label, string glyph, string tone, object? badge, bool muted, string accent, Action open)
    {
        var t = new StatTile { Value = value, Label = label, Glyph = glyph, Tone = tone, Badge = badge, Muted = muted, Accent = accent, Margin = new Thickness(8, 0, 8, 0) };
        t.Click += (_, _) => open();
        return t;
    }

    private Button Tab(string text, string? glyph, string style, ExpenseFilter filter, UIElement? badge)
    {
        var content = Ui.Row(0, Ui.Text(text, "Body", 12.8).Also(t => t.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1) })));
        if (badge is not null) content.Children.Add(badge);
        var b = new Button { Content = content, Style = Ui.Style(style) };
        Btn.SetSmall(b, true);
        if (glyph is not null) Btn.SetIcon(b, glyph);
        if (filter == _filter)
        {
            b.SetResourceReference(Button.BackgroundProperty, style switch
            {
                "Btn.OutlineWarning" => "Warning",
                "Btn.OutlineSuccess" => "Success",
                _ => "Secondary",
            });
            b.Foreground = style == "Btn.OutlineWarning" ? System.Windows.Media.Brushes.Black : System.Windows.Media.Brushes.White;
        }
        b.Click += (_, _) => Run(filter, _search, _category);
        return b;
    }

    private void Run(ExpenseFilter filter, string search, long? category) => Shell.Navigate(Routes.Expenses(Shell, filter, search.Trim(), category));
}

/// <summary>2.2. New Expense and Edit Expense.</summary>
public sealed class ExpenseFormPage : AppPage
{
    private readonly long? _id;
    private readonly string _desc;
    private readonly Dictionary<string, Field> _fields = new();
    private readonly RecordPicker _vendor;
    private readonly ComboBox _category, _method;
    private readonly DateBox _date, _due, _paidOn;
    private readonly TextBox _description, _reference, _amount, _notes, _check;
    private readonly CheckBox _paid;
    private readonly Dictionary<long, long?> _vendorDefaults;

    public override string NavKey => "expenses";

    public override IReadOnlyList<Crumb> Crumbs
    {
        get
        {
            var list = new List<Crumb> { new("Expenses", () => Shell.Navigate(Routes.Expenses(Shell))) };
            if (_id is { } id) list.Add(new Crumb(_desc, () => Shell.Navigate(Routes.Expense(Shell, id))));
            list.Add(new Crumb(_id is null ? "New Expense" : "Edit Expense"));
            return list;
        }
    }

    public ExpenseFormPage(MainWindow shell, long? id, long? vendorId) : base(shell)
    {
        _id = id;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var e = id is { } eid ? Store.Expenses.Get(eid) : new Expense { VendorId = vendorId, Date = today };
        _desc = e.Description;
        _vendorDefaults = Store.Vendors.ActiveForPicker().ToDictionary(v => v.Id, v => v.DefaultCategoryId);
        long? category = id is null && vendorId is { } v0 ? _vendorDefaults.GetValueOrDefault(v0) : e.CategoryId;

        _vendor = QuickAdd.VendorPicker(Shell, e.VendorId, (added, def) => _vendorDefaults![added] = def);
        _category = ExpenseUi.CategoryPicker(Store, id is null ? category : e.CategoryId);
        _date = Ui.DateBox(e.Date);
        _due = Ui.DateBox(e.DueDate);
        _description = Ui.TextBox(e.Description, "e.g. Printer ink, March phone bill");
        _reference = Ui.TextBox(e.Reference, "Bill or receipt number (optional)");
        _amount = Ui.TextBox(id is null ? "" : e.Amount.ToString("0.00", CultureInfo.InvariantCulture));
        Input.SetPrefix(_amount, "$");
        _notes = Ui.TextArea(e.Notes, 70);
        _paid = ExpenseUi.Switch("Already paid", false);
        _method = ExpenseUi.MethodPicker(null);
        _paidOn = Ui.DateBox(today);
        _check = Ui.TextBox(null, "e.g. 1042").Also(t => t.MaxLength = 50);

        _vendor.SelectionChanged += () =>
        {
            if (_vendor.SelectedId is { } v && _vendorDefaults.GetValueOrDefault(v) is { } def)
            {
                int at = ((List<Choice>)_category.ItemsSource).FindIndex(c => c.Id == def);
                if (at >= 0) _category.SelectedIndex = at;
            }
        };

        var form = new StackPanel();
        form.Children.Add(Ui.Columns(16, (Ui.Star(), F("vendor_id", "Vendor", _vendor)), (Ui.Star(), F("category_id", "Category", _category, required: true))));
        form.Children.Add(F("description", "Description", _description, required: true));
        form.Children.Add(Ui.Columns(16,
            (Ui.Star(), F("amount", "Amount", _amount, required: true)),
            (Ui.Star(), F("date", "Date", _date, required: true)),
            (Ui.Star(), F("due_date", "Due Date", _due).Also(f => f.Hint = "Optional. When the bill must be paid by."))));
        form.Children.Add(F("reference", "Reference", _reference));
        form.Children.Add(F("notes", "Notes", _notes));

        if (id is null)
        {
            var paidRow = Ui.Columns(16,
                (Ui.Star(), F("paid_method", "Paid With", _method)),
                (Ui.Star(), F("paid_date", "Paid On", _paidOn)),
                (Ui.Star(), F("check_number", "Check Number", _check)));
            var checkField = _fields["check_number"];
            void Sync()
            {
                paidRow.Visibility = _paid.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
                checkField.Visibility = ExpenseUi.SelectedMethod(_method) == PaymentMethods.Check ? Visibility.Visible : Visibility.Hidden;
            }
            _paid.Checked += (_, _) => Sync();
            _paid.Unchecked += (_, _) => Sync();
            _method.SelectionChanged += (_, _) => Sync();
            Sync();
            form.Children.Add(_paid.Margin(0, 4, 0, 12));
            form.Children.Add(paidRow);
        }
        else if (e.AmountPaid > 0)
            form.Children.Add(Ui.Muted($"Payments so far: {Ui.Money(e.AmountPaid)}. Record or delete payments on the expense page.", 13.6).Also(t => t.TextWrapping = TextWrapping.Wrap));

        form.Children.Add(new Border { Style = Ui.Style("Rule"), Margin = new Thickness(0, 24, 0, 24) });
        form.Children.Add(Ui.Row(8,
            Ui.Button("Save Expense", "Btn.Primary", "check-lg", Save),
            Ui.Button("Cancel", "Btn.OutlineSecondary", null, () => Shell.Navigate(_id is { } i ? Routes.Expense(Shell, i) : Routes.Expenses(Shell)))));

        var page = new StackPanel();
        page.Children.Add(Ui.PageHeader(_id is null ? "New Expense" : "Edit Expense"));
        var holder = new Grid();
        holder.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star(1) });
        holder.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star(4) });
        holder.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star(1) });
        var card = Ui.Card(form);
        Grid.SetColumn(card, 1);
        holder.Children.Add(card);
        page.Children.Add(holder);
        Content = page;

        foreach (var box in new[] { _description, _reference, _amount, _date.TextBox, _due.TextBox })
            box.KeyDown += (_, k) => { if (k.Key == Key.Enter) Save(); };
    }

    public override void OnShown()
    {
        if (_vendor.SelectedId is null && _id is null) _vendor.FocusBox();
        else _description.Focus();
    }

    private Field F(string key, string label, UIElement input, bool required = false) => _fields[key] = Ui.Field(label, input, required);

    private void Save()
    {
        foreach (var f in _fields.Values) { f.Error = null; if (f.Content is TextBox t) Input.SetInvalid(t, false); }
        _vendor.Invalid = false;
        if (ExpenseUi.VendorProblem(_vendor) is { } vendorProblem) { ShowFieldErrors(new Dictionary<string, string> { ["vendor_id"] = vendorProblem }); Shell.ShowError(vendorProblem); return; }
        double? amount = null;
        if (_amount.Text.Trim().Length > 0)
        {
            if (!Ui.TryParseCents(_amount.Text, out decimal dec)) { ShowFieldErrors(new Dictionary<string, string> { ["amount"] = Ui.CentsError }); return; }
            amount = (double)dec;
        }
        var bad = new Dictionary<string, string>();
        if (!_date.IsBlank && _date.Date is null) bad["date"] = "Enter a date like 2026-03-15.";
        if (!_due.IsBlank && _due.Date is null) bad["due_date"] = "Enter a date like 2026-03-15.";
        PaidNow? paid = null;
        if (_id is null && _paid.IsChecked == true)
        {
            string method = ExpenseUi.SelectedMethod(_method);
            if (_paidOn.Date is null) bad["paid_date"] = "Enter the date it was paid.";
            if (method == PaymentMethods.Check && _check.Text.Trim().Length == 0) bad["check_number"] = "Enter the check number.";
            paid = new PaidNow(method, _paidOn.Date, _check.Text);
        }
        if (bad.Count > 0) { ShowFieldErrors(bad); Shell.ShowError(string.Join(" ", bad.Values.Distinct())); return; }

        var draft = new ExpenseDraft(_vendor.SelectedId, ExpenseUi.SelectedId(_category), _date.Date, _due.Date,
            _description.Text, _reference.Text, amount, _notes.Text, paid);
        Try(() =>
        {
            if (_id is { } id) Shell.Navigate(Routes.Expense(Shell, id), Store.Expenses.Update(id, draft));
            else
            {
                var created = Store.Expenses.Create(draft);
                Shell.Navigate(Routes.Expense(Shell, created.Id), created.Notice);
            }
        });
    }

    protected override void ShowFieldErrors(IReadOnlyDictionary<string, string> errors)
    {
        foreach (var (key, message) in errors)
            if (_fields.TryGetValue(key, out var f))
            {
                f.Error = message;
                if (f.Content is TextBox t) Input.SetInvalid(t, true);
                if (f.Content is Controls.RecordPicker rp) rp.Invalid = true;
            }
    }
}

/// <summary>2.2. One expense: payments, receipts, details. Laid out like an invoice.</summary>
public sealed class ExpenseDetailPage : AppPage
{
    private readonly Expense _e;

    public override string NavKey => "expenses";
    public override IReadOnlyList<Crumb> Crumbs => new[] { new Crumb("Expenses", () => Shell.Navigate(Routes.Expenses(Shell))), new Crumb(_e.Description) };

    public ExpenseDetailPage(MainWindow shell, long id) : base(shell)
    {
        _e = Store.Expenses.Get(id);
        var e = _e;
        var today = DateOnly.FromDateTime(DateTime.Today);

        // ---- header
        var lead = new WrapPanel();
        if (e.VendorId is { } vid) lead.Children.Add(Ui.Link(e.VendorName, () => Shell.Navigate(Routes.Vendor(Shell, vid)), bold: false).Also(l => l.FontSize = 14));
        else lead.Children.Add(Ui.Text("No vendor", "Lead"));
        lead.Children.Add(Ui.Text($"  ·  {Ui.LongDate(e.Date)}", "Lead"));
        var actions = new List<UIElement>();
        if (e.BalanceDue > 0) actions.Add(Ui.Button("Record Payment", "Btn.Success", "cash-coin", () => PaymentDialog.Open(Shell, e)));
        actions.Add(Ui.Button("Attach Receipt", "Btn.OutlineSecondary", "paperclip", Attach));
        actions.Add(Ui.Button("Edit", "Btn.Primary", "pencil", () => Shell.Navigate(Routes.EditExpense(Shell, e.Id))));
        actions.Add(Ui.Button("Delete", "Btn.OutlineDanger", "trash", Delete));
        var header = (DockPanel)Ui.PageHeader(e.Description, " ", ExpenseUi.StatusBadge(e, today, big: true), actions.ToArray());
        var left = (StackPanel)header.Children[1];
        left.Children.RemoveAt(1);
        left.Children.Add(lead.Margin(0, 4, 0, 0));

        var mainCol = new StackPanel();
        TextBlock Right(string text, string style = "Body") => Ui.Text(text, style).Also(t => t.HorizontalAlignment = HorizontalAlignment.Right);

        // ---- payments
        var payCols = new List<Column<ExpensePayment>>
        {
            new("Date", Ui.Auto, p => Ui.Text(Ui.Date(p.Date), "Body", 14.4)),
            new("Method", Ui.Auto, p => Ui.Text(p.MethodLabel, "Body", 14.4)),
            new("Check #", Ui.Auto, p => Ui.Muted(string.IsNullOrEmpty(p.CheckNumber) ? "—" : p.CheckNumber, 14.4).Also(t => t.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont"))),
            new("Notes", Ui.Star(), p => Ui.Muted(p.Notes, 14.4).Also(t => t.TextWrapping = TextWrapping.Wrap)),
            new("Amount", Ui.Auto, p => Ui.Text(Ui.Money(p.Amount), "Money", 14.4).WithResource(TextBlock.ForegroundProperty, "SuccessText"), HorizontalAlignment.Right),
            new("", Ui.Auto, p => Ui.IconButton("trash", "Btn.OutlineDanger", "Delete payment", () => DeletePayment(p)), HorizontalAlignment.Right),
        };
        FrameworkElement payBody;
        if (e.Payments.Count == 0)
            payBody = Ui.Stack(0, Table.Build(payCols, Array.Empty<ExpensePayment>()), Ui.Empty("cash-coin", "No payments yet.", "Record one.", () => PaymentDialog.Open(Shell, e)));
        else
        {
            var payFooter = new List<UIElement?[]>
            {
                new UIElement?[] { null, null, null, Right("Total Paid").Also(t => t.FontWeight = FontWeights.Bold), Right(Ui.Money(e.AmountPaid)).Also(t => t.FontWeight = FontWeights.Bold).WithResource(TextBlock.ForegroundProperty, "SuccessText"), null },
            };
            if (e.BalanceDue > 0)
                payFooter.Add(new UIElement?[] { null, null, null, Right("Balance Due", "Muted"), Right(Ui.Money(e.BalanceDue), "Strong").WithResource(TextBlock.ForegroundProperty, "DangerText"), null });
            payBody = Table.Build(payCols, e.Payments, footer: payFooter, footerShaded: f => f == 0);
        }
        mainCol.Children.Add(Ui.Card(payBody, "Payment History", "cash-stack", "Success",
            headerRight: Ui.Badge(e.Payments.Count.ToString(), "secondary"), bodyPadding: new Thickness(0)));

        // ---- receipts
        var receiptCols = new List<Column<ExpenseReceipt>>
        {
            new("File", Ui.Star(), r =>
            {
                bool found = Store.Receipts.Resolve(r) is not null;
                var cell = Ui.Row(6, new Icon { Glyph = Path.GetExtension(r.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase) ? "file-earmark-pdf" : "file-earmark-image", Size = 14 }.WithResource(Icon.ForegroundProperty, "TextMuted"));
                cell.Children.Add(found ? Ui.Link(r.FileName, () => OpenReceipt(r), bold: false) : Ui.Muted(r.FileName, 14));
                if (!found) cell.Children.Add(Ui.Badge("Missing", "danger"));
                return cell;
            }),
            new("Size", Ui.Auto, r => Ui.Muted(Size(r.SizeBytes), 13.6), HorizontalAlignment.Right),
            new("Added", Ui.Auto, r => Ui.Muted(SqlFormat.ParseDateTime(r.CreatedAt) is { } c ? Ui.Date(DateOnly.FromDateTime(c.ToLocalTime())) : "—", 13.6)),
            new("", Ui.Auto, r => Ui.Row(4,
                Ui.IconButton("box-arrow-up-right", "Btn.OutlineSecondary", "Open", () => OpenReceipt(r)),
                Ui.IconButton("trash", "Btn.OutlineDanger", "Remove", () => RemoveReceipt(r))), HorizontalAlignment.Right),
        };
        FrameworkElement receiptBody = e.Receipts.Count == 0
            ? Ui.Stack(0, Table.Build(receiptCols, Array.Empty<ExpenseReceipt>()), Ui.Empty("paperclip", "No receipts attached.", "Attach a PDF or photo.", Attach))
            : Table.Build(receiptCols, e.Receipts);
        mainCol.Children.Add(Ui.Card(receiptBody, "Receipts", "paperclip",
            headerRight: Ui.Button("Attach", "Btn.OutlineSecondary", "plus-lg", Attach, small: true), bodyPadding: new Thickness(0)).Margin(0, 24, 0, 0));

        // ---- details
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
        Row("Vendor", e.VendorId is { } v2 ? Ui.Link(e.VendorName, () => Shell.Navigate(Routes.Vendor(Shell, v2)), bold: false).Also(l => l.FontSize = 14.4) : Ui.Muted("—", 14.4));
        Row("Category", Ui.Text(Ui.Dash(e.CategoryName), "Body", 14.4));
        Row("Date", Ui.Text(Ui.Date(e.Date), "Body", 14.4));
        if (e.DueDate is { } due) Row("Due", Strong(Ui.Date(due), e.IsOverdue(today) ? "DangerText" : null));
        if (!string.IsNullOrEmpty(e.Reference)) Row("Reference", Ui.Text(e.Reference, "Body", 14.4).WithResource(TextBlock.FontFamilyProperty, "MonoFont"));
        Row("Amount", Strong(Ui.Money(e.Amount)));
        if (e.AmountPaid > 0) Row("Paid", Strong(Ui.Money(e.AmountPaid), "SuccessText"));
        if (e.BalanceDue > 0) Row("Balance Due", Strong(Ui.Money(e.BalanceDue), "DangerText"));
        if (e.Payments.Count > 0) Row("Paid Via", Ui.Text(e.PaidVia, "Body", 14.4).Also(t => t.TextWrapping = TextWrapping.Wrap));
        if (e.RecurringId is { } rid)
            Row("Recurring", Ui.Link("View schedule", () => Shell.Navigate(Routes.EditRecurring(Shell, rid)), bold: false).Also(l => l.FontSize = 14.4));
        if (SqlFormat.ParseDateTime(e.CreatedAt) is { } created)
            Row("Created", Ui.Muted(Ui.Date(DateOnly.FromDateTime(created)), 12.8));

        var side = Ui.Stack(16, Ui.Card(details, "Details", "info-circle"));
        if (!string.IsNullOrWhiteSpace(e.Notes)) side.Children.Add(Ui.Card(Ui.Text(e.Notes, "Body", 14.4, wrap: true), "Notes", "sticky", "Warning"));

        var page = new StackPanel();
        page.Children.Add(header);
        page.Children.Add(Ui.Columns(24, (Ui.Star(2), mainCol), (Ui.Star(1), side)));
        Content = page;
    }

    private static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes / 1024.0 / 1024:0.0} MB",
    };

    private void Attach()
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Title = "Attach receipts",
            Filter = "Receipts (PDF and images)|" + string.Join(";", ReceiptStore.Extensions.Select(x => "*" + x)) + "|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(Shell) != true) return;
        Try(() => Shell.Reload(Store.Expenses.AddReceipts(_e.Id, dialog.FileNames)));
    }

    private void OpenReceipt(ExpenseReceipt r)
    {
        try
        {
            string path = Store.Expenses.ReceiptPath(r.Id);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (UserFacingException ex) { Shell.ShowError(ex.Message); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Shell.ShowError($"Windows could not open '{r.FileName}': {ex.Message}");
        }
    }

    private async void RemoveReceipt(ExpenseReceipt r)
    {
        if (!await Shell.Confirm($"Remove the receipt '{r.FileName}'? ProfitDjinn's copy of the file is deleted.", "Remove", danger: true)) return;
        Try(() => Shell.Reload(Store.Expenses.RemoveReceipt(r.Id)));
    }

    private async void Delete()
    {
        int n = _e.Receipts.Count;
        string files = n == 0 ? "" : $" Its {n} receipt {Fmt(n)} will be deleted too.";
        if (!await Shell.Confirm($"Delete the expense '{_e.Description}'?{files}", "Delete", danger: true)) return;
        Try(() => Shell.Navigate(Routes.Expenses(Shell), Store.Expenses.Delete(_e.Id)));
    }

    private static string Fmt(int n) => n == 1 ? "file" : "files";

    private async void DeletePayment(ExpensePayment p)
    {
        if (!await Shell.Confirm($"Delete this payment of {Ui.Money(p.Amount)}?", "Delete", danger: true)) return;
        Try(() => Shell.Reload(Store.Expenses.DeletePayment(_e.Id, p.Id)));
    }
}
