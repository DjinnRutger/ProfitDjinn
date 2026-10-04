using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Rules;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.Core.Pdf;

/// <summary>
/// The invoice PDF. A coordinate-for-coordinate port of 1.x app/utils/pdf_generator.py
/// (fpdf2), so a 2.0 PDF looks like a 1.x one. Units below are millimetres, as in fpdf2.
///
/// fpdf2 conventions copied here:
///   - a cell's text is inset 1 mm from its edges (c_margin);
///   - the text baseline is at y + h/2 + 0.3 * font size;
///   - content starts at y = 15 (measured against a 1.x PDF of the same invoice).
/// Helvetica is drawn with Arial, its metric twin. Unlike fpdf2's core fonts, Arial covers
/// all of Unicode that the invoice needs, so 1.x's ASCII clean-up is not needed.
/// </summary>
public static class InvoicePdf
{
    private const double PageW = 210, PageH = 297, Margin = 15, CellMargin = 1;
    private const double LineH = 5.5, RowPad = 1.5, RowMinH = 7.0, FooterReserve = 30.0, ServiceH = 4.5;
    private const string Font = "Arial";

    private static readonly XColor Navy = XColor.FromArgb(28, 52, 88);
    private static int _fontsReady;

    public static byte[] Render(Invoice invoice, CompanyInfo company)
    {
        if (Interlocked.Exchange(ref _fontsReady, 1) == 0 && GlobalFontSettings.FontResolver is null)
            GlobalFontSettings.UseWindowsFontsUnderWindows = true;

        var doc = new PdfDocument();
        doc.Info.Title = $"Invoice {invoice.InvoiceNumber}";
        doc.Info.Creator = $"ProfitDjinn {AppInfo.Version}";
        var w = new Writer(doc);

        double effW = PageW - 2 * Margin;
        double half = effW * 0.55;
        double rightX = Margin + half;

        // ---- two-column header ----
        double top = Margin;
        w.Cell(Margin, top, half, 10, company.Name, 18, bold: true);
        w.Cell(rightX, top, effW - half, 10, "INVOICE", 20, bold: true, align: Align.Right);

        var addr = new List<string>();
        if (company.Address.Length > 0) addr.Add(company.Address);
        string cityState = $"{company.City}, {company.State} {company.Zip}".Trim(',', ' ').Trim();
        if (cityState.Length > 0) addr.Add(cityState);
        if (company.Email.Length > 0) addr.Add(company.Email);
        if (company.Phone.Length > 0) addr.Add(company.Phone);

        double leftY = top + 11;
        foreach (string line in addr.Where(l => l.Trim().Length > 0))
        {
            w.Cell(Margin, leftY, half, 5, line, 10);
            leftY += 5;
        }
        w.Cell(rightX, top + 12, effW - half, 6, $"Invoice #: {invoice.InvoiceNumber}", 10, align: Align.Right);
        w.Cell(rightX, top + 18, effW - half, 6, $"Date: {invoice.Date:yyyy-MM-dd}", 10, align: Align.Right);

        double y = Math.Max(leftY + 3, top + 28);
        w.Line(Margin, y, Margin + effW, y, XColor.FromArgb(180, 180, 180));
        y += 5;

        // ---- Bill To ----
        w.Cell(Margin, y, effW, 6, "Bill To:", 10, bold: true);
        y += 6 + 1;
        var c = invoice.Customer ?? new Customer();
        var bill = new List<string> { c.Name };
        if (!string.IsNullOrEmpty(c.Attn)) bill.Add($"Attn: {c.Attn}");
        if (!string.IsNullOrEmpty(c.Address)) bill.Add(c.Address);
        string cityLine = string.Join(", ", new[] { c.City, c.State, c.ZipCode }.Where(p => !string.IsNullOrEmpty(p)));
        if (cityLine.Length > 0) bill.Add(cityLine);
        foreach (string line in bill)
        {
            w.Cell(Margin, y, effW, 5.5, line, 10);
            y += 5.5;
        }
        y += 7;

        // ---- line items ----
        double colNum = effW * 0.08, colDesc = effW * 0.44, colQty = effW * 0.11, colUp = effW * 0.18;
        double colTot = effW - colNum - colDesc - colQty - colUp;

        double Header(double hy)
        {
            w.Fill(Margin, hy, effW, 7.5, Navy);
            var white = XColors.White;
            double x = Margin;
            w.Cell(x, hy, colNum, 7.5, "Line #", 9, bold: true, align: Align.Center, color: white); x += colNum;
            w.Cell(x, hy, colDesc, 7.5, "Description", 9, bold: true, color: white); x += colDesc;
            w.Cell(x, hy, colQty, 7.5, "Qty", 9, bold: true, align: Align.Center, color: white); x += colQty;
            w.Cell(x, hy, colUp, 7.5, "Unit Price", 9, bold: true, align: Align.Right, color: white); x += colUp;
            w.Cell(x, hy, colTot, 7.5, "Total", 9, bold: true, align: Align.Right, color: white);
            return hy + 7.5;
        }
        y = Header(y);

        int n = 0;
        foreach (var item in invoice.Lines)
        {
            n++;
            var wrapped = w.Wrap(item.Description ?? "", colDesc - 2 * CellMargin, 10, bold: false);
            if (wrapped.Count == 0) wrapped.Add("");
            // 2.5: service dates as a smaller grey line under the description.
            string? service = item.ServiceText;
            double rowH = Math.Max(RowMinH, wrapped.Count * LineH + (service is null ? 0 : ServiceH) + RowPad);
            if (y + rowH > PageH - FooterReserve)
            {
                w.NewPage();
                y = Header(Margin);
            }

            w.Fill(Margin, y, effW, rowH, n % 2 == 0 ? XColor.FromArgb(249, 250, 251) : XColors.White);
            w.Line(Margin, y + rowH, Margin + effW, y + rowH, XColor.FromArgb(220, 220, 220));

            double ty = y + RowPad / 2, x0 = Margin;
            w.Cell(x0, ty, colNum, LineH, n.ToString(), 10, align: Align.Center);
            for (int i = 0; i < wrapped.Count; i++)
                w.Cell(x0 + colNum, ty + i * LineH, colDesc, LineH, wrapped[i], 10);
            if (service is not null)
                w.Cell(x0 + colNum, ty + wrapped.Count * LineH, colDesc, ServiceH, service, 8.5, color: XColor.FromArgb(100, 100, 100));
            double xq = x0 + colNum + colDesc;
            w.Cell(xq, ty, colQty, LineH, PyMath.G(item.Quantity), 10, align: Align.Center);
            w.Cell(xq + colQty, ty, colUp, LineH, PyMath.Dollars(item.UnitPrice), 10, align: Align.Right);
            w.Cell(xq + colQty + colUp, ty, colTot, LineH, PyMath.Dollars(item.Amount), 10, align: Align.Right);
            y += rowH;
        }
        y += 5;

        // ---- totals ----
        double labelX = Margin + effW - colUp - colTot, valueX = labelX + colUp;
        if (y + (invoice.CreditApplied > 0 ? 21 : 7) > PageH - FooterReserve) { w.NewPage(); y = Margin; }
        if (invoice.CreditApplied > 0)
        {
            w.Cell(labelX, y, colUp, 6, "Subtotal:", 10, align: Align.Right);
            w.Cell(valueX, y, colTot, 6, PyMath.Dollars(invoice.Total), 10, align: Align.Right);
            y += 6;
            w.Cell(labelX, y, colUp, 6, "Credit:", 10, align: Align.Right);
            w.Cell(valueX, y, colTot, 6, "-" + PyMath.Dollars(invoice.CreditApplied), 10, align: Align.Right);
            y += 6;
            w.Cell(labelX, y, colUp, 7, "Amount Due:", 11, bold: true, align: Align.Right);
            w.Cell(valueX, y, colTot, 7, PyMath.Dollars(invoice.NetTotal), 11, bold: true, align: Align.Right);
            y += 7;
        }
        else
        {
            w.Cell(labelX, y, colUp, 7, "Total:", 11, bold: true, align: Align.Right);
            w.Cell(valueX, y, colTot, 7, PyMath.Dollars(invoice.Total), 11, bold: true, align: Align.Right);
            y += 7;
        }
        y += 8;

        // ---- notes and terms ----
        if (!string.IsNullOrEmpty(invoice.Notes))
        {
            var noteLines = w.Wrap(invoice.Notes, effW - 2 * CellMargin, 9, bold: false);
            if (noteLines.Count == 0) noteLines.Add("");
            if (y + 5 + noteLines.Count * 5 + 3 > PageH - FooterReserve) { w.NewPage(); y = Margin; }
            w.Cell(Margin, y, effW, 5, "Notes:", 9, bold: true);
            y += 5;
            foreach (string line in noteLines)
            {
                w.Cell(Margin, y, effW, 5, line, 9);
                y += 5;
            }
            y += 3;
        }

        var gray = XColor.FromArgb(80, 80, 80);
        if (!string.IsNullOrEmpty(invoice.Term1) || !string.IsNullOrEmpty(invoice.Term2))
            if (y + 10 > PageH - FooterReserve) { w.NewPage(); y = Margin; }
        if (!string.IsNullOrEmpty(invoice.Term1)) { w.Cell(Margin, y, effW, 5, invoice.Term1, 9, color: gray); y += 5; }
        if (!string.IsNullOrEmpty(invoice.Term2)) { w.Cell(Margin, y, effW, 5, invoice.Term2, 9, color: gray); y += 5; }

        // ---- footer, last page only ----
        w.Cell(Margin, PageH - 20, effW, 10, "Thank You for Your Business!", 11, bold: true, align: Align.Center);

        w.Finish();
        using var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        return ms.ToArray();
    }

