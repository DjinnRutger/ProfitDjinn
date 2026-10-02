using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace ProfitDjinn.App.Infrastructure;

/// <summary>
/// Swaps the theme dictionary (Light, Dark, Terminal) and sets the brushes that come from
/// the primary_color setting. The colour mixes are 1.x's CSS color-mix() calls, done per
/// channel:
///   BrandHover = mix(P 82%, #000)          SidebarBg  = mix(P 22%, #04060f)
///   NavActiveBg = P at 28% alpha           FocusRing  = P at 18% alpha
/// </summary>
public static class ThemeManager
{
    public const string Light = "light", Dark = "dark", Terminal = "terminal";

    private static ResourceDictionary? _current;

    public static string Current { get; private set; } = Light;

    public static bool IsTerminal => Current == Terminal;

    public static Color Brand { get; private set; } = Color.FromRgb(0x25, 0x63, 0xEB);

    /// <summary>Raised after the theme or brand colour changes, so open pages can refresh text casing.</summary>
    public static event Action? Changed;

    public static void Apply(string theme, string? primaryColor)
    {
        theme = theme is Dark or Terminal ? theme : Light;
        var app = Application.Current.Resources;
        var dict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/Theme.{char.ToUpperInvariant(theme[0])}{theme[1..]}.xaml"),
        };
        if (_current is not null) app.MergedDictionaries.Remove(_current);
        // Theme first, so Controls.xaml (merged in App.xaml) resolves its DynamicResources against it.
        app.MergedDictionaries.Insert(0, dict);
        _current = dict;
        Current = theme;

        Brand = ParseColor(primaryColor) ?? Color.FromRgb(0x25, 0x63, 0xEB);
        SetBrush("BrandPrimary", Brand);
        SetBrush("BrandHover", Mix(Brand, Colors.Black, 0.82));
        SetBrush("BrandTint8", WithAlpha(Brand, 0.08));
        SetBrush("FocusRing", WithAlpha(Brand, 0.18));
        SetBrush("BrandRing20", WithAlpha(Brand, 0.20));
        if (theme == Terminal)
        {
            SetBrush("SidebarBg", Color.FromRgb(0x04, 0x08, 0x04));
            SetBrush("NavActiveBg", Color.FromArgb(0x1A, 0x00, 0xFF, 0x41));
            SetBrush("NavActiveBar", Color.FromRgb(0x00, 0xFF, 0x41));
            SetBrush("AvatarBg", Colors.Transparent);
            SetBrush("AvatarFg", Color.FromRgb(0x00, 0xFF, 0x41));
            SetBrush("AvatarRing", Color.FromRgb(0x00, 0xFF, 0x41));
            SetBrush("PrimaryButtonBg", Colors.Transparent);
            SetBrush("PrimaryButtonHoverBg", Color.FromArgb(0x26, 0x00, 0xFF, 0x41));
            SetBrush("PrimaryButtonFg", Color.FromRgb(0x00, 0xFF, 0x41));
            SetBrush("PrimaryButtonBorder", Color.FromRgb(0x00, 0xFF, 0x41));
            SetBrush("FocusBorder", Color.FromRgb(0x39, 0xFF, 0x14));
            SetBrush("FocusRing", Color.FromArgb(0x26, 0x00, 0xFF, 0x41));
            SetBrush("BrandPrimary", Color.FromRgb(0x00, 0xFF, 0x41));
        }
        else
        {
            SetBrush("SidebarBg", Mix(Brand, Color.FromRgb(0x04, 0x06, 0x0F), 0.22));
            SetBrush("NavActiveBg", WithAlpha(Brand, 0.28));
            SetBrush("NavActiveBar", Brand);
            SetBrush("AvatarBg", Brand);
            SetBrush("AvatarFg", Colors.White);
            SetBrush("AvatarRing", Colors.Transparent);
            SetBrush("PrimaryButtonBg", Brand);
            SetBrush("PrimaryButtonHoverBg", Mix(Brand, Colors.Black, 0.82));
            SetBrush("PrimaryButtonFg", Colors.White);
            SetBrush("PrimaryButtonBorder", Brand);
            SetBrush("FocusBorder", Brand);
        }
        Changed?.Invoke();
    }

    private static void SetBrush(string key, Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        Application.Current.Resources[key] = b;
    }

    public static Color? ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        string h = hex.Trim().TrimStart('#');
        if (h.Length == 3) h = string.Concat(h.Select(c => $"{c}{c}"));
        if (h.Length != 6 || !int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v)) return null;
        return Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    /// <summary>CSS color-mix(in srgb, a w, b): w of a, the rest of b.</summary>
    public static Color Mix(Color a, Color b, double w) => Color.FromRgb(
        (byte)Math.Round(a.R * w + b.R * (1 - w)),
        (byte)Math.Round(a.G * w + b.G * (1 - w)),
        (byte)Math.Round(a.B * w + b.B * (1 - w)));

    public static Color WithAlpha(Color c, double alpha) => Color.FromArgb((byte)Math.Round(alpha * 255), c.R, c.G, c.B);

    /// <summary>Terminal headings are upper case, as custom.css made them.</summary>
    public static string Heading(string text) => IsTerminal ? text.ToUpperInvariant() : text;
}
