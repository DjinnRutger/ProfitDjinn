using System.Reflection;
using System.Windows;
using System.Windows.Media;

namespace ProfitDjinn.App.Controls;

/// <summary>
/// A Bootstrap Icons glyph, drawn from path data in Assets/bootstrap-icons.txt.
/// Use the icon's Bootstrap name, with or without "bi-": &lt;c:Icon Name="receipt" /&gt;.
/// Colour comes from Foreground (inherited like text), size from Size (default 16).
/// </summary>
public sealed class Icon : FrameworkElement
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(Icon),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(Icon),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = System.Windows.Documents.TextElement.ForegroundProperty.AddOwner(
        typeof(Icon), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>The Bootstrap icon name, e.g. "receipt" or "bi-receipt".</summary>
    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public Icon()
    {
        SnapsToDevicePixels = true;
        VerticalAlignment = VerticalAlignment.Center;
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    protected override void OnRender(DrawingContext dc)
    {
        var geometries = IconData.Get(Glyph);
        if (geometries.Count == 0) return;
        double scale = Size / 16.0;
        dc.PushTransform(new ScaleTransform(scale, scale));
        foreach (var g in geometries) dc.DrawGeometry(Foreground, null, g);
        dc.Pop();
    }
}

/// <summary>Loads the icon path data once, and parses each icon the first time it is drawn.</summary>
public static class IconData
{
    private static readonly Lazy<Dictionary<string, string>> Raw = new(Load);
    private static readonly Dictionary<string, IReadOnlyList<Geometry>> Parsed = new();

    public static bool Exists(string? name) => Raw.Value.ContainsKey(Normalize(name));

    public static IReadOnlyList<Geometry> Get(string? name)
    {
        string key = Normalize(name);
        lock (Parsed)
        {
            if (Parsed.TryGetValue(key, out var g)) return g;
            var list = new List<Geometry>();
            if (Raw.Value.TryGetValue(key, out string? line))
            {
                foreach (string part in line.Split('\t'))
                {
                    var geometry = Geometry.Parse((part[0] == 'E' ? "F0 " : "F1 ") + part[2..]);
                    geometry.Freeze();
                    list.Add(geometry);
                }
            }
            Parsed[key] = list;
            return list;
        }
    }

    private static string Normalize(string? name)
    {
        string n = (name ?? "").Trim();
        return n.StartsWith("bi-", StringComparison.Ordinal) ? n[3..] : n;
    }

    private static Dictionary<string, string> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("bootstrap-icons.txt")
            ?? throw new InvalidOperationException("bootstrap-icons.txt is not embedded in the app. Check ProfitDjinn.App.csproj.");
        using var reader = new StreamReader(stream);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0 || line[0] == '#') continue;
            int tab = line.IndexOf('\t');
            map[line[..tab]] = line[(tab + 1)..];
        }
        return map;
    }
}
