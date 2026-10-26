using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ProfitDjinn.Core;

namespace ProfitDjinn.App.Infrastructure;

/// <summary>
/// 2.9. Turns the image chosen as the invoice / purchase order logo into a modest RGB PNG.
/// Logos arrive as print files: one real logo is a 14,550 x 6,900 CMYK JPEG (13 MB), which the
/// PDF library cannot use as it is and would bloat every PDF. WPF decodes it straight to the
/// smaller size (a JPEG is never fully expanded) and converts the colours, so any PNG or JPEG
/// up to 50 MB works.
/// </summary>
public static class LogoImage
{
    /// <summary>The longest side of the saved logo. A 66 mm wide logo at 1,600 px is about 600 dpi.</summary>
    public const int MaxSide = 1600;

    private const long MaxBytes = 50L * 1024 * 1024;

    public static byte[] ToPng(string file)
    {
        var info = new FileInfo(file);
        string ext = info.Extension.ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg")) throw new UserFacingException("Choose a PNG or JPEG image for the logo.");
        if (!info.Exists) throw new UserFacingException($"{info.Name} no longer exists.");
        if (info.Length > MaxBytes) throw new UserFacingException("That image is larger than 50 MB. Save a smaller copy and try again.");

        try
        {
            int width, height;
            using (var stream = File.OpenRead(info.FullName))
            {
                var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
                width = frame.PixelWidth;
                height = frame.PixelHeight;
            }
            if (width < 1 || height < 1) throw new UserFacingException("That image is empty.");

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.UriSource = new Uri(info.FullName);
            double scale = Math.Min(1.0, (double)MaxSide / Math.Max(width, height));
            if (scale < 1.0)
            {
                // Setting one side keeps the shape.
                if (width >= height) image.DecodePixelWidth = (int)Math.Round(width * scale);
                else image.DecodePixelHeight = (int)Math.Round(height * scale);
            }
            image.EndInit();

            var rgb = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rgb));
            using var output = new MemoryStream();
            encoder.Save(output);
            return output.ToArray();
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException or ArgumentException or InvalidOperationException)
        {
            throw new UserFacingException($"{info.Name} could not be read as an image ({ex.Message}). Try saving it again as a PNG.", ex);
        }
    }
}
