using System.IO;
using System.IO.Compression;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrecisionImageCropper.Models;
using PrecisionImageCropper.Services;
using Xunit;

namespace PrecisionImageCropper.Tests;

public sealed class BatchZipExportServiceTests
{
    [Fact]
    public async Task Export_creates_a_readable_zip_with_unique_names_preserved_originals_and_rendered_edits()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var firstPath = CreateImage(root, "first.png", 80, 40, ".png");
            var secondPath = CreateImage(root, "second.png", 80, 40, ".png");
            var jpegPath = CreateImage(root, "camera.jpeg", 60, 30, ".jpeg");
            var clipboardPath = CreateImage(root, "clipboard.png", 50, 20, ".png");
            var editedPath = CreateImage(root, "edited.png", 80, 40, ".png");
            var outputPath = Path.Combine(root, "export.zip");

            var first = CreateItem(firstPath, "image.png", ".png", 80, 40, ImageImportSource.File);
            var second = CreateItem(secondPath, "image.png", ".png", 80, 40, ImageImportSource.File);
            var jpeg = CreateItem(jpegPath, "camera.jpeg", ".jpeg", 60, 30, ImageImportSource.File);
            var clipboard = CreateItem(clipboardPath, "Clipboard-1.png", ".png", 50, 20, ImageImportSource.Clipboard);
            var edited = CreateItem(editedPath, "edited.png", ".png", 80, 40, ImageImportSource.File);
            edited.CropRectangle = new CropRect(10, 5, 40, 20);
            edited.NetRotation = 90;
            edited.HorizontalFlip = true;
            var directRender = await Task.Run(() => ImageRenderService.RenderFinal(edited));
            Assert.Equal(20, directRender.PixelWidth);
            Assert.Equal(40, directRender.PixelHeight);

            await BatchZipExportService.ExportAsync([first, second, jpeg, clipboard, edited], outputPath);

            Assert.True(File.Exists(outputPath));
            using var archive = ZipFile.OpenRead(outputPath);
            Assert.Equal(5, archive.Entries.Count);
            Assert.Contains(archive.Entries, entry => entry.FullName == "image.png");
            Assert.Contains(archive.Entries, entry => entry.FullName == "image-2.png");
            Assert.Contains(archive.Entries, entry => entry.FullName == "camera.jpeg");
            Assert.Contains(archive.Entries, entry => entry.FullName == "Clipboard-1.png");
            var editedEntry = Assert.Single(archive.Entries, entry => entry.FullName == "edited-cropped-rotated-clockwise-flipped-horizontal.png");

            Assert.Equal(File.ReadAllBytes(firstPath), ReadEntryBytes(archive.GetEntry("image.png")!));
            Assert.Equal(File.ReadAllBytes(jpegPath), ReadEntryBytes(archive.GetEntry("camera.jpeg")!));
            Assert.Equal(File.ReadAllBytes(clipboardPath), ReadEntryBytes(archive.GetEntry("Clipboard-1.png")!));

            using var renderedStream = new MemoryStream(ReadEntryBytes(editedEntry));
            var rendered = BitmapFrame.Create(renderedStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Assert.Equal(20, rendered.PixelWidth);
            Assert.Equal(40, rendered.PixelHeight);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Export_failure_does_not_leave_a_completed_looking_zip()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var corruptPath = Path.Combine(root, "corrupt.png");
            File.WriteAllText(corruptPath, "not an image");
            var outputPath = Path.Combine(root, "export.zip");
            var item = CreateItem(corruptPath, "corrupt.png", ".png", 20, 20, ImageImportSource.File);
            item.NetRotation = 90;

            await Assert.ThrowsAsync<ZipExportException>(() => BatchZipExportService.ExportAsync([item], outputPath));

            Assert.False(File.Exists(outputPath));
            Assert.Empty(Directory.GetFiles(root, ".export.zip.*.tmp"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateTemporaryRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"PrecisionImageCropper-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static string CreateImage(string root, string fileName, int width, int height, string extension)
    {
        var path = Path.Combine(root, fileName);
        var pixels = Enumerable.Repeat((byte)255, width * height * 4).ToArray();
        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        source.Freeze();
        BitmapEncoder encoder = extension switch
        {
            ".jpeg" => new JpegBitmapEncoder(),
            _ => new PngBitmapEncoder()
        };
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    private static BatchImageItem CreateItem(string path, string name, string extension, int width, int height, ImageImportSource source) =>
        new(path, source == ImageImportSource.File ? path : null, name, extension, width, height, source);

    private static byte[] ReadEntryBytes(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
