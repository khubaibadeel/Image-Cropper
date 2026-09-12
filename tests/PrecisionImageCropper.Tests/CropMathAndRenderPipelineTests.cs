using PrecisionImageCropper.Models;
using PrecisionImageCropper.Services;
using PrecisionImageCropper.Utilities;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace PrecisionImageCropper.Tests;

/// <summary>
/// Focused audit tests for crop boundary math, zero dimensions, rotation
/// normalization, flip cancellation, and render-pipeline consistency
/// (audit sections B, C, H, I, J).
/// </summary>
public sealed class CropMathAndRenderPipelineTests
{
    // ─── Section B: Independent BatchImageItem state ──────────────────────

    [Fact]
    public void Mutating_one_item_crop_does_not_affect_another()
    {
        var a = MakeItem("a.png", 800, 600);
        var b = MakeItem("b.png", 800, 600);

        a.CropRectangle = new CropRect(10, 10, 200, 150);

        // b must still have its original full-image crop
        var bCrop = b.CropRectangle;
        Assert.Equal(0, bCrop.X);
        Assert.Equal(0, bCrop.Y);
        Assert.Equal(800, bCrop.Width);
        Assert.Equal(600, bCrop.Height);
        Assert.False(b.HasCrop);
    }

    [Fact]
    public void Mutating_one_item_rotation_does_not_affect_another()
    {
        var a = MakeItem("a.jpg", 400, 300);
        var b = MakeItem("b.jpg", 400, 300);

        a.NetRotation = 90;

        Assert.Equal(0, b.NetRotation);
        Assert.False(b.HasRotation);
    }

    [Fact]
    public void Mutating_one_item_flips_does_not_affect_another()
    {
        var a = MakeItem("a.jpg", 400, 300);
        var b = MakeItem("b.jpg", 400, 300);

        a.HorizontalFlip = true;
        a.VerticalFlip = true;

        Assert.False(b.HorizontalFlip);
        Assert.False(b.VerticalFlip);
    }

    // ─── CropRect Clone isolation ─────────────────────────────────────────

    [Fact]
    public void CropRectangle_getter_returns_a_clone_not_a_reference()
    {
        var item = MakeItem("img.png", 400, 300);
        item.CropRectangle = new CropRect(10, 10, 200, 150);

        var got = item.CropRectangle;
        got.X = 999; // mutate the returned clone

        // Internal state must not have changed
        Assert.Equal(10, item.CropRectangle.X);
    }

    // ─── Section H: Crop boundary math ───────────────────────────────────

    [Fact]
    public void Clamp_brings_out_of_bounds_crop_inside_image()
    {
        var raw = new CropRect(-50, -20, 700, 500);
        var clamped = CropMath.Clamp(raw, 400, 300);
        Assert.Equal(0, clamped.X);
        Assert.Equal(0, clamped.Y);
        Assert.Equal(400, clamped.Width);
        Assert.Equal(300, clamped.Height);
    }

    [Fact]
    public void Clamp_right_edge_overflow_is_corrected()
    {
        var raw = new CropRect(350, 250, 300, 300); // right=650 > 400, bottom=550 > 300
        var clamped = CropMath.Clamp(raw, 400, 300);
        Assert.True(clamped.X + clamped.Width <= 400);
        Assert.True(clamped.Y + clamped.Height <= 300);
        Assert.True(clamped.Width >= CropMath.MinSize);
        Assert.True(clamped.Height >= CropMath.MinSize);
    }

    [Fact]
    public void Full_image_crop_is_not_flagged_as_has_crop()
    {
        var item = MakeItem("img.png", 300, 200);
        // Default after construction is full-image
        Assert.False(item.HasCrop);
    }

    [Fact]
    public void Very_small_image_clamp_keeps_minimum_size()
    {
        var tiny = new CropRect(0, 0, 5, 5); // smaller than MinSize=10
        var clamped = CropMath.Clamp(tiny, 8, 8, CropMath.MinSize);
        // MinSize reduced to image size when image < MinSize
        Assert.True(clamped.Width >= Math.Min(CropMath.MinSize, 8));
        Assert.True(clamped.Height >= Math.Min(CropMath.MinSize, 8));
    }

    [Fact]
    public void Zero_dimension_image_in_clamping_does_not_throw()
    {
        // Should not throw for zero-dimension images
        var ex = Record.Exception(() => CropMath.Clamp(new CropRect(0, 0, 10, 10), 0, 0));
        Assert.Null(ex);
    }

    // ─── Section I: Rotation normalization ───────────────────────────────

    [Theory]
    [InlineData(  90,  90)]
    [InlineData( 180, 180)]
    [InlineData( 270, 270)]
    [InlineData( 360,   0)]
    [InlineData(-90,  270)]
    [InlineData(450,   90)]
    public void NetRotation_normalizes_to_canonical_value(int input, int expected)
    {
        var item = MakeItem("img.png", 100, 100);
        item.NetRotation = input;
        Assert.Equal(expected, item.NetRotation);
    }

