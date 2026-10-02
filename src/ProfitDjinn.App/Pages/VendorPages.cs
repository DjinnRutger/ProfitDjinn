using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ProfitDjinn.App.Controls;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.App.Shell;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.App.Pages;

/// <summary>2.2. The vendor list with search and "Show inactive", laid out like the customer list.</summary>
public sealed class VendorsPage : AppPage
{
    private readonly bool _inactive;

    public override string NavKey => "vendors";
    public override IReadOnlyList<Crumb> Crumbs => new[] { new Crumb("Vendors") };

    public VendorsPage(MainWindow shell, string search, bool inactive) : base(shell)
    {
        _inactive = inactive;
        var vendors = Store.Vendors.List(search, inactive);

        string lead = $"{vendors.Count} vendor{(vendors.Count != 1 ? "s" : "")}" + (search.Length > 0 ? $" matching \"{search}\"" : "");
        var page = new StackPanel();
        page.Children.Add(Ui.PageHeader("Vendors", lead, null,
            Ui.Button("New Vendor", "Btn.Primary", "plus-lg", () => Shell.Navigate(Routes.NewVendor(Shell)))));

        var box = Ui.TextBox(search, "Search by name…", 360);
        Input.SetPrefixGlyph(box, "search");
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Run(box.Text, _inactive); };
        var inactiveBox = new CheckBox { Content = "Show inactive", IsChecked = inactive, Margin = new Thickness(8, 0, 0, 0) };
        inactiveBox.Click += (_, _) => Run(box.Text, inactiveBox.IsChecked == true);
        var bar = Ui.Row(8, box);
        if (search.Length > 0) bar.Children.Add(Ui.Button(null, "Btn.OutlineSecondary", "x-lg", () => Run("", _inactive)).Margin(-8, 0, 0, 0));
        bar.Children.Add(inactiveBox);
        bar.Children.Add(Ui.Button("Search", "Btn.Secondary", null, () => Run(box.Text, inactiveBox.IsChecked == true), small: true).Margin(8, 0, 0, 0));
        page.Children.Add(Ui.Card(bar, bodyPadding: new Thickness(16, 16, 16, 16)).Margin(0, 0, 0, 24));
        Loaded += (_, _) => { box.Focus(); box.CaretIndex = box.Text.Length; };

        var columns = new List<Column<Vendor>>
        {
            new("Name", Ui.Star(2), v =>
            {
                var cell = new StackPanel();
                cell.Children.Add(Ui.Link(v.Name, () => Shell.Navigate(Routes.Vendor(Shell, v.Id))).Also(l => l.HorizontalAlignment = HorizontalAlignment.Left));
                if (!string.IsNullOrEmpty(v.Contact)) cell.Children.Add(Ui.Muted(v.Contact, 12.8));
                return cell;
            }),
            new("Phone", Ui.Star(), v => Ui.Text(Ui.Dash(v.Phone), "Body", 13.6)),
            new("Email", Ui.Star(1.4), v => string.IsNullOrEmpty(v.Email)
                ? Ui.Text("—", "Body", 13.6)
                : Ui.Link(v.Email, () => CustomersPage.MailTo(v.Email), bold: false).Also(l => l.FontSize = 13.6)),
            new("Expenses", Ui.Auto, v => Ui.Muted(v.Expenses.Count.ToString(), 13.6), HorizontalAlignment.Center),
            new("Owed", Ui.Auto, v => v.Owed > 0
                ? Ui.Text(Ui.Money(v.Owed), "Strong").WithResource(TextBlock.ForegroundProperty, "DangerText")
                : Ui.Muted("$0.00"), HorizontalAlignment.Right),
            new("Status", Ui.Auto, v => v.IsActive ? Ui.Badge("Active", "success75") : Ui.Badge("Inactive", "secondary"), HorizontalAlignment.Center),
            new("", Ui.Auto, v => Ui.Row(4,
                Ui.IconButton("eye", "Btn.OutlineSecondary", "View", () => Shell.Navigate(Routes.Vendor(Shell, v.Id))),
                Ui.IconButton("pencil", "Btn.OutlinePrimary", "Edit", () => Shell.Navigate(Routes.EditVendor(Shell, v.Id))),
                Ui.IconButton("wallet2", "Btn.OutlineSuccess", "New Expense", () => Shell.Navigate(Routes.NewExpense(Shell, v.Id)))), HorizontalAlignment.Right),
        };
        FrameworkElement body = vendors.Count == 0
            ? Ui.Stack(0, Table.Build(columns, Array.Empty<Vendor>()), Ui.Empty("shop", "No vendors found.", "Add one now.", () => Shell.Navigate(Routes.NewVendor(Shell))))
            : Table.Build(columns, vendors);
        page.Children.Add(Ui.Card(body, bodyPadding: new Thickness(0)));
        Content = page;
    }

    private void Run(string search, bool inactive) => Shell.Navigate(Routes.Vendors(Shell, search.Trim(), inactive));
}

