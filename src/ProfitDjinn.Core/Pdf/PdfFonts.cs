using PdfSharp.Fonts;

namespace ProfitDjinn.Core.Pdf;

/// <summary>
/// 2.9. Where PDFsharp gets its fonts: the Windows font files, read directly. PDFsharp's own
/// Windows lookup knows only a handful of families (no Arial Black, no Segoe Script), and the
/// purchase order uses both. Any family not listed here is drawn with Arial.
/// Every PDF calls <see cref="Ensure"/> before its first font, because the resolver can only be
/// chosen once per process.
/// </summary>
internal sealed class PdfFonts : IFontResolver
{
    private static int _ready;

    // family -> (regular, bold, italic, bold italic) file names; null = simulate from regular.
    private static readonly Dictionary<string, (string Regular, string? Bold, string? Italic, string? BoldItalic)> Families =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Arial"] = ("arial.ttf", "arialbd.ttf", "ariali.ttf", "arialbi.ttf"),
            ["Arial Black"] = ("ariblk.ttf", null, null, null),
            ["Segoe Script"] = ("segoesc.ttf", "segoescb.ttf", null, null),
        };

    public static void Ensure()
    {
        if (Interlocked.Exchange(ref _ready, 1) == 1) return;
        if (GlobalFontSettings.FontResolver is null) GlobalFontSettings.FontResolver = new PdfFonts();
    }

    /// <summary>True when the family's font file is on this PC.</summary>
    public static bool Has(string family) =>
        Families.TryGetValue(family, out var f) && File.Exists(PathOf(f.Regular));

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        if (!Has(familyName)) familyName = "Arial";
        var f = Families[familyName];
        string? file = (bold, italic) switch
        {
            (true, true) => f.BoldItalic,
            (true, false) => f.Bold,
            (false, true) => f.Italic,
            _ => f.Regular,
        };
        if (file is not null && File.Exists(PathOf(file))) return new FontResolverInfo(file);
        // No such style file: draw the regular face and let PDFsharp embolden / slant it.
        return new FontResolverInfo(f.Regular, bold, italic);
    }

    public byte[]? GetFont(string faceName)
    {
        string path = PathOf(faceName);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    private static string PathOf(string file) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), file);
}
