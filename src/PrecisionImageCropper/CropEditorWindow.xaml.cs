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
    private double _customWidth = 1;
    private double _customHeight = 1;
    private bool _synchronizing;

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
        _draftAspectRatio = _openedAspectRatio;
        _customWidth = item.CustomAspectRatioWidth;
        _customHeight = item.CustomAspectRatioHeight;
        CustomWidthBox.Text = _customWidth.ToString("0.##", CultureInfo.CurrentCulture);
        CustomHeightBox.Text = _customHeight.ToString("0.##", CultureInfo.CurrentCulture);
        ImageNameText.Text = item.OriginalFileName;
        ImageInfoText.Text = item.OriginalDimensionsText;
        PositionText.Text = $"{_items.IndexOf(item) + 1} of {_items.Count}";
        PreviewLoadingText.Visibility = Visibility.Visible;
        PreviewImage.Source = null;
        UpdateNavigationButtons();
        SelectAspectRatio(_draftAspectRatio);
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
        if (PreviewViewport.ActualWidth <= 0 || PreviewViewport.ActualHeight <= 0) return;
        var availableWidth = Math.Max(1, PreviewViewport.ActualWidth - 28);
        var availableHeight = Math.Max(1, PreviewViewport.ActualHeight - 28);
        var fitScale = Math.Min(availableWidth / _item.OriginalWidth, availableHeight / _item.OriginalHeight);
        var scale = Math.Max(0.001, fitScale * ZoomSlider.Value);
        var width = _item.OriginalWidth * scale;
        var height = _item.OriginalHeight * scale;
        PreviewCanvas.Width = width;
        PreviewCanvas.Height = height;
        PreviewImage.Width = width;
        PreviewImage.Height = height;
        CropOverlay.Width = width;
        CropOverlay.Height = height;
        CropOverlay.SourceWidth = _item.OriginalWidth;
        CropOverlay.SourceHeight = _item.OriginalHeight;
        CropOverlay.Scale = scale;
        CropOverlay.Crop = _draftCrop;
    }

    private void UpdateOverlayAndFields()
    {
        _synchronizing = true;
        CropOverlay.Crop = _draftCrop;
        CropOverlay.AspectRatio = GetAspectRatio(_draftAspectRatio);
        WidthBox.Text = _draftCrop.Width.ToString("0.##", CultureInfo.CurrentCulture);
        HeightBox.Text = _draftCrop.Height.ToString("0.##", CultureInfo.CurrentCulture);
        XBox.Text = _draftCrop.X.ToString("0.##", CultureInfo.CurrentCulture);
        YBox.Text = _draftCrop.Y.ToString("0.##", CultureInfo.CurrentCulture);
        ZoomText.Text = $"{ZoomSlider.Value * 100:0}%";
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

        var crop = _draftCrop.Clone();
        var ratio = GetAspectRatio(_draftAspectRatio);
        switch (property)
        {
            case "Width":
                if (ratio is > 0)
                {
                    var maxW = Math.Min(_item.OriginalWidth, _item.OriginalHeight * ratio.Value);
                    crop.Width = Math.Clamp(value, CropMath.MinSize, maxW);
                    crop.Height = crop.Width / ratio.Value;
                }
                else
                {
                    crop.Width = value;
                }
                break;
            case "Height":
                if (ratio is > 0)
                {
                    var maxH = Math.Min(_item.OriginalHeight, _item.OriginalWidth / ratio.Value);
                    crop.Height = Math.Clamp(value, CropMath.MinSize, maxH);
                    crop.Width = crop.Height * ratio.Value;
                }
                else
                {
                    crop.Height = value;
                }
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

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        UpdatePreviewLayout();
        UpdateOverlayAndFields();
    }

    private void PreviewViewport_SizeChanged(object sender, SizeChangedEventArgs e) => UpdatePreviewLayout();

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _draftCrop = new CropRect(0, 0, _item.OriginalWidth, _item.OriginalHeight);
        _draftAspectRatio = "FreeForm";
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
            _item.CropRectangle = _draftCrop;
            _item.SelectedAspectRatio = _draftAspectRatio;
            _item.CustomAspectRatioWidth = _customWidth;
            _item.CustomAspectRatioHeight = _customHeight;
            await _refreshThumbnailAsync(_item);
            _openedCrop = _draftCrop.Clone();
            _openedAspectRatio = _draftAspectRatio;
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
        !string.Equals(_draftAspectRatio, _openedAspectRatio, StringComparison.Ordinal);

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
}
