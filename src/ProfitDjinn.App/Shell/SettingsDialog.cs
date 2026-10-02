using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ProfitDjinn.App.Controls;
using ProfitDjinn.App.Infrastructure;
using ProfitDjinn.Core;

namespace ProfitDjinn.App.Shell;

/// <summary>
/// The dialog behind the top-right button (1.x's user settings dialog): the three theme
/// tiles, and changing the app password when one is set.
/// </summary>
public static class SettingsDialog
{
    public static void Open(MainWindow shell)
    {
        var body = new StackPanel();

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var avatar = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(24), BorderThickness = new Thickness(2), Margin = new Thickness(0, 0, 12, 0) }
            .WithResource(Border.BackgroundProperty, "AvatarBg").WithResource(Border.BorderBrushProperty, "AvatarRing");
        string company = shell.Store.Settings.Get(Core.Data.SettingKeys.CompanyName);
        avatar.Child = new TextBlock { Text = MainWindow.Initials(company), FontSize = 17.6, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            .WithResource(TextBlock.ForegroundProperty, "AvatarFg");
        head.Children.Add(avatar);
        var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        who.Children.Add(new TextBlock { Text = company, FontWeight = FontWeights.Bold, FontSize = 15 }.WithResource(TextBlock.ForegroundProperty, "Text"));
        who.Children.Add(new TextBlock { Text = shell.Store.Settings.Get(Core.Data.SettingKeys.CompanyEmail), FontSize = 12.5 }.WithResource(TextBlock.ForegroundProperty, "TextMuted"));
        head.Children.Add(who);
        body.Children.Add(head);

        body.Children.Add(new TextBlock { Text = "APPEARANCE", Style = (Style)shell.FindResource("SectionLabel") });
        var tiles = new UniformGrid { Columns = 3 };
        foreach (string theme in new[] { ThemeManager.Light, ThemeManager.Dark, ThemeManager.Terminal })
            tiles.Children.Add(ThemeTile(shell, theme));
        body.Children.Add(tiles);

        if (shell.Store.Password.IsSet)
        {
            body.Children.Add(new Border { Style = (Style)shell.FindResource("Rule") });
            body.Children.Add(new TextBlock { Text = "CHANGE PASSWORD", Style = (Style)shell.FindResource("SectionLabel") });
            var current = new PasswordBox { Margin = new Thickness(0, 0, 0, 8) };
            var fresh = new PasswordBox { Margin = new Thickness(0, 0, 0, 8) };
            var confirm = new PasswordBox { Margin = new Thickness(0, 0, 0, 8) };
            var message = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8), FontSize = 13 };
            body.Children.Add(new Field { Label = "Current password", Content = current, Margin = new Thickness(0, 0, 0, 4) });
            body.Children.Add(new Field { Label = "New password (8+ characters)", Content = fresh, Margin = new Thickness(0, 0, 0, 4) });
            body.Children.Add(new Field { Label = "Confirm new password", Content = confirm, Margin = new Thickness(0, 0, 0, 4) });
            body.Children.Add(message);
            var update = new Button { Style = (Style)shell.FindResource("Btn.Primary"), Content = "Update Password", HorizontalAlignment = HorizontalAlignment.Stretch };
            Btn.SetSmall(update, true);
            update.Click += (_, _) =>
            {
                try
                {
                    shell.Store.Password.Set(current.Password, fresh.Password, confirm.Password);
                    message.Text = "Password updated.";
                    message.SetResourceReference(TextBlock.ForegroundProperty, "SuccessText");
                    current.Clear(); fresh.Clear(); confirm.Clear();
                }
                catch (UserFacingException ex)
                {
                    message.Text = ex.Message;
                    message.SetResourceReference(TextBlock.ForegroundProperty, "DangerText");
                }
                message.Visibility = Visibility.Visible;
            };
            body.Children.Add(update);
        }

        _ = shell.OpenDialog(ThemeManager.Heading("Settings"), "person-gear", body, "Close", () => true, "Btn.Secondary", maxWidth: 460, cancelText: "");
    }

    private static FrameworkElement ThemeTile(MainWindow shell, string theme)
    {
        (string bg, string border, string strip, string stripBorder) = theme switch
        {
            ThemeManager.Dark => ("#0D1117", "#30363D", "#0A0E14", "#30363D"),
            ThemeManager.Terminal => ("#0D0208", "#5900FF41", "#040804", "#00FF41"),
            _ => ("#F8FAFC", "#E2E8F0", "#0F172A", "#0F172A"),
        };
        var conv = new BrushConverter();
        Brush B(string hex) => (Brush)conv.ConvertFromString(hex)!;

        var preview = new Grid { Height = 60, Margin = new Thickness(0, 0, 0, 8) };
        preview.Children.Add(new Border { Background = B(bg), BorderBrush = B(border), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6) });
        preview.Children.Add(new Border
        {
            Background = B(strip), BorderBrush = B(stripBorder), BorderThickness = new Thickness(0, 0, 1, 0),
            CornerRadius = new CornerRadius(6, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Left, Width = 32, Margin = new Thickness(1),
        });
        if (theme == ThemeManager.Terminal)
            preview.Children.Add(new TextBlock
            {
                Text = "> _", Foreground = B("#00FF41"), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 8, 6),
                FontFamily = new FontFamily(new Uri("pack://application:,,,/"), "./Assets/Fonts/#VT323"),
            });

        var label = new TextBlock { Text = char.ToUpperInvariant(theme[0]) + theme[1..], FontSize = 12.8, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center }
            .WithResource(TextBlock.ForegroundProperty, "Text");
        var stack = new StackPanel();
        stack.Children.Add(preview);
        stack.Children.Add(label);

        bool active = ThemeManager.Current == theme;
        var tile = new Border { BorderThickness = new Thickness(2), Padding = new Thickness(12), Margin = new Thickness(4), CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand, Child = stack, Background = Brushes.Transparent };
        tile.SetResourceReference(Border.BorderBrushProperty, active ? "BrandPrimary" : "Border");
        tile.MouseLeftButtonUp += (_, _) =>
        {
            shell.CloseDialog(false);
            shell.SetTheme(theme);
            Open(shell);
        };
        return tile;
    }
}
