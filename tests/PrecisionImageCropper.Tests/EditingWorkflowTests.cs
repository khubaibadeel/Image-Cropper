using PrecisionImageCropper.Models;
using PrecisionImageCropper.Services;
using PrecisionImageCropper.Utilities;
using System.IO;
using System.Windows;
using Xunit;

namespace PrecisionImageCropper.Tests;

public sealed class EditingWorkflowTests
{
    [Fact]
    public void Crop_move_and_keyboard_sized_move_clamp_at_image_edges()
    {
        var crop = new CropRect(5, 5, 80, 60);
        var moved = CropMath.Move(crop, -10, 100, 100, 100);

        Assert.Equal(0, moved.X);
        Assert.Equal(40, moved.Y);
        Assert.Equal(80, moved.Width);
        Assert.Equal(60, moved.Height);
    }

    [Fact]
    public void Locked_dimension_change_updates_other_axis_and_respects_bounds()
    {
        var crop = CropMath.SetDimension(new CropRect(0, 0, 100, 100), true, 600, 500, 300, 3d / 2);

        Assert.Equal(450, crop.Width);
        Assert.Equal(300, crop.Height);
        Assert.Equal(1.5, crop.Width / crop.Height, 5);
    }

    [Fact]
    public void Unlocked_dimension_change_does_not_change_other_axis()
    {
        var crop = CropMath.SetDimension(new CropRect(10, 10, 100, 80), false, 120, 500, 300);

        Assert.Equal(100, crop.Width);
        Assert.Equal(120, crop.Height);
    }

    [Fact]
    public void Image_history_is_independent_and_clears_redo_after_new_edit()
    {
        var first = MakeItem("first.png");
        var second = MakeItem("second.png");
        var original = first.CaptureEditState();
        first.CropRectangle = new CropRect(20, 10, 150, 100);
        first.CommitEdit(original);

        Assert.True(first.CanUndo);
        Assert.False(second.CanUndo);
        Assert.True(first.Undo());
        Assert.True(first.CanRedo);
        first.NetRotation = 90;
        first.CommitEdit(first.CaptureEditState()); // unchanged snapshots do not add history
        Assert.True(first.CanRedo);

        var beforeRotation = first.CaptureEditState();
        first.NetRotation = 180;
        first.CommitEdit(beforeRotation);
        Assert.False(first.CanRedo);
    }

    [Fact]
    public void Reset_participates_in_undo_history()
    {
        var item = MakeItem("reset.png");
        var before = item.CaptureEditState();
        item.CropRectangle = new CropRect(10, 10, 100, 100);
        item.CommitEdit(before);
        item.ResetEdits();

        Assert.False(item.HasCrop);
        Assert.True(item.Undo());
        Assert.True(item.HasCrop);
    }

    [Fact]
    public void Recent_crop_sizes_are_deduplicated_limited_and_newest_first()
    {
        var root = Path.Combine(Path.GetTempPath(), $"PrecisionImageCropper-tests-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "settings.json");
        try
        {
            var settings = new SettingsService(path);
            for (var value = 1; value <= 6; value++) settings.RecordCropSize(value * 100, value * 50);
            settings.RecordCropSize(300, 150);

            Assert.Equal(5, settings.RecentCropSizes.Count);
            Assert.Equal(new RecentCropSize(300, 150), settings.RecentCropSizes[0]);
            Assert.Equal(settings.RecentCropSizes, new SettingsService(path).RecentCropSizes);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Centered_viewport_layout_keeps_display_offset_separate_from_source_coordinates()
    {
        var crop = new CropRect(20, 30, 150, 90);
        var layout = CropViewportGeometry.Calculate(1000, 800, 400, 200, 1);

        Assert.Equal(2.43, layout.Scale, 2);
        Assert.Equal(14, layout.ImageLeft, 2);
        Assert.Equal(157, layout.ImageTop, 2);
        var display = layout.SourceToDisplay(new Point(100, 50));
        var roundTrip = layout.DisplayToSource(display);
        Assert.Equal(257, display.X, 5);
        Assert.Equal(278.5, display.Y, 5);
        Assert.Equal(100, roundTrip.X, 5);
        Assert.Equal(50, roundTrip.Y, 5);
        Assert.Equal(20, crop.X);
        Assert.Equal(30, crop.Y);
    }

    [Fact]
    public void Crop_body_hit_testing_returns_move_for_every_interior_area_and_handles_win()
    {
        var selection = new Rect(100, 100, 300, 200);
        var interiorPoints = new[]
        {
            new Point(140, 140), new Point(360, 140), new Point(250, 200),
            new Point(140, 260), new Point(360, 260), new Point(250, 166.67),
            new Point(200, 200), new Point(200, 166.67)
        };

        foreach (var point in interiorPoints)
            Assert.Equal(CropPointerRegion.Move, CropInteractionGeometry.GetRegion(selection, point));

        Assert.Equal(CropPointerRegion.TopLeft, CropInteractionGeometry.GetRegion(selection, new Point(100, 100)));
        Assert.Equal(CropPointerRegion.Outside, CropInteractionGeometry.GetRegion(selection, new Point(80, 80)));
    }

    [Fact]
    public void Pixel_display_normalization_and_completed_move_create_one_history_entry()
    {
        Assert.Equal(434, CropViewportGeometry.NormalizePixel(433.99));
        Assert.Equal(470, CropViewportGeometry.NormalizePixel(470.45));

        var item = MakeItem("move.png");
        var before = item.CaptureEditState();
        item.CropRectangle = CropMath.Move(new CropRect(20, 20, 150, 100), 40, 30, 500, 300);
        item.CommitEdit(before);

        Assert.True(item.CanUndo);
        Assert.True(item.Undo());
        Assert.False(item.CanUndo);
        Assert.Equal(0, item.CropRectangle.X);
        Assert.Equal(0, item.CropRectangle.Y);
    }

    private static BatchImageItem MakeItem(string name) =>
        new(name, name, name, ".png", 500, 300, ImageImportSource.File);
}
