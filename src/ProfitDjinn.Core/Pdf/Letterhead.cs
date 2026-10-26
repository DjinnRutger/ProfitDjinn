using PdfSharp.Drawing;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.Core.Pdf;

/// <summary>
/// 2.9. The business block at the top left of invoices and purchase orders, laid out like a
/// printed letterhead: the logo, beside it a contact line, "phone • email" and the address, and
/// under it the services as bullets.
/// With a logo the first text line is the contact line (or the business name when there is no
/// contact line), because a logo usually carries the name already. Without one the business
/// name comes first, in large type.
/// </summary>
internal static class Letterhead
{
    private const double LogoMaxW = 66, LogoMaxH = 24, Gap = 5;

    /// <summary>Draws the block in the box starting at (x, y), <paramref name="width"/> wide. Returns the y below it.</summary>
    public static double Draw(PdfWriter w, CompanyInfo c, double x, double y, double width)
    {
        double logoW = 0, logoH = 0;
        if (c.Logo is { } logo) (logoW, logoH) = w.Image(logo, x, y, Math.Min(LogoMaxW, width * 0.55), LogoMaxH);
        double tx = c.Logo is null ? x : x + logoW + Gap;
        double tw = x + width - tx;
        double ty = y;

        if (c.Logo is null && c.Name.Length > 0)
        {
            Line(w, tx, ref ty, tw, c.Name, 16, 8, bold: true);
            if (c.ContactLine.Length > 0) Line(w, tx, ref ty, tw, c.ContactLine, 11, 6, bold: true);
        }
        else
        {
            string first = c.ContactLine.Length > 0 ? c.ContactLine : c.Name;
            if (first.Length > 0) Line(w, tx, ref ty, tw, first, 12, 6.5, bold: true);
        }
        Line(w, tx, ref ty, tw, Join(c.Phone, c.Email), 10, 5);
        string cityLine = $"{c.City}, {c.State} {c.Zip}".Trim(',', ' ').Trim();
        Line(w, tx, ref ty, tw, Join(c.Address, cityLine), 10, 5);

        double by = Math.Max(y + logoH, ty) + (c.ServiceList.Count > 0 ? 2 : 0);
        foreach (string service in c.ServiceList)
        {
            Line(w, x, ref by, width, "•  " + service, 10, 4.7);
        }
        return by;
    }

    private static string Join(string a, string b) =>
        a.Length > 0 && b.Length > 0 ? $"{a}  •  {b}" : a + b;

    /// <summary>One line of text, shrunk (down to 7 pt) rather than run past the block's edge.</summary>
    private static void Line(PdfWriter w, double x, ref double y, double width, string text, double pt, double h, bool bold = false)
    {
        if (text.Length == 0) return;
        double size = pt;
        while (size > 7 && w.TextWidth(text, size, bold) > width - 2 * PdfWriter.CellMargin) size -= 0.5;
        w.Cell(x, y, width, h, text, size, bold, color: XColors.Black);
        y += h;
    }
}
