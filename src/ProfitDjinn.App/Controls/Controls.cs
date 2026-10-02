using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ProfitDjinn.App.Controls;

/// <summary>
/// A Bootstrap badge / status pill. Kind picks the colours from the theme
/// (Badge.{Kind}.Bg): success, success75, info, info75, warning, warningdark, danger,
/// secondary, secondary50, primary. Big makes it the large pill next to a page title.
/// </summary>
public sealed class Badge : Border
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(Badge), new PropertyMetadata("", (d, _) => ((Badge)d).Update()));

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(string), typeof(Badge), new PropertyMetadata("secondary", (d, _) => ((Badge)d).Update()));

    public static readonly DependencyProperty BigProperty = DependencyProperty.Register(
        nameof(Big), typeof(bool), typeof(Badge), new PropertyMetadata(false, (d, _) => ((Badge)d).Update()));

    private readonly TextBlock _text = new() { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public bool Big { get => (bool)GetValue(BigProperty); set => SetValue(BigProperty, value); }

    public Badge()
    {
        Child = _text;
        BorderThickness = new Thickness(1);
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Center;
        SetResourceReference(CornerRadiusProperty, "RadiusSmall");
        Update();
    }

    private void Update()
    {
        string kind = (Kind ?? "secondary").ToLowerInvariant();
        string bgKey = kind switch
        {
            "success" => "Badge.Success.Bg",
            "success75" => "Badge.Success75.Bg",
            "info" => "Badge.Info.Bg",
            "info75" => "Badge.Info75.Bg",
            "warning" or "warningdark" => "Badge.Warning.Bg",
            "danger" => "Badge.Danger.Bg",
            "secondary50" => "Badge.Secondary50.Bg",
            "primary" => "Badge.Primary.Bg",
            _ => "Badge.Secondary.Bg",
        };
        SetResourceReference(BackgroundProperty, bgKey);
        SetResourceReference(BorderBrushProperty, kind == "danger" ? "Badge.DangerBorder" : "Badge.Border");
        _text.SetResourceReference(TextBlock.ForegroundProperty,
            kind == "warningdark" ? "Badge.DarkFg" : kind == "danger" ? "Badge.DangerFg" : "Badge.Fg");
        _text.Text = Text;
        _text.FontSize = Big ? 16 : 11.2;
        Padding = Big ? new Thickness(16, 7, 16, 7) : new Thickness(7, 2.5, 7, 3);
        Visibility = string.IsNullOrEmpty(Text) ? Visibility.Collapsed : Visibility.Visible;
    }
}

/// <summary>A Bootstrap card: optional header (icon, title, something on the right) and a body.</summary>
public class Card : ContentControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(Card), new PropertyMetadata(null));
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(Card), new PropertyMetadata(null));
    public static readonly DependencyProperty GlyphBrushProperty = DependencyProperty.Register(nameof(GlyphBrush), typeof(Brush), typeof(Card), new PropertyMetadata(null));
    public static readonly DependencyProperty HeaderRightProperty = DependencyProperty.Register(nameof(HeaderRight), typeof(object), typeof(Card), new PropertyMetadata(null));
    /// <summary>Replaces the icon and title with custom content (e.g. a collapse toggle). Set Title too, so the header shows.</summary>
    public static readonly DependencyProperty HeaderLeftProperty = DependencyProperty.Register(nameof(HeaderLeft), typeof(object), typeof(Card), new PropertyMetadata(null));
    public static readonly DependencyProperty BodyPaddingProperty = DependencyProperty.Register(nameof(BodyPadding), typeof(Thickness), typeof(Card), new PropertyMetadata(new Thickness(16)));


    public string? Title { get => (string?)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Glyph { get => (string?)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public Brush? GlyphBrush { get => (Brush?)GetValue(GlyphBrushProperty); set => SetValue(GlyphBrushProperty, value); }
    public object? HeaderRight { get => GetValue(HeaderRightProperty); set => SetValue(HeaderRightProperty, value); }
    public object? HeaderLeft { get => GetValue(HeaderLeftProperty); set => SetValue(HeaderLeftProperty, value); }
    public Thickness BodyPadding { get => (Thickness)GetValue(BodyPaddingProperty); set => SetValue(BodyPaddingProperty, value); }
}

/// <summary>
/// A dashboard stat tile: big value, small upper-case label, coloured icon box.
/// Tone: primary, info, warning, danger, success. Accent: a coloured border
/// ("warning", "danger") or a success bottom bar ("bottom").
/// </summary>
public sealed class StatTile : Button
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(string), typeof(StatTile), new PropertyMetadata(""));
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(StatTile), new PropertyMetadata(""));
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(StatTile), new PropertyMetadata(""));
    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(nameof(Tone), typeof(string), typeof(StatTile), new PropertyMetadata("primary"));
    public static readonly DependencyProperty MutedProperty = DependencyProperty.Register(nameof(Muted), typeof(bool), typeof(StatTile), new PropertyMetadata(false));
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(nameof(Accent), typeof(string), typeof(StatTile), new PropertyMetadata(""));
    public static readonly DependencyProperty BadgeProperty = DependencyProperty.Register(nameof(Badge), typeof(object), typeof(StatTile), new PropertyMetadata(null));
    public static readonly DependencyProperty ValueSizeProperty = DependencyProperty.Register(nameof(ValueSize), typeof(double), typeof(StatTile), new PropertyMetadata(32.0));


    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public string Tone { get => (string)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
    public bool Muted { get => (bool)GetValue(MutedProperty); set => SetValue(MutedProperty, value); }
    public string Accent { get => (string)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public object? Badge { get => GetValue(BadgeProperty); set => SetValue(BadgeProperty, value); }
    public double ValueSize { get => (double)GetValue(ValueSizeProperty); set => SetValue(ValueSizeProperty, value); }
}

/// <summary>A form field: label (with a red * when required), the input, an error line and a hint.</summary>
public sealed class Field : ContentControl
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(Field), new PropertyMetadata(""));
    public static readonly DependencyProperty RequiredProperty = DependencyProperty.Register(nameof(Required), typeof(bool), typeof(Field), new PropertyMetadata(false));
    public static readonly DependencyProperty ErrorProperty = DependencyProperty.Register(nameof(Error), typeof(string), typeof(Field), new PropertyMetadata(null));
    public static readonly DependencyProperty HintProperty = DependencyProperty.Register(nameof(Hint), typeof(string), typeof(Field), new PropertyMetadata(null));


    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public bool Required { get => (bool)GetValue(RequiredProperty); set => SetValue(RequiredProperty, value); }
    public string? Error { get => (string?)GetValue(ErrorProperty); set => SetValue(ErrorProperty, value); }
    public string? Hint { get => (string?)GetValue(HintProperty); set => SetValue(HintProperty, value); }
}

