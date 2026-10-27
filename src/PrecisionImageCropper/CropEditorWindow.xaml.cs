using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using PrecisionImageCropper.Models;
using PrecisionImageCropper.Services;
using PrecisionImageCropper.Utilities;

namespace PrecisionImageCropper;

public partial class CropEditorWindow : Window
{
    private readonly ObservableCollection<BatchImageItem> _items;
    private readonly Func<BatchImageItem, Task> _refreshThumbnailAsync;
    private readonly Action<BatchImageItem>? _onItemSelected;
    private BatchImageItem _item;
    private CropRect _draftCrop = new();
    private CropRect _openedCrop = new();
    private string _draftAspectRatio = "FreeForm";
    private string _openedAspectRatio = "FreeForm";
    private bool _openedAspectLocked;
    private double _customWidth = 1;
    private double _customHeight = 1;
    private bool _aspectLocked;
    private double _zoom = 1;
    private bool _synchronizing;
    private readonly SettingsService _settings = new();

    public CropEditorWindow(
        Window owner,
        ObservableCollection<BatchImageItem> items,
        BatchImageItem item,
        Func<BatchImageItem, Task> refreshThumbnailAsync,
        Action<BatchImageItem>? onItemSelected = null)
    {
        Owner = owner;
        _items = items;
        _item = item;
        _refreshThumbnailAsync = refreshThumbnailAsync;
        _onItemSelected = onItemSelected;
        InitializeComponent();
        Loaded += async (_, _) => await LoadItemAsync(_item);
        PreviewKeyDown += CropEditorWindow_PreviewKeyDown;
    }