    [Fact]
    public void Four_clockwise_rotations_return_to_zero()
    {
        var item = MakeItem("img.png", 100, 100);
        for (var i = 0; i < 4; i++)
            item.NetRotation = item.NetRotation + 90;
        Assert.Equal(0, item.NetRotation);
        Assert.False(item.HasRotation);
    }

    // ─── Section I: Flip cancellation ────────────────────────────────────

    [Fact]
    public void Two_horizontal_flips_cancel_out()
    {
        var item = MakeItem("img.png", 100, 100);
        item.HorizontalFlip = true;
        item.HorizontalFlip = false; // caller cancels by toggling
        Assert.False(item.HorizontalFlip);
        Assert.False(item.HasHorizontalFlip);
    }

    [Fact]
    public void Two_vertical_flips_cancel_out()
    {
        var item = MakeItem("img.png", 100, 100);
        item.VerticalFlip = true;
        item.VerticalFlip = false;
        Assert.False(item.VerticalFlip);
        Assert.False(item.HasVerticalFlip);
    }

    // ─── Section J: Transform pipeline consistency ───────────────────────
    // Both preview and export call CropService.Render with the same arguments,
    // so we verify the contract: crop first, then flip, then rotate.

    [Fact]
    public void Render_crop_only_has_correct_dimensions()
    {
        var bitmap = MakeBitmap(100, 80);
        var rendered = CropService.Render(bitmap, new CropRect(10, 10, 60, 40));
        Assert.Equal(60, rendered.PixelWidth);
        Assert.Equal(40, rendered.PixelHeight);
    }

    [Theory]
    [InlineData( 90, 40, 60)]
    [InlineData(180, 60, 40)]
    [InlineData(270, 40, 60)]
    [InlineData(  0, 60, 40)]
    public void Render_crop_plus_rotation_swaps_dimensions_for_90_and_270(
        int rotation, int expectedWidth, int expectedHeight)
    {
        var bitmap = MakeBitmap(100, 80);
        var rendered = CropService.Render(bitmap, new CropRect(10, 10, 60, 40), rotation);
        Assert.Equal(expectedWidth, rendered.PixelWidth);
        Assert.Equal(expectedHeight, rendered.PixelHeight);
    }

    [Fact]
    public void Render_crop_plus_flip_preserves_crop_dimensions()
    {
        var bitmap = MakeBitmap(100, 80);
        var rendered = CropService.Render(bitmap, new CropRect(10, 10, 60, 40), 0, true, false);
        Assert.Equal(60, rendered.PixelWidth);
        Assert.Equal(40, rendered.PixelHeight);
    }

    [Fact]
    public void Render_crop_plus_rotate_plus_both_flips_output_dimensions_are_correct()
    {
        var bitmap = MakeBitmap(200, 100);
        // crop = 120×60, rotate 90° → 60×120, flips don't change dims
        var rendered = CropService.Render(bitmap, new CropRect(0, 0, 120, 60), 90, true, true);
        Assert.Equal(60, rendered.PixelWidth);
        Assert.Equal(120, rendered.PixelHeight);
    }

    // ─── BatchImageItem.OutputWidth / OutputHeight match CropService dims ─

    [Theory]
    [InlineData( 90)]
    [InlineData(270)]
    public void BatchItem_output_dimensions_match_CropService_render_dimensions_for_rotated(int rotation)
    {
        var item = MakeItem("img.png", 200, 100);
        item.CropRectangle = new CropRect(10, 10, 120, 60); // cropped region
        item.NetRotation = rotation;

        var bitmap = MakeBitmap(200, 100);
        var rendered = CropService.Render(bitmap, item.CropRectangle, item.NetRotation);
        Assert.Equal(rendered.PixelWidth, item.OutputWidth);
        Assert.Equal(rendered.PixelHeight, item.OutputHeight);
    }

    [Fact]
    public void BatchItem_output_dimensions_match_CropService_render_dimensions_without_rotation()
    {
        var item = MakeItem("img.png", 300, 200);
        item.CropRectangle = new CropRect(50, 30, 150, 100);
        item.NetRotation = 0;

        var bitmap = MakeBitmap(300, 200);
        var rendered = CropService.Render(bitmap, item.CropRectangle, item.NetRotation);
        Assert.Equal(rendered.PixelWidth, item.OutputWidth);
        Assert.Equal(rendered.PixelHeight, item.OutputHeight);
    }

    // ─── Section G: Source overwrite prevention ───────────────────────────

    [Fact]
    public void IsOriginalSourcePath_is_case_insensitive_on_windows()
    {
        Assert.True(CropService.IsOriginalSourcePath(
            @"C:\Photos\image.jpg",
            @"c:\photos\IMAGE.JPG"));
    }

    [Fact]
    public void IsOriginalSourcePath_returns_false_for_null_original()
    {
        // Clipboard images have no protected source path
        Assert.False(CropService.IsOriginalSourcePath(null, @"C:\Photos\output.jpg"));
    }

