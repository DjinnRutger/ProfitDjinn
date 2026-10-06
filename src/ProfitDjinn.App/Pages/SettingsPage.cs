using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using ProfitDjinn.App.Controls;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.App.Shell;
using ProfitDjinn.Core;
using ProfitDjinn.Core.Data;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.App.Pages;

/// <summary>
/// App Settings, 2.3 layout: a list of sections on the left (Business, Invoices, Work Orders,
/// Expenses, Appearance, Security, Backup, Updates), one section's settings on the right,
/// written as plain labels rather than setting keys. Changes are collected until Save; a bar
/// pinned to the bottom of the window appears as soon as anything changes, and leaving with
/// unsaved changes asks first. Uploads and the app password act at once, as before.
/// </summary>
public sealed class SettingsPage : AppPage
{
    private sealed record Section(string Key, string Title, string Glyph, string Lead, Func<FrameworkElement> Build);

    /// <summary>Settings that are never listed: internal values, or ones with their own control.</summary>
    private static readonly HashSet<string> Internal = new()
    {
        SettingKeys.AppPasswordHash, SettingKeys.Theme, "allow_registration", "maintenance_mode", "session_timeout",
        "items_per_page", "default_theme", SettingKeys.BackupReminderLast, SettingKeys.DatabaseAppVersion,
        SettingKeys.UpdateLastCheck, SettingKeys.UpdateLatestVersion, SettingKeys.UpdateLatestUrl,
        SettingKeys.LoginLogo, SettingKeys.AppIconImage,
    };

    /// <summary>1.x / 2.0 category names that links still use, and the section each now lives in.</summary>
    private static readonly Dictionary<string, string> OldCategories = new()
    {
        ["general"] = "appearance", ["ui"] = "appearance", ["login"] = "security",
    };

    /// <summary>The section to reopen after Save or Discard rebuilds the page.</summary>
    private static string? _reopen;

    private readonly Dictionary<string, Func<string>> _readers = new();
    private readonly Dictionary<string, string> _initial = new();
    private readonly Dictionary<string, Field> _fields = new();
    private readonly Dictionary<string, (Border Item, FrameworkElement Body)> _sections = new();
    private readonly HashSet<string> _shown = new();
    private readonly ContentControl _body = new() { Focusable = false };
    private readonly Border _bar;
    private readonly TextBlock _barText;
    private bool _building = true;
    private string _current = "business";

    public override string NavKey => "settings";
    public override IReadOnlyList<Crumb> Crumbs => new[] { new Crumb("Settings") };
    public override UIElement? PinnedBar => _bar;

    private bool Dirty => _readers.Any(r => r.Value() != _initial.GetValueOrDefault(r.Key));

    public SettingsPage(MainWindow shell, string? category) : base(shell)
    {
        string? wanted = _reopen ?? category;
        _reopen = null;
        if (wanted is not null && OldCategories.TryGetValue(wanted, out var mapped)) wanted = mapped;

        var sections = new List<Section>
        {
            new("business", "Business", "building", "Your business details, printed at the top of every invoice.", Business),
            new("features", "Features", "toggles", "Turn parts of ProfitDjinn on or off. Turning one off hides it; nothing is deleted.", Features),
            new("invoices", "Invoices", "receipt", "Invoice numbers and the payment terms printed on each invoice.", Invoices),
            new("workorders", "Work Orders", "clipboard-check", "Work order numbers and your default labor rate.", WorkOrders),
            new("expenses", "Expenses", "wallet2", "Vendors, bills, recurring costs and the Profit & Loss report.", Expenses),
            new("appearance", "Appearance", "palette", "Theme, colour, text size and the names shown in the app.", Appearance),
            new("security", "Security & Lock Screen", "shield-lock", "The optional app password and the lock screen.", Security),
            new("backup", "Backup", "database", "Reminders to keep a copy of your data.", Backup),
            new("updates", "Updates & About", "arrow-up-circle", "New versions, and where ProfitDjinn keeps your data.", Updates),
        };

        // Anything not placed in a section above (a setting added later, say) is still reachable.
        var leftovers = Store.Settings.All().Where(s => !Internal.Contains(s.Key)).ToList();
        foreach (var sec in sections) _sections[sec.Key] = (null!, Wrap(sec, sec.Build()));
        leftovers = leftovers.Where(s => !_shown.Contains(s.Key)).ToList();
        if (leftovers.Count > 0)
        {
            var adv = new Section("advanced", "Advanced", "gear", "Other settings, by their setting name.", () => Advanced(leftovers));
            sections.Add(adv);
            _sections[adv.Key] = (null!, Wrap(adv, adv.Build()));
        }

        // ---- section list
        var list = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
        foreach (var sec in sections)
        {
            var item = NavItem(sec);
            _sections[sec.Key] = (item, _sections[sec.Key].Body);
            list.Children.Add(item);
        }
        var listCard = Ui.Card(list, bodyPadding: new Thickness(0));
        listCard.VerticalAlignment = VerticalAlignment.Top;

        var page = new StackPanel();
        page.Children.Add(Ui.PageHeaderWithGlyph("sliders", "Settings", "Choose a section. Changes are saved when you click Save."));
        page.Children.Add(Ui.Columns(24, (Ui.Px(250), listCard), (Ui.Star(), _body)));
        Content = page;

        // ---- unsaved-changes bar, pinned above the footer by the shell
        _barText = Ui.Text("You have unsaved changes.", "Strong", 14);
        var actions = Ui.Row(8,
            Ui.Button("Discard", "Btn.OutlineSecondary", "arrow-counterclockwise", Discard, small: true),
            Ui.Button("Save Changes", "Btn.Primary", "floppy", Save, small: true));
        DockPanel.SetDock(actions, Dock.Right);
        var dock = new DockPanel();
        dock.Children.Add(actions);
        dock.Children.Add(Ui.Row(8, new Icon { Glyph = "pencil-square", Size = 15 }.WithResource(Icon.ForegroundProperty, "Warning"), _barText)
            .Also(r => r.VerticalAlignment = VerticalAlignment.Center));
        _bar = new Border { Padding = new Thickness(24, 10, 24, 10), BorderThickness = new Thickness(0, 1, 0, 0), Child = dock, Visibility = Visibility.Collapsed }
            .WithResource(Border.BackgroundProperty, "Alert.Warning.Bg").WithResource(Border.BorderBrushProperty, "Alert.Warning.Border");

        foreach (var (key, read) in _readers) _initial[key] = read();
        _building = false;
        ShowSection(wanted is not null && _sections.ContainsKey(wanted) ? wanted : "business");
    }

