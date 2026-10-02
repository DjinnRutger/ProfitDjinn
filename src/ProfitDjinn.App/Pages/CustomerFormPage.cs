using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ProfitDjinn.App.Controls;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.App.Shell;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.App.Pages;

/// <summary>New Customer and Edit Customer (1.x customers/form.html).</summary>
public sealed class CustomerFormPage : AppPage
{
    private readonly long? _id;
    private readonly string _name;
    private readonly Dictionary<string, Field> _fields = new();
    private readonly TextBox _nameBox, _attn, _phone, _email, _address, _city, _state, _zip, _notes;
    private readonly CheckBox _active;

    public override string NavKey => "customers";

    public override IReadOnlyList<Crumb> Crumbs
    {
        get
        {
            var list = new List<Crumb> { new("Customers", () => Shell.Navigate(Routes.Customers(Shell))) };
            if (_id is { } id) list.Add(new Crumb(_name, () => Shell.Navigate(Routes.Customer(Shell, id))));
            list.Add(new Crumb(_id is null ? "New Customer" : "Edit Customer"));
            return list;
        }
    }

    public CustomerFormPage(MainWindow shell, long? id) : base(shell)
    {
        _id = id;
        var c = id is { } cid ? Store.Customers.Get(cid) : new Customer { IsActive = true };
        _name = c.Name;

        _nameBox = Ui.TextBox(c.Name);
        _attn = Ui.TextBox(c.Attn);
        _phone = Ui.TextBox(c.Phone);
        _email = Ui.TextBox(c.Email);
        _address = Ui.TextBox(c.Address);
        _city = Ui.TextBox(c.City);
        _state = Ui.TextBox(c.State).Also(t => { t.MaxLength = 2; t.CharacterCasing = CharacterCasing.Upper; });
        _zip = Ui.TextBox(c.ZipCode);
        _notes = Ui.TextArea(c.Notes, 80);
        _active = new CheckBox { Content = "Active", IsChecked = c.IsActive };

        var grid = new StackPanel();
        grid.Children.Add(F("name", "Company / Name", _nameBox, required: true));
        grid.Children.Add(Ui.Columns(16, (Ui.Star(), F("attn", "Attn / Contact", _attn)), (Ui.Star(), F("phone", "Phone", _phone))));
        grid.Children.Add(F("email", "Email", _email));
        grid.Children.Add(F("address", "Address", _address));
        grid.Children.Add(Ui.Columns(16, (Ui.Star(2), F("city", "City", _city)), (Ui.Star(), F("state", "State", _state)), (Ui.Star(), F("zip_code", "ZIP", _zip))));
        grid.Children.Add(F("notes", "Notes", _notes));
        grid.Children.Add(_active);
        grid.Children.Add(new Border { Style = Ui.Style("Rule"), Margin = new Thickness(0, 24, 0, 24) });
        grid.Children.Add(Ui.Row(8,
            Ui.Button("Save Customer", "Btn.Primary", "check-lg", Save),
            Ui.Button("Cancel", "Btn.OutlineSecondary", null, () => Shell.Navigate(_id is { } i ? Routes.Customer(Shell, i) : Routes.Customers(Shell)))));

        var page = new StackPanel();
        page.Children.Add(Ui.PageHeader(_id is null ? "New Customer" : "Edit Customer"));
        // Bootstrap col-lg-8, centred.
        var holder = new Grid();
        holder.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star(1) });
        holder.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star(4) });
        holder.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star(1) });
        var card = Ui.Card(grid);
        Grid.SetColumn(card, 1);
        holder.Children.Add(card);
        page.Children.Add(holder);
        Content = page;

        foreach (var box in new[] { _nameBox, _attn, _phone, _email, _address, _city, _state, _zip })
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Save(); };
    }

    public override void OnShown() => _nameBox.Focus();

    private Field F(string key, string label, UIElement input, bool required = false) => _fields[key] = Ui.Field(label, input, required);

    private void Save()
    {
        foreach (var f in _fields.Values) { f.Error = null; if (f.Content is TextBox t) Input.SetInvalid(t, false); }
        var draft = new CustomerDraft(_nameBox.Text, _attn.Text, _address.Text, _city.Text, _state.Text, _zip.Text,
            _phone.Text, _email.Text, _notes.Text, _active.IsChecked == true);
        Try(() =>
        {
            if (_id is { } id)
            {
                var notice = Store.Customers.Update(id, draft);
                Shell.Navigate(Routes.Customer(Shell, id), notice);
            }
            else
            {
                var created = Store.Customers.Create(draft);
                Shell.Navigate(Routes.Customer(Shell, created.Id), created.Notice);
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
