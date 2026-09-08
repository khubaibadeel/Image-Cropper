using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PrecisionImageCropper.Services;

public sealed record LoadedImage(BitmapSource Source, string FilePath, int Width, int Height);

public static class ImageService
{
    private static readonly string[] Extensions = [".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff"];

    public static LoadedImage Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("The selected image could not be found.", path);
        if (!Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            throw new NotSupportedException("This file type is not supported. Choose JPG, PNG, BMP, or TIFF.");

        // OnLoad releases the source file immediately; Rotation applies the common camera EXIF orientations
        // before both preview and final crop use the same bitmap.
        var rotation = ReadExifRotation(path);
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        image.Rotation = rotation;
        image.EndInit();
        image.Freeze();
        return new LoadedImage(image, path, image.PixelWidth, image.PixelHeight);
    }

    private static Rotation ReadExifRotation(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if (frame.Metadata is BitmapMetadata metadata && metadata.GetQuery("/app1/ifd/{ushort=274}") is not null)
            {
                return Convert.ToUInt16(metadata.GetQuery("/app1/ifd/{ushort=274}")) switch
                {
                    3 => Rotation.Rotate180,
                    6 => Rotation.Rotate90,
                    8 => Rotation.Rotate270,
                    _ => Rotation.Rotate0
                };
            }
        }
        catch { /* WPF will provide the user-facing decode error if the file is corrupt. */ }
        return Rotation.Rotate0;
    }
}
