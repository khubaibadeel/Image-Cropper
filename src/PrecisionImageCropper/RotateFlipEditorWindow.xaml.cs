using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using PrecisionImageCropper.Models;
using PrecisionImageCropper.Services;

namespace PrecisionImageCropper;

public partial class RotateFlipEditorWindow : Window
{
    private readonly ObservableCollection<BatchImageItem> _items;
    private readonly Func<BatchImageItem, Task> _refreshThumbnailAsync;
    private BatchImageItem _item;
    private BitmapSource? _previewSource;
    private int _draftRotation;
    private bool _draftHorizontalFlip;
    private bool _draftVerticalFlip;
    private int _openedRotation;
    private bool _openedHorizontalFlip;
    private bool _openedVerticalFlip;

    public RotateFlipEditorWindow(Window owner, ObservableCollection<BatchImageItem> items, BatchImageItem item, Func<BatchImageItem, Task> refreshThumbnailAsync)
    {
        Owner = owner;
        _items = items;
        _item = item;
        _refreshThumbnailAsync = refreshThumbnailAsync;
        InitializeComponent();
        Loaded += async (_, _) => await LoadItemAsync(_item);
        PreviewKeyDown += RotateFlipEditorWindow_PreviewKeyDown;
    }

    private async Task LoadItemAsync(BatchImageItem item)
    {
        _item = item;
        _openedRotation = _draftRotation = item.NetRotation;
        _openedHorizontalFlip = _draftHorizontalFlip = item.HorizontalFlip;
        _openedVerticalFlip = _draftVerticalFlip = item.VerticalFlip;
        ImageNameText.Text = item.OriginalFileName;
        ImageInfoText.Text = item.OriginalDimensionsText;
        PositionText.Text = $"{_items.IndexOf(item) + 1} of {_items.Count}";
        PreviewLoadingText.Visibility = Visibility.Visible;
        PreviewImage.Source = null;
        _previewSource = null;
        UpdateNavigationButtons();

        try
        {
            _previewSource = await Task.Run(() => ImageService.LoadThumbnail(item.SourceDataPath, 1600));
            if (!ReferenceEquals(item, _item)) return;
            UpdatePreview();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Unable to load this image for editing.\n\n{ex.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (ReferenceEquals(item, _item))
                PreviewLoadingText.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdatePreview()
    {
        if (_previewSource is null) return;
        var crop = _item.CropRectangle;
        var previewCrop = new CropRect(
            crop.X * _previewSource.PixelWidth / _item.OriginalWidth,
            crop.Y * _previewSource.PixelHeight / _item.OriginalHeight,
            crop.Width * _previewSource.PixelWidth / _item.OriginalWidth,
            crop.Height * _previewSource.PixelHeight / _item.OriginalHeight);
        PreviewImage.Source = CropService.Render(_previewSource, previewCrop, _draftRotation, _draftHorizontalFlip, _draftVerticalFlip);
        HorizontalFlipButton.Background = _draftHorizontalFlip ? System.Windows.Media.Brushes.LightBlue : System.Windows.Media.Brushes.WhiteSmoke;
        VerticalFlipButton.Background = _draftVerticalFlip ? System.Windows.Media.Brushes.LightBlue : System.Windows.Media.Brushes.WhiteSmoke;
        StateText.Text = $"Rotation: {RotationLabel(_draftRotation)}\nHorizontal flip: {(_draftHorizontalFlip ? "On" : "Off")}\nVertical flip: {(_draftVerticalFlip ? "On" : "Off")}";
    }

    private void Clockwise_Click(object sender, RoutedEventArgs e)
    {
        _draftRotation = NormalizeRotation(_draftRotation + 90);
        UpdatePreview();
    }

    private void CounterClockwise_Click(object sender, RoutedEventArgs e)
    {
        _draftRotation = NormalizeRotation(_draftRotation - 90);
        UpdatePreview();
    }

    private void HorizontalFlip_Click(object sender, RoutedEventArgs e)
    {
        _draftHorizontalFlip = !_draftHorizontalFlip;
        UpdatePreview();
    }

    private void VerticalFlip_Click(object sender, RoutedEventArgs e)
    {
        _draftVerticalFlip = !_draftVerticalFlip;
        UpdatePreview();
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (!await ApplyCurrentAsync()) return;
        DialogResult = true;
    }

    private async void ApplyNext_Click(object sender, RoutedEventArgs e)
    {
        if (!await ApplyCurrentAsync()) return;
        await MoveToAsync(1, skipSavePrompt: true);
    }

    private async void Previous_Click(object sender, RoutedEventArgs e) => await MoveToAsync(-1);

    private async void Next_Click(object sender, RoutedEventArgs e) => await MoveToAsync(1);

    private async Task MoveToAsync(int delta, bool skipSavePrompt = false)
    {
        var targetIndex = _items.IndexOf(_item) + delta;
        if (targetIndex < 0 || targetIndex >= _items.Count) return;
        if (!skipSavePrompt && IsDirty())
        {
            var result = MessageBox.Show(this, "Apply changes before moving to the next image?\n\nYes applies and continues. No discards the changes.", Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (result == MessageBoxResult.Cancel) return;
            if (result == MessageBoxResult.Yes && !await ApplyCurrentAsync()) return;
        }
        await LoadItemAsync(_items[targetIndex]);
    }

    private async Task<bool> ApplyCurrentAsync()
    {
        try
        {
            var before = _item.CaptureEditState();
            _item.NetRotation = _draftRotation;
            _item.HorizontalFlip = _draftHorizontalFlip;
            _item.VerticalFlip = _draftVerticalFlip;
            _item.CommitEdit(before);
            await _refreshThumbnailAsync(_item);
            _openedRotation = _draftRotation;
            _openedHorizontalFlip = _draftHorizontalFlip;
            _openedVerticalFlip = _draftVerticalFlip;
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Unable to apply the image transform.\n\n{ex.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void RotateFlipEditorWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        DialogResult = false;
        e.Handled = true;
    }

    private void UpdateNavigationButtons()
    {
        var index = _items.IndexOf(_item);
        PreviousButton.IsEnabled = index > 0;
        NextButton.IsEnabled = index >= 0 && index < _items.Count - 1;
        ApplyNextButton.IsEnabled = NextButton.IsEnabled;
    }

    private bool IsDirty() => _draftRotation != _openedRotation || _draftHorizontalFlip != _openedHorizontalFlip || _draftVerticalFlip != _openedVerticalFlip;

    private static int NormalizeRotation(int rotation) => ((rotation % 360) + 360) % 360;

    private static string RotationLabel(int rotation) => NormalizeRotation(rotation) switch
    {
        90 => "Clockwise 90°",
        180 => "180°",
        270 => "Counter-clockwise 90°",
        _ => "None"
    };
}
