using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using ProfitDjinn.App.Controls;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.Core.Data;

namespace ProfitDjinn.App.Shell;

/// <summary>
/// Shown at start (and from the sidebar's Lock) when an app password is set. Same layout
/// as 1.x's sign-in page: logo panel and form side by side ("left") or stacked ("top"),
/// chosen by the login_logo_layout setting. Terminal shows its RobCo banner.
/// </summary>
public sealed class LockScreen : Grid
{
    private readonly MainWindow _shell;
    private readonly PasswordBox _password = new();
    private readonly TextBlock _error = new() { Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };

    public LockScreen(MainWindow shell)
    {
        _shell = shell;
        this.WithResource(BackgroundProperty, "PageBg");
        var s = shell.Store.Settings;
        string appName = s.Get(SettingKeys.AppName, "ProfitDjinn");
        bool left = s.Get(SettingKeys.LoginLogoLayout, "left") != "top";

        var form = new StackPanel();
        form.Children.Add(Text("Unlock", 21.6, FontWeights.Bold, "Heading"));
        form.Children.Add(Text("Enter the app password to continue", 13.6, FontWeights.Normal, "TextMuted", new Thickness(0, 4, 0, 24)));
        _error.SetResourceReference(TextBlock.ForegroundProperty, "DangerText");
        form.Children.Add(_error);
        form.Children.Add(new Field { Label = "Password", Content = _password });
        _password.KeyDown += (_, e) => { if (e.Key == Key.Enter) TryUnlock(); };
        var unlock = new Button { Style = (Style)Application.Current.FindResource("Btn.Primary"), Content = "Unlock", Padding = new Thickness(12, 8, 12, 8) };
        Btn.SetIcon(unlock, "box-arrow-in-right");
        unlock.HorizontalAlignment = HorizontalAlignment.Stretch;
        unlock.Click += (_, _) => TryUnlock();
        form.Children.Add(unlock);

        var brand = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var logo = Logo(shell, left ? 180 : 80);
        brand.Children.Add(logo);
        var title = Text(ThemeManager.Heading(appName), left ? 24 : 25.6, FontWeights.Bold, "Heading", new Thickness(0, 8, 0, 4));
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.SetResourceReference(EffectProperty, "HeadingGlow");
        brand.Children.Add(title);
        var tagline = Text(s.Get(SettingKeys.AppTagline), 13.1, FontWeights.Normal, "TextMuted");
        tagline.HorizontalAlignment = HorizontalAlignment.Center;
        tagline.TextAlignment = TextAlignment.Center;
        tagline.TextWrapping = TextWrapping.Wrap;
        brand.Children.Add(tagline);

        FrameworkElement card;
        if (left)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var brandPanel = new Border { Padding = new Thickness(28, 40, 28, 40), Child = brand };
            var formPanel = new Border { Padding = new Thickness(32, 40, 32, 40), BorderThickness = new Thickness(1, 0, 0, 0), Child = form };
            formPanel.SetResourceReference(Border.BorderBrushProperty, "Border");
            Grid.SetColumn(formPanel, 1);
            grid.Children.Add(brandPanel);
            grid.Children.Add(formPanel);
            card = Card(grid, 780);
        }
        else
        {
            var stack = new StackPanel();
            stack.Children.Add(brand);
            stack.Children.Add(new Border { Height = 28 });
            stack.Children.Add(form);
            card = Card(new Border { Padding = new Thickness(32, 40, 32, 40), Child = stack }, 420);
        }

        var column = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(16) };
        if (ThemeManager.IsTerminal)
        {
            var banner = Text("ROBCO INDUSTRIES (TM) TERMLINK PROTOCOL", 16, FontWeights.Normal, "Heading", new Thickness(0, 0, 0, 8));
            banner.HorizontalAlignment = HorizontalAlignment.Center;
            banner.SetResourceReference(EffectProperty, "HeadingGlow");
            column.Children.Add(banner);
        }
        column.Children.Add(card);
        if (ThemeManager.IsTerminal)
        {
            var prompt = Text("> ENTER PASSWORD", 14.4, FontWeights.Normal, "TextMuted", new Thickness(0, 24, 0, 0));
            prompt.HorizontalAlignment = HorizontalAlignment.Center;
            var blink = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(1), RepeatBehavior = RepeatBehavior.Forever };
            blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromPercent(0)));
            blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromPercent(0.5)));
            prompt.BeginAnimation(OpacityProperty, blink);
            column.Children.Add(prompt);
        }
        Children.Add(column);
        Loaded += (_, _) => _password.Focus();
    }

    private void TryUnlock()
    {
        if (_shell.Store.Password.Verify(_password.Password))
        {
            _shell.Unlock();
            return;
        }
        _error.Text = "That password is not right.";
        _error.Visibility = Visibility.Visible;
        _password.Clear();
        _password.Focus();
    }

    private static FrameworkElement Card(UIElement content, double maxWidth)
    {
        var shadow = new Border();
        shadow.SetResourceReference(Border.BackgroundProperty, "Surface");
        shadow.SetResourceReference(Border.CornerRadiusProperty, "RadiusDialog");
        shadow.SetResourceReference(EffectProperty, "ShadowLg");
        var face = new Border { BorderThickness = new Thickness(1), Child = content, ClipToBounds = true };
        face.SetResourceReference(Border.BackgroundProperty, "Surface");
        face.SetResourceReference(Border.BorderBrushProperty, "Border");
        face.SetResourceReference(Border.CornerRadiusProperty, "RadiusDialog");
        var g = new Grid { MaxWidth = maxWidth, Width = maxWidth };
        g.Children.Add(shadow);
        g.Children.Add(face);
        return g;
    }

    internal static FrameworkElement Logo(MainWindow shell, double maxHeight)
    {
        var s = shell.Store.Settings;
        bool show = s.Get(SettingKeys.LoginLogo).Length > 0;
        BitmapImage? image = null;
        if (show) image = File.Exists(shell.Store.Paths.LoginLogoPath) ? MainWindow.LoadImage(shell.Store.Paths.LoginLogoPath) : null;
        if (show && image is null) image = new BitmapImage(new Uri("pack://application:,,,/Assets/genie.png"));
        if (image is not null)
            return new Image { Source = image, MaxHeight = maxHeight, MaxWidth = maxHeight * 1.5, HorizontalAlignment = HorizontalAlignment.Center };
        return new Icon { Glyph = s.Get(SettingKeys.AppIcon, "lightning-charge-fill"), Size = 56, HorizontalAlignment = HorizontalAlignment.Center }
            .WithResource(Icon.ForegroundProperty, "BrandPrimary");
    }

    private static TextBlock Text(string text, double size, FontWeight weight, string brush, Thickness? margin = null)
    {
        var t = new TextBlock { Text = text, FontSize = size, FontWeight = weight, Margin = margin ?? new Thickness(0) };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }
}
