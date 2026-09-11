using PrecisionImageCropper.Models;
using System.IO;
using Xunit;

namespace PrecisionImageCropper.Tests;

public sealed class BatchImageItemTests
{
    [Fact]
    public void Each_item_keeps_an_independent_crop_and_edit_state()
    {
        var first = CreateItem("first.jpg", 6000, 4000);
        var second = CreateItem("second.jpg", 6000, 4000);

        first.CropRectangle = new CropRect(100, 50, 1200, 800);
        first.NetRotation = 90;
        first.HorizontalFlip = true;

        Assert.True(first.HasCrop);
        Assert.True(first.HasRotation);
        Assert.True(first.HasHorizontalFlip);
        Assert.True(first.HasUnsavedChanges);
        Assert.Equal(800, first.OutputWidth);
        Assert.Equal(1200, first.OutputHeight);

        Assert.False(second.HasUnsavedChanges);
        Assert.Equal(6000, second.OutputWidth);
        Assert.Equal(4000, second.OutputHeight);
    }

    [Fact]
    public void Suggested_output_name_preserves_source_format()
    {
        var item = CreateItem("summer.photo.tiff", 100, 100);

        Assert.Equal("summer.photo-copy.tiff", item.OutputFileName);
        item.CropRectangle = new CropRect(0, 0, 50, 50);
        Assert.Equal("summer.photo-edited.tiff", item.OutputFileName);
    }

    [Fact]
    public void Removing_selected_item_updates_selected_batch_item_in_view_model()
    {
        var vm = new PrecisionImageCropper.ViewModels.MainViewModel();
        var item1 = CreateItem("first.jpg", 100, 100);
        var item2 = CreateItem("second.jpg", 100, 100);
        vm.BatchItems.Add(item1);
        vm.BatchItems.Add(item2);
        vm.SelectedBatchItem = item1;

        vm.BatchItems.Remove(item1);

        Assert.Equal(item2, vm.SelectedBatchItem);

        vm.BatchItems.Remove(item2);
        Assert.Null(vm.SelectedBatchItem);
    }

    [Fact]
    public void Crop_recipes_persist_independently_for_multiple_batch_images()
    {
        var first = CreateItem("one-to-one.jpg", 2400, 1600);
        var second = CreateItem("freeform.jpg", 3000, 2000);
        var third = CreateItem("widescreen.jpg", 3200, 2400);

        first.CropRectangle = new CropRect(400, 0, 1600, 1600);
        first.SelectedAspectRatio = "1:1";
        second.CropRectangle = new CropRect(110, 230, 1820, 970);
        second.SelectedAspectRatio = "FreeForm";
        third.CropRectangle = new CropRect(160, 300, 2880, 1620);
        third.SelectedAspectRatio = "16:9";

        AssertCrop(first.CropRectangle, 400, 0, 1600, 1600);
        Assert.Equal("1:1", first.SelectedAspectRatio);
        AssertCrop(second.CropRectangle, 110, 230, 1820, 970);
        Assert.Equal("FreeForm", second.SelectedAspectRatio);
        AssertCrop(third.CropRectangle, 160, 300, 2880, 1620);
        Assert.Equal("16:9", third.SelectedAspectRatio);

        second.SelectedAspectRatio = "Custom";
        second.CustomAspectRatioWidth = 7;
        second.CustomAspectRatioHeight = 5;

        Assert.Equal(7, second.CustomAspectRatioWidth);
        Assert.Equal(5, second.CustomAspectRatioHeight);
        Assert.Equal("1:1", first.SelectedAspectRatio);
        Assert.Equal("16:9", third.SelectedAspectRatio);
    }

    private static void AssertCrop(CropRect crop, double x, double y, double width, double height)
    {
        Assert.Equal(x, crop.X);
        Assert.Equal(y, crop.Y);
        Assert.Equal(width, crop.Width);
        Assert.Equal(height, crop.Height);
    }

    private static BatchImageItem CreateItem(string name, int width, int height) =>
        new($"C:\\images\\{name}", $"C:\\images\\{name}", name, Path.GetExtension(name), width, height, ImageImportSource.File);
}
