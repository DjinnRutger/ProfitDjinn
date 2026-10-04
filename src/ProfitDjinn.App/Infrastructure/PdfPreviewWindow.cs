using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ProfitDjinn.Core;

namespace ProfitDjinn.App.Infrastructure;

/// <summary>
/// 2.5. A PDF shown in a ProfitDjinn window, drawn by PDFium, without writing a file anywhere.
/// Save PDF and Print run the caller's actions; zoom with the buttons or Ctrl + mouse wheel.
/// </summary>
public sealed class PdfPreviewWindow : Window
{
    private const int Dpi = 150;
    private readonly ScaleTransform _zoom = new(1, 1);
    private readonly TextBlock _zoomText = new() { MinWidth = 48, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _pages = new() { Margin = new Thickness(24) };
    private double _fitWidth = 1;

    /// <summary>Draws the PDF and shows it. <paramref name="save"/> and <paramref name="print"/> are optional buttons.</summary>
    public static void Show(Window owner, byte[] pdf, string title, Action<byte[], Window>? save, Action<byte[], Window>? print)
    {
        List<BitmapSource> pages;
        try { pages = InvoiceOutput.RenderPages(pdf, Dpi); }
        catch (DllNotFoundException ex) { throw new UserFacingException("The PDF preview could not start (pdfium.dll is missing). Save the PDF instead.", ex); }
        new PdfPreviewWindow(owner, pdf, title, pages, save, print).ShowDialog();
    }

    private PdfPreviewWindow(Window owner, byte[] pdf, string title, List<BitmapSource> pages, Action<byte[], Window>? save, Action<byte[], Window>? print)
    {
        Owner = owner;
        Title = $"{title} - Preview";
        Icon = owner.Icon;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var area = SystemParameters.WorkArea;
        Width = Math.Min(980, area.Width * 0.9);
        Height = Math.Min(1100, area.Height * 0.92);
        SetResourceReference(BackgroundProperty, "PageBg");
        SetResourceReference(FontFamilyProperty, "BodyFont");

        foreach (var bitmap in pages)
        {
            var image = new Image { Source = bitmap, Width = bitmap.PixelWidth * 96.0 / Dpi, Height = bitmap.PixelHeight * 96.0 / Dpi, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            _pages.Children.Add(new Border
            {
                Child = image, Margin = new Thickness(0, 0, 0, 20), HorizontalAlignment = HorizontalAlignment.Center, Background = Brushes.White,
                BorderThickness = new Thickness(1),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.25 },
            }.WithResource(Border.BorderBrushProperty, "Border"));
        }
        _pages.LayoutTransform = _zoom;
        _scroll = new ScrollViewer
        {
            Content = _pages, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = true,
        };
        _scroll.PreviewMouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            Zoom(_zoom.ScaleX * (e.Delta > 0 ? 1.1 : 1 / 1.1));
            e.Handled = true;
        };

        // ---- toolbar
        _zoomText.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        var left = Ui.Row(8,
            Ui.IconButton("zoom-out", "Btn.OutlineSecondary", "Zoom out", () => Zoom(_zoom.ScaleX / 1.2)),
            _zoomText,
            Ui.IconButton("zoom-in", "Btn.OutlineSecondary", "Zoom in", () => Zoom(_zoom.ScaleX * 1.2)),
            Ui.Button("Fit Width", "Btn.OutlineSecondary", "arrows-angle-expand", () => Zoom(_fitWidth), small: true),
            Ui.Muted(pages.Count == 1 ? "1 page" : $"{pages.Count} pages", 13).Also(t => { t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(8, 0, 0, 0); }));
        var right = Ui.Row(8);
        if (print is not null) right.Children.Add(Ui.Button("Print", "Btn.OutlineSecondary", "printer", () => Run(() => print(pdf, this))));
        if (save is not null) right.Children.Add(Ui.Button("Save PDF", "Btn.Primary", "download", () => Run(() => save(pdf, this))));
        right.Children.Add(Ui.Button("Close", "Btn.OutlineSecondary", null, Close));
        var bar = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(right);
        bar.Children.Add(left);
        var toolbar = new Border { Child = bar, Padding = new Thickness(16, 10, 16, 10), BorderThickness = new Thickness(0, 0, 0, 1) }
            .WithResource(Border.BackgroundProperty, "Surface").WithResource(Border.BorderBrushProperty, "Border");

        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        root.Children.Add(_scroll);
        Content = root;

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Loaded += (_, _) =>
        {
            DarkTitleBar.Apply(this, ThemeManager.Current != ThemeManager.Light);
            double pageW = pages.Count > 0 ? pages[0].PixelWidth * 96.0 / Dpi : 794;
            _fitWidth = Math.Clamp((_scroll.ViewportWidth - 60) / pageW, 0.3, 3);
            Zoom(_fitWidth);
            _scroll.Focus();
        };
    }

    private void Zoom(double scale)
    {
        scale = Math.Clamp(scale, 0.3, 3);
        _zoom.ScaleX = _zoom.ScaleY = scale;
        _zoomText.Text = $"{Math.Round(scale * 100)}%";
    }

    private void Run(Action action)
    {
        try { action(); }
        catch (UserFacingException ex) { MessageBox.Show(this, ex.Message, "ProfitDjinn", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
