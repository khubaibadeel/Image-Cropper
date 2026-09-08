using Microsoft.Win32;

namespace PrecisionImageCropper.Services;

public static class FileDialogService
{
    public static string? OpenImage(string? initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open Image",
            InitialDirectory = initialDirectory,
            Filter = "Image files|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff|JPEG image|*.jpg;*.jpeg|PNG image|*.png|Bitmap image|*.bmp|TIFF image|*.tif;*.tiff"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public static string? SaveImage(string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save Cropped Image",
            FileName = suggestedName,
            DefaultExt = ".png",
            AddExtension = true,
            Filter = "PNG image|*.png|JPEG image|*.jpg;*.jpeg|Bitmap image|*.bmp|TIFF image|*.tif;*.tiff"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
