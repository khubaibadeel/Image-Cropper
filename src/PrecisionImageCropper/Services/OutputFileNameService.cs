using System.IO;

namespace PrecisionImageCropper.Services;

/// <summary>Creates stable, human-readable output names from an image edit recipe.</summary>
public static class OutputFileNameService
{
    public static string Create(
        string originalFileName,
        string originalExtension,
        bool hasCrop,
        int netRotation,
        bool horizontalFlip,
        bool verticalFlip)
    {
        var stem = Path.GetFileNameWithoutExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(stem))
            stem = "image";

        var extension = string.IsNullOrWhiteSpace(originalExtension)
            ? Path.GetExtension(originalFileName)
            : originalExtension;
        if (!string.IsNullOrWhiteSpace(extension) && !extension.StartsWith('.'))
            extension = "." + extension;

        var suffixes = new List<string>();

        if (hasCrop)
            suffixes.Add("cropped");

        switch (((netRotation % 360) + 360) % 360)
        {
            case 90: suffixes.Add("rotated-clockwise"); break;
            case 180: suffixes.Add("rotated-180"); break;
            case 270: suffixes.Add("rotated-counterclockwise"); break;
        }

        if (horizontalFlip)
            suffixes.Add("flipped-horizontal");
        if (verticalFlip)
            suffixes.Add("flipped-vertical");

        return suffixes.Count == 0
            ? $"{stem}{extension}"
            : $"{stem}-{string.Join("-", suffixes)}{extension}";
    }
}
