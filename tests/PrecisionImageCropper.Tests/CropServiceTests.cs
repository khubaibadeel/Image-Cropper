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
