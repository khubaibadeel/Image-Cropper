using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrecisionImageCropper.Models;

namespace PrecisionImageCropper.Services;

public sealed record LoadedImage(BitmapSource Source, string FilePath, int Width, int Height);

public static class ImageService
{
    private static readonly string[] Extensions = [".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff"];

    public static bool IsSupportedFile(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static LoadedImage Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("The selected image could not be found.", path);
        if (!IsSupportedFile(path))
            throw new NotSupportedException("This file type is not supported. Choose JPG, PNG, BMP, or TIFF.");

        // OnLoad releases the source file immediately. Orientation is materialized before both
        // preview and final crop use the same, correctly oriented pixel coordinate system.
        using var stream = File.OpenRead(path);
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        var orientation = ReadExifOrientation(frame.Metadata as BitmapMetadata);

        stream.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.StreamSource = stream;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        image.EndInit();
        image.Freeze();
        var oriented = Orient(image, orientation);
        return new LoadedImage(oriented, path, oriented.PixelWidth, oriented.PixelHeight);
    }

    /// <summary>Reads only image headers and EXIF orientation, without retaining decoded pixels.</summary>
    public static ImageFileInfo ReadInfo(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("The selected image could not be found.", path);
        if (!IsSupportedFile(path))
            throw new NotSupportedException("This file type is not supported. Choose JPG, PNG, BMP, or TIFF.");

        using var stream = File.OpenRead(path);
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        var orientation = ReadExifOrientation(frame.Metadata as BitmapMetadata);
        var swapsDimensions = orientation is 5 or 6 or 7 or 8;
        return swapsDimensions
            ? new ImageFileInfo(frame.PixelHeight, frame.PixelWidth)
            : new ImageFileInfo(frame.PixelWidth, frame.PixelHeight);
    }

    /// <summary>Returns a frozen, bounded preview suitable for queue cards.</summary>
    public static BitmapSource LoadThumbnail(string path, int maximumDimension = 360)
    {
        if (maximumDimension < 1) throw new ArgumentOutOfRangeException(nameof(maximumDimension));
        if (!File.Exists(path)) throw new FileNotFoundException("The selected image could not be found.", path);
        if (!IsSupportedFile(path))
            throw new NotSupportedException("This file type is not supported. Choose JPG, PNG, BMP, or TIFF.");

        using var stream = File.OpenRead(path);
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        var rawWidth = frame.PixelWidth;
        var rawHeight = frame.PixelHeight;
        var orientation = ReadExifOrientation(frame.Metadata as BitmapMetadata);

        stream.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.StreamSource = stream;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        if (Math.Max(rawWidth, rawHeight) > maximumDimension)
        {
            if (rawWidth >= rawHeight)
                image.DecodePixelWidth = maximumDimension;
            else
                image.DecodePixelHeight = maximumDimension;
        }
        image.EndInit();
        image.Freeze();

        return Orient(image, orientation);
    }

    public static string SaveClipboardImageToTemporaryFile(BitmapSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var folder = Path.Combine(Path.GetTempPath(), "PrecisionImageCropper");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"clipboard-{Guid.NewGuid():N}.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var output = File.Create(path);
        encoder.Save(output);
        return path;
    }

    public static BitmapSource CopyBitmap(BitmapSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var copy = source.Clone();
        copy.Freeze();
        return copy;
    }

    private static ushort ReadExifOrientation(BitmapMetadata? metadata)
    {
        try
        {
            if (metadata?.GetQuery("/app1/ifd/{ushort=274}") is not null)
            {
                return Convert.ToUInt16(metadata.GetQuery("/app1/ifd/{ushort=274}"));
            }
        }
        catch { /* WPF will provide the user-facing decode error if the file is corrupt. */ }
        return 1;
    }

    private static ushort ReadExifOrientation(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            return ReadExifOrientation(frame.Metadata as BitmapMetadata);
        }
        catch { /* WPF will provide the user-facing decode error if the file is corrupt. */ }
        return 1;
    }

    private static BitmapSource Orient(BitmapSource source, ushort orientation)
    {
        if (orientation is < 2 or > 8) return source;
        var width = source.PixelWidth;
        var height = source.PixelHeight;
        var sourceStride = width * 4;
        var sourcePixels = new byte[sourceStride * height];
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.CopyPixels(sourcePixels, sourceStride, 0);
        var swapDimensions = orientation is 5 or 6 or 7 or 8;
        var outputWidth = swapDimensions ? height : width;
        var outputHeight = swapDimensions ? width : height;
        var outputStride = outputWidth * 4;
        var outputPixels = new byte[outputStride * outputHeight];
        for (var y = 0; y < outputHeight; y++)
        for (var x = 0; x < outputWidth; x++)
        {
            var (sx, sy) = orientation switch
            {
                2 => (width - 1 - x, y),
                3 => (width - 1 - x, height - 1 - y),
                4 => (x, height - 1 - y),
                5 => (y, x),
                6 => (y, height - 1 - x),
                7 => (width - 1 - y, height - 1 - x),
                8 => (width - 1 - y, x),
                _ => (x, y)
            };
            Buffer.BlockCopy(sourcePixels, sy * sourceStride + sx * 4, outputPixels, y * outputStride + x * 4, 4);
        }
        var result = new WriteableBitmap(outputWidth, outputHeight, source.DpiX, source.DpiY, PixelFormats.Bgra32, null);
        result.WritePixels(new System.Windows.Int32Rect(0, 0, outputWidth, outputHeight), outputPixels, outputStride, 0);
        result.Freeze();
        return result;
    }
}