    private enum Align { Left, Center, Right }

    /// <summary>Draws in millimetres with fpdf2's cell rules.</summary>
    private sealed class Writer
    {
        private const double PtPerMm = 72.0 / 25.4;
        private readonly PdfDocument _doc;
        private XGraphics _g = null!;

        public Writer(PdfDocument doc)
        {
            _doc = doc;
            NewPage();
        }

        public void NewPage()
        {
            _g?.Dispose();
            var page = _doc.AddPage();
            page.Width = XUnit.FromMillimeter(PageW);
            page.Height = XUnit.FromMillimeter(PageH);
            _g = XGraphics.FromPdfPage(page);
        }

        public void Finish() => _g.Dispose();

        private static XFont MakeFont(double pt, bool bold) => new(Font, pt, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);

        public void Cell(double x, double y, double w, double h, string text, double pt, bool bold = false,
            Align align = Align.Left, XColor? color = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            var font = MakeFont(pt, bold);
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

        public void Line(double x1, double y1, double x2, double y2, XColor color) =>
            _g.DrawLine(new XPen(color, 0.2 * PtPerMm), x1 * PtPerMm, y1 * PtPerMm, x2 * PtPerMm, y2 * PtPerMm);

        /// <summary>
        /// Greedy word wrap to <paramref name="widthMm"/>, like fpdf2's multi_cell: break at
        /// spaces, honour line breaks, and split a word that is wider than the column.
        /// </summary>
        public List<string> Wrap(string text, double widthMm, double pt, bool bold)
        {
            var font = MakeFont(pt, bold);
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
}
