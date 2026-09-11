using System.IO;
using System.IO.Compression;
using System.Windows.Media.Imaging;
using PrecisionImageCropper.Models;

namespace PrecisionImageCropper.Services;

/// <summary>
/// The single full-resolution rendering gateway for a batch item. CropService
/// owns the pixel algorithm; this service ensures save, copy, and ZIP export
/// all apply the exact same item recipe.
/// </summary>
public static class ImageRenderService
{
    public static BitmapSource RenderFinal(BatchImageItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var source = ImageService.Load(item.SourceDataPath).Source;
        return CropService.Render(source, item.CropRectangle, item.NetRotation, item.HorizontalFlip, item.VerticalFlip);
    }

    public static void SaveFinal(BatchImageItem item, string outputPath, int jpegQuality = 95)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (CropService.IsOriginalSourcePath(item.OriginalFilePath, outputPath))
            throw new InvalidOperationException(CropService.CannotOverwriteOriginalMessage);

        if (CanCopyOriginal(item, outputPath))
        {
            File.Copy(item.SourceDataPath, outputPath, true);
            return;
        }

        var image = RenderFinal(item);
        CropService.SaveRendered(image, outputPath, jpegQuality);
    }

    internal static void WriteZipEntry(BatchImageItem item, ZipArchive archive, string entryName, int jpegQuality = 95)
    {
        ArgumentNullException.ThrowIfNull(item);
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var destination = entry.Open();
        if (CanCopyOriginal(item, entryName))
        {
            using var source = File.OpenRead(item.SourceDataPath);
            source.CopyTo(destination);
            return;
        }

        var image = RenderFinal(item);
        CropService.SaveRendered(image, entryName, jpegQuality, destination);
    }

    private static bool CanCopyOriginal(BatchImageItem item, string outputName)
    {
        return !item.HasUnsavedChanges &&
               File.Exists(item.SourceDataPath) &&
               string.Equals(Path.GetExtension(item.SourceDataPath), Path.GetExtension(outputName), StringComparison.OrdinalIgnoreCase);
    }
}
