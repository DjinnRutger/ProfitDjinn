using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ProfitDjinn.App.Controls;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.Core;
using ProfitDjinn.Core.Data;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.App.Shell;

/// <summary>
/// The application window: sidebar, top bar with breadcrumb, content area, footer, and the
/// overlays (dialogs, lock screen, terminal scanlines). Pages are shown in the content area
/// and kept in a Back/Forward history, like a browser, so Alt+Left goes back.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly (string Key, string Label, string Glyph)[] MainNav =
    {
        ("dashboard", "Dashboard", "speedometer2"),
        ("customers", "Customers", "building"),
        ("invoices", "Invoices", "receipt"),
        ("workorders", "Work Orders", "clipboard-check"),
        ("vendors", "Vendors", "shop"),            // 2.2, shown only while Expenses is on
        ("expenses", "Expenses", "wallet2"),
        ("revenue", "Revenue", "graph-up-arrow"),
        ("profit", "Profit & Loss", "bar-chart-line"),   // 2.2, shown only while Expenses is on
        ("banking", "Banking", "bank"),                  // 2.6, shown only while Bank Accounts is on
        ("items", "Items", "box-seam"),
    };

    private static readonly (string Key, string Label, string Glyph)[] AdminNav =
    {
        ("settings", "Settings", "sliders"),
        ("backup", "Backup & Restore", "database"),
    };

    /// <summary>Sidebar items that belong to the optional Expenses feature.</summary>
    private static readonly string[] ExpenseNav = { "vendors", "expenses", "profit" };

    private readonly List<Func<AppPage>> _history = new();
    private int _index = -1;
    private readonly Dictionary<string, Button> _navButtons = new();
    private readonly UiState _ui;
    private bool _collapsed;
    private Storyboard? _flicker;
    private TaskCompletionSource<bool>? _dialog;
    private Func<bool>? _dialogPrimary;

    public Store Store { get; }

    public AppPage? CurrentPage => PageHost.Content as AppPage;

    public MainWindow(Store store, string? startPage = null)
    {
        Store = store;
        InitializeComponent();
        _ui = UiState.Load(store.Paths.UiStatePath);
        _ui.ApplyTo(this);
        BuildNav();
        SetCollapsed(_ui.SidebarCollapsed, animate: false);
        RefreshChrome();
        ThemeManager.Changed += RefreshChrome;

        PreviewKeyDown += OnPreviewKeyDown;
        MouseDown += OnMouseNav;
        Closing += (_, _) => { _ui.CaptureFrom(this, _collapsed); _ui.Save(store.Paths.UiStatePath); };

        Navigate(startPage is null ? Routes.Dashboard(this) : Routes.ForNav(this, startPage));
        if (store.Password.IsSet) ShowLock();
        else ContentRendered += (_, _) => AfterStart();
    }

    // ------------------------------------------------------------------ navigation

    /// <summary>Opens a page, adding it to the history. <paramref name="notice"/> is shown on the new page.</summary>
    public async void Navigate(Func<AppPage> open, Notice? notice = null)
    {
        if (CurrentPage is { } current && !await current.CanLeaveAsync()) return;
        // Build the page before touching the history: a page can refuse to open (nothing to
        // bill, the record was deleted), and then we stay where we are and say why.
        AppPage page;
        try { page = open(); }
        catch (UserFacingException ex)
        {
            ShowError(ex.Message);
            return;
        }
        if (_index < _history.Count - 1) _history.RemoveRange(_index + 1, _history.Count - _index - 1);
        _history.Add(open);
        _index = _history.Count - 1;
        Show(page, notice);
    }

    /// <summary>Rebuilds the current page from the database (after an action that stays on the page).</summary>
    public void Reload(Notice? notice = null)
    {
        if (_index < 0) return;
        double scroll = ContentScroll.VerticalOffset;
        AppPage page;
        try { page = _history[_index](); }
        catch (UserFacingException ex)
        {
            // The page's record is gone (e.g. its last billable line was just billed). Go home.
            _history.Clear();
            _index = -1;
            Navigate(Routes.Dashboard(this), notice ?? Notice.Warning(ex.Message));
            return;
        }
        Show(page, notice, animate: false);
        ContentScroll.ScrollToVerticalOffset(scroll);
    }

    public async void Back()
    {
        if (_index <= 0) return;
        if (CurrentPage is { } current && !await current.CanLeaveAsync()) return;
        _index--;
        ShowFromHistory();
    }

    public async void Forward()
    {
        if (_index >= _history.Count - 1) return;
        if (CurrentPage is { } current && !await current.CanLeaveAsync()) return;
        _index++;
        ShowFromHistory();
    }

    private void ShowFromHistory()
    {
        try { Show(_history[_index]()); }
        catch (UserFacingException ex) { ShowError(ex.Message); }
    }

    private void Show(AppPage page, Notice? notice = null, bool animate = true)
    {
        Alerts.Children.Clear();
        PageHost.Content = page;
        PinnedBar.Content = page.PinnedBar;
        if (notice is not null) ShowNotice(notice);
        BuildBreadcrumb(page.Crumbs);
        foreach (var (key, button) in _navButtons) button.Uid = key == page.NavKey ? "active" : "";
        if (animate)
        {
            ContentScroll.ScrollToTop();
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0.6, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
            PageShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(4, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
        }
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, page.OnShown);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DialogLayer.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Escape) { CloseDialog(false); e.Handled = true; }
            return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Alt && e.SystemKey == Key.Left) { Back(); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Alt && e.SystemKey == Key.Right) { Forward(); e.Handled = true; }
        else if (e.Key == Key.F5) { Reload(); e.Handled = true; }
        else if (e.Key == Key.G && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) { Navigate(Routes.Gallery(this)); e.Handled = true; }
    }

    private void OnMouseNav(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.XButton1) Back();
        else if (e.ChangedButton == MouseButton.XButton2) Forward();
    }

    // ------------------------------------------------------------------ sidebar and top bar

    private void BuildNav()
    {
        NavPanel.Children.Clear();
        _navButtons.Clear();
        foreach (var (key, label, glyph) in MainNav) NavPanel.Children.Add(NavButton(key, label, glyph));

        NavPanel.Children.Add(new Border { Height = 1, Margin = new Thickness(20, 8, 20, 8) }.WithResource(Border.BackgroundProperty, "SidebarDivider"));
        var section = new TextBlock
        {
            Text = "ADMINISTRATION", FontSize = 10.4, FontWeight = FontWeights.Bold,
            Margin = new Thickness(20, 13.6, 20, 5.6), Name = "AdminLabel",
        }.WithResource(TextBlock.ForegroundProperty, "SidebarSection");
        NavPanel.Children.Add(section);
        foreach (var (key, label, glyph) in AdminNav) NavPanel.Children.Add(NavButton(key, label, glyph));
    }

    private Button NavButton(string key, string label, string glyph)
    {
        var b = new Button { Style = (Style)FindResource("NavLink"), Tag = glyph, Content = label, ToolTip = label };
        ToolTipService.SetIsEnabled(b, false);
        b.Click += (_, _) => Navigate(Routes.ForNav(this, key));
        _navButtons[key] = b;
        return b;
    }

    private void OnToggleSidebar(object sender, RoutedEventArgs e) => SetCollapsed(!_collapsed, animate: true);

    private void SetCollapsed(bool collapsed, bool animate)
    {
        _collapsed = collapsed;
        double width = collapsed ? 70 : 260;
        if (animate)
        {
            var anim = new DoubleAnimation(width, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            };
            Sidebar.BeginAnimation(WidthProperty, anim);
        }
        else
        {
            Sidebar.BeginAnimation(WidthProperty, null);
            Sidebar.Width = width;
        }
        BrandText.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        BrandRow.Margin = collapsed ? new Thickness(0) : new Thickness(20, 0, 20, 0);
        BrandRow.HorizontalAlignment = collapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        foreach (var child in NavPanel.Children.OfType<FrameworkElement>())
        {
            if (child is TextBlock t) t.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            if (child is Button b)
            {
                ToolTipService.SetIsEnabled(b, collapsed);
                b.Content = collapsed ? null : ((string)b.ToolTip);
            }
        }
        LockButton.Content = collapsed ? null : "Lock";
    }

    /// <summary>App name, logo, footer, avatar, font size, theme effects: everything read from Settings.</summary>
    public void RefreshChrome()
    {
        var s = Store.Settings;
        string appName = s.Get(SettingKeys.AppName, "ProfitDjinn");
        Title = Store.Paths.IsOverride ? $"{appName} [test data: {Store.Paths.DataFolder}]" : appName;
        BrandText.Text = ThemeManager.Heading(appName);
        FooterText.Text = $"{s.Get(SettingKeys.FooterText, "ProfitDjinn")}   v{AppInfo.Version}";

        string iconImage = s.Get(SettingKeys.AppIconImage);
        var custom = iconImage.Length > 0 && File.Exists(Store.Paths.AppIconPath) ? LoadImage(Store.Paths.AppIconPath) : null;
        var image = custom ?? (iconImage.Length > 0 ? new BitmapImage(new Uri("pack://application:,,,/Assets/genie.png")) : null);
        BrandImage.Source = image;
        BrandImage.Visibility = image is null ? Visibility.Collapsed : Visibility.Visible;
        BrandIcon.Visibility = image is null ? Visibility.Visible : Visibility.Collapsed;
        BrandIcon.Glyph = s.Get(SettingKeys.AppIcon, "lightning-charge-fill");

        // 2.5: the business name starts empty (Settings shows a hint), so fall back to the app name.
        string company = s.Get(SettingKeys.CompanyName).Trim() is { Length: > 0 } named ? named : s.Get(SettingKeys.AppName, "ProfitDjinn");
        UserName.Text = company;
        AvatarText.Text = Initials(company);

        double scale = Setting.AsNumber(s.Get(SettingKeys.UiFontScale, "1.0"));
        if (scale < 0.5 || scale > 2) scale = 1;
        FontScale.ScaleX = FontScale.ScaleY = scale;

        LockButton.Visibility = Store.Password.IsSet ? Visibility.Visible : Visibility.Collapsed;
        ShowUpdateBadge();
        var expenses = Store.Expenses.Enabled ? Visibility.Visible : Visibility.Collapsed;
        foreach (string key in ExpenseNav) if (_navButtons.TryGetValue(key, out var b)) b.Visibility = expenses;
        if (_navButtons.TryGetValue("workorders", out var wo)) wo.Visibility = Store.WorkOrders.Enabled ? Visibility.Visible : Visibility.Collapsed;
        if (_navButtons.TryGetValue("banking", out var bank)) bank.Visibility = Store.Banking.Enabled ? Visibility.Visible : Visibility.Collapsed;
        SidebarFooter.Visibility = LockButton.Visibility;

        DarkTitleBar.Apply(this, ThemeManager.Current != ThemeManager.Light);
        bool terminal = ThemeManager.IsTerminal;
        foreach (var nav in _navButtons.Values) nav.FontSize = terminal ? 16 : 14;   // custom.css: terminal nav links 1rem
        LockButton.FontSize = terminal ? 16 : 14;
        ScanlineLayer.Visibility = terminal ? Visibility.Visible : Visibility.Collapsed;
        if (terminal) StartFlicker(); else StopFlicker();
    }

    /// <summary>Like 1.x get_initials(): first letters of the first and last words, else the first two letters.</summary>
    public static string Initials(string name)
    {
        var parts = (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2) return (parts[0][..1] + parts[^1][..1]).ToUpperInvariant();
        return (name ?? "").Length >= 2 ? name![..2].ToUpperInvariant() : (name ?? "").ToUpperInvariant();
    }

    public static BitmapImage? LoadImage(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;   // do not keep the file open
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException)
        {
            return null;
        }
    }

    private void BuildBreadcrumb(IReadOnlyList<Crumb> crumbs)
    {
        Breadcrumb.Children.Clear();
        var all = new List<Crumb> { new("Home", crumbs.Count == 0 ? null : () => Navigate(Routes.Dashboard(this))) };
        all.AddRange(crumbs);
        for (int i = 0; i < all.Count; i++)
        {
            if (i > 0)
                Breadcrumb.Children.Add(new TextBlock { Text = "/", Margin = new Thickness(8, 0, 8, 0), FontSize = 15 }
                    .WithResource(TextBlock.ForegroundProperty, "TextMuted"));
            var crumb = all[i];
            var text = new TextBlock { Text = crumb.Text, FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
            if (crumb.Open is { } open && i < all.Count - 1)
            {
                text.Style = (Style)FindResource("CrumbLink");
                text.MouseLeftButtonUp += (_, _) => open();
            }
            else text.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
            Breadcrumb.Children.Add(text);
        }
    }

    private void OnUserMenu(object sender, RoutedEventArgs e) => SettingsDialog.Open(this);

    // ------------------------------------------------------------------ theme

    public void SetTheme(string theme)
    {
        Store.Settings.SetTheme(theme);
        ThemeManager.Apply(theme, Store.Settings.Get(SettingKeys.PrimaryColor));
        Reload();
    }

    private void StartFlicker()
    {
        if (_flicker is not null) return;
        // custom.css: opacity 1 until 98%, then .97 / .93 / .98, back to 1, over 8 s.
        var anim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(8), RepeatBehavior = RepeatBehavior.Forever };
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromPercent(0)));
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromPercent(0.98)));
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.97, KeyTime.FromPercent(0.985)));
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.93, KeyTime.FromPercent(0.99)));
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.98, KeyTime.FromPercent(0.995)));
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromPercent(1)));
        Storyboard.SetTarget(anim, Root);
        Storyboard.SetTargetProperty(anim, new PropertyPath(OpacityProperty));
        _flicker = new Storyboard();
        _flicker.Children.Add(anim);
        _flicker.Begin(this, true);
    }

    private void StopFlicker()
    {
        _flicker?.Stop(this);
        _flicker = null;
        Root.Opacity = 1;
    }

    // ------------------------------------------------------------------ notices

    /// <summary>Shows a notice at the top of the page; it fades after 5 seconds, as 1.x's flash messages did.</summary>
    public void ShowNotice(Notice notice) => AddAlert(notice.Message, notice.Kind);

    public void ShowError(string message) => AddAlert(message, NoticeKind.Danger);

    private void AddAlert(string message, NoticeKind kind)
    {
        string k = kind.ToString();
        string glyph = kind switch
        {
            NoticeKind.Success => "check-circle-fill",
            NoticeKind.Danger => "exclamation-triangle-fill",
            NoticeKind.Warning => "exclamation-circle-fill",
            _ => "info-circle-fill",
        };
        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, $"Alert.{k}.Fg");
        var icon = new Controls.Icon { Glyph = glyph, Size = 16, Margin = new Thickness(0, 2, 10, 0), VerticalAlignment = VerticalAlignment.Top };
        icon.SetResourceReference(Controls.Icon.ForegroundProperty, $"Alert.{k}.Fg");
        var close = new Button { Style = (Style)FindResource("Btn.Bare"), Padding = new Thickness(4), FontSize = 12, VerticalAlignment = VerticalAlignment.Top };
        Btn.SetIcon(close, "x-lg");
        close.SetResourceReference(Button.ForegroundProperty, $"Alert.{k}.Fg");
        Btn.SetHoverForeground(close, (Brush)FindResource($"Alert.{k}.Fg"));
        Btn.SetHoverBackground(close, Brushes.Transparent);

        var row = new DockPanel();
        DockPanel.SetDock(close, Dock.Right);
        row.Children.Add(close);
        row.Children.Add(icon);
        row.Children.Add(text);
        var alert = new Border { Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 20), Child = row };
        alert.SetResourceReference(Border.BackgroundProperty, $"Alert.{k}.Bg");
        alert.SetResourceReference(Border.BorderBrushProperty, $"Alert.{k}.Border");
        alert.SetResourceReference(Border.BorderThicknessProperty, "AlertBorderThickness");
        alert.SetResourceReference(Border.CornerRadiusProperty, "Radius");
        close.Click += (_, _) => Alerts.Children.Remove(alert);
        Alerts.Children.Add(alert);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(kind == NoticeKind.Danger ? 10 : 5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150));
            fade.Completed += (_, _) => Alerts.Children.Remove(alert);
            alert.BeginAnimation(OpacityProperty, fade);
        };
        timer.Start();
    }

    // ------------------------------------------------------------------ dialogs

    /// <summary>
    /// Shows a modal dialog over the window. <paramref name="primary"/> runs when the main
    /// button is clicked and returns true to close (false keeps the dialog open, e.g. to show
    /// a field error). Resolves to true when closed by the main button.
    /// </summary>
    public Task<bool> OpenDialog(string title, string glyph, FrameworkElement body, string primaryText,
        Func<bool> primary, string primaryStyle = "Btn.Primary", string? primaryGlyph = null, double maxWidth = 480,
        string cancelText = "Cancel")
    {
        _dialog?.TrySetResult(false);
        _dialog = new TaskCompletionSource<bool>();
        _dialogPrimary = primary;
        DialogTitle.Text = title;
        DialogIcon.Glyph = glyph;
        DialogIcon.Visibility = string.IsNullOrEmpty(glyph) ? Visibility.Collapsed : Visibility.Visible;
        DialogBody.Content = body;
        DialogCard.MaxWidth = maxWidth;
        DialogCard.Width = maxWidth;

        DialogButtons.Children.Clear();
        var cancel = new Button { Style = (Style)FindResource("Btn.OutlineSecondary"), Content = cancelText, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => CloseDialog(false);
        var ok = new Button { Style = (Style)FindResource(primaryStyle), Content = primaryText, IsDefault = true };
        if (primaryGlyph is not null) Btn.SetIcon(ok, primaryGlyph);
        ok.Click += (_, _) => { if (_dialogPrimary?.Invoke() ?? true) CloseDialog(true); };
        if (cancelText.Length > 0) DialogButtons.Children.Add(cancel);
        DialogButtons.Children.Add(ok);

        DialogLayer.Visibility = Visibility.Visible;
        DialogLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            var first = FindFirstInput(body);
            if (first is not null) { first.Focus(); if (first is TextBox tb) tb.SelectAll(); }
            else ok.Focus();
        });
        return _dialog.Task;
    }

    /// <summary>The confirm() replacement: a small dialog with the question and two buttons.</summary>
    public Task<bool> Confirm(string message, string confirmText = "OK", bool danger = false, string title = "Please confirm")
    {
        var body = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 15 };
        body.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        return OpenDialog(title, danger ? "exclamation-triangle" : "question-circle", body, confirmText, () => true,
            danger ? "Btn.Danger" : "Btn.Primary", maxWidth: 440);
    }

    public void CloseDialog(bool result)
    {
        DialogLayer.Visibility = Visibility.Collapsed;
        DialogBody.Content = null;
        var tcs = _dialog;
        _dialog = null;
        tcs?.TrySetResult(result);
    }

    private void OnDialogCancel(object sender, RoutedEventArgs e) => CloseDialog(false);

    private void OnBackdropClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == DialogLayer) CloseDialog(false);
    }

    private void OnDialogClick(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private static Control? FindFirstInput(DependencyObject root)
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBox or ComboBox or PasswordBox && ((Control)child).IsEnabled && ((Control)child).IsVisible) return (Control)child;
            if (FindFirstInput(child) is { } found) return found;
        }
        return null;
    }

    // ------------------------------------------------------------------ lock

    private void OnLock(object sender, RoutedEventArgs e) => ShowLock();

    public void ShowLock()
    {
        CloseDialog(false);
        LockLayer.Content = new LockScreen(this);
        LockLayer.Visibility = Visibility.Visible;
    }

    public void Unlock()
    {
        LockLayer.Visibility = Visibility.Collapsed;
        LockLayer.Content = null;
        Reload();
        AfterStart();
    }

    /// <summary>Once the window is up (and unlocked): create due recurring invoices and expenses, then the backup reminder.</summary>
    private void AfterStart()
    {
        RunRecurringOnce();
        RemindBackupIfDue();
        CheckForUpdateOnce();
    }

    // ------------------------------------------------------------------ updates (2.3)

    private bool _updateChecked;

    /// <summary>Once per start, at most once a day, in the background: ask GitHub for a newer release.</summary>
    private async void CheckForUpdateOnce()
    {
        if (_updateChecked || !Store.Updates.DueToday) return;
        _updateChecked = true;
        try { await Store.Updates.CheckAsync(); }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException) { return; }   // tries again next start
        ShowUpdateBadge();
    }

    /// <summary>The footer badge: "Update available: v2.4.0" when a newer release is known.</summary>
    public void ShowUpdateBadge()
    {
        var update = Store.Updates.Enabled ? Store.Updates.Known : null;
        UpdateBadge.Visibility = update is null ? Visibility.Collapsed : Visibility.Visible;
        if (update is null) return;
        UpdateBadge.Content = Ui.Row(4, new Controls.Icon { Glyph = "arrow-up-circle-fill", Size = 12 }.WithResource(Controls.Icon.ForegroundProperty, "SuccessText"),
            Ui.Badge($"Update available: v{update.Version}", "success"));
        UpdateBadge.ToolTip = "Open the release page on GitHub to see what's new and download it";
        UpdateBadge.Tag = update.Url;
    }

    private void OnUpdateBadge(object sender, RoutedEventArgs e) => OpenLink((UpdateBadge.Tag as string) ?? UpdateService.ReleasesPage);

    /// <summary>Opens a web page in the default browser. Only https links are opened.</summary>
    public void OpenLink(string url)
    {
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Clipboard.SetText(url);
            ShowError($"No web browser could be opened, so the link was copied instead:\n{url}");
        }
    }

    private bool _recurringChecked;

    /// <summary>
    /// Once per start: create the recurring invoices (2.4) and recurring expenses (2.2) that have
    /// come due, and say so. The page is rebuilt once when it shows their figures, then both
    /// notices are shown (a rebuild would clear a notice shown before it).
    /// </summary>
    private void RunRecurringOnce()
    {
        if (_recurringChecked) return;
        _recurringChecked = true;
        var notices = new List<Notice>();
        try
        {
            if (RecurringInvoiceService.Summarize(Store.RecurringInvoices.GenerateDue()) is { } invoices) notices.Add(invoices);
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException or UserFacingException)
        {
            notices.Add(new Notice($"Recurring invoices could not be created: {ex.Message}", NoticeKind.Danger));
        }
        try
        {
            if (RecurringService.Summarize(Store.Recurring.GenerateDue()) is { } expenses) notices.Add(expenses);
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException or UserFacingException)
        {
            notices.Add(new Notice($"Recurring expenses could not be added: {ex.Message}", NoticeKind.Danger));
        }
        if (notices.Count == 0) return;
        if (CurrentPage is Pages.DashboardPage or Pages.ExpensesPage or Pages.InvoicesPage or Pages.CustomerDetailPage) Reload();
        foreach (var n in notices) ShowNotice(n);
    }

    // ------------------------------------------------------------------ backup reminder

    private bool _backupReminderChecked;

    /// <summary>
    /// Once per start (after unlocking, when there is an app password): if the reminder is due,
    /// ask to back up. Either answer restarts the countdown, so it waits the full interval again.
    /// </summary>
    private async void RemindBackupIfDue()
    {
        if (_backupReminderChecked) return;
        _backupReminderChecked = true;
        bool due;
        try { due = Store.BackupReminder.IsDue(); }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            ShowError($"The backup reminder could not be checked: {ex.Message}");
            return;
        }
        if (!due) return;

        int days = Store.BackupReminder.Days;
        var body = new TextBlock
        {
            Text = $"It has been {days} day{(days == 1 ? "" : "s")} or more since your last backup or reminder. " +
                   "Save a copy of your customers, invoices and payments somewhere other than this PC, " +
                   "such as a USB drive or cloud folder.\n\nYou can change or turn off this reminder in Settings.",
            TextWrapping = TextWrapping.Wrap, FontSize = 15,
        };
        body.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        bool backUp = await OpenDialog("Back up your data?", "database", body, "Back Up Now", () => true,
            primaryGlyph: "cloud-download", maxWidth: 460, cancelText: "Not Now");
        Store.BackupReminder.Restart();
        if (backUp) Pages.BackupPage.SaveBackupAs(this);
    }
}
