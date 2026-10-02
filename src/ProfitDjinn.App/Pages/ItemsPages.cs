using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ProfitDjinn.App.Controls;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.App.Shell;
using ProfitDjinn.Core.Model;

namespace ProfitDjinn.App.Pages;

/// <summary>The service-item price list (1.x items/list.html).</summary>
public sealed class ItemsPage : AppPage
{
    private readonly bool _inactive;

    public override string NavKey => "items";
    public override IReadOnlyList<Crumb> Crumbs => new[] { new Crumb("Service Items") };

    public ItemsPage(MainWindow shell, bool inactive) : base(shell)
    {
        _inactive = inactive;
        var items = Store.Items.List(inactive);

        var show = new CheckBox { Content = "Show inactive", IsChecked = inactive, FontSize = 13.6 }.WithResource(CheckBox.ForegroundProperty, "TextMuted");
        show.Click += (_, _) => Shell.Navigate(Routes.Items(Shell, show.IsChecked == true));

        var page = new StackPanel();
        page.Children.Add(Ui.PageHeaderWithGlyph("box-seam", "Service Items", null, null,
            show, Ui.Button("New Item", "Btn.Primary", "plus-lg", () => Shell.Navigate(Routes.NewItem(Shell))).Margin(8, 0, 0, 0)));

        var columns = new List<Column<ServiceItem>>
        {
            new("Description", Ui.Star(), i =>
            {
                var t = Ui.Text(i.Description, "Body");
                if (!i.IsActive) { t.TextDecorations = TextDecorations.Strikethrough; t.Opacity = 0.6; t.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted"); }
                return t;
            }),
            new("Default Price", Ui.Px(130), i => Ui.Text(Ui.Money(i.Price), "Mono").Also(t => t.HorizontalAlignment = HorizontalAlignment.Right), HorizontalAlignment.Right),
            new("Status", Ui.Px(100), i => i.IsActive ? Ui.Badge("Active", "success75") : Ui.Badge("Inactive", "secondary"), HorizontalAlignment.Center),
            new("", Ui.Px(140), i => Ui.Row(4,
                Ui.IconButton("pencil", "Btn.OutlinePrimary", "Edit", () => Shell.Navigate(Routes.EditItem(Shell, i.Id))),
                Ui.IconButton(i.IsActive ? "toggle-on" : "toggle-off", "Btn.OutlineSecondary", i.IsActive ? "Deactivate" : "Activate",
                    () => Try(() => Shell.Reload(Store.Items.ToggleActive(i.Id)))),
                Ui.IconButton("trash", "Btn.OutlineDanger", "Delete", () => Delete(i))), HorizontalAlignment.Right),
        };
        FrameworkElement body = items.Count == 0
            ? Ui.Stack(0, Table.Build(columns, Array.Empty<ServiceItem>()), Ui.Empty("box-seam", "No service items yet.", "Add the first one.", () => Shell.Navigate(Routes.NewItem(Shell))))
            : Table.Build(columns, items);
        page.Children.Add(Ui.Card(body, bodyPadding: new Thickness(0)));
        Content = page;
    }

    private async void Delete(ServiceItem item)
    {
        if (!await Shell.Confirm($"Delete '{item.Description}'?", "Delete", danger: true)) return;
        Try(() => Shell.Reload(Store.Items.Delete(item.Id)));
    }
}

/// <summary>New Item and Edit Item (1.x items/form.html).</summary>
public sealed class ItemFormPage : AppPage
{
    private readonly long? _id;
    private readonly TextBox _desc;
    private readonly TextBox _price;
    private readonly CheckBox _active;
    private readonly Field _descField, _priceField;

    public override string NavKey => "items";
    public override IReadOnlyList<Crumb> Crumbs => new[] { new Crumb("Service Items", () => Shell.Navigate(Routes.Items(Shell))), new Crumb(_id is null ? "New Item" : "Edit Item") };

    public ItemFormPage(MainWindow shell, long? id) : base(shell)
    {
        _id = id;
        var item = id is { } iid ? Store.Items.Get(iid) : new ServiceItem { IsActive = true };
        _desc = Ui.TextBox(item.Description, "e.g. Remote Support (per hour)");
        _price = Ui.TextBox(id is null ? "" : item.Price.ToString("0.00", CultureInfo.InvariantCulture));
        Input.SetPrefix(_price, "$");
        _active = new CheckBox { Content = "Active", IsChecked = item.IsActive };
        _descField = Ui.Field("Description", _desc, required: true);
        _priceField = Ui.Field("Default Price ($)", _price, required: true, hint: "This becomes the default price when adding this item to an invoice.");

        var buttons = new DockPanel();
        if (id is not null)
        {
            // 1.x nested this form inside the edit form, so Delete actually saved. Fixed in 2.0.
            var del = Ui.Button("Delete", "Btn.OutlineDanger", "trash", Delete);
            DockPanel.SetDock(del, Dock.Right);
            buttons.Children.Add(del);
        }
        buttons.Children.Add(Ui.Row(8,
            Ui.Button("Save Item", "Btn.Primary", "check-lg", Save),
            Ui.Button("Cancel", "Btn.OutlineSecondary", null, () => Shell.Navigate(Routes.Items(Shell)))));

        var form = Ui.Stack(0, _descField, _priceField,
            Ui.Stack(0, _active, Ui.Text("Inactive items won't appear in the invoice quick-pick list.", "HintText")).Margin(0, 0, 0, 24),
            buttons);

        var holder = new Grid();
        holder.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star(1) });
        holder.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star(2) });
        holder.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star(1) });
        var card = Ui.Card(form);
        Grid.SetColumn(card, 1);
        holder.Children.Add(card);

        var page = new StackPanel();
        page.Children.Add(Ui.PageHeader(id is null ? "New Item" : "Edit Item"));
        page.Children.Add(holder);
        Content = page;
        _desc.KeyDown += (_, e) => { if (e.Key == Key.Enter) Save(); };
        _price.KeyDown += (_, e) => { if (e.Key == Key.Enter) Save(); };
    }

    public override void OnShown() => _desc.Focus();

    private void Save()
    {
        _descField.Error = _priceField.Error = null;
        Input.SetInvalid(_desc, false);
        Input.SetInvalid(_price, false);
        string priceText = _price.Text.Trim().TrimStart('$');
        double? price = double.TryParse(priceText, NumberStyles.Float, CultureInfo.InvariantCulture, out double p) && double.IsFinite(p) ? p : null;
        if (price is null && priceText.Length > 0)
        {
            _priceField.Error = "Not a valid decimal value.";
            Input.SetInvalid(_price, true);
            return;
        }
        Try(() =>
        {
            var notice = _id is { } id
                ? Store.Items.Update(id, _desc.Text, price, _active.IsChecked == true)
                : Store.Items.Create(_desc.Text, price, _active.IsChecked == true).Notice;
            Shell.Navigate(Routes.Items(Shell), notice);
        });
    }

    protected override void ShowFieldErrors(IReadOnlyDictionary<string, string> errors)
    {
        if (errors.TryGetValue("description", out var d)) { _descField.Error = d; Input.SetInvalid(_desc, true); }
        if (errors.TryGetValue("price", out var p)) { _priceField.Error = p; Input.SetInvalid(_price, true); }
    }

    private async void Delete()
    {
        if (!await Shell.Confirm("Delete this item?", "Delete", danger: true)) return;
        Try(() => Shell.Navigate(Routes.Items(Shell), Store.Items.Delete(_id!.Value)));
    }
}