    private async Task LoadItemAsync(BatchImageItem item)
    {
        _item = item;
        _onItemSelected?.Invoke(item);
        _openedCrop = item.CropRectangle;
        _draftCrop = _openedCrop.Clone();
        _openedAspectRatio = item.SelectedAspectRatio;
        _openedAspectLocked = item.IsAspectLocked;
        _draftAspectRatio = _openedAspectRatio;
        _customWidth = item.CustomAspectRatioWidth;
        _customHeight = item.CustomAspectRatioHeight;
        _aspectLocked = item.IsAspectLocked;
        CustomWidthBox.Text = _customWidth.ToString("0.##", CultureInfo.CurrentCulture);
        CustomHeightBox.Text = _customHeight.ToString("0.##", CultureInfo.CurrentCulture);
        ImageNameText.Text = item.OriginalFileName;
        ImageInfoText.Text = item.OriginalDimensionsText;
        PositionText.Text = $"{_items.IndexOf(item) + 1} of {_items.Count}";
        PreviewLoadingText.Visibility = Visibility.Visible;
        PreviewImage.Source = null;
        UpdateNavigationButtons();
        SelectAspectRatio(_draftAspectRatio);
        RefreshRecentSizes();
        UpdateOverlayAndFields();

        try
        {
            var preview = await Task.Run(() => ImageService.LoadThumbnail(item.SourceDataPath, 1600));
            if (!ReferenceEquals(item, _item)) return;
            PreviewImage.Source = preview;
            UpdatePreviewLayout();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Unable to load this image for cropping.\n\n{ex.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (ReferenceEquals(item, _item))
                PreviewLoadingText.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdatePreviewLayout()
    {
        var viewportWidth = PreviewViewport.ViewportWidth > 0 ? PreviewViewport.ViewportWidth : PreviewViewport.ActualWidth;
        var viewportHeight = PreviewViewport.ViewportHeight > 0 ? PreviewViewport.ViewportHeight : PreviewViewport.ActualHeight;
        if (viewportWidth <= 0 || viewportHeight <= 0) return;

        var layout = CropViewportGeometry.Calculate(
            viewportWidth, viewportHeight, _item.OriginalWidth, _item.OriginalHeight, _zoom);
        // PreviewHost maintains a viewport-sized surface when the image is smaller.
        // PreviewCanvas is centered inside it; its own origin stays at source (0,0).
        PreviewHost.Width = layout.HostWidth;
        PreviewHost.Height = layout.HostHeight;
        PreviewCanvas.Width = layout.ImageWidth;
        PreviewCanvas.Height = layout.ImageHeight;
        PreviewImage.Width = layout.ImageWidth;
        PreviewImage.Height = layout.ImageHeight;
        CropOverlay.Width = layout.ImageWidth;
        CropOverlay.Height = layout.ImageHeight;
        CropOverlay.SourceWidth = _item.OriginalWidth;
        CropOverlay.SourceHeight = _item.OriginalHeight;
        CropOverlay.Scale = layout.Scale;
        CropOverlay.Crop = _draftCrop;
    }

    private void UpdateOverlayAndFields()
    {
        _synchronizing = true;
        CropOverlay.Crop = _draftCrop;
        CropOverlay.AspectRatio = _aspectLocked ? GetAspectRatio(_draftAspectRatio) : null;
        WidthBox.Text = FormatPixel(_draftCrop.Width);
        HeightBox.Text = FormatPixel(_draftCrop.Height);
        XBox.Text = FormatPixel(_draftCrop.X);
        YBox.Text = FormatPixel(_draftCrop.Y);
        ZoomText.Text = $"{_zoom * 100:0}%";
        LockButton.Content = "↔";
        LockButton.Foreground = _aspectLocked ? System.Windows.Media.Brushes.DodgerBlue : System.Windows.Media.Brushes.SlateGray;
        StatusText.Text = $"{_item.OriginalDimensionsText}  |  Crop {FormatPixel(_draftCrop.Width)} × {FormatPixel(_draftCrop.Height)} px  |  Zoom {_zoom * 100:0}%";
        UndoButton.IsEnabled = _item.CanUndo;
        RedoButton.IsEnabled = _item.CanRedo;
        _synchronizing = false;
    }

    private void CropOverlay_CropChanged(object? sender, CropRect crop)
    {
        if (_synchronizing) return;
        _draftCrop = CropMath.Clamp(crop, _item.OriginalWidth, _item.OriginalHeight);
        UpdateOverlayAndFields();
    }

    private void AspectRatioBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_synchronizing || AspectRatioBox.SelectedItem is not ComboBoxItem option) return;
        _draftAspectRatio = option.Content?.ToString() ?? "FreeForm";
        CustomRatioPanel.Visibility = _draftAspectRatio == "Custom" ? Visibility.Visible : Visibility.Collapsed;
        _aspectLocked = _draftAspectRatio != "FreeForm";
        var ratio = GetAspectRatio(_draftAspectRatio);
        if (ratio is > 0)
            _draftCrop = CropMath.ApplyRatio(_draftCrop, ratio.Value, _item.OriginalWidth, _item.OriginalHeight);
        UpdateOverlayAndFields();
    }

    private void SelectAspectRatio(string value)
    {
        _synchronizing = true;
        foreach (var option in AspectRatioBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(option.Content?.ToString(), value, StringComparison.Ordinal))
            {
                AspectRatioBox.SelectedItem = option;
                break;
            }
        }
        CustomRatioPanel.Visibility = value == "Custom" ? Visibility.Visible : Visibility.Collapsed;
        _synchronizing = false;
    }

    private void NumericField_LostFocus(object sender, RoutedEventArgs e) => CommitNumericField(sender as TextBox);

