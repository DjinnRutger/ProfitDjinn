using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using ProfitDjinn.Core.Rules;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.Core.Pdf;

/// <summary>
/// 2.2: a one-page (A4) Profit &amp; Loss summary for a year: totals, the month-by-month table,
/// and expenses by category. Styled to sit beside the invoice PDF (same navy, same Arial).
/// Units are millimetres. A long category list continues on a second page.
/// </summary>
public static class ProfitPdf
{
    private const double PageW = 210, PageH = 297, Margin = 15, PtPerMm = 72.0 / 25.4;
    private const string Font = "Arial";
    private static readonly XColor Navy = XColor.FromArgb(28, 52, 88);
    private static readonly XColor Grey = XColor.FromArgb(110, 110, 110);
    private static readonly XColor Rule = XColor.FromArgb(200, 200, 200);
    private static readonly XColor Shade = XColor.FromArgb(240, 242, 246);
    private static readonly XColor Green = XColor.FromArgb(25, 135, 84);
    private static readonly XColor Red = XColor.FromArgb(200, 35, 51);

    public static byte[] Render(ProfitReport r, CompanyInfo company, DateOnly printed)
    {
        if (GlobalFontSettings.FontResolver is null) GlobalFontSettings.UseWindowsFontsUnderWindows = true;

        var doc = new PdfDocument();
        doc.Info.Title = $"Profit and Loss {r.Year}";
        doc.Info.Creator = $"ProfitDjinn {AppInfo.Version}";
        var page = NewPage(doc);
        var g = XGraphics.FromPdfPage(page);
        double effW = PageW - 2 * Margin;

        void Text(double x, double y, string s, double pt, bool bold = false, XColor? color = null, bool right = false, double w = 0)
        {
            var f = new XFont(Font, pt, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);
            double tx = right ? x + w - g.MeasureString(s, f).Width / PtPerMm : x;
            g.DrawString(s, f, new XSolidBrush(color ?? XColors.Black), new XPoint(tx * PtPerMm, y * PtPerMm), XStringFormats.BaseLineLeft);
        }
        void Line(double y) => g.DrawLine(new XPen(Rule, 0.2 * PtPerMm), Margin * PtPerMm, y * PtPerMm, (Margin + effW) * PtPerMm, y * PtPerMm);
        void Fill(double y, double h) => g.DrawRectangle(new XSolidBrush(Shade), Margin * PtPerMm, y * PtPerMm, effW * PtPerMm, h * PtPerMm);

        // ---- header
        double y = Margin + 6;
        Text(Margin, y, company.Name, 16, bold: true, color: Navy);
        Text(Margin, y, "PROFIT & LOSS", 16, bold: true, color: Navy, right: true, w: effW);
        y += 7;
        string basis = r.Basis == ProfitBasis.Cash ? "Cash basis: money received and paid, by date" : "Accrual basis: invoices and expenses by their own dates";
        string period = r.Year == r.CurrentYear && printed.Year == r.Year
            ? $"January 1 to {printed.ToString("MMMM d", CultureInfo.InvariantCulture)}, {r.Year} (year to date)"
            : $"January 1 to December 31, {r.Year}";
        Text(Margin, y, period, 10, color: Grey);
        Text(Margin, y, basis, 9, color: Grey, right: true, w: effW);
        y += 4;
        Line(y);
        y += 9;

        // ---- totals
        double col = effW / 4;
        var totals = new (string Label, string Value, XColor Color)[]
        {
            ("Income", Money(r.Income), Green),
            ("Expenses", Money(r.Expenses), Red),
            ("Net Profit", Money(r.Net), r.Net >= 0 ? Navy : Red),
            ("Margin", r.Margin is { } m ? m.ToString("0.0", CultureInfo.InvariantCulture) + "%" : "—", Navy),
        };
        for (int i = 0; i < totals.Length; i++)
        {
            Text(Margin + col * i, y, totals[i].Value, 15, bold: true, color: totals[i].Color);
            Text(Margin + col * i, y + 5, totals[i].Label.ToUpperInvariant(), 8, color: Grey);
        }
        y += 14;

        // ---- months
        double[] cw = { 40, 45, 45, 50 };
        string[] head = { "Month", "Income", "Expenses", "Net" };
        void Header(string[] titles, double[] widths)
        {
            Fill(y - 4.5, 7);
            double x = Margin;
            for (int i = 0; i < titles.Length; i++)
            {
                Text(x + 1, y, titles[i].ToUpperInvariant(), 8, bold: true, color: Grey, right: i > 0, w: widths[i] - 2);
                x += widths[i];
            }
            y += 7;
        }
        Text(Margin, y, "By month", 11, bold: true, color: Navy);
        y += 6;
        Header(head, cw);
        foreach (var mo in r.Months)
        {
            double x = Margin;
            Text(x + 1, y, mo.Name, 9.5); x += cw[0];
            Text(x, y, Money(mo.Income), 9.5, right: true, w: cw[1] - 1); x += cw[1];
            Text(x, y, Money(mo.Expenses), 9.5, right: true, w: cw[2] - 1); x += cw[2];
            Text(x, y, Money(mo.Net), 9.5, color: mo.Net < 0 ? Red : null, right: true, w: cw[3] - 1);
            y += 5.6;
        }
        Line(y - 3.6);
        {
            double x = Margin;
            y += 1.5;
            Text(x + 1, y, "Total", 9.5, bold: true); x += cw[0];
            Text(x, y, Money(r.Income), 9.5, bold: true, right: true, w: cw[1] - 1); x += cw[1];
            Text(x, y, Money(r.Expenses), 9.5, bold: true, right: true, w: cw[2] - 1); x += cw[2];
            Text(x, y, Money(r.Net), 9.5, bold: true, color: r.Net < 0 ? Red : null, right: true, w: cw[3] - 1);
            y += 11;
        }

        // ---- categories
        double[] kw = { 110, 40, 30 };
        Text(Margin, y, "Expenses by category", 11, bold: true, color: Navy);
        y += 6;
        Header(new[] { "Category", "Amount", "Share" }, kw);
        if (r.Categories.Count == 0)
        {
            Text(Margin + 1, y, "No expenses in this year.", 9.5, color: Grey);
            y += 5.6;
        }
        foreach (var c in r.Categories)
        {
            if (y > PageH - Margin - 12)
            {
                g.Dispose();
                page = NewPage(doc);
                g = XGraphics.FromPdfPage(page);
                y = Margin + 6;
                Header(new[] { "Category", "Amount", "Share" }, kw);
            }
            Text(Margin + 1, y, c.Name, 9.5);
            Text(Margin + kw[0], y, Money(c.Amount), 9.5, right: true, w: kw[1] - 1);
            Text(Margin + kw[0] + kw[1], y, c.Share.ToString("0.0", CultureInfo.InvariantCulture) + "%", 9.5, color: Grey, right: true, w: kw[2] - 1);
            y += 5.6;
        }

        // ---- footer
        Text(Margin, PageH - Margin + 2, $"Prepared {printed.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)} with ProfitDjinn {AppInfo.Version}. Not a tax filing; check figures with your accountant.", 7.5, color: Grey);
        g.Dispose();

        using var ms = new MemoryStream();
        doc.Save(ms);
        return ms.ToArray();
    }

    private static PdfPage NewPage(PdfDocument doc)
    {
        var p = doc.AddPage();
        p.Width = XUnit.FromMillimeter(PageW);
        p.Height = XUnit.FromMillimeter(PageH);
        return p;
    }

    /// <summary>"$1,234.50", or "-$1,234.50" for a loss.</summary>
    public static string Money(double x) => (x < 0 ? "-" : "") + PyMath.Dollars(Math.Abs(x));
}