    [Fact]
    public void IsOriginalSourcePath_returns_false_for_empty_original()
    {
        Assert.False(CropService.IsOriginalSourcePath("", @"C:\Photos\output.jpg"));
    }

    [Fact]
    public void IsOriginalSourcePath_normalizes_relative_segments()
    {
        Assert.True(CropService.IsOriginalSourcePath(
            @"C:\Photos\sub\..\image.jpg",
            @"C:\Photos\image.jpg"));
    }

    // ─── Section E: Duplicate path detection (case-insensitive) ──────────

    [Fact]
    public void Duplicate_path_detection_is_case_insensitive()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.True(set.Add(@"E:\Photos\image.jpg"));
        Assert.False(set.Add(@"e:\photos\IMAGE.jpg"));
    }

    // ─── Section N/O: ZIP unique entry names ──────────────────────────────

    [Fact]
    public void GetUniqueEntryName_returns_original_when_no_collision()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = BatchZipExportService.GetUniqueEntryName("photo.jpg", used);
        Assert.Equal("photo.jpg", result);
    }

    [Fact]
    public void GetUniqueEntryName_adds_numeric_suffix_on_collision()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        BatchZipExportService.GetUniqueEntryName("photo.jpg", used); // registers "photo.jpg"

        var result = BatchZipExportService.GetUniqueEntryName("photo.jpg", used);
        Assert.Equal("photo-2.jpg", result);
    }

    [Fact]
    public void GetUniqueEntryName_increments_suffix_for_further_collisions()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        BatchZipExportService.GetUniqueEntryName("photo.jpg", used);
        BatchZipExportService.GetUniqueEntryName("photo.jpg", used);

        var result = BatchZipExportService.GetUniqueEntryName("photo.jpg", used);
        Assert.Equal("photo-3.jpg", result);
    }

    [Fact]
    public void GetUniqueEntryName_strips_directory_traversal_components()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Path.GetFileName removes leading directory components
        var result = BatchZipExportService.GetUniqueEntryName(@"..\etc\passwd", used);
        Assert.DoesNotContain("..", result);
        Assert.DoesNotContain("/", result);
        Assert.DoesNotContain("\\", result);
    }

    // ─── Recent files limits ──────────────────────────────────────────────

    [Fact]
    public void Recent_files_capped_at_10()
    {
        var settingsPath = TempSettingsPath();
        var svc = new SettingsService(settingsPath);
        for (var i = 1; i <= 15; i++)
            svc.RecordOpenedFile($@"C:\img{i}.png");
        Assert.Equal(10, svc.RecentFiles.Count);
    }

    [Fact]
    public void Recent_folders_capped_at_5()
    {
        var settingsPath = TempSettingsPath();
        var svc = new SettingsService(settingsPath);
        for (var i = 1; i <= 8; i++)
            svc.RecordFolder($@"C:\folder{i}");
        Assert.Equal(5, svc.RecentFolders.Count);
    }

    [Fact]
    public void Recent_files_deduplication_moves_to_front()
    {
        var settingsPath = TempSettingsPath();
        var svc = new SettingsService(settingsPath);
        svc.RecordOpenedFile(@"C:\a.png");
        svc.RecordOpenedFile(@"C:\b.png");
        svc.RecordOpenedFile(@"C:\a.png"); // re-add a

        Assert.Equal(@"C:\a.png", svc.RecentFiles[0]);
        Assert.Equal(2, svc.RecentFiles.Count); // no duplicates
    }

    [Fact]
    public void Recent_files_newest_is_first()
    {
        var settingsPath = TempSettingsPath();
        var svc = new SettingsService(settingsPath);
        svc.RecordOpenedFile(@"C:\first.png");
        svc.RecordOpenedFile(@"C:\second.png");
        Assert.Equal(@"C:\second.png", svc.RecentFiles[0]);
    }

    [Fact]
    public void After_remove_same_file_can_be_re_added()
    {
        var settingsPath = TempSettingsPath();
        var svc = new SettingsService(settingsPath);
        svc.RecordOpenedFile(@"C:\img.png");
        svc.RemoveRecentFile(@"C:\img.png");
        Assert.Empty(svc.RecentFiles);

        // Re-add should succeed
        svc.RecordOpenedFile(@"C:\img.png");
        Assert.Single(svc.RecentFiles);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static BatchImageItem MakeItem(string name, int w, int h) =>
        new(name, name, name, System.IO.Path.GetExtension(name), w, h, Models.ImageImportSource.File);

    private static BitmapSource MakeBitmap(int w, int h)
    {
        var pixels = new byte[w * h * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 80; pixels[i + 1] = 120; pixels[i + 2] = 200; pixels[i + 3] = 255; }
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
        bmp.Freeze();
        return bmp;
    }

    private static string TempSettingsPath()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"PrecisionAuditTests-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(dir);
        return System.IO.Path.Combine(dir, "settings.json");
    }
}
