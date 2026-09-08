using PrecisionImageCropper.Models;
using PrecisionImageCropper.Utilities;
using Xunit;

namespace PrecisionImageCropper.Tests;

public sealed class CoordinateConverterTests
{
    [Fact]
    public void Converts_display_crop_to_source_pixels_without_drift()
    {
        Assert.Equal(500, CoordinateConverter.DisplayToImageX(100, 1200, 6000));
        Assert.Equal(500, CoordinateConverter.DisplayToImageY(100, 800, 4000));
        Assert.Equal(3000, CoordinateConverter.DisplayToImageX(600, 1200, 6000));
        Assert.Equal(2000, CoordinateConverter.DisplayToImageY(400, 800, 4000));
    }

    [Fact]
    public void Handles_non_integer_display_scaling()
    {
        const double source = 4032;
        const double display = 777;
        var imagePoint = CoordinateConverter.DisplayToImageX(231.25, display, source);
        Assert.Equal(231.25, CoordinateConverter.ImageToDisplayX(imagePoint, display, source), 8);
    }

    [Fact]
    public void Zoom_changes_only_display_geometry()
    {
        var crop = new CropRect(500, 250, 1000, 750);
        var atFit = CoordinateConverter.ImageToDisplay(crop, .2);
        var atTwoHundred = CoordinateConverter.ImageToDisplay(crop, 2);
        Assert.Equal(100, atFit.X); Assert.Equal(50, atFit.Y);
        Assert.Equal(1000, atTwoHundred.X); Assert.Equal(500, atTwoHundred.Y);
        Assert.Equal(500, crop.X); Assert.Equal(250, crop.Y);
        Assert.Equal(1000, crop.Width); Assert.Equal(750, crop.Height);
    }

    [Fact]
    public void Clamp_keeps_crop_inside_all_boundaries()
    {
        var crop = CropMath.Clamp(new CropRect(-20, 700, 500, 500), 1000, 1000);
        Assert.Equal(0, crop.X);
        Assert.Equal(500, crop.Y);
        Assert.Equal(500, crop.Width);
        Assert.Equal(500, crop.Height);
    }

    [Fact]
    public void Move_past_bottom_right_stops_at_boundary()
    {
        var crop = CropMath.Move(new CropRect(800, 850, 200, 150), 50, 50, 1000, 1000);
        Assert.Equal(800, crop.X);
        Assert.Equal(850, crop.Y);
    }

    [Fact]
    public void Ratio_constraint_keeps_ratio_and_bounds()
    {
        var crop = CropMath.ApplyRatio(new CropRect(50, 50, 900, 800), 16d / 9, 1000, 600);
        Assert.InRange(crop.Width / crop.Height, 16d / 9 - .0001, 16d / 9 + .0001);
        Assert.True(crop.X + crop.Width <= 1000);
        Assert.True(crop.Y + crop.Height <= 600);
    }

    [Fact]
    public void Pixel_conversion_is_safe_for_full_image_and_tiny_crop()
    {
        var full = CropMath.ToPixelRect(new CropRect(0, 0, 6000, 4000), 6000, 4000);
        Assert.Equal(6000, full.Width); Assert.Equal(4000, full.Height);
        var tiny = CropMath.ToPixelRect(new CropRect(5999.8, 3999.8, .1, .1), 6000, 4000);
        Assert.Equal(1, tiny.Width); Assert.Equal(1, tiny.Height);
    }
}