/// <summary>2.2. One vendor: contact, money totals, notes, expense history. Laid out like a customer.</summary>
public sealed class VendorDetailPage : AppPage
{
    private readonly Vendor _v;

    public override string NavKey => "vendors";
    public override IReadOnlyList<Crumb> Crumbs => new[] { new Crumb("Vendors", () => Shell.Navigate(Routes.Vendors(Shell))), new Crumb(_v.Name) };

    public VendorDetailPage(MainWindow shell, long id) : base(shell)
    {
        _v = Store.Vendors.Get(id);
        var v = _v;
        var today = DateOnly.FromDateTime(DateTime.Today);

        var page = new StackPanel();
        page.Children.Add(Ui.PageHeader(v.Name, string.IsNullOrEmpty(v.Contact) ? null : $"Contact: {v.Contact}", null,
            Ui.Button("New Expense", "Btn.Success", "wallet2", () => Shell.Navigate(Routes.NewExpense(Shell, v.Id))),
            Ui.Button("Edit", "Btn.Primary", "pencil", () => Shell.Navigate(Routes.EditVendor(Shell, v.Id))),
            Ui.Button("Delete", "Btn.OutlineDanger", "trash", Delete)));

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
        if (!string.IsNullOrEmpty(v.Address))
        {
            string cityLine = string.Join(", ", new[] { v.City, v.State, v.ZipCode }.Where(p => !string.IsNullOrEmpty(p)));
            Pair("Address", Ui.Text(cityLine.Length > 0 ? $"{v.Address}\n{cityLine}" : v.Address, "Body", 14.4, wrap: true));
        }
        if (!string.IsNullOrEmpty(v.Phone)) Pair("Phone", Ui.Text(v.Phone, "Body", 14.4));
        if (!string.IsNullOrEmpty(v.Email)) Pair("Email", Ui.Link(v.Email, () => CustomersPage.MailTo(v.Email!), bold: false).Also(l => { l.FontSize = 14.4; l.TextWrapping = TextWrapping.Wrap; }));
        if (v.DefaultCategoryId is { } cat)
        {
            string name = Store.Categories.List().FirstOrDefault(u => u.Category.Id == cat)?.Category.Name ?? "—";
            Pair("Category", Ui.Text(name, "Body", 14.4));
        }
        Pair("Status", v.IsActive ? Ui.Badge("Active", "success75") : Ui.Badge("Inactive", "secondary"));
        left.Children.Add(Ui.Card(info, "Contact Info", "person-vcard").Margin(0, 0, 0, 24));

        var money = new Grid();
        money.ColumnDefinitions.Add(new ColumnDefinition());
        money.ColumnDefinitions.Add(new ColumnDefinition());
        money.RowDefinitions.Add(new RowDefinition());
        money.RowDefinitions.Add(new RowDefinition());
        void Place(UIElement e, int row, int col, int span = 1) { Grid.SetRow(e, row); Grid.SetColumn(e, col); Grid.SetColumnSpan(e, span); money.Children.Add(e); }
        Place(ExpenseUi.Small(Ui.Money(v.TotalBilled), "Total Billed", "primary").Margin(0, 0, 8, 16), 0, 0);
        Place(ExpenseUi.Small(Ui.Money(v.TotalPaid), "Total Paid", "success").Margin(8, 0, 0, 16), 0, 1);
        var owed = ExpenseUi.Small(Ui.Money(v.Owed), "Owed", "danger", 25.6);
        owed.Muted = v.Owed <= 0;
        Place(owed, 1, 0, 2);
        left.Children.Add(money);

        if (!string.IsNullOrWhiteSpace(v.Notes))
            left.Children.Add(Ui.Card(Ui.Text(v.Notes, "Body", 14.4, wrap: true), "Notes", "sticky", "Warning").Margin(0, 24, 0, 0));

        var expenses = Store.Vendors.ExpenseHistory(v);
        var columns = ExpenseUi.Columns(Shell, today, showVendor: false);
        FrameworkElement table = expenses.Count == 0
            ? Ui.Stack(0, Table.Build(columns, Array.Empty<Expense>()), Ui.Empty("wallet2", "No expenses yet.", "Add the first one.", () => Shell.Navigate(Routes.NewExpense(Shell, v.Id))))
            : Table.Build(columns, expenses, onRowClick: e => Shell.Navigate(Routes.Expense(Shell, e.Id)));
        var history = Ui.Card(table, "Expenses", "wallet2", headerRight: Ui.Badge(expenses.Count.ToString(), "secondary"), bodyPadding: new Thickness(0));
        history.VerticalAlignment = VerticalAlignment.Top;

        page.Children.Add(Ui.Columns(24, (Ui.Star(1), left), (Ui.Star(2), history)));
        Content = page;
    }

