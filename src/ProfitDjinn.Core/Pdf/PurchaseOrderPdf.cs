using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Rules;
using ProfitDjinn.Core.Services;

namespace ProfitDjinn.Core.Pdf;

/// <summary>
/// 2.9. The purchase order PDF, drawn like a printed PO pad on US Letter: letterhead top left,
/// "PURCHASE ORDER" and the number top right, then one rounded box holding the vendor's details
/// on ruled lines and a ruled QUAN. / DESCRIPTION / PRICE / AMOUNT table that runs to the bottom
/// of the box, and "Thank You" under it. Measurements are millimetres, taken from the paper form.
/// A description too long for its row carries on in the rows below, as it would be handwritten;
/// the notes follow the lines; the total takes the last row of the last page.
/// </summary>
public static class PurchaseOrderPdf
{
    private const double PageW = 215.9, PageH = 279.4;
    private const double BoxX = 8.7, BoxR = 207.2, BoxBottom = 266, Radius = 4;
    private const double TitleX = 164.4, TitleR = 207.7;
    private const double HeadH = 9, RowH = 7.05, Pt = 10;
    // Column edges: QUAN. | DESCRIPTION | PRICE | AMOUNT
    private const double ColDesc = 27.2, ColPrice = 149.5, ColAmount = 169.5;

    private static readonly XColor Black = XColors.Black;
    private static readonly XColor Gray = XColor.FromArgb(90, 90, 90);
    private static readonly XColor TotalFill = XColor.FromArgb(236, 236, 236);

    private sealed record Row(string Qty, string Description, string Price, string Amount, bool Note = false);

    public static byte[] Render(PurchaseOrder order, CompanyInfo company)
    {
        var doc = new PdfDocument();
        doc.Info.Title = $"Purchase Order {order.PoNumber}";
        doc.Info.Creator = $"ProfitDjinn {AppInfo.Version}";
        var w = new PdfWriter(doc, PageW, PageH);

        // Rows first, so the page count is known before drawing.
        double descW = ColPrice - ColDesc - 2 * PdfWriter.CellMargin;
        var rows = new List<Row>();
        foreach (var line in order.Lines)
        {
            var wrapped = w.Wrap(line.Description, descW, Pt, bold: false);
            if (wrapped.Count == 0) wrapped.Add("");
            rows.Add(new Row(PyMath.G(line.Quantity), wrapped[0], PyMath.Dollars(line.UnitPrice), PyMath.Dollars(line.Amount)));
            rows.AddRange(wrapped.Skip(1).Select(t => new Row("", t, "", "")));
        }
        if (!string.IsNullOrWhiteSpace(order.Notes))
        {
            rows.Add(new Row("", "", "", ""));
            var notes = w.Wrap("Note: " + order.Notes!.Trim(), descW, Pt, bold: false, italic: true);
            rows.AddRange(notes.Select(t => new Row("", t, "", "", Note: true)));
        }

        // Each page has the same header and box; the last row of the last page is the total.
        double tableTop = TableTop(w, company);
        int perPage = Math.Max(2, (int)Math.Floor((BoxBottom - tableTop - HeadH) / RowH + 0.01));
        var pages = new List<List<Row>>();
        int at = 0;
        while (rows.Count - at > perPage - 1)
        {
            pages.Add(rows.GetRange(at, perPage));
            at += perPage;
        }
        pages.Add(rows.GetRange(at, rows.Count - at));

        for (int p = 0; p < pages.Count; p++)
        {
            if (p > 0) w.NewPage();
            bool last = p == pages.Count - 1;
            DrawPage(w, order, company, pages[p], perPage, last);
            if (pages.Count > 1)
                w.Cell(BoxX, BoxBottom + 2, 60, 5, $"Page {p + 1} of {pages.Count}", 8, color: Gray);
        }

        w.Finish();
        using var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        return ms.ToArray();
    }

    /// <summary>Where the table header starts: below the letterhead, never higher than on the paper form.</summary>
    private static double TableTop(PdfWriter w, CompanyInfo company)
    {
        // Measure the letterhead on a throwaway document; drawing it twice would be wasteful but harmless.
        var probe = new PdfWriter(new PdfDocument(), PageW, PageH);
        double bottom = Letterhead.Draw(probe, company, 10.5, 9, TitleX - 10.5 - 4);
        probe.Finish();
        double boxTop = Math.Max(45, Math.Max(bottom, 34) + 5);
        return boxTop + 42.7;
    }

