using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using PrecisionImageCropper.Models;
using PrecisionImageCropper.Services;
using PrecisionImageCropper.Utilities;
using PrecisionImageCropper.ViewModels;

namespace PrecisionImageCropper;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private BitmapSource? _source;
    private string? _sourcePath;
    private string? _recentFolder;
    private bool _updatingControls;
    private double? _aspectRatio;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        CropLayer.CropChanged += (_, crop) => { _viewModel.Crop = crop; UpdateControlsFromModel(); };
        UpdateControlsFromModel();
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var path = FileDialogService.OpenImage(_recentFolder);
        if (path is not null) OpenImage(path);
    }

    private void OpenImage(string path)
    {
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            var loaded = ImageService.Load(path);
            _source = loaded.Source; _sourcePath = path; _recentFolder = Path.GetDirectoryName(path);
            _viewModel.Load(_source);
            var crop = CropMath.Default(loaded.Width, loaded.Height);
            CropLayer.SourceWidth = loaded.Width; CropLayer.SourceHeight = loaded.Height; CropLayer.Crop = crop;
            _viewModel.Crop = crop; PreviewImage.Source = _source;
            SetZoom(1); FitImage(); UpdateControlsFromModel();
        }
        catch (Exception ex) { ShowError("Unable to open image", ex); }
        finally { Mouse.OverrideCursor = null; }
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (_source is null) return;
        _aspectRatio = null; AspectCombo.SelectedIndex = 0; CropLayer.AspectRatio = null;
        SetCrop(CropMath.Default(_source.PixelWidth, _source.PixelHeight));
    }
    private void Fit_Click(object sender, RoutedEventArgs e) => FitImage();
    private void Actual_Click(object sender, RoutedEventArgs e) => SetZoom(1);
    private void FitImage()
    {
        if (_source is null || ImageScroll.ViewportWidth <= 0 || ImageScroll.ViewportHeight <= 0) return;
        SetZoom(Math.Min(ImageScroll.ViewportWidth / _source.PixelWidth, ImageScroll.ViewportHeight / _source.PixelHeight));
    }
    private void SetZoom(double zoom)
    {
        if (_source is null) return;
        zoom = Math.Clamp(zoom, .05, 4); _viewModel.Zoom = zoom;
        ImageHost.Width = _source.PixelWidth * zoom; ImageHost.Height = _source.PixelHeight * zoom;
        PreviewImage.Width = ImageHost.Width; PreviewImage.Height = ImageHost.Height;
        CropLayer.Width = ImageHost.Width; CropLayer.Height = ImageHost.Height; CropLayer.Scale = zoom;
        CropLayer.InvalidateVisual(); UpdateControlsFromModel();
    }
    private void ZoomCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_source is null || ZoomCombo.SelectedItem is not ComboBoxItem item) return;
        if (double.TryParse(item.Content?.ToString()?.TrimEnd('%'), out var percent)) SetZoom(percent / 100);
    }
    private void ImageScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_source is null) return;
        var pointer = e.GetPosition(ImageScroll); var contentPoint = e.GetPosition(ImageHost); var oldZoom = _viewModel.Zoom;
        var newZoom = Math.Clamp(oldZoom * (e.Delta > 0 ? 1.15 : 1 / 1.15), .05, 4);
        SetZoom(newZoom);
        ImageScroll.ScrollToHorizontalOffset(Math.Max(0, contentPoint.X / oldZoom * newZoom - pointer.X));
        ImageScroll.ScrollToVerticalOffset(Math.Max(0, contentPoint.Y / oldZoom * newZoom - pointer.Y));
        e.Handled = true;
    }
    private void ImageScroll_SizeChanged(object sender, SizeChangedEventArgs e) { if (_source is not null && _viewModel.Zoom == 1) FitImage(); }

    private void Geometry_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingControls || _source is null) return;
        if (!TryNumber(WidthBox.Text, out var width) || !TryNumber(HeightBox.Text, out var height) || !TryNumber(XBox.Text, out var x) || !TryNumber(YBox.Text, out var y)) return;
        var crop = new CropRect(x, y, width, height);
        if (_aspectRatio is > 0) crop = CropMath.ApplyRatio(crop, _aspectRatio.Value, _source.PixelWidth, _source.PixelHeight);
        SetCrop(CropMath.Clamp(crop, _source.PixelWidth, _source.PixelHeight));
    }
    private void AspectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_source is null || AspectCombo.SelectedItem is not ComboBoxItem item) return;
        _aspectRatio = RatioFor(item.Content?.ToString() ?? "FreeForm"); CropLayer.AspectRatio = _aspectRatio;
        if (_aspectRatio is > 0) SetCrop(CropMath.ApplyRatio(_viewModel.Crop, _aspectRatio.Value, _source.PixelWidth, _source.PixelHeight));
    }
    private double? RatioFor(string choice) => choice switch
    {
        "Original" when _source is not null => (double)_source.PixelWidth / _source.PixelHeight,
        "1:1" => 1, "4:3" => 4d / 3, "3:2" => 3d / 2, "16:9" => 16d / 9, "9:16" => 9d / 16,
        "5:4" => 5d / 4, "4:5" => 4d / 5, "A4 Portrait" => 210d / 297, "A4 Landscape" => 297d / 210,
        "Custom" => _viewModel.Crop.Height > 0 ? _viewModel.Crop.Width / _viewModel.Crop.Height : null, _ => null
    };

    private void UnitsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateUnitControls();
    private void Unit_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingControls || _source is null || UnitsCombo.SelectedItem is not ComboBoxItem unit || unit.Content?.ToString() == "Pixels") { UpdateCalculatedPixels(); return; }
        if (!TryNumber(UnitWidthBox.Text, out var width) || !TryNumber(UnitHeightBox.Text, out var height) || !TryNumber(DpiBox.Text, out var dpi) || dpi <= 0) { UpdateCalculatedPixels(); return; }
        var multiplier = unit.Content.ToString() switch { "Inches" => dpi, "Centimeters" => dpi / 2.54, "Millimeters" => dpi / 25.4, _ => 1 };
        var crop = _viewModel.Crop; crop.Width = width * multiplier; crop.Height = height * multiplier;
        if (_aspectRatio is > 0) crop = CropMath.ApplyRatio(crop, _aspectRatio.Value, _source.PixelWidth, _source.PixelHeight);
        SetCrop(CropMath.Clamp(crop, _source.PixelWidth, _source.PixelHeight));
    }
    private void UpdateControlsFromModel()
    {
        _updatingControls = true;
        WidthBox.Text = _viewModel.WidthText; HeightBox.Text = _viewModel.HeightText; XBox.Text = _viewModel.XText; YBox.Text = _viewModel.YText;
        InfoText.Text = _source is null ? "Open an image to start cropping." : $"Original Width: {_source.PixelWidth:N0} px\nOriginal Height: {_source.PixelHeight:N0} px\nCrop Width: {_viewModel.Crop.Width:0} px\nCrop Height: {_viewModel.Crop.Height:0} px\nOutput Size: {_viewModel.Crop.Width:0} × {_viewModel.Crop.Height:0} px\nZoom: {_viewModel.ZoomText}";
        UpdateUnitControls(); _updatingControls = false;
    }
    private void UpdateUnitControls()
    {
        if (UnitsCombo.SelectedItem is not ComboBoxItem unit) return;
        var multiplier = unit.Content?.ToString() switch { "Inches" => GetDpi(), "Centimeters" => GetDpi() / 2.54, "Millimeters" => GetDpi() / 25.4, _ => 1 };
        if (_updatingControls) { UnitWidthBox.Text = (_viewModel.Crop.Width / multiplier).ToString("0.##"); UnitHeightBox.Text = (_viewModel.Crop.Height / multiplier).ToString("0.##"); }
        UpdateCalculatedPixels();
    }
    private void UpdateCalculatedPixels() => CalculatedPixels.Text = _source is null ? "" : $"Calculated: {_viewModel.Crop.Width:0} × {_viewModel.Crop.Height:0} pixels";
    private double GetDpi() => TryNumber(DpiBox.Text, out var dpi) && dpi > 0 ? dpi : 300;

    private async void CropSave_Click(object sender, RoutedEventArgs e)
    {
        if (_source is null || _sourcePath is null) { MessageBox.Show(this, "Open an image before saving a crop.", Title, MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var path = FileDialogService.SaveImage($"{Path.GetFileNameWithoutExtension(_sourcePath)}-crop.png"); if (path is null) return;
        try
        {
            BusyIndicator.Visibility = Visibility.Visible; CropSaveButton.IsEnabled = false;
            var crop = _viewModel.Crop; await Task.Run(() => CropService.Save(_source, crop, path, 95));
            var saved = CropMath.ToPixelRect(crop, _source.PixelWidth, _source.PixelHeight);
            MessageBox.Show(this, $"Saved {saved.Width} × {saved.Height} pixel crop.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { ShowError("Unable to save crop", ex); }
        finally { BusyIndicator.Visibility = Visibility.Collapsed; CropSaveButton.IsEnabled = true; }
    }
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.O) { Open_Click(this, e); e.Handled = true; return; }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S) { CropSave_Click(this, e); e.Handled = true; return; }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.D0) { FitImage(); e.Handled = true; return; }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.D1) { SetZoom(1); e.Handled = true; return; }
        if (e.Key == Key.R && Keyboard.Modifiers == ModifierKeys.None) { Reset_Click(this, e); e.Handled = true; return; }
        if (e.Key == Key.Escape) { CropLayer.CancelOperation(); e.Handled = true; return; }
        if (_source is null) return;
        var step = Keyboard.Modifiers == ModifierKeys.Shift ? 10 : 1; var dx = e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0; var dy = e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0;
        if (dx != 0 || dy != 0) { SetCrop(CropMath.Move(_viewModel.Crop, dx, dy, _source.PixelWidth, _source.PixelHeight)); e.Handled = true; }
    }
    private void Window_DragOver(object sender, DragEventArgs e) => e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
    private void Window_Drop(object sender, DragEventArgs e) { if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) OpenImage(files[0]); }
    private void SetCrop(CropRect crop) { if (_source is null) return; crop = CropMath.Clamp(crop, _source.PixelWidth, _source.PixelHeight); _viewModel.Crop = crop; CropLayer.Crop = crop; UpdateControlsFromModel(); }
    private static bool TryNumber(string text, out double value) => double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) && value >= 0;
    private void ShowError(string title, Exception error) => MessageBox.Show(this, $"{title}.\n\n{error.Message}", this.Title, MessageBoxButton.OK, MessageBoxImage.Error);
}
