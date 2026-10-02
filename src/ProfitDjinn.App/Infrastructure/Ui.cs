using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using ProfitDjinn.App.Controls;
using ProfitDjinn.Core.Rules;

namespace ProfitDjinn.App.Infrastructure;

/// <summary>
/// Builders for the pieces every page uses, so pages read as layout, not plumbing. Each one
/// matches a Bootstrap/custom.css component from 1.x (see docs/design-spec.md).
/// </summary>
public static class Ui
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static Style Style(string key) => (Style)Application.Current.FindResource(key);

    // ------------------------------------------------------------------ text

    public static TextBlock Text(string? text, string style = "Body", double? size = null, bool wrap = false)
    {
        var t = new TextBlock { Text = text ?? "", Style = Style(style) };
        if (size is { } s) t.FontSize = s;
        if (wrap) t.TextWrapping = TextWrapping.Wrap;
        return t;
    }

    public static TextBlock Muted(string? text, double size = 14) => Text(text, "Muted", size);

    public static TextBlock Bold(string? text, double size = 14) => Text(text, "Strong", size);

    /// <summary>A text link, e.g. an invoice number or a customer name in a table.</summary>
    public static Link Link(string text, Action open, bool mono = false, bool bold = true)
    {
        var l = new Link { Text = text, FontSize = 14, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
        if (mono) l.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        l.Click += (_, _) => open();
        return l;
    }

    // ------------------------------------------------------------------ formats (1.x templates)

    /// <summary>"$1234.50": 1.x's templates printed money with "%.2f", no thousands separator.</summary>
    public static string Money(double x) => "$" + PyMath.Round(x, 2).ToString("F2", Inv);

    /// <summary>"$1,234.50": the invoice and bill line builders (JavaScript fmt()) grouped thousands.</summary>
    public static string MoneyGrouped(double x) => PyMath.Dollars(x);

    /// <summary>"Jan 05, 2026", strftime('%b %d, %Y').</summary>
    public static string Date(DateOnly? d) => d is { } v ? v.ToString("MMM dd, yyyy", Inv) : "—";

    /// <summary>"January 05, 2026", strftime('%B %d, %Y').</summary>
    public static string LongDate(DateOnly? d) => d is { } v ? v.ToString("MMMM dd, yyyy", Inv) : "—";

    public static string Dash(string? s) => string.IsNullOrWhiteSpace(s) ? "—" : s;

    // ------------------------------------------------------------------ layout

    public static StackPanel Row(double gap, params UIElement[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] is FrameworkElement fe && i > 0) fe.Margin = new Thickness(gap, fe.Margin.Top, fe.Margin.Right, fe.Margin.Bottom);
            p.Children.Add(children[i]);
        }
        return p;
    }

    public static StackPanel Stack(double gap, params UIElement[] children)
    {
        var p = new StackPanel();
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] is FrameworkElement fe && i > 0) fe.Margin = new Thickness(fe.Margin.Left, gap, fe.Margin.Right, fe.Margin.Bottom);
            p.Children.Add(children[i]);
        }
        return p;
    }

    /// <summary>Columns with the given widths (Bootstrap's row/col), gap between them.</summary>
    public static Grid Columns(double gap, params (GridLength Width, UIElement Content)[] cols)
    {
        var g = new Grid();
        for (int i = 0; i < cols.Length; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = cols[i].Width });
            if (cols[i].Content is FrameworkElement fe)
            {
                fe.Margin = new Thickness(i == 0 ? 0 : gap / 2, fe.Margin.Top, i == cols.Length - 1 ? 0 : gap / 2, fe.Margin.Bottom);
                // Bootstrap columns line up at the top; a card keeps its own height.
                if (fe.VerticalAlignment == VerticalAlignment.Stretch) fe.VerticalAlignment = VerticalAlignment.Top;
            }
            Grid.SetColumn(cols[i].Content, i);
            g.Children.Add(cols[i].Content);
        }
        return g;
    }

    public static GridLength Star(double n = 1) => new(n, GridUnitType.Star);
    public static GridLength Px(double n) => new(n);
    public static readonly GridLength Auto = GridLength.Auto;

    /// <summary>Title (with an optional big status pill), lead line, and the action buttons on the right.</summary>
    public static FrameworkElement PageHeader(string title, string? lead = null, UIElement? pill = null, params UIElement[] actions) =>
        PageHeaderWithGlyph(null, title, lead, pill, actions);

    /// <summary>A page header whose title starts with a coloured icon, as Items and Work Orders had.</summary>
    public static FrameworkElement PageHeaderWithGlyph(string? glyph, string title, string? lead = null, UIElement? pill = null, params UIElement[] actions)
    {
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        if (glyph is not null)
            titleRow.Children.Add(new Icon { Glyph = glyph, Size = 21.6, Margin = new Thickness(0, 0, 8, 0) }.WithResource(Icon.ForegroundProperty, "BsPrimary"));
        titleRow.Children.Add(new TextBlock { Text = ThemeManager.Heading(title), Style = Style("H1"), VerticalAlignment = VerticalAlignment.Center });
        if (pill is FrameworkElement p) { p.Margin = new Thickness(12, 0, 0, 0); titleRow.Children.Add(p); }

        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(titleRow);
        if (!string.IsNullOrEmpty(lead)) left.Children.Add(new TextBlock { Text = lead, Style = Style("Lead"), Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });

        var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 24), LastChildFill = true };
        var right = Row(8, actions);
        right.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(right);
        dock.Children.Add(left);
        return dock;
    }

    /// <summary>Recolours a page header's title icon (Revenue's is green, not blue).</summary>
    public static T WithGlyphBrush<T>(this T header, string brushKey) where T : FrameworkElement
    {
        if (FindFirst<Icon>(header) is { } icon) icon.SetResourceReference(Icon.ForegroundProperty, brushKey);
        return header;
    }

    private static TChild? FindFirst<TChild>(DependencyObject root) where TChild : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is TChild found) return found;
            if (FindFirst<TChild>(child) is { } deeper) return deeper;
        }
        return null;
    }

    public static Card Card(object content, string? title = null, string? glyph = null, string? glyphBrush = null,
        object? headerRight = null, Thickness? bodyPadding = null)
    {
        var c = new Card { Title = title, Glyph = glyph, Content = content, HeaderRight = headerRight };
        if (glyphBrush is not null) c.SetResourceReference(Controls.Card.GlyphBrushProperty, glyphBrush);
        if (bodyPadding is { } bp) c.BodyPadding = bp;
        return c;
    }

    // ------------------------------------------------------------------ buttons and badges

    public static Button Button(string? text, string style, string? glyph, Action onClick, bool small = false, string? tooltip = null)
    {
        var b = new Button { Content = text, Style = Style(style) };
        if (glyph is not null) Btn.SetIcon(b, glyph);
        if (small) Btn.SetSmall(b, true);
        if (tooltip is not null) b.ToolTip = tooltip;
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>The icon-only small outline button used in table action columns.</summary>
    public static Button IconButton(string glyph, string style, string tooltip, Action onClick) =>
        Button(null, style, glyph, onClick, small: true, tooltip: tooltip).Also(b => b.Padding = new Thickness(7, 4.8, 7, 4.8));

    public static Badge Badge(string text, string kind, bool big = false) => new() { Text = text, Kind = kind, Big = big };

    public static T Also<T>(this T element, Action<T> setup)
    {
        setup(element);
        return element;
    }

    public static T Margin<T>(this T element, double left, double top, double right, double bottom) where T : FrameworkElement
    {
        element.Margin = new Thickness(left, top, right, bottom);
        return element;
    }

    // ------------------------------------------------------------------ empty state

    public static FrameworkElement Empty(string glyph, string text, string? linkText = null, Action? link = null)
    {
        var p = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(16, 48, 16, 48) };
        p.Children.Add(new Icon { Glyph = glyph, Size = 40, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) }
            .WithResource(Icon.ForegroundProperty, "TextMuted"));
        var line = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        line.Children.Add(Muted(text, 15));
        if (linkText is not null && link is not null) line.Children.Add(Link(" " + linkText, link, bold: false).Also(l => l.FontSize = 15));
        p.Children.Add(line);
        return p;
    }

    // ------------------------------------------------------------------ inputs

    public static TextBox TextBox(string? value = null, string? placeholder = null, double? width = null)
    {
        var t = new TextBox { Text = value ?? "" };
        if (placeholder is not null)
        {
            Input.SetPlaceholder(t, placeholder);
            // A box with no label is announced (and found by the smoke tests) by its placeholder;
            // a Field around it replaces this with the field's label.
            System.Windows.Automation.AutomationProperties.SetName(t, placeholder);
        }
        if (width is { } w) t.Width = w;
        return t;
    }

    public static TextBox TextArea(string? value = null, double minHeight = 80, string? placeholder = null)
    {
        var t = new TextBox { Text = value ?? "", Style = Style("Input.TextArea"), MinHeight = minHeight };
        if (placeholder is not null) Input.SetPlaceholder(t, placeholder);
        return t;
    }

    public static Field Field(string label, UIElement input, bool required = false, string? hint = null) =>
        new() { Label = label, Content = input, Required = required, Hint = hint };

    /// <summary>A text box for a date, "yyyy-MM-dd", as 1.x's &lt;input type=date&gt; posted it.</summary>
    public static DateBox DateBox(DateOnly? value) => new() { Date = value };
}

