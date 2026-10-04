using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using ProfitDjinn.Core;
using ProfitDjinn.Core.Model;
using ProfitDjinn.Core.Pdf;

namespace ProfitDjinn.App.Infrastructure;

/// <summary>
/// The invoice's two ways out: the PDF button (save the file and open it) and Print (the
/// same PDF, drawn by PDFium and sent to a printer). One layout for both, so a printed
/// invoice and a PDF one always match. 1.x had a separate HTML print view.
/// </summary>
public static class InvoiceOutput
{
    public static void SavePdf(Invoice invoice, CompanyInfoProvider company, Window owner) =>
        SavePdf(InvoicePdf.Render(invoice, company()), SafeFileName(invoice.InvoiceNumber) + ".pdf", owner);

    /// <summary>2.5. Opens the invoice in the preview window; Save PDF and Print run from there.</summary>
    public static void Preview(Invoice invoice, CompanyInfoProvider company, Window owner) =>
        PdfPreviewWindow.Show(owner, InvoicePdf.Render(invoice, company()), $"Invoice {invoice.InvoiceNumber}",
            save: (pdf, w) => SavePdf(pdf, SafeFileName(invoice.InvoiceNumber) + ".pdf", w),
            print: (pdf, _) => Print(pdf, $"Invoice {invoice.InvoiceNumber}"));

    /// <summary>Asks where to save <paramref name="pdf"/>, writes it and opens it. Returns the path, or null if cancelled.</summary>
    public static string? SavePdf(byte[] pdf, string fileName, Window owner)
    {
        var dialog = new SaveFileDialog
        {
            FileName = fileName,
            Filter = "PDF document (*.pdf)|*.pdf",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is { Length: > 0 } home && Directory.Exists(Path.Combine(home, "Downloads"))
                ? Path.Combine(home, "Downloads") : null,
        };
        if (dialog.ShowDialog(owner) != true) return null;
        try
        {
            File.WriteAllBytes(dialog.FileName, pdf);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UserFacingException($"The PDF could not be saved to\n{dialog.FileName}\n\n{ex.Message}\n\nIf it is open in a PDF viewer, close it and try again.", ex);
        }
        try { Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { /* no PDF viewer: the file is saved, which is what was asked */ }
        return dialog.FileName;
    }

    public static void Print(Invoice invoice, CompanyInfoProvider company) =>
        Print(InvoicePdf.Render(invoice, company()), $"Invoice {invoice.InvoiceNumber}");

    /// <summary>Prints <paramref name="pdf"/> as drawn by PDFium, after the system print dialog.</summary>
    public static void Print(byte[] pdf, string jobName)
    {
        var dialog = new PrintDialog { UserPageRangeEnabled = false };
        if (dialog.ShowDialog() != true) return;

        var doc = new FixedDocument();
        double pageW = dialog.PrintableAreaWidth > 0 ? dialog.PrintableAreaWidth : 793.7;   // A4 at 96 dpi
        double pageH = dialog.PrintableAreaHeight > 0 ? dialog.PrintableAreaHeight : 1122.5;
        doc.DocumentPaginator.PageSize = new Size(pageW, pageH);
        foreach (var bitmap in RenderPages(pdf, dpi: 300))
        {
            var image = new Image { Source = bitmap, Width = pageW, Height = pageH, Stretch = Stretch.Uniform };
            var page = new FixedPage { Width = pageW, Height = pageH, Background = Brushes.White };
            page.Children.Add(image);
            var content = new PageContent();
            ((System.Windows.Markup.IAddChild)content).AddChild(page);
            doc.Pages.Add(content);
        }
        dialog.PrintDocument(doc.DocumentPaginator, jobName);
    }

    /// <summary>Draws every page of the PDF at <paramref name="dpi"/>.</summary>
    public static List<BitmapSource> RenderPages(byte[] pdf, int dpi)
    {
        var pages = new List<BitmapSource>();
        lock (Pdfium.Lock)
        {
            Pdfium.EnsureInitialized();
            var handle = GCHandle.Alloc(pdf, GCHandleType.Pinned);
            try
            {
                IntPtr doc = Pdfium.FPDF_LoadMemDocument(handle.AddrOfPinnedObject(), pdf.Length, null);
                if (doc == IntPtr.Zero) throw new UserFacingException("The PDF could not be read back for display or printing. Save it and open the saved file instead.");
                try
                {
                    int count = Pdfium.FPDF_GetPageCount(doc);
                    for (int i = 0; i < count; i++)
                    {
                        IntPtr p = Pdfium.FPDF_LoadPage(doc, i);
                        double wIn = Pdfium.FPDF_GetPageWidthF(p) / 72.0, hIn = Pdfium.FPDF_GetPageHeightF(p) / 72.0;
                        Pdfium.FPDF_ClosePage(p);
                        pages.Add(Pdfium.RenderPage(doc, i, (int)Math.Round(wIn * dpi), (int)Math.Round(hIn * dpi), new Size(wIn, hIn)));
                    }
                }
                finally { Pdfium.FPDF_CloseDocument(doc); }
            }
            finally { handle.Free(); }
        }
        return pages;
    }

    public static string SafeFileName(string name) =>
        string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
}

public delegate Core.Services.CompanyInfo CompanyInfoProvider();

/// <summary>
/// The PDFium calls printing needs, over the native pdfium.dll from bblanchon.PDFium.Win32
/// (Apache-2.0). Adapted from FireDjinn. PDFium is not thread-safe: hold <see cref="Lock"/>.
/// </summary>
internal static class Pdfium
{
    public static readonly object Lock = new();
    private static bool _initialized;
    private const string Dll = "pdfium";
    private const int FpdfAnnot = 0x01, FpdfPrinting = 0x800;

