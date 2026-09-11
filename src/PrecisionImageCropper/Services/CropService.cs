using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using PrecisionImageCropper.Models;

namespace PrecisionImageCropper.Services
{
    public static class CropService
    {
        public const string CannotOverwriteOriginalMessage =
            "The original image cannot be overwritten. Please choose a different filename or location.";

        public static bool IsOriginalSourcePath(string? originalFilePath, string outputPath)
        {
            if (string.IsNullOrWhiteSpace(originalFilePath) || string.IsNullOrWhiteSpace(outputPath))
                return false;

            try
            {
                var canonicalOriginal = Path.GetFullPath(originalFilePath);
                var canonicalOutput = Path.GetFullPath(outputPath);

                if (string.Equals(canonicalOriginal, canonicalOutput, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (File.Exists(canonicalOriginal) && File.Exists(canonicalOutput))
                {
                    var origInfo = new FileInfo(canonicalOriginal);
                    var outInfo = new FileInfo(canonicalOutput);
                    if (string.Equals(origInfo.FullName, outInfo.FullName, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch
            {
                if (string.Equals(originalFilePath?.Trim(), outputPath.Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public static void Save(
            BitmapSource source,
            CropRect crop,
            string outputPath,
            int jpegQuality)
        {
            Save(source, crop, outputPath, jpegQuality, 0, false, false, null);
        }

        public static void Save(
            BitmapSource source,
            CropRect crop,
            string outputPath,
            int jpegQuality,
            int netRotation,
            bool horizontalFlip,
            bool verticalFlip)
        {
            Save(source, crop, outputPath, jpegQuality, netRotation, horizontalFlip, verticalFlip, null);
        }

        public static void Save(
            BitmapSource source,
            CropRect crop,
            string outputPath,
            int jpegQuality,
            int netRotation,
            bool horizontalFlip,
            bool verticalFlip,
            string? originalSourcePath)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            if (crop == null)
                throw new ArgumentNullException(nameof(crop));

            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException(
                    "Output path cannot be empty.",
                    nameof(outputPath));

            if (IsOriginalSourcePath(originalSourcePath, outputPath))
                throw new InvalidOperationException(CannotOverwriteOriginalMessage);

            using FileStream output = new FileStream(
                outputPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);
            SaveRendered(Render(source, crop, netRotation, horizontalFlip, verticalFlip), outputPath, jpegQuality, output);
        }

        public static void SaveRendered(BitmapSource rendered, string outputPath, int jpegQuality)
        {
            using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            SaveRendered(rendered, outputPath, jpegQuality, output);
        }

        public static void SaveRendered(BitmapSource rendered, string outputName, int jpegQuality, Stream output)
        {
            ArgumentNullException.ThrowIfNull(rendered);
            ArgumentNullException.ThrowIfNull(output);
            var encoder = CreateEncoder(Path.GetExtension(outputName), jpegQuality);
            encoder.Frames.Add(BitmapFrame.Create(rendered));
            if (output.CanSeek)
            {
                encoder.Save(output);
                return;
            }

            // WPF bitmap encoders require a seekable target, while ZIP entry
            // streams are intentionally forward-only. Buffer one encoded image
            // at a time, then stream it into the archive entry.
            using var encoded = new MemoryStream();
            encoder.Save(encoded);
            encoded.Position = 0;
            encoded.CopyTo(output);
        }

        private static BitmapEncoder CreateEncoder(string extension, int jpegQuality) => extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = Math.Clamp(jpegQuality, 1, 100) },
            ".png" => new PngBitmapEncoder(),
            ".bmp" => new BmpBitmapEncoder(),
            ".tif" or ".tiff" => new TiffBitmapEncoder(),
            _ => throw new NotSupportedException("The output file extension must be JPG, JPEG, PNG, BMP, TIF, or TIFF.")
        };

        /// <summary>
        /// Renders the canonical batch edit order: ImageService first normalizes
        /// EXIF orientation, crop is applied in that upright source-pixel space,
        /// then flips and the net clockwise rotation are applied. Previews and
        /// exports both call this method so they cannot disagree.
        /// </summary>
        public static BitmapSource Render(
            BitmapSource source,
            CropRect crop,
            int netRotation = 0,
            bool horizontalFlip = false,
            bool verticalFlip = false)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            if (crop is null) throw new ArgumentNullException(nameof(crop));

            var sourceWidth = source.PixelWidth;
            var sourceHeight = source.PixelHeight;
            var x = Math.Clamp((int)Math.Round(crop.X), 0, Math.Max(0, sourceWidth - 1));
            var y = Math.Clamp((int)Math.Round(crop.Y), 0, Math.Max(0, sourceHeight - 1));
            var width = Math.Max(1, (int)Math.Round(crop.Width));
            var height = Math.Max(1, (int)Math.Round(crop.Height));
            width = Math.Min(width, sourceWidth - x);
            height = Math.Min(height, sourceHeight - y);
            if (width <= 0 || height <= 0)
                throw new InvalidOperationException("The crop rectangle is outside the image bounds.");

            var cropped = new CroppedBitmap(source, new Int32Rect(x, y, width, height));
            cropped.Freeze();
            var normalizedRotation = ((netRotation % 360) + 360) % 360;
            if (normalizedRotation is not (0 or 90 or 180 or 270))
                throw new ArgumentOutOfRangeException(nameof(netRotation));
            BitmapSource output = cropped;
            if (horizontalFlip || verticalFlip || normalizedRotation != 0)
            {
                var transform = new System.Windows.Media.TransformGroup();
                if (horizontalFlip || verticalFlip)
                    transform.Children.Add(new System.Windows.Media.ScaleTransform(horizontalFlip ? -1 : 1, verticalFlip ? -1 : 1));
                if (normalizedRotation != 0)
                    transform.Children.Add(new System.Windows.Media.RotateTransform(normalizedRotation));
                var rendered = new TransformedBitmap(cropped, transform);
                rendered.Freeze();
                output = rendered;
            }

            var materialized = new WriteableBitmap(output);
            materialized.Freeze();
            return materialized;
        }
    }
}