    /// <summary>Puts the cursor in the first box of the section shown.</summary>
    public override void OnShown()
    {
        if (FirstBox(_sections[_current].Body) is { } box) { box.Focus(); box.CaretIndex = box.Text.Length; }
    }

    private static TextBox? FirstBox(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is TextBox { IsReadOnly: false, IsVisible: true } t) return t;
            if (FirstBox(child) is { } found) return found;
        }
        return null;
    }

    public override async Task<bool> CanLeaveAsync()
    {
        if (!Dirty) return true;
        return await Shell.Confirm("You have unsaved changes in Settings. Leave without saving them?", "Leave Without Saving", danger: true, title: "Unsaved changes");
    }

    // ------------------------------------------------------------------ layout

    private Border NavItem(Section sec)
    {
        var icon = new Icon { Glyph = sec.Glyph, Size = 15, Margin = new Thickness(0, 0, 10, 0) };
        var text = Ui.Text(sec.Title, "Body", 14.4);
        var button = new Button { Style = Ui.Style("Btn.Bare"), Content = Ui.Row(0, icon, text), HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(16, 10, 16, 10) };
        // "Expenses settings", not "Expenses": the sidebar already has an Expenses button.
        System.Windows.Automation.AutomationProperties.SetName(button, sec.Title + " settings");
        button.Click += (_, _) => ShowSection(sec.Key);
        var item = new Border { BorderThickness = new Thickness(3, 0, 0, 0), Child = button, Tag = (icon, text) };
        return item;
    }

    private void ShowSection(string key)
    {
        _current = key;
        _body.Content = _sections[key].Body;
        foreach (var (k, (item, _)) in _sections)
        {
            bool on = k == key;
            var (icon, text) = ((Icon, TextBlock))item.Tag;
            item.SetResourceReference(Border.BorderBrushProperty, on ? "BrandPrimary" : "Surface");
            if (on) item.SetResourceReference(Border.BackgroundProperty, "TertiaryBg"); else item.Background = Brushes.Transparent;
            icon.SetResourceReference(Icon.ForegroundProperty, on ? "BrandPrimary" : "TextMuted");
            text.SetResourceReference(TextBlock.ForegroundProperty, on ? "BrandPrimary" : "Text");
            text.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private static FrameworkElement Wrap(Section sec, FrameworkElement body)
    {
        var head = Ui.Stack(4,
            Ui.Row(10, new Icon { Glyph = sec.Glyph, Size = 18 }.WithResource(Icon.ForegroundProperty, "BrandPrimary"), Ui.Text(sec.Title, "Strong", 18)),
            Ui.Muted(sec.Lead, 13.6).Also(t => t.TextWrapping = TextWrapping.Wrap));
        // Ui.Stack sets each child's top margin to the gap, so spacing goes in padding here.
        var rule = new Border { Padding = new Thickness(0, 4, 0, 6), Child = new Border { Style = Ui.Style("Rule") } };
        return Ui.Card(Ui.Stack(0, head, rule, body), bodyPadding: new Thickness(24, 20, 24, 8));
    }

    /// <summary>A sub-heading inside a section.</summary>
    private static FrameworkElement Group(string title) =>
        new Border { Padding = new Thickness(0, 8, 0, 12), Child = Ui.Text(title.ToUpperInvariant(), "TableHeaderText", 12) };

    /// <summary>A switch with its title and explanation on the left.</summary>
    private FrameworkElement SwitchRow(string key, string title, string description, CheckBox sw)
    {
        DockPanel.SetDock(sw, Dock.Right);
        sw.VerticalAlignment = VerticalAlignment.Center;
        sw.Margin = new Thickness(16, 0, 0, 0);
        var text = Ui.Stack(2, Ui.Text(title, "Strong", 14.4), Ui.Muted(description, 13).Also(t => t.TextWrapping = TextWrapping.Wrap));
        var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        dock.Children.Add(sw);
        dock.Children.Add(text);
        System.Windows.Automation.AutomationProperties.SetName(sw, title);
        return dock;
    }

    // ------------------------------------------------------------------ inputs that report changes

    private void Changed()
    {
        if (_building) return;
        bool dirty = Dirty;
        _bar.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
    }

    private Field Text(string key, string label, string? hint = null, string? placeholder = null, bool mono = false, double? maxWidth = null)
    {
        _shown.Add(key);
        var box = Ui.TextBox(Store.Settings.Get(key), placeholder);
        if (mono) box.SetResourceReference(TextBox.FontFamilyProperty, "MonoFont");
        if (maxWidth is { } w) { box.MaxWidth = w; box.HorizontalAlignment = HorizontalAlignment.Left; box.MinWidth = Math.Min(w, 160); }
        box.TextChanged += (_, _) => Changed();
        _readers[key] = () => box.Text.Trim();
        return _fields[key] = Ui.Field(label, box, hint: hint);
    }

    private Field Money(string key, string label, string? hint = null)
    {
        var f = Text(key, label, hint, maxWidth: 200);
        Input.SetPrefix((TextBox)f.Content, "$");
        return f;
    }

    private CheckBox Switch(string key, bool on)
    {
        _shown.Add(key);
        var sw = new CheckBox { Style = Ui.Style("Switch"), IsChecked = on };
        void Label() => sw.Content = sw.IsChecked == true ? "On" : "Off";
        // Checked/Unchecked, not Click: they also fire when the switch is flipped by keyboard or
        // by UI Automation (screen readers, the smoke tests), which Click does not.
        sw.Checked += (_, _) => { Label(); Changed(); };
        sw.Unchecked += (_, _) => { Label(); Changed(); };
        Label();
        _readers[key] = () => sw.IsChecked == true ? "true" : "false";
        return sw;
    }

    private sealed record Option(string Value, string Label) { public override string ToString() => Label; }

    private Field Choice(string key, string label, IReadOnlyList<(string Value, string Label)> options, string current, string? hint = null)
    {
        _shown.Add(key);
        var items = options.Select(o => new Option(o.Value, o.Label)).ToList();
        int at = items.FindIndex(o => o.Value == current);
        if (at < 0) { items.Insert(0, new Option(current, current)); at = 0; }
        var combo = new ComboBox { ItemsSource = items, SelectedIndex = at, MaxWidth = 320, MinWidth = 200, HorizontalAlignment = HorizontalAlignment.Left };
        combo.SelectionChanged += (_, _) => Changed();
        _readers[key] = () => (combo.SelectedItem as Option)?.Value ?? current;
        return _fields[key] = Ui.Field(label, combo, hint: hint);
    }

    // ------------------------------------------------------------------ sections

    // 2.5: the examples are hints (grey placeholder text), not values to delete. A blank field
    // is simply left off the invoice.
    private FrameworkElement Business() => Ui.Stack(0,
        Text(SettingKeys.CompanyName, "Business name", "Your name, or your business's name, as customers should see it.", placeholder: "e.g. Summit Handyman Co."),
        Text(SettingKeys.CompanyAddress, "Street address", placeholder: "e.g. 123 Main Street"),
        Ui.Columns(16,
            (Ui.Star(2), Text(SettingKeys.CompanyCity, "City", placeholder: "e.g. Springfield")),
            (Ui.Star(), Text(SettingKeys.CompanyState, "State", placeholder: "e.g. MT").Also(f => ((TextBox)f.Content).CharacterCasing = CharacterCasing.Upper)),
            (Ui.Star(), Text(SettingKeys.CompanyZip, "ZIP", placeholder: "e.g. 59715"))),
        Ui.Columns(16,
            (Ui.Star(), Text(SettingKeys.CompanyEmail, "Email", placeholder: "e.g. you@example.com")),
            (Ui.Star(), Text(SettingKeys.CompanyPhone, "Phone", placeholder: "e.g. (555) 555-0100"))));

    /// <summary>2.5. One switch per optional feature. The smoke tests find them by their titles.</summary>
    private FrameworkElement Features() => Ui.Stack(0,
        SwitchRow(SettingKeys.WorkOrdersEnabled, "Work orders",
            "A running tab per customer: log work as you do it, then bill it onto an invoice. Off hides Work Orders, the customer page's Work Order button and the Unbilled Work tile.",
            Switch(SettingKeys.WorkOrdersEnabled, Store.Settings.GetBool(SettingKeys.WorkOrdersEnabled))),
        SwitchRow(SettingKeys.RevenueEnabled, "Revenue report",
            "The Revenue page: income by month, year and customer. Off hides it from the sidebar; the dashboard still shows this year's revenue.",
            Switch(SettingKeys.RevenueEnabled, Store.Reports.RevenueEnabled)),
        SwitchRow(SettingKeys.ItemsEnabled, "Service items",
            "A saved price list you can quick-add onto invoices. Off hides Items and the invoice Quick-add list; saved items are kept.",
            Switch(SettingKeys.ItemsEnabled, Store.Items.Enabled)),
        SwitchRow(SettingKeys.ExpensesEnabled, "Track expenses",
            "Adds Vendors, Expenses and Profit & Loss to the sidebar.",
            Switch(SettingKeys.ExpensesEnabled, Store.Expenses.Enabled)),
        SwitchRow(SettingKeys.BankingEnabled, "Bank accounts",
            "Adds Banking to the sidebar: your bank, card and payment-processor accounts, transfers, payouts and reconciling to your statements. Payments can then say which account the money went to. Never changes invoices or the Profit & Loss.",
            Switch(SettingKeys.BankingEnabled, Store.Banking.Enabled)));

    private FrameworkElement Invoices() => Ui.Stack(0,
        Group("Numbering"),
        Ui.Columns(16,
            (Ui.Star(), Text(SettingKeys.InvoicePrefix, "Number prefix", "Letters before the number, e.g. INV for INV1001.", mono: true)),
            (Ui.Star(), Text(SettingKeys.InvoiceNextNumber, "Starting number", "Used for the first invoice. After that, numbers continue from the highest one.", mono: true))),
        Group("Payment terms"),
        Text(SettingKeys.InvoiceTerm1, "Terms, line 1", placeholder: "e.g. Payment due within 30 days"),
        Text(SettingKeys.InvoiceTerm2, "Terms, line 2", placeholder: "e.g. Make checks payable to Your Name"));

    private FrameworkElement WorkOrders() => Ui.Stack(0,
        Group("Numbering"),
        Ui.Columns(16,
            (Ui.Star(), Text(SettingKeys.WorkOrderPrefix, "Number prefix", "e.g. WO for WO1001.", mono: true)),
            (Ui.Star(), Text(SettingKeys.WorkOrderNextNumber, "Starting number", "Used for the first work order.", mono: true))),
        Group("Labor"),
        Money(SettingKeys.DefaultHourlyRate, "Default hourly rate", "Filled in when you log labor. Leave at 0.00 to type the rate each time."));

    private FrameworkElement Expenses()
    {
        _shown.Add(SettingKeys.ReceiptsFolder);
        string setting = Store.Settings.Get(SettingKeys.ReceiptsFolder).Trim();
        var shown = Ui.TextBox(Store.Receipts.Effective(setting)).Also(t => { t.IsReadOnly = true; t.SetResourceReference(TextBox.FontFamilyProperty, "MonoFont"); t.FontSize = 13; });
        var browse = Ui.Button("Browse…", "Btn.OutlinePrimary", "folder2-open", () =>
        {
            var dialog = new OpenFolderDialog { Title = "Folder for receipt files", InitialDirectory = Directory.Exists(shown.Text) ? shown.Text : "" };
            if (dialog.ShowDialog(Shell) != true) return;
            setting = dialog.FolderName;
            shown.Text = Store.Receipts.Effective(setting);
            Changed();
        }, small: true);
        var reset = Ui.Button("Use Default", "Btn.OutlineSecondary", null, () =>
        {
            setting = "";
            shown.Text = Store.Receipts.DefaultFolder;
            Changed();
        }, small: true);
        _readers[SettingKeys.ReceiptsFolder] = () => setting;

        var body = Ui.Stack(0,
            Store.Expenses.Enabled ? new Border() : Ui.Muted("Expenses is off. Turn it on under Features.", 13.6).Also(t => t.Margin = new Thickness(0, 0, 0, 12)),
            Group("Mileage"),
            Money(SettingKeys.MileageRate, "Mileage rate (per mile)", "Used for new mileage expenses. Keep it at the current IRS standard mileage rate; check it each January."),
            Group("Receipts"),
            Ui.Field("Receipts folder", Ui.Stack(8, shown, Ui.Row(8, browse, reset)),
                hint: "Where attached receipts are kept. Choose a OneDrive or network folder to have them backed up there; they are not inside a database backup."));
        if (Store.Expenses.Enabled)
            body.Children.Add(Ui.Stack(0, Group("Categories"),
                Ui.Button("Manage Categories", "Btn.OutlinePrimary", "tags", () => Shell.Navigate(Routes.ExpenseCategories(Shell)), small: true)
                    .Also(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 0, 0, 16); })));
        return body;
    }

    private FrameworkElement Appearance()
    {
        // Theme: the same setting as the theme button in the top bar.
        string theme = Store.Settings.Theme();
        var themeField = Choice(SettingKeys.Theme, "Theme",
            new[] { (ThemeManager.Light, "Light"), (ThemeManager.Dark, "Dark"), (ThemeManager.Terminal, "Terminal") }, theme);

        _shown.Add(SettingKeys.PrimaryColor);
        var hex = Ui.TextBox(Store.Settings.Get(SettingKeys.PrimaryColor)).Also(t => { t.Width = 110; t.SetResourceReference(TextBox.FontFamilyProperty, "MonoFont"); });
        var swatch = new Border { Width = 56, Height = 36, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 8, 0) }
            .WithResource(Border.BorderBrushProperty, "InputBorder");
        void Paint() { if (ThemeManager.ParseColor(hex.Text) is { } c) swatch.Background = new SolidColorBrush(c); }
        hex.TextChanged += (_, _) => { Paint(); Changed(); };
        Paint();
        _readers[SettingKeys.PrimaryColor] = () => hex.Text.Trim();
        var colorField = _fields[SettingKeys.PrimaryColor] = Ui.Field("Accent colour", Ui.Row(0, swatch, hex), hint: "A hex colour such as #2563eb. Used for buttons, links and the sidebar highlight.");

        var scales = new[] { "0.80", "0.85", "0.90", "0.95", "1.0", "1.05", "1.10", "1.15", "1.20", "1.25" }
            .Select(v => (v, v == "1.0" ? "100% (normal)" : $"{double.Parse(v, CultureInfo.InvariantCulture) * 100:0}%")).ToList();
        var scaleField = Choice(SettingKeys.UiFontScale, "Text size", scales, Store.Settings.Get(SettingKeys.UiFontScale, "1.0"));

        return Ui.Stack(0,
            Group("Look"),
            Ui.Columns(16, (Ui.Star(), themeField), (Ui.Star(), scaleField)),
            colorField,
            Group("Names"),
            Ui.Columns(16,
                (Ui.Star(), Text(SettingKeys.AppName, "App name", "Shown in the title bar and the sidebar.")),
                (Ui.Star(), Text(SettingKeys.FooterText, "Footer text", "Shown at the bottom of every page."))),
            Text(SettingKeys.AppTagline, "Tagline", "Shown on the lock screen."),
            Group("Sidebar icon"),
            ImageRow("Sidebar icon image", "A PNG shown at the top of the sidebar instead of the built-in genie.", Store.Paths.AppIconPath,
                SettingKeys.AppIconImage, "app_icon.png", "Remove the custom sidebar icon?"),
            Text(SettingKeys.AppIcon, "Icon when there is no image", "A Bootstrap Icons name, used only when no image is set.", mono: true));
    }

    private FrameworkElement Security()
    {
        _shown.Add(SettingKeys.LoginLogoLayout);
        return Ui.Stack(0,
            Group("App password"),
            PasswordRow(),
            Group("Lock screen"),
            Ui.Field("Logo position", LayoutPicker(SettingKeys.LoginLogoLayout, Store.Settings.Get(SettingKeys.LoginLogoLayout))),
            ImageRow("Lock screen logo", "A PNG shown on the lock screen instead of the built-in genie.", Store.Paths.LoginLogoPath,
                SettingKeys.LoginLogo, "login_logo.png", "Remove the custom lock screen logo?"));
    }

    private FrameworkElement Backup()
    {
        var reminder = Store.BackupReminder;
        var sw = Switch(SettingKeys.BackupReminderEnabled, reminder.Enabled);
        var days = Text(SettingKeys.BackupReminderDays, "Days between reminders", "Making a backup also restarts the count.", maxWidth: 120);
        ((TextBox)days.Content).Text = reminder.Days.ToString(CultureInfo.InvariantCulture);
        void Sync() => days.Visibility = sw.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        sw.Checked += (_, _) => Sync();
        sw.Unchecked += (_, _) => Sync();
        Sync();
        // A hidden field keeps its saved value, so turning the reminder off never trips validation.
        var read = _readers[SettingKeys.BackupReminderDays];
        _readers[SettingKeys.BackupReminderDays] = () => sw.IsChecked == true ? read() : reminder.Days.ToString(CultureInfo.InvariantCulture);
        return Ui.Stack(0,
            SwitchRow(SettingKeys.BackupReminderEnabled, "Remind me to back up", "When ProfitDjinn opens, it asks you to save a backup every few days.", sw),
            days,
            Group("Backup and restore"),
            Ui.Button("Open Backup & Restore", "Btn.OutlinePrimary", "database", () => Shell.Navigate(Routes.Backup(Shell)), small: true)
                .Also(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 0, 0, 16); }));
    }

    private FrameworkElement Updates()
    {
        var u = Store.Updates;
        var sw = Switch(SettingKeys.UpdateCheckEnabled, u.Enabled);
        var status = Ui.Text(StatusText(), "Body", 14.4, wrap: true);
        var view = Ui.Button("View Release", "Btn.Success", "box-arrow-up-right", () => Shell.OpenLink(u.Known?.Url ?? UpdateService.ReleasesPage), small: true);
        view.Visibility = u.Known is null ? Visibility.Collapsed : Visibility.Visible;
        Button check = null!;
        check = Ui.Button("Check Now", "Btn.OutlinePrimary", "arrow-repeat", async () =>
        {
            check.IsEnabled = false;
            status.Text = "Checking GitHub…";
            var result = await u.CheckAsync();
            status.Text = result.Message;
            view.Visibility = result.Update is null ? Visibility.Collapsed : Visibility.Visible;
            check.IsEnabled = true;
            Shell.ShowUpdateBadge();
        }, small: true);

        string StatusText()
        {
            string last = u.LastChecked is { } d ? $" Last checked {Ui.Date(d)}." : "";
            return u.Known is { } k ? $"Version {k.Version} is available. You have {UpdateService.Clean(u.RunningVersion)}.{last}"
                : $"You have version {UpdateService.Clean(u.RunningVersion)}.{last}";
        }

        var about = new Grid();
        about.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        about.ColumnDefinitions.Add(new ColumnDefinition());
        void Fact(string label, UIElement value)
        {
            int r = about.RowDefinitions.Count;
            about.RowDefinitions.Add(new RowDefinition());
            var l = Ui.Muted(label, 14).Margin(0, 0, 8, 10);
            Grid.SetRow(l, r);
            if (value is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 0, 10);
            Grid.SetRow(value, r);
            Grid.SetColumn(value, 1);
            about.Children.Add(l);
            about.Children.Add(value);
        }
        Fact("Version", Ui.Text(AppInfo.Version, "Body", 14).WithResource(TextBlock.FontFamilyProperty, "MonoFont"));
        Fact("Built", Ui.Text(AppInfo.BuildDate, "Body", 14));
        Fact("Data folder", Ui.Stack(6, Ui.Text(Store.Paths.DataFolder, "Body", 13.6, wrap: true).WithResource(TextBlock.FontFamilyProperty, "MonoFont"),
            Ui.Button("Open Folder", "Btn.OutlineSecondary", "folder2-open", () =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Store.Paths.DataFolder) { UseShellExecute = true }); }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Shell.ShowError($"The folder could not be opened: {ex.Message}"); }
            }, small: true).Also(b => b.HorizontalAlignment = HorizontalAlignment.Left)));

        return Ui.Stack(0,
            SwitchRow(SettingKeys.UpdateCheckEnabled, "Check for updates",
                "Once a day, ProfitDjinn asks GitHub whether a newer version is out and shows a badge at the bottom of the window. Nothing about you or your data is sent.", sw),
            Ui.Stack(10, status, Ui.Row(8, check, view)).Margin(0, 0, 0, 20),
            Group("About"),
            about);
    }

    /// <summary>Settings no section claims, shown by key with a widget for their type.</summary>
    private FrameworkElement Advanced(IReadOnlyList<Setting> settings)
    {
        var stack = new StackPanel();
        foreach (var s in settings)
        {
            if (s.Type == "boolean")
                stack.Children.Add(SwitchRow(s.Key, s.Key, s.Description ?? "", Switch(s.Key, Setting.AsBool(s.Value))));
            else
                stack.Children.Add(Text(s.Key, s.Key, s.Description, mono: true));
        }
        return stack;
    }

    // ------------------------------------------------------------------ pieces kept from 2.2

    private FrameworkElement LayoutPicker(string key, string value)
    {
        string current = value == "top" ? "top" : "left";
        var cards = new List<Border>();
        FrameworkElement Card(string option, string label)
        {
            var preview = new Grid { Width = 90, Height = 60, Margin = new Thickness(0, 0, 0, 6) };
            preview.Children.Add(new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4) }.WithResource(Border.BorderBrushProperty, "Border").WithResource(Border.BackgroundProperty, "TertiaryBg"));
            if (option == "left")
            {
                preview.Children.Add(new Border { Width = 30, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(4, 0, 0, 4) }.WithResource(Border.BackgroundProperty, "BrandPrimary"));
                preview.Children.Add(new Border { Width = 40, Height = 6, Margin = new Thickness(38, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(3) }.WithResource(Border.BackgroundProperty, "Border"));
            }
            else
            {
                preview.Children.Add(new Border { Width = 20, Height = 14, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 0, 0), CornerRadius = new CornerRadius(3) }.WithResource(Border.BackgroundProperty, "BrandPrimary"));
                preview.Children.Add(new Border { Width = 50, Height = 6, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 12), CornerRadius = new CornerRadius(3) }.WithResource(Border.BackgroundProperty, "Border"));
            }
            var b = new Border
            {
                BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 12, 16, 12), MinWidth = 110, Margin = new Thickness(0, 0, 12, 0),
                Cursor = Cursors.Hand, Tag = option, Background = Brushes.Transparent,
                Child = Ui.Stack(0, preview, Ui.Text(label, "Strong", 12.8).Also(t => t.HorizontalAlignment = HorizontalAlignment.Center)),
            };
            b.MouseLeftButtonUp += (_, _) => { current = option; Paint(); Changed(); };
            cards.Add(b);
            return b;
        }
        void Paint()
        {
            foreach (var c in cards) c.SetResourceReference(Border.BorderBrushProperty, (string)c.Tag == current ? "BrandPrimary" : "Border");
        }
        var row = Ui.Row(0, Card("top", "Top"), Card("left", "Left"));
        Paint();
        _readers[key] = () => current;
        return row;
    }

    /// <summary>A PNG upload. It saves at once (not with Save), as in 2.0.</summary>
    private FrameworkElement ImageRow(string label, string hint, string path, string settingKey, string settingValue, string removeQuestion)
    {
        bool custom = File.Exists(path) && Store.Settings.Get(settingKey).Length > 0;
        var widget = Ui.Row(8);
        if (custom && MainWindow.LoadImage(path) is { } img)
            widget.Children.Add(new Border { Padding = new Thickness(4), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = new Image { Source = img, Height = 40 } }
                .WithResource(Border.BorderBrushProperty, "Border"));
        else widget.Children.Add(Ui.Muted(Store.Settings.Get(settingKey).Length > 0 ? "Using the built-in genie" : "Using the icon below", 13.6).Margin(0, 0, 8, 0).Also(t => t.VerticalAlignment = VerticalAlignment.Center));
        widget.Children.Add(Ui.Button("Upload", "Btn.Primary", "upload", () => Upload(path, settingKey, settingValue), small: true).Margin(8, 0, 0, 0));
        if (Store.Settings.Get(settingKey).Length > 0)
            widget.Children.Add(Ui.Button("Remove", "Btn.OutlineDanger", "trash", async () =>
            {
                if (!await Shell.Confirm(removeQuestion, "Remove", danger: true)) return;
                try { if (File.Exists(path)) File.Delete(path); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Shell.ShowError($"The image could not be removed: {ex.Message}");
                    return;
                }
                Store.Settings.Set(settingKey, "");
                Shell.RefreshChrome();
                ReloadHere(Notice.Success("Image removed."));
            }, small: true).Margin(8, 0, 0, 0));
        return Ui.Field(label, widget, hint: hint);
    }

    /// <summary>1.x's rules: a .png file, really a PNG (signature checked), 2 MB at most.</summary>
    private void Upload(string path, string settingKey, string settingValue)
    {
        var dialog = new OpenFileDialog { Filter = "PNG image (*.png)|*.png" };
        if (dialog.ShowDialog(Shell) != true) return;
        try
        {
            var info = new FileInfo(dialog.FileName);
            if (!info.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase)) throw new UserFacingException("Only PNG images can be used.");
            if (info.Length > 2 * 1024 * 1024) throw new UserFacingException("That image is larger than 2 MB. Use a smaller PNG.");
            byte[] bytes = File.ReadAllBytes(info.FullName);
            byte[] signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            if (bytes.Length < 8 || !bytes.AsSpan(0, 8).SequenceEqual(signature)) throw new UserFacingException("That file is not a real PNG image.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            Store.Settings.Set(settingKey, settingValue);
            Shell.RefreshChrome();
            ReloadHere(Notice.Success("Image uploaded."));
        }
        catch (UserFacingException ex) { Shell.ShowError(ex.Message); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Shell.ShowError($"The image could not be saved: {ex.Message}"); }
    }

    /// <summary>The app password acts at once: set, change or turn off.</summary>
    private FrameworkElement PasswordRow()
    {
        var pw = Store.Password;
        var current = new PasswordBox();
        var fresh = new PasswordBox();
        var confirm = new PasswordBox();
        var form = new StackPanel();
        form.Children.Add(Ui.Muted(pw.IsSet
            ? "ProfitDjinn asks for this password when it starts. Enter the current one to change it or turn it off."
            : "Optional. When set, ProfitDjinn asks for it at start, and Lock in the sidebar locks the app. It keeps casual eyes out on a shared PC; it does not encrypt the data.", 13)
            .Also(t => t.TextWrapping = TextWrapping.Wrap).Margin(0, 0, 0, 12));
        if (pw.IsSet) form.Children.Add(Ui.Field("Current password", current));
        form.Children.Add(Ui.Columns(16, (Ui.Star(), Ui.Field(pw.IsSet ? "New password" : "Password (8+ characters)", fresh)), (Ui.Star(), Ui.Field("Confirm", confirm))));
        var buttons = Ui.Row(8, Ui.Button(pw.IsSet ? "Change Password" : "Set Password", "Btn.Primary", "key-fill", () =>
        {
            Try(() =>
            {
                pw.Set(pw.IsSet ? current.Password : null, fresh.Password, confirm.Password);
                Shell.RefreshChrome();
                ReloadHere(Notice.Success("App password saved. ProfitDjinn will ask for it at start, and Lock in the sidebar locks it now."));
            });
        }, small: true));
        if (pw.IsSet)
            buttons.Children.Add(Ui.Button("Turn Off Password", "Btn.OutlineDanger", "shield-x", async () =>
            {
                if (!await Shell.Confirm("Turn off the app password? ProfitDjinn will open without asking.", "Turn Off", danger: true)) return;
                Try(() =>
                {
                    pw.Remove(current.Password);
                    Shell.RefreshChrome();
                    ReloadHere(Notice.Warning("App password turned off."));
                });
            }, small: true).Margin(8, 0, 0, 0));
        form.Children.Add(buttons.Margin(0, 0, 0, 20));
        return form;
    }

    /// <summary>Rebuilds the page on the section being shown.</summary>
    private void ReloadHere(Notice notice)
    {
        _reopen = _current;
        Shell.Reload(notice);
    }

    // ------------------------------------------------------------------ save

    private void Discard() => ReloadHere(Notice.Info("Changes discarded."));

    /// <summary>
    /// Before a new receipts folder is saved: it must be writable, and existing receipts can be
    /// moved there. Returns false to stop the save, and a message about any move.
    /// </summary>
    private async Task<(bool Ok, string? Message)> ChangeReceiptsFolder(string newSetting)
    {
        string from = Store.Receipts.CurrentFolder, to = Store.Receipts.Effective(newSetting);
        if (string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(from)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(to)), StringComparison.OrdinalIgnoreCase)) return (true, null);
        if (ReceiptStore.ValidateFolder(to) is { } problem) { Shell.ShowError(problem); return (false, null); }
        int count = Store.Expenses.ReceiptCount();
        if (count == 0) return (true, null);

        var body = Ui.Text($"{count} receipt {(count == 1 ? "file is" : "files are")} in\n{from}\n\nMove {(count == 1 ? "it" : "them")} to the new folder? " +
            "If you leave them, they still open from where they are, and only new receipts go to the new folder.", "Body", 14.4, wrap: true);
        bool move = await Shell.OpenDialog("Move receipts?", "folder2-open", body, $"Move {count} {(count == 1 ? "Receipt" : "Receipts")}", () => true,
            primaryGlyph: "arrow-right", maxWidth: 520, cancelText: "Leave Them");
        if (!move) return (true, null);
        var r = Store.Expenses.MoveReceipts(to);
        string msg = $"{r.Moved} {(r.Moved == 1 ? "receipt" : "receipts")} moved.";
        if (r.Missing > 0) msg += $" {r.Missing} could not be found.";
        if (r.Failed.Count > 0) msg += $" {r.Failed.Count} could not be moved: {string.Join("; ", r.Failed.Take(3))}";
        return (true, msg);
    }

    /// <summary>The checks before saving. Field problems show under their fields and the section opens.</summary>
    private Dictionary<string, string> Problems(IReadOnlyDictionary<string, string> v)
    {
        var bad = new Dictionary<string, string>();
        if (v.TryGetValue(SettingKeys.PrimaryColor, out var color) && ThemeManager.ParseColor(color) is null)
            bad[SettingKeys.PrimaryColor] = $"\"{color}\" is not a colour. Use a hex value such as #2563eb.";
        if (v.TryGetValue(SettingKeys.BackupReminderDays, out var days) && BackupReminder.Validate(days) is { } p)
            bad[SettingKeys.BackupReminderDays] = p;
        foreach (string key in new[] { SettingKeys.InvoiceNextNumber, SettingKeys.WorkOrderNextNumber })
            if (v.TryGetValue(key, out var n) && (n.Length == 0 || !n.All(char.IsAsciiDigit)))
                bad[key] = "Enter a whole number, e.g. 1001.";
        if (v.TryGetValue(SettingKeys.MileageRate, out var mileage) &&
            !(decimal.TryParse(mileage.TrimStart('$'), NumberStyles.Number, CultureInfo.InvariantCulture, out var mr) && mr > 0 && mr < 100))
            bad[SettingKeys.MileageRate] = "Enter dollars per mile, such as 0.70.";
        if (v.TryGetValue(SettingKeys.DefaultHourlyRate, out var rate) &&
            !(decimal.TryParse(rate.TrimStart('$'), NumberStyles.Number, CultureInfo.InvariantCulture, out var r) && r >= 0 && decimal.Round(r, 2) == r))
            bad[SettingKeys.DefaultHourlyRate] = "Enter an amount such as 75.00, or 0.00.";
        return bad;
    }

    private async void Save()
    {
        foreach (var f in _fields.Values) { f.Error = null; if (f.Content is TextBox t) Input.SetInvalid(t, false); }
        var values = _readers.ToDictionary(r => r.Key, r => r.Value());
        var changed = values.Where(kv => kv.Value != _initial.GetValueOrDefault(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        if (changed.Count == 0) { Changed(); return; }

        var bad = Problems(changed);
        if (bad.Count > 0)
        {
            foreach (var (key, message) in bad)
                if (_fields.TryGetValue(key, out var f)) { f.Error = message; if (f.Content is TextBox t) Input.SetInvalid(t, true); }
            string first = bad.Keys.First();
            var home = _sections.FirstOrDefault(s => IsInside(_fields.GetValueOrDefault(first), s.Value.Body)).Key;
            if (home is not null) ShowSection(home);
            Shell.ShowError(string.Join(" ", bad.Values.Distinct()));
            return;
        }

        string? moved = null;
        if (changed.TryGetValue(SettingKeys.ReceiptsFolder, out var folder))
        {
            var (ok, message) = await ChangeReceiptsFolder(folder);
            if (!ok) return;
            moved = message;
        }

        bool wasOn = Store.Expenses.Enabled;
        if (changed.Remove(SettingKeys.Theme, out var theme)) Store.Settings.SetTheme(theme);
        Store.Settings.SetMany(changed);

        string extra = moved is null ? "" : " " + moved;
        if (!wasOn && Store.Expenses.Enabled)
        {
            extra += " Expenses is on: Vendors, Expenses and Profit & Loss are now in the sidebar.";
            if (RecurringService.Summarize(Store.Recurring.GenerateDue()) is { } made) extra += " " + made.Message;
        }
        foreach (var (key, value) in changed) _initial[key] = value;
        if (theme is not null) _initial[SettingKeys.Theme] = theme;
        ThemeManager.Apply(Store.Settings.Theme(), Store.Settings.Get(SettingKeys.PrimaryColor));
        Shell.RefreshChrome();
        ReloadHere(Notice.Success("Settings saved." + extra));
    }

    private static bool IsInside(DependencyObject? child, DependencyObject root)
    {
        for (var d = child; d is not null; d = LogicalTreeHelper.GetParent(d))
            if (ReferenceEquals(d, root)) return true;
        return false;
    }

    protected override void ShowFieldErrors(IReadOnlyDictionary<string, string> errors)
    {
        foreach (var (key, message) in errors)
            if (_fields.TryGetValue(key, out var f)) f.Error = message;
    }
}
