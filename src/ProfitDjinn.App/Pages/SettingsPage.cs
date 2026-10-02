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
/// App Settings (1.x admin/settings.html): every setting by category, one widget per type,
/// Save All. 2.0 additions: the app password under Security, logo and icon uploads kept in
/// the data folder, and the settings that do nothing are hidden.
/// </summary>
public sealed class SettingsPage : AppPage
{
    /// <summary>
    /// Not shown. app_password_hash and theme have their own controls; the rest did nothing in
    /// 1.x either (no registration, no maintenance page, no session timeout, no paging setting,
    /// one theme for the whole app). The rows stay in the database for the 1.x build.
    /// </summary>
    private static readonly HashSet<string> Hidden = new()
    {
        SettingKeys.AppPasswordHash, SettingKeys.Theme, "allow_registration", "maintenance_mode",
        "session_timeout", "items_per_page", "default_theme",
    };

    private readonly Dictionary<string, Func<string>> _readers = new();
    private readonly Dictionary<string, FrameworkElement> _categories = new();
    private readonly string? _scrollTo;

    public override string NavKey => "settings";
    public override IReadOnlyList<Crumb> Crumbs => new[] { new Crumb("Settings") };

    public SettingsPage(MainWindow shell, string? category) : base(shell)
    {
        _scrollTo = category;
        var page = new StackPanel();
        page.Children.Add(Ui.PageHeaderWithGlyph("sliders", "App Settings", "Changes apply after you click Save All Settings."));

        var settings = Store.Settings.All().Where(s => !Hidden.Contains(s.Key)).ToList();
        foreach (var group in settings.GroupBy(s => s.Category ?? "general").OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var body = new StackPanel();
            if (group.Key == "security") body.Children.Add(PasswordRow());
            if (group.Key == "appearance") body.Children.Add(ImageRow("app_icon_img", "Upload a PNG to replace the Bootstrap icon in the sidebar", Store.Paths.AppIconPath, SettingKeys.AppIconImage, "app_icon.png", "Remove the custom app icon?"));
            if (group.Key == "login") body.Children.Add(ImageRow("login_logo", "Upload a PNG image to replace the icon on the lock screen", Store.Paths.LoginLogoPath, SettingKeys.LoginLogo, "login_logo.png", "Remove the custom login logo?"));
            if (group.Key == "backup") body.Children.Add(BackupReminderRows());
            if (group.Key == "expenses") body.Children.Add(ExpensesRows());
            foreach (var s in group.Where(s => group.Key is not ("backup" or "expenses")))
            {
                if (s.Key is SettingKeys.LoginLogo or SettingKeys.AppIconImage) continue;
                body.Children.Add(SettingRow(s));
            }
            var card = CategoryCard(group.Key, body);
            _categories[group.Key] = card;
            page.Children.Add(card.Margin(0, 0, 0, 24));
        }
        if (!settings.Any(s => s.Category == "security"))
        {
            var card = CategoryCard("security", Ui.Stack(0, PasswordRow()));
            _categories["security"] = card;
            page.Children.Add(card.Margin(0, 0, 0, 24));
        }

        page.Children.Add(Ui.Row(8,
            Ui.Button("Save All Settings", "Btn.Primary", "floppy", SaveAll),
            Ui.Button("Cancel", "Btn.OutlineSecondary", null, () => Shell.Navigate(Routes.Dashboard(Shell)))));
        Content = page;
    }

    public override void OnShown()
    {
        if (_scrollTo is not null && _categories.TryGetValue(_scrollTo, out var card)) card.BringIntoView();
    }

    // ------------------------------------------------------------------ layout

