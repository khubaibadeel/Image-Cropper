using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrecisionImageCropper.Models;
using PrecisionImageCropper.Services;
using Xunit;

namespace PrecisionImageCropper.Tests;

public sealed class CropServiceTests
{
    [Fact]
    public void Render_without_transforms_returns_correct_crop_dimensions()
    {
        var bitmap = CreateTestBitmap(100, 80);
        var crop = new CropRect(10, 20, 50, 40);

        var rendered = CropService.Render(bitmap, crop);

        Assert.Equal(50, rendered.PixelWidth);
        Assert.Equal(40, rendered.PixelHeight);
    }

    [Fact]
    public void Render_with_rotation_swaps_dimensions_for_90_and_270()
    {
        var bitmap = CreateTestBitmap(100, 80);
        var crop = new CropRect(0, 0, 100, 80);

        var rot90 = CropService.Render(bitmap, crop, netRotation: 90);
        Assert.Equal(80, rot90.PixelWidth);
        Assert.Equal(100, rot90.PixelHeight);

        var rot180 = CropService.Render(bitmap, crop, netRotation: 180);
        Assert.Equal(100, rot180.PixelWidth);
        Assert.Equal(80, rot180.PixelHeight);

        var rot270 = CropService.Render(bitmap, crop, netRotation: 270);
        Assert.Equal(80, rot270.PixelWidth);
        Assert.Equal(100, rot270.PixelHeight);
    }

    [Fact]
    public void Render_with_horizontal_and_vertical_flip_preserves_dimensions()
    {
        var bitmap = CreateTestBitmap(100, 80);
        var crop = new CropRect(0, 0, 100, 80);

        var flippedH = CropService.Render(bitmap, crop, horizontalFlip: true);
        Assert.Equal(100, flippedH.PixelWidth);
        Assert.Equal(80, flippedH.PixelHeight);

        var flippedV = CropService.Render(bitmap, crop, verticalFlip: true);
        Assert.Equal(100, flippedV.PixelWidth);
        Assert.Equal(80, flippedV.PixelHeight);

        var flippedBoth = CropService.Render(bitmap, crop, horizontalFlip: true, verticalFlip: true);
        Assert.Equal(100, flippedBoth.PixelWidth);
        Assert.Equal(80, flippedBoth.PixelHeight);
    }

    [Fact]
    public void Render_with_flip_and_rotation_matches_batch_item_output_dimensions()
    {
        var bitmap = CreateTestBitmap(200, 100);
        var crop = new CropRect(10, 10, 120, 60);

        var rendered = CropService.Render(bitmap, crop, netRotation: 90, horizontalFlip: true);
        Assert.Equal(60, rendered.PixelWidth);
        Assert.Equal(120, rendered.PixelHeight);
    }

    [Fact]
    public void Render_horizontal_flip_actually_reverses_pixels()
    {
        // 2x1 bitmap: Left pixel is Red, Right pixel is Blue
        var pixels = new byte[] {
            0, 0, 255, 255, // B, G, R, A = Red
            255, 0, 0, 255  // B, G, R, A = Blue
        };
        var bitmap = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 8);
        bitmap.Freeze();

        var rendered = CropService.Render(bitmap, new CropRect(0, 0, 2, 1), horizontalFlip: true);
        var outPixels = new byte[8];
        rendered.CopyPixels(outPixels, 8, 0);