/// <summary>Button extras used by the button styles: a leading icon and hover colours.</summary>
public static class Btn
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached("Icon", typeof(string), typeof(Btn), new PropertyMetadata(null));
    public static readonly DependencyProperty HoverBackgroundProperty = DependencyProperty.RegisterAttached("HoverBackground", typeof(Brush), typeof(Btn), new PropertyMetadata(null));
    public static readonly DependencyProperty HoverForegroundProperty = DependencyProperty.RegisterAttached("HoverForeground", typeof(Brush), typeof(Btn), new PropertyMetadata(null));
    public static readonly DependencyProperty HoverBorderProperty = DependencyProperty.RegisterAttached("HoverBorder", typeof(Brush), typeof(Btn), new PropertyMetadata(null));
    public static readonly DependencyProperty SmallProperty = DependencyProperty.RegisterAttached("Small", typeof(bool), typeof(Btn), new PropertyMetadata(false));

    public static string? GetIcon(DependencyObject o) => (string?)o.GetValue(IconProperty);
    public static void SetIcon(DependencyObject o, string? v) => o.SetValue(IconProperty, v);
    public static Brush? GetHoverBackground(DependencyObject o) => (Brush?)o.GetValue(HoverBackgroundProperty);
    public static void SetHoverBackground(DependencyObject o, Brush? v) => o.SetValue(HoverBackgroundProperty, v);
    public static Brush? GetHoverForeground(DependencyObject o) => (Brush?)o.GetValue(HoverForegroundProperty);
    public static void SetHoverForeground(DependencyObject o, Brush? v) => o.SetValue(HoverForegroundProperty, v);
    public static Brush? GetHoverBorder(DependencyObject o) => (Brush?)o.GetValue(HoverBorderProperty);
    public static void SetHoverBorder(DependencyObject o, Brush? v) => o.SetValue(HoverBorderProperty, v);
    public static bool GetSmall(DependencyObject o) => (bool)o.GetValue(SmallProperty);
    public static void SetSmall(DependencyObject o, bool v) => o.SetValue(SmallProperty, v);
}

/// <summary>Input extras: placeholder text and an invalid state (red border).</summary>
public static class Input
{
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached("Placeholder", typeof(string), typeof(Input), new PropertyMetadata(null));
    public static readonly DependencyProperty InvalidProperty = DependencyProperty.RegisterAttached("Invalid", typeof(bool), typeof(Input), new PropertyMetadata(false));
    public static readonly DependencyProperty PrefixProperty = DependencyProperty.RegisterAttached("Prefix", typeof(string), typeof(Input), new PropertyMetadata(null));

    public static string? GetPlaceholder(DependencyObject o) => (string?)o.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject o, string? v) => o.SetValue(PlaceholderProperty, v);
    public static bool GetInvalid(DependencyObject o) => (bool)o.GetValue(InvalidProperty);
    public static void SetInvalid(DependencyObject o, bool v) => o.SetValue(InvalidProperty, v);
    public static string? GetPrefix(DependencyObject o) => (string?)o.GetValue(PrefixProperty);
    public static void SetPrefix(DependencyObject o, string? v) => o.SetValue(PrefixProperty, v);

    /// <summary>An icon in the prefix addon (e.g. the search box's magnifier). The addon shows when Prefix is set, so this also sets Prefix to "".</summary>
    public static readonly DependencyProperty PrefixGlyphProperty = DependencyProperty.RegisterAttached("PrefixGlyph", typeof(string), typeof(Input),
        new PropertyMetadata(null, (d, e) => { if (e.NewValue is string g && g.Length > 0 && GetPrefix(d) is null or "") SetPrefix(d, "\u200B"); }));
    public static string? GetPrefixGlyph(DependencyObject o) => (string?)o.GetValue(PrefixGlyphProperty);
    public static void SetPrefixGlyph(DependencyObject o, string? v) => o.SetValue(PrefixGlyphProperty, v);
}

/// <summary>A text link (an invoice number, a customer name) that runs Click.</summary>
public sealed class Link : TextBlock
{
    public event RoutedEventHandler? Click;

    public Link()
    {
        Cursor = Cursors.Hand;
        SetResourceReference(ForegroundProperty, "Link");
        MouseEnter += (_, _) => { TextDecorations = System.Windows.TextDecorations.Underline; SetResourceReference(ForegroundProperty, "LinkHover"); };
        MouseLeave += (_, _) => { TextDecorations = null; SetResourceReference(ForegroundProperty, "Link"); };
        MouseLeftButtonUp += (_, e) => { Click?.Invoke(this, new RoutedEventArgs()); e.Handled = true; };
    }
}
