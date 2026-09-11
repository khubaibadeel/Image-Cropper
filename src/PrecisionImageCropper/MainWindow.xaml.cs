using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using PrecisionImageCropper.Models;
using PrecisionImageCropper.Services;
using PrecisionImageCropper.ViewModels;

namespace PrecisionImageCropper;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private readonly HashSet<string> _queuedFilePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _thumbnailThrottle = new(4, 4);
    private string? _recentFolder;
    private bool _isInitializing = true;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _isInitializing = false;
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var paths = FileDialogService.OpenImages(_recentFolder);
        if (paths is not null)
            await ImportFilesAsync(paths);
    }

    private async Task ImportFilesAsync(IEnumerable<string> paths)
    {
        var failures = new List<string>();

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                failures.Add("Unknown file — Invalid path");
                continue;
            }

            string canonicalPath;
            try
            {
                canonicalPath = Path.GetFullPath(path);
            }
            catch (Exception)
            {
                failures.Add($"{Path.GetFileName(path)} — Invalid path");
                continue;
            }

            var fileName = Path.GetFileName(canonicalPath);
            if (!ImageService.IsSupportedFile(canonicalPath))
            {
                failures.Add($"{fileName} — Unsupported format");
                continue;
            }

            if (!_queuedFilePaths.Add(canonicalPath))
            {
                failures.Add($"{fileName} — Duplicate file");
                continue;
            }

            try
            {
                // Header parsing happens off the UI thread. No full-size decoded bitmap is retained.
                var info = await Task.Run(() => ImageService.ReadInfo(canonicalPath));
                var item = new BatchImageItem(
                    canonicalPath,
                    canonicalPath,
                    fileName,
                    Path.GetExtension(canonicalPath),
                    info.Width,
                    info.Height,
                    ImageImportSource.File);

                _viewModel.BatchItems.Add(item);
                _viewModel.SelectedBatchItem ??= item;
                _recentFolder = Path.GetDirectoryName(canonicalPath);
                _ = LoadThumbnailAsync(item);
            }
            catch (Exception)
            {
                _queuedFilePaths.Remove(canonicalPath);
                failures.Add($"{fileName} — Unable to read image");
            }
        }

        if (failures.Count > 0)
        {
            MessageBox.Show(
                this,
                "The following files were not added:\n\n" + string.Join("\n", failures.Select(f => $"• {f}")),
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async Task LoadThumbnailAsync(BatchImageItem item)
    {
        await _thumbnailThrottle.WaitAsync();
        try
        {
            var thumbnail = await Task.Run(() => ImageService.LoadThumbnail(item.SourceDataPath));
            item.Thumbnail = thumbnail;
        }
        catch (Exception)
        {
            item.StatusMessage = "Preview unavailable";
        }
        finally
        {
            item.IsThumbnailLoading = false;
            _thumbnailThrottle.Release();
        }
    }

    private async Task RefreshThumbnailAsync(BatchImageItem item)
    {
        item.IsThumbnailLoading = true;
        await _thumbnailThrottle.WaitAsync();
        try
        {
            var thumbnail = await Task.Run(() => ImageService.LoadCroppedThumbnail(
                item.SourceDataPath,
                item.CropRectangle,
                item.OriginalWidth,
                item.OriginalHeight));
            item.Thumbnail = thumbnail;
        }
        catch (Exception)
        {
            item.StatusMessage = "Preview unavailable";
        }
        finally
        {
            item.IsThumbnailLoading = false;
            _thumbnailThrottle.Release();
        }
    }

    private async void PasteImage_Click(object sender, RoutedEventArgs e) => await PasteImageFromClipboardAsync();

    private async Task PasteImageFromClipboardAsync()
    {
        string[]? filePaths = null;
        BitmapSource? clipboardImage = null;
        Exception? clipboardError = null;

        // The Windows clipboard can be briefly locked by another application.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (Clipboard.ContainsFileDropList())
                    filePaths = Clipboard.GetFileDropList().Cast<string>().ToArray();

                if ((filePaths is null || filePaths.Length == 0) && Clipboard.ContainsImage())
                    clipboardImage = Clipboard.GetImage();

                break;
            }
            catch (COMException ex) { clipboardError = ex; }
            catch (ExternalException ex) { clipboardError = ex; }
            catch (InvalidOperationException ex) { clipboardError = ex; }
            await Task.Delay(50);
        }

        if (filePaths is { Length: > 0 })
        {
            await ImportFilesAsync(filePaths);
            return;
        }

        if (clipboardImage is not null)
        {
            try
            {
                var width = clipboardImage.PixelWidth;
                var height = clipboardImage.PixelHeight;
                var writeable = new WriteableBitmap(clipboardImage);
                writeable.Freeze();

                // Persist clipboard content to a private temporary PNG off the UI thread, then release its full bitmap.
                var temporaryPath = await Task.Run(() => ImageService.SaveClipboardImageToTemporaryFile(writeable));
                var item = new BatchImageItem(
                    temporaryPath,
                    null,
                    $"Clipboard Image {DateTime.Now:yyyy-MM-dd HHmmss}.png",
                    ".png",
                    width,
                    height,
                    ImageImportSource.Clipboard);
                _viewModel.BatchItems.Add(item);
                _viewModel.SelectedBatchItem ??= item;
                _ = LoadThumbnailAsync(item);
            }
            catch (Exception ex)
            {
                ShowError("Unable to paste image", ex);
            }

            return;
        }

        if (clipboardError is not null)
        {
            ShowError("Unable to access the clipboard", clipboardError);
            return;
        }

        MessageBox.Show(this, "No supported image was found on the clipboard.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CropItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetItem(sender, out var item)) return;

        _viewModel.SelectedBatchItem = item;
        var editor = new CropEditorWindow(this, _viewModel.BatchItems, item, RefreshThumbnailAsync)
        {
            Owner = this
        };
        editor.ShowDialog();
    }

    private void RotateFlipItem_Click(object sender, RoutedEventArgs e) => ExplainEditorPhase("Rotate and flip editor");

    private void ExplainEditorPhase(string feature)
    {
        MessageBox.Show(
            this,
            $"{feature} is the next batch-editing phase. This image's independent edit state is already retained in the queue.",
            Title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async void CopyItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetItem(sender, out var item)) return;

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            var image = await Task.Run(() =>
            {
                var source = ImageService.Load(item.SourceDataPath).Source;
                return CropService.Render(source, item.CropRectangle, item.NetRotation, item.HorizontalFlip, item.VerticalFlip);
            });
            await SetClipboardImageWithRetryAsync(image);
        }
        catch (Exception ex)
        {
            ShowError("Unable to copy image", ex);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private static async Task SetClipboardImageWithRetryAsync(BitmapSource image)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Clipboard.SetImage(image);
                return;
            }
            catch (Exception) when (attempt < 2)
            {
                await Task.Delay(50);
            }
        }
    }

    private async void SaveItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetItem(sender, out var item)) return;
        var outputPath = FileDialogService.SaveImage(item.OutputFileName, item.OriginalExtension);
        if (outputPath is null) return;

        if (item.ImportSource != ImageImportSource.Clipboard &&
            CropService.IsOriginalSourcePath(item.OriginalFilePath, outputPath))
        {
            MessageBox.Show(
                this,
                "The original image cannot be overwritten. Please choose a different filename or location.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            await Task.Run(() =>
            {
                var source = ImageService.Load(item.SourceDataPath).Source;
                CropService.Save(source, item.CropRectangle, outputPath, 95, item.NetRotation, item.HorizontalFlip, item.VerticalFlip, item.OriginalFilePath);
            });
            item.StatusMessage = "Saved";
        }
        catch (Exception ex)
        {
            ShowError("Unable to save image", ex);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void RemoveItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetItem(sender, out var item)) return;
        var result = MessageBox.Show(
            this,
            $"Remove \"{item.OriginalFileName}\" from the batch?\n\nThe original file will not be deleted.",
            Title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes) return;

        _viewModel.BatchItems.Remove(item);
        if (item.OriginalFilePath is not null)
        {
            _queuedFilePaths.Remove(item.OriginalFilePath);
        }
        else if (item.ImportSource == ImageImportSource.Clipboard)
        {
            try
            {
                if (File.Exists(item.SourceDataPath))
                    File.Delete(item.SourceDataPath);
            }
            catch { }
        }
    }

    private void SaveAll_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "Batch Save All and ZIP export are scheduled for the next phase. Individual image saving is available on every card.",
            Title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            await ImportFilesAsync(files);
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_isInitializing) return;
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.O)
        {
            Open_Click(this, e);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V && !IsEditableTextBoxFocused())
        {
            await PasteImageFromClipboardAsync();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S && _viewModel.SelectedBatchItem is not null)
        {
            var button = new Button { Tag = _viewModel.SelectedBatchItem };
            SaveItem_Click(button, e);
            e.Handled = true;
        }
    }

    private static bool TryGetItem(object sender, out BatchImageItem item)
    {
        item = (sender as FrameworkElement)?.Tag as BatchImageItem ?? null!;
        return item is not null;
    }

    private static bool IsEditableTextBoxFocused() => Keyboard.FocusedElement is TextBox textBox && !textBox.IsReadOnly;

    private void ShowError(string title, Exception error) =>
        MessageBox.Show(this, $"{title}.\n\n{error.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Error);

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        try
        {
            var tempFolder = Path.Combine(Path.GetTempPath(), "PrecisionImageCropper");
            if (Directory.Exists(tempFolder))
            {
                foreach (var file in Directory.GetFiles(tempFolder, "clipboard-*.png"))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch { }
    }
}