    private async void Delete()
    {
        if (!await Shell.Confirm($"Delete {_v.Name}?", "Delete", danger: true)) return;
        Try(() => Shell.Navigate(Routes.Vendors(Shell), Store.Vendors.Delete(_v.Id)));
    }
}

/// <summary>2.2. New Vendor and Edit Vendor, laid out like the customer form.</summary>
public sealed class VendorFormPage : AppPage
{
    private readonly long? _id;
    private readonly string _name;
    private readonly Dictionary<string, Field> _fields = new();
    private readonly TextBox _nameBox, _contact, _phone, _email, _address, _city, _state, _zip, _notes;
    private readonly ComboBox _category;
    private readonly CheckBox _active;

    public override string NavKey => "vendors";

    public override IReadOnlyList<Crumb> Crumbs
    {
        get
        {
            var list = new List<Crumb> { new("Vendors", () => Shell.Navigate(Routes.Vendors(Shell))) };
            if (_id is { } id) list.Add(new Crumb(_name, () => Shell.Navigate(Routes.Vendor(Shell, id))));
            list.Add(new Crumb(_id is null ? "New Vendor" : "Edit Vendor"));
            return list;
        }
    }

    public VendorFormPage(MainWindow shell, long? id) : base(shell)
    {
        _id = id;
        var v = id is { } vid ? Store.Vendors.Get(vid) : new Vendor { IsActive = true };
        _name = v.Name;

        _nameBox = Ui.TextBox(v.Name);
        _contact = Ui.TextBox(v.Contact);
        _phone = Ui.TextBox(v.Phone);
        _email = Ui.TextBox(v.Email);
        _address = Ui.TextBox(v.Address);
        _city = Ui.TextBox(v.City);
        _state = Ui.TextBox(v.State).Also(t => { t.MaxLength = 2; t.CharacterCasing = CharacterCasing.Upper; });
        _zip = Ui.TextBox(v.ZipCode);
        _category = ExpenseUi.CategoryPicker(Store, v.DefaultCategoryId, allowNone: true);
        _notes = Ui.TextArea(v.Notes, 80);
        _active = new CheckBox { Content = "Active", IsChecked = v.IsActive };

        var grid = new StackPanel();
        grid.Children.Add(F("name", "Vendor Name", _nameBox, required: true));
        grid.Children.Add(Ui.Columns(16, (Ui.Star(), F("contact", "Contact", _contact)), (Ui.Star(), F("phone", "Phone", _phone))));
        grid.Children.Add(F("email", "Email", _email));
        grid.Children.Add(F("address", "Address", _address));
        grid.Children.Add(Ui.Columns(16, (Ui.Star(2), F("city", "City", _city)), (Ui.Star(), F("state", "State", _state)), (Ui.Star(), F("zip_code", "ZIP", _zip))));
        var catField = F("default_category_id", "Default Category", _category);
        catField.Hint = "Filled in for you on this vendor's new expenses.";
        grid.Children.Add(catField);
        grid.Children.Add(F("notes", "Notes", _notes));
        grid.Children.Add(_active);
        grid.Children.Add(new Border { Style = Ui.Style("Rule"), Margin = new Thickness(0, 24, 0, 24) });
        grid.Children.Add(Ui.Row(8,
            Ui.Button("Save Vendor", "Btn.Primary", "check-lg", Save),
            Ui.Button("Cancel", "Btn.OutlineSecondary", null, () => Shell.Navigate(_id is { } i ? Routes.Vendor(Shell, i) : Routes.Vendors(Shell)))));

        var page = new StackPanel();
        page.Children.Add(Ui.PageHeader(_id is null ? "New Vendor" : "Edit Vendor"));
        var holder = new Grid();
        holder.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star(1) });
        holder.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star(4) });
        holder.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star(1) });
        var card = Ui.Card(grid);
        Grid.SetColumn(card, 1);
        holder.Children.Add(card);
        page.Children.Add(holder);
        Content = page;

        foreach (var box in new[] { _nameBox, _contact, _phone, _email, _address, _city, _state, _zip })
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Save(); };
    }

    public override void OnShown() => _nameBox.Focus();

    private Field F(string key, string label, UIElement input, bool required = false) => _fields[key] = Ui.Field(label, input, required);

    private void Save()
    {
        foreach (var f in _fields.Values) { f.Error = null; if (f.Content is TextBox t) Input.SetInvalid(t, false); }
        var draft = new VendorDraft(_nameBox.Text, _contact.Text, _address.Text, _city.Text, _state.Text, _zip.Text,
            _phone.Text, _email.Text, ExpenseUi.SelectedId(_category), _notes.Text, _active.IsChecked == true);
        Try(() =>
        {
            if (_id is { } id) Shell.Navigate(Routes.Vendor(Shell, id), Store.Vendors.Update(id, draft));
            else
            {
                var created = Store.Vendors.Create(draft);
                Shell.Navigate(Routes.Vendor(Shell, created.Id), created.Notice);
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
            }
    }
}
