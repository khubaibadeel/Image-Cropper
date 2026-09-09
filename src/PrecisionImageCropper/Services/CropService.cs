using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using PrecisionImageCropper.Models;

namespace PrecisionImageCropper.Services
{
    public static class CropService
    {
        public static void Save(
            BitmapSource source,
            CropRect crop,
            string outputPath,
            int jpegQuality)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            if (crop == null)
                throw new ArgumentNullException(nameof(crop));

            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException(
                    "Output path cannot be empty.",
                    nameof(outputPath));

            int sourceWidth = source.PixelWidth;
            int sourceHeight = source.PixelHeight;

            // Convert crop coordinates to integer source-image pixels.
            int x = (int)Math.Round(crop.X);
            int y = (int)Math.Round(crop.Y);
            int width = (int)Math.Round(crop.Width);
            int height = (int)Math.Round(crop.Height);

            // Keep X and Y inside the source image.
            x = Math.Clamp(x, 0, Math.Max(0, sourceWidth - 1));
            y = Math.Clamp(y, 0, Math.Max(0, sourceHeight - 1));

            // Minimum crop size = 1 pixel.
            width = Math.Max(1, width);
            height = Math.Max(1, height);

            // Prevent crop from extending outside image.
            if (x + width > sourceWidth)
            {
                width = sourceWidth - x;
            }

            if (y + height > sourceHeight)
            {
                height = sourceHeight - y;
            }

            if (width <= 0 || height <= 0)
            {
                throw new InvalidOperationException(
                    "The crop rectangle is outside the image bounds.");
            }

            var pixelRect = new Int32Rect(
                x,
                y,
                width,
                height);

            var croppedBitmap = new CroppedBitmap(
                source,
                pixelRect);

            croppedBitmap.Freeze();

            string extension =
                Path.GetExtension(outputPath).ToLowerInvariant();

            BitmapEncoder encoder;

            switch (extension)
            {
                case ".jpg":
                case ".jpeg":
                    encoder = new JpegBitmapEncoder
                    {
                        QualityLevel = Math.Clamp(jpegQuality, 1, 100)
                    };
                    break;

                case ".png":
                    encoder = new PngBitmapEncoder();
                    break;

                case ".bmp":
                    encoder = new BmpBitmapEncoder();
                    break;

                case ".tif":
                case ".tiff":
                    encoder = new TiffBitmapEncoder();
                    break;

                default:
                    throw new NotSupportedException(
                        "The output file extension must be JPG, JPEG, PNG, BMP, TIF, or TIFF.");
            }

            encoder.Frames.Add(
                BitmapFrame.Create(croppedBitmap));

            using FileStream output = new FileStream(
                outputPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);

            encoder.Save(output);
        }
    }
}
