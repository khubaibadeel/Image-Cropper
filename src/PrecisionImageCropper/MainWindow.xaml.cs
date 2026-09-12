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
    private readonly SettingsService _settings = new();
    private string? _recentFolder;
    private int _clipboardImageSequence;
    private CancellationTokenSource? _exportCancellation;
    private bool _isInitializing = true;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _recentFolder = _settings.RecentFolders.FirstOrDefault();
        RebuildRecentMenu();
        UpdateSaveAllState();
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
                failures.Add($"{fileName} — Unsupported");
                continue;
            }

            if (!_queuedFilePaths.Add(canonicalPath))
            {
                failures.Add($"{fileName} — Duplicate");
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
                _settings.RecordOpenedFile(canonicalPath);
                RebuildRecentMenu();
                _ = LoadThumbnailAsync(item);
            }
            catch (Exception)
            {
                _queuedFilePaths.Remove(canonicalPath);
                failures.Add($"{fileName} — Corrupt image");
            }
        }

        if (failures.Count > 0)
        {
            var heading = failures.Count == 1 ? "1 file was not added:" : $"{failures.Count} files were not added:";
            MessageBox.Show(
                this,
                heading + "\n\n" + string.Join("\n", failures.Select(f => $"• {f}")),
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
            var thumbnail = await Task.Run(() => ImageService.LoadEditedThumbnail(
                item.SourceDataPath,
                item.CropRectangle,
                item.OriginalWidth,
                item.OriginalHeight,
                item.NetRotation,
                item.HorizontalFlip,
                item.VerticalFlip));
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
                    GetNextClipboardFileName(),
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

    private void RotateFlipItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetItem(sender, out var item)) return;

        _viewModel.SelectedBatchItem = item;
        var editor = new RotateFlipEditorWindow(this, _viewModel.BatchItems, item, RefreshThumbnailAsync);
        editor.ShowDialog();
    }

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
            var image = await Task.Run(() => ImageRenderService.RenderFinal(item));
            await SetClipboardImageWithRetryAsync(image);
            item.StatusMessage = $"Copied \"{item.OutputFileName}\" to clipboard.";
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
            await Task.Run(() => ImageRenderService.SaveFinal(item, outputPath));
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

    private async void SaveAll_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.BatchItems.Count == 0 || _exportCancellation is not null) return;
        var suggestedName = $"PrecisionImageCropper-{DateTime.Now:yyyy-MM-dd-HHmm}.zip";
        var outputPath = FileDialogService.SaveZip(suggestedName);
        if (outputPath is null) return;

        var exportItems = _viewModel.BatchItems.ToList();
        _exportCancellation = new CancellationTokenSource();
        UpdateSaveAllState();
        CancelExportButton.Visibility = Visibility.Visible;
        var progress = new Progress<ZipExportProgress>(value => ExportProgressText.Text = $"Saving {value.Current} of {value.Total}...");
        try
        {
            await BatchZipExportService.ExportAsync(exportItems, outputPath, progress, _exportCancellation.Token);
            ExportProgressText.Text = string.Empty;
            MessageBox.Show(this, $"{exportItems.Count} images saved successfully.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            ExportProgressText.Text = "Export canceled.";
        }
        catch (ZipExportException ex)
        {
            ExportProgressText.Text = string.Empty;
            ShowError($"Unable to export {ex.FileName}", ex.InnerException ?? ex);
        }
        catch (Exception ex)
        {
            ExportProgressText.Text = string.Empty;
            ShowError("Unable to export images", ex);
        }
        finally
        {
            _exportCancellation.Dispose();
            _exportCancellation = null;
            UpdateSaveAllState();
            CancelExportButton.Visibility = Visibility.Collapsed;
        }
    }

    private void CancelExport_Click(object sender, RoutedEventArgs e) => _exportCancellation?.Cancel();

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.QueueIsEmpty))
            UpdateSaveAllState();
    }

    private void UpdateSaveAllState() => SaveAllButton.IsEnabled = !_viewModel.QueueIsEmpty && _exportCancellation is null;

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
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C && !IsEditableTextBoxFocused() && _viewModel.SelectedBatchItem is not null)
        {
            CopyItem_Click(new Button { Tag = _viewModel.SelectedBatchItem }, e);
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

    private string GetNextClipboardFileName()
    {
        string name;
        do
        {
            _clipboardImageSequence++;
            name = $"Clipboard-{_clipboardImageSequence}.png";
        }
        while (_viewModel.BatchItems.Any(item => string.Equals(item.OriginalFileName, name, StringComparison.OrdinalIgnoreCase)));
        return name;
    }

    private void RebuildRecentMenu()
    {
        PopulateRecentMenu(RecentFilesMenu, _settings.RecentFiles, "No recent files", RecentFile_Click);
        PopulateRecentMenu(RecentFoldersMenu, _settings.RecentFolders, "No recent folders", RecentFolder_Click);
    }

    private static void PopulateRecentMenu(MenuItem menu, IReadOnlyList<string> paths, string emptyLabel, RoutedEventHandler click)
    {
        menu.Items.Clear();
        if (paths.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = emptyLabel, IsEnabled = false });
            return;
        }

        foreach (var path in paths)
        {
            var menuItem = new MenuItem { Header = Path.GetFileName(path), ToolTip = path, Tag = path };
            menuItem.Click += click;
            menu.Items.Add(menuItem);
        }
    }

    private async void RecentFile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.Tag is not string path) return;
        var fileName = Path.GetFileName(path);
        if (!File.Exists(path))
        {
            _settings.RemoveRecentFile(path);
            RebuildRecentMenu();
            MessageBox.Show(this, $"Recent file \"{fileName}\" is no longer available.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_queuedFilePaths.Contains(path))
        {
            MessageBox.Show(this, $"Duplicate \"{fileName}\" detected.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await ImportFilesAsync([path]);
    }

    private async void RecentFolder_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.Tag is not string path) return;
        if (!Directory.Exists(path))
        {
            _settings.RemoveRecentFolder(path);
            RebuildRecentMenu();
            MessageBox.Show(this, "The recent folder is no longer available.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var paths = FileDialogService.OpenImages(path);
        if (paths is not null) await ImportFilesAsync(paths);
    }

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
