using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace ProfitDjinn.Core.Pdf;

internal enum Align { Left, Center, Right }

/// <summary>
/// Draws on PDF pages in millimetres with fpdf2's cell rules (moved out of InvoicePdf in 2.9 so
/// purchase orders share it):
///   - a cell's text is inset 1 mm from its edges (c_margin);
///   - the text baseline is at y + h/2 + 0.3 * font size.
/// </summary>
internal sealed class PdfWriter
{
    public const double CellMargin = 1;
    public const string Font = "Arial";
    private const double PtPerMm = 72.0 / 25.4;

    private readonly PdfDocument _doc;
    private readonly double _pageW, _pageH;
    private XGraphics _g = null!;

    public PdfWriter(PdfDocument doc, double pageWidthMm, double pageHeightMm)
    {
        PdfFonts.Ensure();
        _doc = doc;
        _pageW = pageWidthMm;
        _pageH = pageHeightMm;
        NewPage();
    }

    public void NewPage()
    {
        _g?.Dispose();
        var page = _doc.AddPage();
        page.Width = XUnit.FromMillimeter(_pageW);
        page.Height = XUnit.FromMillimeter(_pageH);
        _g = XGraphics.FromPdfPage(page);
    }

    public void Finish() => _g.Dispose();

    public static XFont MakeFont(double pt, bool bold, string family = Font, bool italic = false) =>
        new(family, pt, (bold, italic) switch
        {
            (true, true) => XFontStyleEx.BoldItalic,
            (true, false) => XFontStyleEx.Bold,
            (false, true) => XFontStyleEx.Italic,
            _ => XFontStyleEx.Regular,
        });

    public double TextWidth(string text, double pt, bool bold = false, string family = Font) =>
        _g.MeasureString(text, MakeFont(pt, bold, family)).Width / PtPerMm;

    public void Cell(double x, double y, double w, double h, string text, double pt, bool bold = false,
        Align align = Align.Left, XColor? color = null, string family = Font, bool italic = false)
    {
        if (string.IsNullOrEmpty(text)) return;
        var font = MakeFont(pt, bold, family, italic);
        double textW = _g.MeasureString(text, font).Width / PtPerMm;
        double tx = align switch
        {
            Align.Right => x + w - CellMargin - textW,
            Align.Center => x + (w - textW) / 2,
            _ => x + CellMargin,
        };
        double baseline = y + 0.5 * h + 0.3 * (pt / PtPerMm);
        _g.DrawString(text, font, new XSolidBrush(color ?? XColors.Black),
            new XPoint(tx * PtPerMm, baseline * PtPerMm), XStringFormats.BaseLineLeft);
    }

    public void Fill(double x, double y, double w, double h, XColor color) =>
        _g.DrawRectangle(new XSolidBrush(color), x * PtPerMm, y * PtPerMm, w * PtPerMm, h * PtPerMm);

    public void Line(double x1, double y1, double x2, double y2, XColor color, double widthMm = 0.2) =>
        _g.DrawLine(new XPen(color, widthMm * PtPerMm), x1 * PtPerMm, y1 * PtPerMm, x2 * PtPerMm, y2 * PtPerMm);

    public void RoundedRect(double x, double y, double w, double h, double radius, XColor color, double widthMm) =>
        _g.DrawRoundedRectangle(new XPen(color, widthMm * PtPerMm), x * PtPerMm, y * PtPerMm, w * PtPerMm, h * PtPerMm,
            2 * radius * PtPerMm, 2 * radius * PtPerMm);

    /// <summary>Draws a PNG or JPEG scaled to fit inside the box, keeping its shape, at the box's top left. Returns the size drawn.</summary>
    public (double W, double H) Image(byte[] bytes, double x, double y, double maxW, double maxH)
    {
        using var image = XImage.FromStream(new MemoryStream(bytes));
        double ratio = image.PixelWidth / (double)image.PixelHeight;
        double w = maxW, h = maxW / ratio;
        if (h > maxH) { h = maxH; w = maxH * ratio; }
        _g.DrawImage(image, x * PtPerMm, y * PtPerMm, w * PtPerMm, h * PtPerMm);
        return (w, h);
    }

    /// <summary>True when this PC has the font family's file; used to fall back gracefully.</summary>
    public static bool HasFont(string family) => PdfFonts.Has(family);

    /// <summary>
    /// Greedy word wrap to <paramref name="widthMm"/>, like fpdf2's multi_cell: break at
    /// spaces, honour line breaks, and split a word that is wider than the column.
    /// </summary>
    public List<string> Wrap(string text, double widthMm, double pt, bool bold, bool italic = false)
    {
        var font = MakeFont(pt, bold, italic: italic);
        double Width(string s) => _g.MeasureString(s, font).Width / PtPerMm;
        var lines = new List<string>();
        foreach (string paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            string current = "";
            foreach (string word in paragraph.Split(' '))
            {
                string candidate = current.Length == 0 ? word : current + " " + word;
                if (Width(candidate) <= widthMm) { current = candidate; continue; }
                if (current.Length > 0) { lines.Add(current); current = ""; }
                string rest = word;
                while (rest.Length > 0 && Width(rest) > widthMm)
                {
                    int take = 1;
                    while (take < rest.Length && Width(rest[..(take + 1)]) <= widthMm) take++;
                    lines.Add(rest[..take]);
                    rest = rest[take..];
                }
                current = rest;
            }
            lines.Add(current);
        }
        return lines;
    }
}
