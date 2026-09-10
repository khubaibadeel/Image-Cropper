using System.IO;
using Microsoft.Win32;

namespace PrecisionImageCropper.Services;

public static class FileDialogService
{
    public static string[]? OpenImages(string? initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open Images",
            InitialDirectory = initialDirectory,
            Multiselect = true,
            Filter = "Image files|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff|JPEG image|*.jpg;*.jpeg|PNG image|*.png|Bitmap image|*.bmp|TIFF image|*.tif;*.tiff"
        };

        return dialog.ShowDialog() == true ? dialog.FileNames : null;
    }

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

    public static string? SaveImage(string suggestedFileName, string? preferredExtension)
    {
        var normalizedExtension = NormalizeExtension(preferredExtension);
        var dialog = new SaveFileDialog
        {
            Title = "Save Cropped Image",
            FileName = suggestedFileName,
            DefaultExt = normalizedExtension,
            AddExtension = true,
            OverwritePrompt = true,
            Filter = "JPEG Image (*.jpg;*.jpeg)|*.jpg;*.jpeg|PNG Image (*.png)|*.png|Bitmap Image (*.bmp)|*.bmp|TIFF Image (*.tif;*.tiff)|*.tif;*.tiff",
            FilterIndex = GetFilterIndex(normalizedExtension)
        };
        if (dialog.ShowDialog() != true) return null;

        // If the user only changes the file-type selector, make the saved
        // extension follow that selection. An explicitly typed extension is
        // left untouched and CropService selects its encoder from that path.
        var selectedExtension = GetPrimaryExtension(dialog.FilterIndex);
        if (string.Equals(Path.GetExtension(dialog.FileName), normalizedExtension, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(selectedExtension, normalizedExtension, StringComparison.OrdinalIgnoreCase))
        {
            return Path.ChangeExtension(dialog.FileName, selectedExtension);
        }

        return dialog.FileName;
    }

    private static string NormalizeExtension(string? extension)
    {
        var normalized = extension?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized)) return ".png";
        if (!normalized.StartsWith('.')) normalized = "." + normalized;

        return normalized switch
        {
            ".jpg" or ".jpeg" or ".png" or ".bmp" or ".tif" or ".tiff" => normalized,
            _ => ".png"
        };
    }

    private static int GetFilterIndex(string extension) => extension switch
    {
        ".jpg" or ".jpeg" => 1,
        ".png" => 2,
        ".bmp" => 3,
        ".tif" or ".tiff" => 4,
        _ => 2
    };

    private static string GetPrimaryExtension(int filterIndex) => filterIndex switch
    {
        1 => ".jpg",
        2 => ".png",
        3 => ".bmp",
        4 => ".tif",
        _ => ".png"
    };
}
