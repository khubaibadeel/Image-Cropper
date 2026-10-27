using PrecisionImageCropper.Services;
using Xunit;

namespace PrecisionImageCropper.Tests;

/// <summary>
/// Covers audit section L (output filename generation).
/// </summary>
public sealed class OutputFileNameServiceTests
{
    // ─── Extension preservation ─────────────────────────────────────────────

    [Theory]
    [InlineData("photo", ".jpg",  false, 0, false, false, "photo.jpg")]
    [InlineData("photo", ".jpeg", false, 0, false, false, "photo.jpeg")]
    [InlineData("photo", ".png",  false, 0, false, false, "photo.png")]
    [InlineData("photo", ".bmp",  false, 0, false, false, "photo.bmp")]
    [InlineData("photo", ".tif",  false, 0, false, false, "photo.tif")]
    [InlineData("photo", ".tiff", false, 0, false, false, "photo.tiff")]
    [InlineData("photo", ".PNG",  false, 0, false, false, "photo.PNG")]
    public void Extension_is_preserved_verbatim_when_unedited(
        string stem, string extension,
        bool hasCrop, int rotation, bool h, bool v,
        string expected)
    {
        var result = OutputFileNameService.Create(stem + extension, extension, hasCrop, rotation, h, v);
        Assert.Equal(expected, result);
    }

    // ─── Crop descriptor ─────────────────────────────────────────────────────

    [Fact]
    public void Crop_adds_cropped_suffix()
    {
        var result = OutputFileNameService.Create("image.png", ".png", hasCrop: true, 0, false, false);
        Assert.Equal("image-cropped.png", result);
    }

    // ─── Rotation descriptors ────────────────────────────────────────────────

    [Theory]
    [InlineData( 90, "image-rotated-clockwise.png")]
    [InlineData(180, "image-rotated-180.png")]
    [InlineData(270, "image-rotated-counterclockwise.png")]
    [InlineData(  0, "image.png")]
    public void Rotation_produces_expected_suffix(int rotation, string expected)
    {
        var result = OutputFileNameService.Create("image.png", ".png", false, rotation, false, false);
        Assert.Equal(expected, result);
    }

    // ─── Flip descriptors ────────────────────────────────────────────────────

    [Fact]
    public void Horizontal_flip_adds_flipped_horizontal_suffix()
    {
        var result = OutputFileNameService.Create("shot.jpg", ".jpg", false, 0, true, false);
        Assert.Equal("shot-flipped-horizontal.jpg", result);
    }

    [Fact]
    public void Vertical_flip_adds_flipped_vertical_suffix()
    {
        var result = OutputFileNameService.Create("shot.jpg", ".jpg", false, 0, false, true);
        Assert.Equal("shot-flipped-vertical.jpg", result);
    }

    // ─── Combined descriptors — ordering must be deterministic ───────────────

    [Fact]
    public void Combined_descriptors_follow_crop_rotation_flip_order()
    {
        // crop + CCW + horizontal flip
        var result = OutputFileNameService.Create(
            "image.png", ".png", hasCrop: true, netRotation: 270, horizontalFlip: true, verticalFlip: false);
        Assert.Equal("image-cropped-rotated-counterclockwise-flipped-horizontal.png", result);
    }

    [Fact]
    public void All_four_transforms_combined_order_is_correct()
    {
        var result = OutputFileNameService.Create(
            "scan.tiff", ".tiff", hasCrop: true, netRotation: 90, horizontalFlip: true, verticalFlip: true);
        Assert.Equal("scan-cropped-rotated-clockwise-flipped-horizontal-flipped-vertical.tiff", result);
    }

    // ─── Net-state semantics: cancelled-out operations ───────────────────────

    [Fact]
    public void Zero_rotation_produces_no_rotation_suffix()
    {
        // Four CW rotations cancel → net 0 → no suffix
        var result = OutputFileNameService.Create("image.png", ".png", false, 0, false, false);
        Assert.Equal("image.png", result);
    }

    [Fact]
    public void Both_flips_together_still_append_both_descriptors_when_both_true()
    {
        // Both flip flags true simultaneously → both suffixes present
        var result = OutputFileNameService.Create("img.png", ".png", false, 0, true, true);
        Assert.Equal("img-flipped-horizontal-flipped-vertical.png", result);
    }

    [Fact]
    public void No_flip_flag_set_produces_no_flip_suffix()
    {
        // Represents two horizontal flips cancelled out: caller passes horizontalFlip=false
        var result = OutputFileNameService.Create("img.png", ".png", false, 0, false, false);
        Assert.Equal("img.png", result);
    }

    // ─── Stem edge cases ─────────────────────────────────────────────────────

    [Fact]
    public void Multi_dot_filename_preserves_full_stem()
    {
        var result = OutputFileNameService.Create("summer.photo.tiff", ".tiff", true, 0, false, false);
        Assert.Equal("summer.photo-cropped.tiff", result);
    }

    [Fact]
    public void Empty_stem_falls_back_to_image()
    {
        // originalFileName with no stem part
        var result = OutputFileNameService.Create(".png", ".png", false, 0, false, false);
        Assert.Equal("image.png", result);
    }

    // ─── Extension normalization ──────────────────────────────────────────────

    [Fact]
    public void Extension_without_leading_dot_is_normalized()
    {
        var result = OutputFileNameService.Create("image.jpg", "jpg", false, 0, false, false);
        Assert.Equal("image.jpg", result);
    }

    [Fact]
    public void Raw_clipboard_image_defaults_to_png()
    {
        // Clipboard items are created with originalExtension ".png" by the importer
        var result = OutputFileNameService.Create("Clipboard-1.png", ".png", false, 0, false, false);
        Assert.Equal("Clipboard-1.png", result);
    }
}