        // After horizontal flip: Left should be Blue, Right should be Red
        Assert.Equal(255, outPixels[0]); // B of left pixel
        Assert.Equal(0, outPixels[2]);   // R of left pixel
        Assert.Equal(0, outPixels[4]);   // B of right pixel
        Assert.Equal(255, outPixels[6]); // R of right pixel
    }

    [Fact]
    public void ImageService_ReadInfo_and_LoadThumbnail_do_not_lock_file()
    {
        var tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"test-image-{Guid.NewGuid():N}.png");
        try
        {
            var bitmap = CreateTestBitmap(400, 200);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var fs = System.IO.File.Create(tempFile))
            {
                encoder.Save(fs);
            }

            var info = ImageService.ReadInfo(tempFile);
            Assert.Equal(400, info.Width);
            Assert.Equal(200, info.Height);

            var thumb = ImageService.LoadThumbnail(tempFile, 360);
            Assert.True(thumb.PixelWidth <= 360);
            Assert.True(thumb.PixelHeight <= 360);

            // Verify file is not locked by opening with exclusive write access
            using var writeCheck = System.IO.File.Open(tempFile, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None);
            Assert.True(writeCheck.CanWrite);
        }
        finally
        {
            if (System.IO.File.Exists(tempFile))
                System.IO.File.Delete(tempFile);
        }
    }

    [Theory]
    [InlineData(@"C:\images\photo.jpg", @"C:\images\photo.jpg", true)]
    [InlineData(@"C:\images\photo.jpg", @"c:\IMAGES\PHOTO.JPG", true)]
    [InlineData(@"C:\images\sub\..\photo.jpg", @"C:\images\photo.jpg", true)]
    [InlineData(@"C:\images\photo.jpg", @"C:\images\photo-copy.jpg", false)]
    [InlineData(@"C:\images\photo.jpg", @"C:\other\photo.jpg", false)]
    [InlineData(null, @"C:\images\photo.jpg", false)]
    [InlineData("", @"C:\images\photo.jpg", false)]
    public void IsOriginalSourcePath_correctly_identifies_same_physical_path(string? originalPath, string outputPath, bool expected)
    {
        Assert.Equal(expected, CropService.IsOriginalSourcePath(originalPath, outputPath));
    }

    [Fact]
    public void Save_blocks_overwriting_original_source_file_and_preserves_content()
    {
        var tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"source-protect-{Guid.NewGuid():N}.png");
        try
        {
            var originalBitmap = CreateTestBitmap(100, 100);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(originalBitmap));
            using (var fs = System.IO.File.Create(tempFile))
            {
                encoder.Save(fs);
            }

            var originalBytes = System.IO.File.ReadAllBytes(tempFile);

            var editedBitmap = CreateTestBitmap(50, 50);
            var crop = new CropRect(0, 0, 50, 50);

            // Attempt to save to the original source path
            var ex = Assert.Throws<InvalidOperationException>(() =>
                CropService.Save(editedBitmap, crop, tempFile, 95, 0, false, false, tempFile));

            Assert.Equal(
                "The original image cannot be overwritten. Please choose a different filename or location.",
                ex.Message);

            // Verify original file was not modified or corrupted
            var currentBytes = System.IO.File.ReadAllBytes(tempFile);
            Assert.Equal(originalBytes, currentBytes);
        }
        finally
        {
            if (System.IO.File.Exists(tempFile))
                System.IO.File.Delete(tempFile);
        }
    }

    [Fact]
    public void Save_succeeds_when_destination_differs_from_source_or_for_clipboard()
    {
        var sourceFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"source-{Guid.NewGuid():N}.png");
        var destFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"dest-{Guid.NewGuid():N}.png");
        try
        {
            var bitmap = CreateTestBitmap(100, 100);
            var crop = new CropRect(0, 0, 50, 50);

            // Saving to different path succeeds
            CropService.Save(bitmap, crop, destFile, 95, 0, false, false, sourceFile);
            Assert.True(System.IO.File.Exists(destFile));

            // Saving clipboard image (originalSourcePath == null) succeeds
            System.IO.File.Delete(destFile);
            CropService.Save(bitmap, crop, destFile, 95, 0, false, false, null);
            Assert.True(System.IO.File.Exists(destFile));
        }
        finally
        {
            if (System.IO.File.Exists(sourceFile)) System.IO.File.Delete(sourceFile);
            if (System.IO.File.Exists(destFile)) System.IO.File.Delete(destFile);
        }
    }

    private static BitmapSource CreateTestBitmap(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 100;     // B
            pixels[i + 1] = 150; // G
            pixels[i + 2] = 200; // R
            pixels[i + 3] = 255; // A
        }
        var bitmap = BitmapSource.Create(
            width, height, 96, 96,
            PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
