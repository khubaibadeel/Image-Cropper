using System.Windows;
using System.Windows.Media.Imaging;
using PrecisionImageCropper.Models;
using PrecisionImageCropper.Utilities;

namespace PrecisionImageCropper.Services;

public static class CropService
{
    public static void Save(BitmapSource source, CropRect crop, string outputPath, int jpegQuality)
    {
        var pixels = CropMath.ToPixelRect(crop, source.PixelWidth, source.PixelHeight);
        var result = new CroppedBitmap(source, pixels);
        result.Freeze();
        BitmapEncoder encoder = Path.GetExtension(outputPath).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = Math.Clamp(jpegQuality, 1, 100) },
            ".bmp" => new BmpBitmapEncoder(),
            ".tif" or ".tiff" => new TiffBitmapEncoder(),
            _ => new PngBitmapEncoder()
        };
        encoder.Frames.Add(BitmapFrame.Create(result));
        using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(output);
    }
}
