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
        string Pct(double? v) => v is { } x ? x.ToString("0.0", CultureInfo.InvariantCulture) + "%" : "—";
        // 2.6: with a cost-of-revenue category, two rows of three: the gross profit line first.
        var totals = r.ShowGross
            ? new (string Label, string Value, XColor Color)[]
            {
                ("Income", Money(r.Income), Green),
                ("Cost of Revenue", Money(r.CostOfRevenue), Red),
                ($"Gross Profit ({Pct(r.GrossMargin)})", Money(r.GrossProfit), r.GrossProfit >= 0 ? Navy : Red),
                ("Operating Expenses", Money(r.OperatingExpenses), Red),
                ("Net Profit", Money(r.Net), r.Net >= 0 ? Navy : Red),
                ("Net Margin", Pct(r.Margin), Navy),
            }
            : new (string Label, string Value, XColor Color)[]
            {
                ("Income", Money(r.Income), Green),
                ("Expenses", Money(r.Expenses), Red),
                ("Net Profit", Money(r.Net), r.Net >= 0 ? Navy : Red),
                ("Margin", Pct(r.Margin), Navy),
            };
        int perRow = r.ShowGross ? 3 : 4;
        double col = effW / perRow;
        for (int i = 0; i < totals.Length; i++)
        {
            double ty = y + (i / perRow) * 14;
            Text(Margin + col * (i % perRow), ty, totals[i].Value, 15, bold: true, color: totals[i].Color);
            Text(Margin + col * (i % perRow), ty + 5, totals[i].Label.ToUpperInvariant(), 8, color: Grey);
        }
        y += 14 * ((totals.Length + perRow - 1) / perRow);

        // ---- months
        double[] cw = r.ShowGross ? new double[] { 28, 30, 30, 30, 32, 30 } : new double[] { 40, 45, 45, 50 };
        string[] head = r.ShowGross ? new[] { "Month", "Income", "Cost", "Gross", "Operating", "Net" } : new[] { "Month", "Income", "Expenses", "Net" };
        // One row of the month table: the figures in the columns above.
        double[] Figures(double income, double expenses, double cost, double gross, double operating, double net) =>
            r.ShowGross ? new[] { income, cost, gross, operating, net } : new[] { income, expenses, net };
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
        void Row(string label, double[] values, bool bold)
        {
            double x = Margin;
            Text(x + 1, y, label, 9.5, bold: bold); x += cw[0];
            for (int i = 0; i < values.Length; i++)
            {
                bool last = i == values.Length - 1;
                Text(x, y, Money(values[i]), 9.5, bold: bold, color: last && values[i] < 0 ? Red : null, right: true, w: cw[i + 1] - 1);
                x += cw[i + 1];
            }
        }
        foreach (var mo in r.Months)
        {
            Row(mo.Name, Figures(mo.Income, mo.Expenses, mo.CostOfRevenue, mo.GrossProfit, mo.OperatingExpenses, mo.Net), bold: false);
            y += 5.6;
        }
        Line(y - 3.6);
        y += 1.5;
        Row("Total", Figures(r.Income, r.Expenses, r.CostOfRevenue, r.GrossProfit, r.OperatingExpenses, r.Net), bold: true);
        y += 11;

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
            Text(Margin + 1, y, c.Name + (r.ShowGross && c.CostOfRevenue ? "  (cost of revenue)" : ""), 9.5);
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