/// <summary>
/// A date field: a text box (yyyy-MM-dd or any date Windows understands) with a calendar
/// button. Date is null when it is empty or does not parse.
/// </summary>
public sealed class DateBox : Grid
{
    private readonly TextBox _text = new();
    private readonly System.Windows.Controls.Primitives.Popup _popup = new() { StaysOpen = false, AllowsTransparency = true };
    private readonly System.Windows.Controls.Calendar _calendar = new();

    public event Action? Changed;

    public DateBox()
    {
        Input.SetPlaceholder(_text, "yyyy-mm-dd");
        _text.TextChanged += (_, _) => Changed?.Invoke();
        Children.Add(_text);
        var pick = new Button { Style = Ui.Style("Btn.Bare"), Padding = new Thickness(8, 4, 8, 4), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 4, 0), Focusable = false };
        Btn.SetIcon(pick, "calendar3");
        pick.Click += (_, _) =>
        {
            _calendar.SelectedDate = Date?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today;
            _calendar.DisplayDate = _calendar.SelectedDate.Value;
            _popup.IsOpen = true;
        };
        Children.Add(pick);
        _popup.PlacementTarget = _text;
        _popup.Child = new Border { Child = _calendar, Effect = (Effect?)Application.Current.TryFindResource("ShadowMd") };
        _calendar.SelectedDatesChanged += (_, _) =>
        {
            if (_calendar.SelectedDate is { } d) { Date = DateOnly.FromDateTime(d); _popup.IsOpen = false; }
        };
        Children.Add(_popup);
    }

    public TextBox TextBox => _text;

    public DateOnly? Date
    {
        get
        {
            string s = _text.Text.Trim();
            if (s.Length == 0) return null;
            if (DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
            return DateOnly.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out d) ? d : null;
        }
        set => _text.Text = value is { } v ? v.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "";
    }

    public bool IsBlank => _text.Text.Trim().Length == 0;

    public bool Invalid { set => Input.SetInvalid(_text, value); }

    public new bool Focus() => _text.Focus();
}