    [DllImport(Dll)] private static extern void FPDF_InitLibrary();
    [DllImport(Dll, CharSet = CharSet.Ansi, BestFitMapping = false)]
    public static extern IntPtr FPDF_LoadMemDocument(IntPtr data, int size, string? password);
    [DllImport(Dll)] public static extern int FPDF_GetPageCount(IntPtr doc);
    [DllImport(Dll)] public static extern IntPtr FPDF_LoadPage(IntPtr doc, int index);
    [DllImport(Dll)] public static extern float FPDF_GetPageWidthF(IntPtr page);
    [DllImport(Dll)] public static extern float FPDF_GetPageHeightF(IntPtr page);
    [DllImport(Dll)] public static extern void FPDF_ClosePage(IntPtr page);
    [DllImport(Dll)] public static extern void FPDF_CloseDocument(IntPtr doc);
    [DllImport(Dll)] private static extern IntPtr FPDFBitmap_Create(int width, int height, int alpha);
    [DllImport(Dll)] private static extern void FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);
    [DllImport(Dll)] private static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int x, int y, int w, int h, int rotate, int flags);
    [DllImport(Dll)] private static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);
    [DllImport(Dll)] private static extern int FPDFBitmap_GetStride(IntPtr bitmap);
    [DllImport(Dll)] private static extern void FPDFBitmap_Destroy(IntPtr bitmap);

    public static void EnsureInitialized()
    {
        if (_initialized) return;
        FPDF_InitLibrary();
        _initialized = true;
    }

    public static BitmapSource RenderPage(IntPtr doc, int pageIndex, int width, int height, Size inches)
    {
        IntPtr page = FPDF_LoadPage(doc, pageIndex);
        if (page == IntPtr.Zero) throw new UserFacingException($"Page {pageIndex + 1} of the invoice PDF could not be drawn.");
        IntPtr bitmap = FPDFBitmap_Create(width, height, 0);
        if (bitmap == IntPtr.Zero)
        {
            FPDF_ClosePage(page);
            throw new OutOfMemoryException($"Not enough memory to draw a {width} x {height} page.");
        }
        try
        {
            FPDFBitmap_FillRect(bitmap, 0, 0, width, height, 0xFFFFFFFF);
            FPDF_RenderPageBitmap(bitmap, page, 0, 0, width, height, 0, FpdfAnnot | FpdfPrinting);
            int stride = FPDFBitmap_GetStride(bitmap);
            var pixels = new byte[stride * height];
            Marshal.Copy(FPDFBitmap_GetBuffer(bitmap), pixels, 0, pixels.Length);
            var result = BitmapSource.Create(width, height, width / inches.Width, height / inches.Height, PixelFormats.Bgr32, null, pixels, stride);
            result.Freeze();
            return result;
        }
        finally
        {
            FPDFBitmap_Destroy(bitmap);
            FPDF_ClosePage(page);
        }
    }
}