    private static void DrawPage(PdfWriter w, PurchaseOrder order, CompanyInfo company, List<Row> rows, int perPage, bool last)
    {
        // ---- letterhead and title
        double headerBottom = Letterhead.Draw(w, company, 10.5, 9, TitleX - 10.5 - 4);
        double titleW = TitleR - TitleX;
        w.Line(TitleX, 8.7, TitleR, 8.7, Black, 0.6);
        w.Line(TitleX, 25.1, TitleR, 25.1, Black, 0.6);
        string family = PdfWriter.HasFont("Arial Black") ? "Arial Black" : PdfWriter.Font;
        double pt = 22;
        double widest = w.TextWidth("PURCHASE", pt, bold: family == PdfWriter.Font, family: family);
        pt = Math.Min(26, pt * (titleW - 2) / widest);
        bool bold = family == PdfWriter.Font;
        w.Cell(TitleX, 9.5, titleW, 7.5, "PURCHASE", pt, bold, Align.Center, family: family);
        w.Cell(TitleX, 16.8, titleW, 7.5, "ORDER", pt, bold, Align.Center, family: family);
        w.Cell(TitleX, 27.5, titleW, 7, "#" + order.PoNumber, 14, bold: true, Align.Center);

        // ---- the box
        double boxTop = Math.Max(45, Math.Max(headerBottom, 34) + 5);
        w.RoundedRect(BoxX, boxTop, BoxR - BoxX, BoxBottom - boxTop, Radius, Black, 0.4);

        // ---- vendor fields on ruled lines
        var v = order.Vendor ?? new Vendor();
        double fy = boxTop + 4;
        Field(w, 14, fy, 153.8, "Name", v.Name);
        Field(w, 153.8, fy, 200.6, "Date", order.Date.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture));
        fy += 8.5;
        Field(w, 14, fy, 200.6, "Address", v.Address ?? "");
        fy += 8.5;
        Field(w, 14, fy, 75.5, "City", v.City ?? "");
        Field(w, 75.9, fy, 101.8, "State", v.State ?? "");
        Field(w, 101.8, fy, 132, "Zip", v.ZipCode ?? "");
        Field(w, 133.2, fy, 201.3, "Phone", v.Phone ?? "");
        fy += 8.5;
        Field(w, 14, fy, 100.4, "Email", v.Email ?? "");

        // ---- table header (black band) and ruled rows down to the box bottom
        double ty = boxTop + 42.7;
        w.Fill(BoxX, ty, BoxR - BoxX, HeadH, Black);
        var white = XColors.White;
        w.Cell(BoxX, ty, ColDesc - BoxX, HeadH, "QUAN.", 10, bold: true, Align.Center, white);
        w.Cell(ColDesc, ty, ColPrice - ColDesc, HeadH, "DESCRIPTION", 10, bold: true, Align.Center, white);
        w.Cell(ColPrice, ty, ColAmount - ColPrice, HeadH, "PRICE", 10, bold: true, Align.Center, white);
        w.Cell(ColAmount, ty, BoxR - ColAmount, HeadH, "AMOUNT", 10, bold: true, Align.Center, white);
        foreach (double x in new[] { ColDesc, ColPrice, ColAmount }) w.Line(x, ty, x, ty + HeadH, white, 0.5);

        double rowsTop = ty + HeadH;
        int totalRow = last ? perPage - 1 : -1;
        if (totalRow >= 0) w.Fill(BoxX + 0.2, rowsTop + totalRow * RowH, BoxR - BoxX - 0.4, BoxBottom - rowsTop - totalRow * RowH - 0.2, TotalFill);
        for (int i = 1; i < perPage; i++) w.Line(BoxX, rowsTop + i * RowH, BoxR, rowsTop + i * RowH, Black, 0.25);
        foreach (double x in new[] { ColDesc, ColPrice, ColAmount }) w.Line(x, rowsTop, x, BoxBottom, Black, 0.25);

        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            double ry = rowsTop + i * RowH;
            w.Cell(BoxX, ry, ColDesc - BoxX, RowH, r.Qty, Pt, align: Align.Center);
            w.Cell(ColDesc, ry, ColPrice - ColDesc, RowH, r.Description, Pt, color: r.Note ? Gray : Black, italic: r.Note);
            w.Cell(ColPrice, ry, ColAmount - ColPrice, RowH, r.Price, Pt, align: Align.Right);
            w.Cell(ColAmount, ry, BoxR - ColAmount, RowH, r.Amount, Pt, align: Align.Right);
        }
        if (totalRow >= 0)
        {
            double ry = rowsTop + totalRow * RowH;
            double th = BoxBottom - ry;
            w.Cell(ColPrice, ry, ColAmount - ColPrice, th, "TOTAL", Pt, bold: true, Align.Right);
            w.Cell(ColAmount, ry, BoxR - ColAmount, th, PyMath.Dollars(order.Total), Pt + 0.5, bold: true, Align.Right);

            string script = PdfWriter.HasFont("Segoe Script") ? "Segoe Script" : PdfWriter.Font;
            w.Cell(150, 267, 50, 10, "Thank You", 18, bold: true, Align.Center, family: script, italic: script == PdfWriter.Font);
        }
    }

    /// <summary>A label and its ruled line, with the value written on the line.</summary>
    private static void Field(PdfWriter w, double x, double y, double right, string label, string value)
    {
        const double h = 7, labelPt = 11;
        w.Cell(x - PdfWriter.CellMargin, y, 30, h, label, labelPt);
        double lineX = x + w.TextWidth(label, labelPt) + 0.6;
        double lineY = y + h - 1.2;
        w.Line(lineX, lineY, right, lineY, Black, 0.3);
        if (value.Length == 0) return;
        double size = 11;
        while (size > 7 && w.TextWidth(value, size) > right - lineX - 3) size -= 0.5;
        w.Cell(lineX + 1, y - 0.6, right - lineX - 1, h, value, size);
    }
}
