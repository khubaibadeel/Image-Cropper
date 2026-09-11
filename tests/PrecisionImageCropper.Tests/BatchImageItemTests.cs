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

    private static BatchImageItem CreateItem(string name, int width, int height) =>
        new($"C:\\images\\{name}", $"C:\\images\\{name}", name, Path.GetExtension(name), width, height, ImageImportSource.File);
}