    private void NumericField_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (ReferenceEquals(sender, CustomWidthBox) || ReferenceEquals(sender, CustomHeightBox))
        {
            CustomRatio_LostFocus(sender, e);
        }
        else
        {
            CommitNumericField(sender as TextBox);
        }
        e.Handled = true;
    }

    private void CommitNumericField(TextBox? field)
    {
        if (_synchronizing || field?.Tag is not string property || !TryReadPositiveOrZero(field, out var value))
        {
            UpdateOverlayAndFields();
            return;
        }

        value = CropViewportGeometry.NormalizePixel(value);
        var crop = _draftCrop.Clone();
        var ratio = _aspectLocked ? GetAspectRatio(_draftAspectRatio) : null;
        switch (property)
        {
            case "Width":
                crop = CropMath.SetDimension(crop, true, value, _item.OriginalWidth, _item.OriginalHeight, ratio);
                break;
            case "Height":
                crop = CropMath.SetDimension(crop, false, value, _item.OriginalWidth, _item.OriginalHeight, ratio);
                break;
            case "X": crop.X = value; break;
            case "Y": crop.Y = value; break;
        }
        _draftCrop = CropMath.Clamp(crop, _item.OriginalWidth, _item.OriginalHeight);
        UpdateOverlayAndFields();
    }

    private void CustomRatio_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_synchronizing) return;
        if (!TryReadPositive(CustomWidthBox, out _customWidth) || !TryReadPositive(CustomHeightBox, out _customHeight))
        {
            _customWidth = _customHeight = 1;
        }
        _draftCrop = CropMath.ApplyRatio(_draftCrop, _customWidth / _customHeight, _item.OriginalWidth, _item.OriginalHeight);
        UpdateOverlayAndFields();
    }

    private void PreviewViewport_SizeChanged(object sender, SizeChangedEventArgs e) => UpdatePreviewLayout();

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _draftCrop = new CropRect(0, 0, _item.OriginalWidth, _item.OriginalHeight);
        _draftAspectRatio = "FreeForm";
        _aspectLocked = false;
        SelectAspectRatio(_draftAspectRatio);
        UpdateOverlayAndFields();
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
            var result = MessageBox.Show(
                this,
                "Apply changes before moving to the next image?\n\nYes applies and continues. No discards the changes.",
                Title,
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);
            if (result == MessageBoxResult.Cancel) return;
            if (result == MessageBoxResult.Yes && !await ApplyCurrentAsync()) return;
        }
        await LoadItemAsync(_items[targetIndex]);
    }

    private async Task<bool> ApplyCurrentAsync()
    {
        try
        {
            _draftCrop = CropMath.Clamp(_draftCrop, _item.OriginalWidth, _item.OriginalHeight);
            var before = _item.CaptureEditState();
            // Capture before assignment so a completed drag/resize or typed
            // change becomes one history record when Apply is chosen.
            _item.CropRectangle = _draftCrop;
            _item.SelectedAspectRatio = _draftAspectRatio;
            _item.IsAspectLocked = _aspectLocked;
            _item.CustomAspectRatioWidth = _customWidth;
            _item.CustomAspectRatioHeight = _customHeight;
            _item.CommitEdit(before);
            _settings.RecordCropSize(_draftCrop.Width, _draftCrop.Height);
            RefreshRecentSizes();
            await _refreshThumbnailAsync(_item);
            _openedCrop = _draftCrop.Clone();
            _openedAspectRatio = _draftAspectRatio;
            _openedAspectLocked = _aspectLocked;
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Unable to apply the crop.\n\n{ex.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void CropEditorWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z)
        {
            Undo_Click(this, e); e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Y)
        {
            Redo_Click(this, e); e.Handled = true;
        }
        else if (!IsEditableTextBoxFocused() && (e.Key is Key.Left or Key.Right or Key.Up or Key.Down))
        {
            var amount = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            var dx = e.Key == Key.Left ? -amount : e.Key == Key.Right ? amount : 0;
            var dy = e.Key == Key.Up ? -amount : e.Key == Key.Down ? amount : 0;
            _draftCrop = CropMath.Move(_draftCrop, dx, dy, _item.OriginalWidth, _item.OriginalHeight);
            UpdateOverlayAndFields(); e.Handled = true;
        }
        else if (!IsEditableTextBoxFocused() && e.Key == Key.F)
        {
            Fit_Click(this, e); e.Handled = true;
        }
        else if (!IsEditableTextBoxFocused() && e.Key == Key.D1)
        {
            ActualSize_Click(this, e); e.Handled = true;
        }
    }

    private void UpdateNavigationButtons()
    {
        var index = _items.IndexOf(_item);
        PreviousButton.IsEnabled = index > 0;
        NextButton.IsEnabled = index >= 0 && index < _items.Count - 1;
        ApplyNextButton.IsEnabled = NextButton.IsEnabled;
    }

    private bool IsDirty() =>
        Math.Abs(_draftCrop.X - _openedCrop.X) > .01 ||
        Math.Abs(_draftCrop.Y - _openedCrop.Y) > .01 ||
        Math.Abs(_draftCrop.Width - _openedCrop.Width) > .01 ||
        Math.Abs(_draftCrop.Height - _openedCrop.Height) > .01 ||
        !string.Equals(_draftAspectRatio, _openedAspectRatio, StringComparison.Ordinal) ||
        _aspectLocked != _openedAspectLocked;

    private double? GetAspectRatio(string value) => value switch
    {
        "Original" => _item.OriginalWidth / (double)_item.OriginalHeight,
        "1:1" => 1,
        "4:3" => 4d / 3,
        "3:2" => 3d / 2,
        "16:9" => 16d / 9,
        "9:16" => 9d / 16,
        "5:4" => 5d / 4,
        "4:5" => 4d / 5,
        "A4 Portrait" => 1d / Math.Sqrt(2),
        "A4 Landscape" => Math.Sqrt(2),
        "Custom" when _customHeight > 0 => _customWidth / _customHeight,
        _ => null
    };

    private static bool TryReadPositive(TextBox field, out double value) =>
        double.TryParse(field.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) && value > 0;

    private static bool TryReadPositiveOrZero(TextBox field, out double value) =>
        double.TryParse(field.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) && value >= 0;

    private void Lock_Click(object sender, RoutedEventArgs e)
    {
        _aspectLocked = !_aspectLocked;
        var ratio = GetAspectRatio(_draftAspectRatio);
        if (_aspectLocked && ratio is > 0)
            _draftCrop = CropMath.ApplyRatio(_draftCrop, ratio.Value, _item.OriginalWidth, _item.OriginalHeight);
        UpdateOverlayAndFields();
    }

    private void RecentSizesBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_synchronizing || RecentSizesBox.SelectedItem is not RecentCropSize size) return;
        _draftCrop = CropMath.SetDimension(_draftCrop, true, size.Width, _item.OriginalWidth, _item.OriginalHeight, null);
        _draftCrop = CropMath.SetDimension(_draftCrop, false, size.Height, _item.OriginalWidth, _item.OriginalHeight, null);
        UpdateOverlayAndFields();
        RecentSizesBox.SelectedIndex = -1;
    }

    private void RefreshRecentSizes()
    {
        _synchronizing = true;
        RecentSizesBox.Items.Clear();
        RecentSizesBox.Items.Add(new ComboBoxItem { Content = "Choose a recent size…", IsEnabled = false });
        foreach (var size in _settings.RecentCropSizes) RecentSizesBox.Items.Add(size);
        RecentSizesBox.SelectedIndex = 0;
        _synchronizing = false;
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e) { _zoom = Math.Max(.1, _zoom - .1); UpdatePreviewLayout(); UpdateOverlayAndFields(); }
    private void ZoomIn_Click(object sender, RoutedEventArgs e) { _zoom = Math.Min(4, _zoom + .1); UpdatePreviewLayout(); UpdateOverlayAndFields(); }
    private void Fit_Click(object sender, RoutedEventArgs e) { _zoom = 1; UpdatePreviewLayout(); UpdateOverlayAndFields(); }
    private void ActualSize_Click(object sender, RoutedEventArgs e)
    {
        var viewportWidth = PreviewViewport.ViewportWidth > 0 ? PreviewViewport.ViewportWidth : PreviewViewport.ActualWidth;
        var viewportHeight = PreviewViewport.ViewportHeight > 0 ? PreviewViewport.ViewportHeight : PreviewViewport.ActualHeight;
        if (viewportWidth <= 0 || viewportHeight <= 0) return;
        var fit = CropViewportGeometry.Calculate(viewportWidth, viewportHeight, _item.OriginalWidth, _item.OriginalHeight, 1).Scale;
        _zoom = Math.Clamp(1 / fit, .1, 4);
        UpdatePreviewLayout(); UpdateOverlayAndFields();
    }

    private async void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (!_item.Undo()) return;
        await LoadItemAsync(_item);
        await _refreshThumbnailAsync(_item);
    }

    private async void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (!_item.Redo()) return;
        await LoadItemAsync(_item);
        await _refreshThumbnailAsync(_item);
    }

    private static bool IsEditableTextBoxFocused() => Keyboard.FocusedElement is TextBox textBox && !textBox.IsReadOnly;

    private static string FormatPixel(double value) =>
        CropViewportGeometry.NormalizePixel(value).ToString(CultureInfo.CurrentCulture);
}