    private static FrameworkElement CategoryCard(string category, UIElement body)
    {
        string glyph = category switch
        {
            "general" => "grid-1x2", "appearance" => "palette", "security" => "shield-lock", "login" => "box-arrow-in-right",
            "ui" => "type", "invoices" => "receipt", "workorders" => "clipboard-check", "backup" => "database", "expenses" => "wallet2", _ => "gear",
        };
        string title = category == "workorders" ? "Work Orders" : category == "login" ? "Lock Screen"
            : category.Length == 0 ? "General" : char.ToUpperInvariant(category[0]) + category[1..].ToLowerInvariant();
        var header = new Border { Padding = new Thickness(20, 13.6, 20, 13.6), BorderThickness = new Thickness(0, 0, 0, 1),
            Child = Ui.Row(8, new Icon { Glyph = glyph, Size = 13 }.WithResource(Icon.ForegroundProperty, "TextMuted"),
                Ui.Text(title.ToUpperInvariant(), "TableHeaderText", 12)) }
            .WithResource(Border.BackgroundProperty, "CardHeaderBg").WithResource(Border.BorderBrushProperty, "Border");
        var card = Ui.Card(Ui.Stack(0, header, body), bodyPadding: new Thickness(0));
        return card;
    }

    /// <summary>One setting: key, description and type badge on the left (2fr), its widget on the right (3fr).</summary>
    private FrameworkElement Row(string key, string? description, string? type, UIElement widget)
    {
        var left = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        left.Children.Add(Ui.Text(key, "Strong", 14).Also(t => t.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont")));
        if (!string.IsNullOrEmpty(description)) left.Children.Add(Ui.Muted(description, 12.5).Also(t => t.TextWrapping = TextWrapping.Wrap).Margin(0, 2, 0, 0));
        if (type is not null) left.Children.Add(Ui.Badge(type, "secondary50").Margin(0, 4, 0, 0));
        var grid = Ui.Columns(0, (Ui.Star(2), left), (Ui.Star(3), widget));
        return new Border { Padding = new Thickness(20, 16, 20, 16), BorderThickness = new Thickness(0, 0, 0, 1), Child = grid }.WithResource(Border.BorderBrushProperty, "Border");
    }

    private FrameworkElement SettingRow(Setting s)
    {
        string value = s.Value ?? "";
        FrameworkElement widget;
        switch (s.Type)
        {
            case "boolean":
            {
                var sw = new CheckBox { Style = Ui.Style("Switch"), IsChecked = Setting.AsBool(value) };
                sw.Content = sw.IsChecked == true ? "Enabled" : "Disabled";
                sw.Click += (_, _) => sw.Content = sw.IsChecked == true ? "Enabled" : "Disabled";
                _readers[s.Key] = () => sw.IsChecked == true ? "true" : "false";
                widget = sw;
                break;
            }
            case "color":
            {
                var hex = Ui.TextBox(value).Also(t => { t.Width = 110; t.SetResourceReference(TextBox.FontFamilyProperty, "MonoFont"); });
                var swatch = new Border { Width = 56, Height = 36, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 8, 0) }
                    .WithResource(Border.BorderBrushProperty, "InputBorder");
                void Paint() { if (ThemeManager.ParseColor(hex.Text) is { } c) swatch.Background = new SolidColorBrush(c); }
                hex.TextChanged += (_, _) => Paint();
                Paint();
                _readers[s.Key] = () => hex.Text.Trim();
                widget = Ui.Row(0, swatch, hex);
                break;
            }
            case "select" when s.Key == SettingKeys.LoginLogoLayout:
                widget = LayoutPicker(s.Key, value);
                break;
            case "select":
            {
                var options = s.OptionList.ToList();
                if (!options.Contains(value)) options.Insert(0, value);
                var combo = new ComboBox { ItemsSource = options.Select(Capitalize).ToList(), SelectedIndex = options.IndexOf(value), MaxWidth = 320, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 160 };
                _readers[s.Key] = () => combo.SelectedIndex >= 0 ? options[combo.SelectedIndex] : value;
                widget = combo;
                break;
            }
            case "json":
            {
                var area = Ui.TextArea(value, 80).Also(t => t.SetResourceReference(TextBox.FontFamilyProperty, "MonoFont"));
                _readers[s.Key] = () => area.Text;
                widget = area;
                break;
            }
            default:
            {
                var box = Ui.TextBox(value);
                if (s.Type == "number") { box.MaxWidth = 200; box.HorizontalAlignment = HorizontalAlignment.Left; box.MinWidth = 160; }
                _readers[s.Key] = () => box.Text;
                widget = box;
                break;
            }
        }
        return Row(s.Key, s.Description, s.Type, widget);
    }

    /// <summary>Python's str.capitalize(), as 1.x labelled select options.</summary>
    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();

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
            b.MouseLeftButtonUp += (_, _) => { current = option; Paint(); };
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

    // ------------------------------------------------------------------ images

    private FrameworkElement ImageRow(string key, string description, string path, string settingKey, string settingValue, string removeQuestion)
    {
        bool custom = File.Exists(path) && Store.Settings.Get(settingKey).Length > 0;
        var widget = Ui.Row(8);
        if (custom && MainWindow.LoadImage(path) is { } img)
            widget.Children.Add(new Border { Padding = new Thickness(4), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = new Image { Source = img, Height = 40 } }
                .WithResource(Border.BorderBrushProperty, "Border"));
        else widget.Children.Add(Ui.Muted(Store.Settings.Get(settingKey).Length > 0 ? "Using the built-in genie" : "Using the Bootstrap icon", 13.6).Margin(0, 0, 8, 0));
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
                Shell.Reload(Notice.Success("Image removed."));
            }, small: true).Margin(8, 0, 0, 0));
        return Row(key, description, "image", widget);
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
            Shell.Reload(Notice.Success("Image uploaded."));
        }
        catch (UserFacingException ex) { Shell.ShowError(ex.Message); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Shell.ShowError($"The image could not be saved: {ex.Message}"); }
    }

    // ------------------------------------------------------------------ app password

    private FrameworkElement PasswordRow()
    {
        var pw = Store.Password;
        var current = new PasswordBox();
        var fresh = new PasswordBox();
        var confirm = new PasswordBox();
        var form = new StackPanel();
        if (pw.IsSet) form.Children.Add(Ui.Field("Current password", current));
        form.Children.Add(Ui.Columns(16, (Ui.Star(), Ui.Field(pw.IsSet ? "New password" : "Password (8+ characters)", fresh)), (Ui.Star(), Ui.Field("Confirm", confirm))));
        var buttons = Ui.Row(8, Ui.Button(pw.IsSet ? "Change Password" : "Set Password", "Btn.Primary", "key-fill", () =>
        {
            Try(() =>
            {
                pw.Set(pw.IsSet ? current.Password : null, fresh.Password, confirm.Password);
                Shell.RefreshChrome();
                Shell.Reload(Notice.Success("App password saved. ProfitDjinn will ask for it at start, and Lock in the sidebar locks it now."));
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
                    Shell.Reload(Notice.Warning("App password turned off."));
                });
            }, small: true).Margin(8, 0, 0, 0));
        form.Children.Add(buttons);
        string desc = pw.IsSet
            ? "ProfitDjinn asks for this password when it starts. Enter the current one to change it or turn it off."
            : "Optional. When set, ProfitDjinn asks for it at start. It keeps casual eyes out on a shared PC; it does not encrypt the data.";
        return Row("App password", desc, pw.IsSet ? "on" : "off", form);
    }

    // ------------------------------------------------------------------ backup reminder

    /// <summary>The reminder switch, and the days field, which only shows while the reminder is on.</summary>
    private FrameworkElement BackupReminderRows()
    {
        var reminder = Store.BackupReminder;
        var sw = new CheckBox { Style = Ui.Style("Switch"), IsChecked = reminder.Enabled };
        var days = Ui.TextBox(reminder.Days.ToString(CultureInfo.InvariantCulture)).Also(t => { t.MaxWidth = 200; t.MinWidth = 160; t.HorizontalAlignment = HorizontalAlignment.Left; });
        var daysRow = Row(SettingKeys.BackupReminderDays, "Days between backup reminders. Making a backup also restarts the count.", "number", days);
        void Sync()
        {
            sw.Content = sw.IsChecked == true ? "Enabled" : "Disabled";
            daysRow.Visibility = sw.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }
        sw.Click += (_, _) => Sync();
        Sync();
        _readers[SettingKeys.BackupReminderEnabled] = () => sw.IsChecked == true ? "true" : "false";
        // A hidden field keeps its saved value, so turning the reminder off never trips validation.
        _readers[SettingKeys.BackupReminderDays] = () => sw.IsChecked == true ? days.Text.Trim() : reminder.Days.ToString(CultureInfo.InvariantCulture);
        return Ui.Stack(0,
            Row(SettingKeys.BackupReminderEnabled, "When ProfitDjinn opens, ask me to back up my data every few days.", "boolean", sw),
            daysRow);
    }

    // ------------------------------------------------------------------ expenses (2.2)

    /// <summary>The Expenses switch, where receipt files go, and a way to the categories.</summary>
    private FrameworkElement ExpensesRows()
    {
        bool on = Store.Expenses.Enabled;
        var sw = new CheckBox { Style = Ui.Style("Switch"), IsChecked = on };
        void Label() => sw.Content = sw.IsChecked == true ? "Enabled" : "Disabled";
        sw.Click += (_, _) => Label();
        Label();
        _readers[SettingKeys.ExpensesEnabled] = () => sw.IsChecked == true ? "true" : "false";

        string setting = Store.Settings.Get(SettingKeys.ReceiptsFolder).Trim();
        var shown = Ui.TextBox(Store.Receipts.Effective(setting)).Also(t => { t.IsReadOnly = true; t.SetResourceReference(TextBox.FontFamilyProperty, "MonoFont"); t.FontSize = 13; });
        var browse = Ui.Button("Browse…", "Btn.OutlinePrimary", "folder2-open", () =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Folder for receipt files", InitialDirectory = Directory.Exists(shown.Text) ? shown.Text : "" };
            if (dialog.ShowDialog(Shell) != true) return;
            setting = dialog.FolderName;
            shown.Text = Store.Receipts.Effective(setting);
        }, small: true);
        var reset = Ui.Button("Use Default", "Btn.OutlineSecondary", null, () =>
        {
            setting = "";
            shown.Text = Store.Receipts.DefaultFolder;
        }, small: true);
        _readers[SettingKeys.ReceiptsFolder] = () => setting;
        var folderWidget = Ui.Stack(8, shown, Ui.Row(8, browse, reset));

        var rows = Ui.Stack(0,
            Row(SettingKeys.ExpensesEnabled, "Track vendors, bills and recurring costs. Off hides Vendors and Expenses from the sidebar; nothing is deleted.", "boolean", sw),
            Row(SettingKeys.ReceiptsFolder, "Where attached receipt files are kept. Pick a OneDrive or network folder to keep them backed up there. Receipts are not inside a database backup.", "folder", folderWidget));
        if (on)
            rows.Children.Add(Row("Categories", "Add, rename or hide expense categories.", null,
                Ui.Button("Manage Categories", "Btn.OutlinePrimary", "tags", () => Shell.Navigate(Routes.ExpenseCategories(Shell)), small: true)
                    .Also(b => b.HorizontalAlignment = HorizontalAlignment.Left)));
        return rows;
    }

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

    // ------------------------------------------------------------------ save

    private async void SaveAll()
    {
        var values = _readers.ToDictionary(r => r.Key, r => r.Value());
        if (values.TryGetValue(SettingKeys.PrimaryColor, out var color) && ThemeManager.ParseColor(color) is null)
        {
            Shell.ShowError($"\"{color}\" is not a colour. Use a hex value such as #2563eb.");
            return;
        }
        if (values.TryGetValue(SettingKeys.BackupReminderDays, out var days) && BackupReminder.Validate(days) is { } problem)
        {
            Shell.ShowError(problem);
            return;
        }
        string? moved = null;
        if (values.TryGetValue(SettingKeys.ReceiptsFolder, out var folder))
        {
            var (ok, message) = await ChangeReceiptsFolder(folder);
            if (!ok) return;
            moved = message;
        }
        bool wasOn = Store.Expenses.Enabled;
        Store.Settings.SetMany(values);
        string extra = moved is null ? "" : " " + moved;
        if (!wasOn && Store.Expenses.Enabled)
        {
            extra += " Expenses is on: Vendors and Expenses are now in the sidebar.";
            if (RecurringService.Summarize(Store.Recurring.GenerateDue()) is { } made) extra += " " + made.Message;
        }
        ThemeManager.Apply(ThemeManager.Current, Store.Settings.Get(SettingKeys.PrimaryColor));
        Shell.RefreshChrome();
        Shell.Reload(Notice.Success("Settings saved successfully." + extra));
    }
}
