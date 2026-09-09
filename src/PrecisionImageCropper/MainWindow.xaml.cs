using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using PrecisionImageCropper.Models;
using PrecisionImageCropper.Services;
using PrecisionImageCropper.Utilities;
using PrecisionImageCropper.ViewModels;

namespace PrecisionImageCropper
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel = new();

        private BitmapSource? _source;
        private string? _sourcePath;
        private string? _sourceName;
        private string? _sourceExtension;
        private string? _recentFolder;

        private bool _updatingControls;
        private bool _isInitializing = true;

        private double? _aspectRatio;


        public MainWindow()
        {
            _isInitializing = true;

            InitializeComponent();

            DataContext = _viewModel;

            CropLayer.CropChanged += (_, crop) =>
            {
                if (_isInitializing)
                    return;

                _viewModel.Crop = crop;
                UpdateControlsFromModel();
            };

            _isInitializing = false;

            UpdateControlsFromModel();
        }


        // ============================================================
        // OPEN IMAGE
        // ============================================================

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            var path = FileDialogService.OpenImage(_recentFolder);

            if (path is not null)
            {
                LoadImageFromFile(path);
            }
        }


        private void LoadImageFromFile(string path)
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;

                var loaded = ImageService.Load(path);
                _recentFolder = Path.GetDirectoryName(path);

                InitializeImage(
                    loaded.Source,
                    Path.GetFileName(path),
                    Path.GetExtension(path),
                    path);
            }
            catch (Exception ex)
            {
                ShowError("Unable to open image", ex);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }


        private void LoadImageFromBitmap(
            BitmapSource source,
            string sourceName,
            string sourceExtension)
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;

                InitializeImage(
                    ImageService.CopyBitmap(source),
                    sourceName,
                    sourceExtension,
                    null);
            }
            catch (Exception ex)
            {
                ShowError("Unable to paste image", ex);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }


        // All load sources flow through this method, so the crop model remains
        // in original source-image pixels regardless of where the image came from.
        private void InitializeImage(
            BitmapSource source,
            string sourceName,
            string sourceExtension,
            string? sourcePath)
        {
            _source = source;
            _sourcePath = sourcePath;
            _sourceName = Path.GetFileNameWithoutExtension(sourceName);
            _sourceExtension = string.IsNullOrWhiteSpace(sourceExtension)
                ? ".png"
                : sourceExtension;

            _viewModel.Load(_source);

            var crop = CropMath.Default(source.PixelWidth, source.PixelHeight);

            CropLayer.SourceWidth = source.PixelWidth;
            CropLayer.SourceHeight = source.PixelHeight;
            CropLayer.Crop = crop;

            _viewModel.Crop = crop;
            PreviewImage.Source = _source;

            SetZoom(1.0);
            FitImage();
            UpdateControlsFromModel();
        }


        private void PasteImage_Click(object sender, RoutedEventArgs e)
        {
            PasteImageFromClipboard();
        }


        private void PasteImageFromClipboard()
        {
            string? imagePath = null;
            BitmapSource? bitmap = null;
            Exception? clipboardError = null;

            // Another application can briefly lock the Windows clipboard.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (Clipboard.ContainsFileDropList())
                    {
                        var files = Clipboard.GetFileDropList();
                        foreach (string? file in files)
                        {
                            if (!string.IsNullOrWhiteSpace(file) &&
                                ImageService.IsSupportedFile(file))
                            {
                                imagePath = file;
                                break;
                            }
                        }
                    }

                    if (imagePath is null && Clipboard.ContainsImage())
                    {
                        var clipboardImage = Clipboard.GetImage();
                        if (clipboardImage is not null)
                        {
                            bitmap = ImageService.CopyBitmap(clipboardImage);
                        }
                    }

                    break;
                }
                catch (COMException ex)
                {
                    clipboardError = ex;
                }
                catch (ExternalException ex)
                {
                    clipboardError = ex;
                }
                catch (InvalidOperationException ex)
                {
                    clipboardError = ex;
                }

                Thread.Sleep(50);
            }

            if (imagePath is not null)
            {
                LoadImageFromFile(imagePath);
                return;
            }

            if (bitmap is not null)
            {
                LoadImageFromBitmap(bitmap, "Clipboard Image", ".png");
                return;
            }

            if (clipboardError is not null)
            {
                ShowError("Unable to access the clipboard", clipboardError);
                return;
            }

            MessageBox.Show(
                this,
                "No supported image was found on the clipboard.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }


        // ============================================================
        // RESET
        // ============================================================

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            if (_source is null)
                return;

            _aspectRatio = null;

            _updatingControls = true;

            try
            {
                AspectCombo.SelectedIndex = 0;
            }
            finally
            {
                _updatingControls = false;
            }

            CropLayer.AspectRatio = null;

            SetCrop(
                CropMath.Default(
                    _source.PixelWidth,
                    _source.PixelHeight));
        }


        // ============================================================
        // ZOOM
        // ============================================================

        private void Fit_Click(object sender, RoutedEventArgs e)
        {
            FitImage();
        }


        private void Actual_Click(object sender, RoutedEventArgs e)
        {
            SetZoom(1.0);
        }


        private void FitImage()
        {
            if (_source is null)
                return;

            if (ImageScroll.ViewportWidth <= 0 ||
                ImageScroll.ViewportHeight <= 0)
            {
                return;
            }

            double availableWidth =
                Math.Max(1, ImageScroll.ViewportWidth - 4);

            double availableHeight =
                Math.Max(1, ImageScroll.ViewportHeight - 4);

            double zoomX =
                availableWidth / _source.PixelWidth;

            double zoomY =
                availableHeight / _source.PixelHeight;

            double zoom =
                Math.Min(zoomX, zoomY);

            SetZoom(zoom);
        }


        private void SetZoom(double zoom)
        {
            if (_source is null)
                return;

            zoom = Math.Clamp(
                zoom,
                0.05,
                4.0);

            _viewModel.Zoom = zoom;

            double displayWidth =
                _source.PixelWidth * zoom;

            double displayHeight =
                _source.PixelHeight * zoom;

            ImageHost.Width = displayWidth;
            ImageHost.Height = displayHeight;

            PreviewImage.Width = displayWidth;
            PreviewImage.Height = displayHeight;

            CropLayer.Width = displayWidth;
            CropLayer.Height = displayHeight;

            CropLayer.Scale = zoom;

            CropLayer.InvalidateVisual();

            UpdateControlsFromModel();
        }


        private void ZoomCombo_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (_isInitializing ||
                _updatingControls)
            {
                return;
            }

            if (_source is null)
                return;

            if (ZoomCombo.SelectedItem is not ComboBoxItem item)
                return;

            string? content =
                item.Content?.ToString();

            if (string.IsNullOrWhiteSpace(content))
                return;

            content =
                content.TrimEnd('%');

            if (double.TryParse(
                    content,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double percent))
            {
                SetZoom(
                    percent / 100.0);
            }
        }


        private void ImageScroll_PreviewMouseWheel(
            object sender,
            MouseWheelEventArgs e)
        {
            if (_source is null)
                return;

            var pointer =
                e.GetPosition(ImageScroll);

            var contentPoint =
                e.GetPosition(ImageHost);

            double oldZoom =
                _viewModel.Zoom;

            if (oldZoom <= 0)
                oldZoom = 1.0;

            double factor =
                e.Delta > 0
                    ? 1.15
                    : 1.0 / 1.15;

            double newZoom =
                Math.Clamp(
                    oldZoom * factor,
                    0.05,
                    4.0);

            SetZoom(newZoom);

            double horizontal =
                contentPoint.X / oldZoom * newZoom -
                pointer.X;

            double vertical =
                contentPoint.Y / oldZoom * newZoom -
                pointer.Y;

            ImageScroll.ScrollToHorizontalOffset(
                Math.Max(0, horizontal));

            ImageScroll.ScrollToVerticalOffset(
                Math.Max(0, vertical));

            e.Handled = true;
        }


        private void ImageScroll_SizeChanged(
            object sender,
            SizeChangedEventArgs e)
        {
            if (_isInitializing)
                return;

            if (_source is null)
                return;

            /*
             * Do not continuously force Fit mode.
             * Only refit when the displayed image is currently
             * approximately at source scale.
             */

            if (Math.Abs(
                    _viewModel.Zoom - 1.0) < 0.0001)
            {
                FitImage();
            }
        }


        // ============================================================
        // CROP GEOMETRY
        // ============================================================

        private void Geometry_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            if (_isInitializing ||
                _updatingControls ||
                _source is null)
            {
                return;
            }

            if (!TryNumber(
                    WidthBox.Text,
                    out double width))
            {
                return;
            }

            if (!TryNumber(
                    HeightBox.Text,
                    out double height))
            {
                return;
            }

            if (!TryNumber(
                    XBox.Text,
                    out double x))
            {
                return;
            }

            if (!TryNumber(
                    YBox.Text,
                    out double y))
            {
                return;
            }

            if (width <= 0 ||
                height <= 0)
            {
                return;
            }

            var crop =
                new CropRect(
                    x,
                    y,
                    width,
                    height);

            if (_aspectRatio is > 0)
            {
                crop =
                    CropMath.ApplyRatio(
                        crop,
                        _aspectRatio.Value,
                        _source.PixelWidth,
                        _source.PixelHeight);
            }

            crop =
                CropMath.Clamp(
                    crop,
                    _source.PixelWidth,
                    _source.PixelHeight);

            SetCrop(crop);
        }


        // ============================================================
        // ASPECT RATIO
        // ============================================================

        private void AspectCombo_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (_isInitializing ||
                _updatingControls)
            {
                return;
            }

            if (_source is null)
                return;

            if (AspectCombo.SelectedItem
                is not ComboBoxItem item)
            {
                return;
            }

            string choice =
                item.Content?.ToString()
                ?? "FreeForm";

            _aspectRatio =
                RatioFor(choice);

            CropLayer.AspectRatio =
                _aspectRatio;

            if (_aspectRatio is > 0)
            {
                var crop =
                    CropMath.ApplyRatio(
                        _viewModel.Crop,
                        _aspectRatio.Value,
                        _source.PixelWidth,
                        _source.PixelHeight);

                SetCrop(crop);
            }
        }


        private double? RatioFor(string choice)
        {
            return choice switch
            {
                "Original"
                    when _source is not null
                    =>
                    (double)_source.PixelWidth /
                    _source.PixelHeight,

                "1:1" =>
                    1.0,

                "4:3" =>
                    4.0 / 3.0,

                "3:2" =>
                    3.0 / 2.0,

                "16:9" =>
                    16.0 / 9.0,

                "9:16" =>
                    9.0 / 16.0,

                "5:4" =>
                    5.0 / 4.0,

                "4:5" =>
                    4.0 / 5.0,

                "A4 Portrait" =>
                    210.0 / 297.0,

                "A4 Landscape" =>
                    297.0 / 210.0,

                "Custom"
                    when _viewModel.Crop.Height > 0
                    =>
                    _viewModel.Crop.Width /
                    _viewModel.Crop.Height,

                _ =>
                    null
            };
        }


        // ============================================================
        // UNITS
        // ============================================================

        private void UnitsCombo_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (_isInitializing ||
                _updatingControls)
            {
                return;
            }

            UpdateUnitControls();
        }


        private void Unit_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            /*
             * These TextChanged events can fire WHILE
             * InitializeComponent() is still building the XAML.
             *
             * That was the source of the startup crash.
             */

            if (_isInitializing ||
                _updatingControls)
            {
                return;
            }

            if (_source is null)
            {
                UpdateCalculatedPixels();
                return;
            }

            if (UnitsCombo is null ||
                UnitWidthBox is null ||
                UnitHeightBox is null ||
                DpiBox is null ||
                CalculatedPixels is null)
            {
                return;
            }

            if (UnitsCombo.SelectedItem
                is not ComboBoxItem unit)
            {
                UpdateCalculatedPixels();
                return;
            }

            string unitName =
                unit.Content?.ToString()
                ?? "Pixels";

            if (unitName == "Pixels")
            {
                UpdateCalculatedPixels();
                return;
            }

            if (!TryNumber(
                    UnitWidthBox.Text,
                    out double width))
            {
                UpdateCalculatedPixels();
                return;
            }

            if (!TryNumber(
                    UnitHeightBox.Text,
                    out double height))
            {
                UpdateCalculatedPixels();
                return;
            }

            if (!TryNumber(
                    DpiBox.Text,
                    out double dpi) ||
                dpi <= 0)
            {
                UpdateCalculatedPixels();
                return;
            }

            double multiplier =
                unitName switch
                {
                    "Inches" =>
                        dpi,

                    "Centimeters" =>
                        dpi / 2.54,

                    "Millimeters" =>
                        dpi / 25.4,

                    _ =>
                        1.0
                };

            var crop =
                _viewModel.Crop;

            crop.Width =
                width * multiplier;

            crop.Height =
                height * multiplier;

            if (_aspectRatio is > 0)
            {
                crop =
                    CropMath.ApplyRatio(
                        crop,
                        _aspectRatio.Value,
                        _source.PixelWidth,
                        _source.PixelHeight);
            }

            crop =
                CropMath.Clamp(
                    crop,
                    _source.PixelWidth,
                    _source.PixelHeight);

            SetCrop(crop);
        }


        private void UpdateControlsFromModel()
        {
            /*
             * Never update controls before WPF has completed
             * InitializeComponent().
             */

            if (_isInitializing)
                return;

            if (WidthBox is null ||
                HeightBox is null ||
                XBox is null ||
                YBox is null ||
                InfoText is null)
            {
                return;
            }

            _updatingControls = true;

            try
            {
                WidthBox.Text =
                    _viewModel.WidthText;

                HeightBox.Text =
                    _viewModel.HeightText;

                XBox.Text =
                    _viewModel.XText;

                YBox.Text =
                    _viewModel.YText;

                if (_source is null)
                {
                    InfoText.Text =
                        "Open an image to start cropping.";
                }
                else
                {
                    InfoText.Text =
                        $"Original Width: {_source.PixelWidth:N0} px\n" +
                        $"Original Height: {_source.PixelHeight:N0} px\n" +
                        $"Crop Width: {_viewModel.Crop.Width:0} px\n" +
                        $"Crop Height: {_viewModel.Crop.Height:0} px\n" +
                        $"Output Size: {_viewModel.Crop.Width:0} × {_viewModel.Crop.Height:0} px\n" +
                        $"Zoom: {_viewModel.ZoomText}";
                }

                UpdateUnitControls();
            }
            finally
            {
                _updatingControls = false;
            }
        }


        private void UpdateUnitControls()
        {
            if (_isInitializing)
                return;

            if (UnitsCombo is null ||
                UnitWidthBox is null ||
                UnitHeightBox is null ||
                DpiBox is null ||
                CalculatedPixels is null)
            {
                return;
            }

            if (UnitsCombo.SelectedItem
                is not ComboBoxItem unit)
            {
                return;
            }

            string unitName =
                unit.Content?.ToString()
                ?? "Pixels";

            double dpi =
                GetDpi();

            double multiplier =
                unitName switch
                {
                    "Inches" =>
                        dpi,

                    "Centimeters" =>
                        dpi / 2.54,

                    "Millimeters" =>
                        dpi / 25.4,

                    _ =>
                        1.0
                };

            if (multiplier <= 0)
                multiplier = 1.0;

            /*
             * Update these fields programmatically.
             * _updatingControls prevents their TextChanged
             * events from feeding back into the crop model.
             */

            bool wasUpdating =
                _updatingControls;

            _updatingControls = true;

            try
            {
                UnitWidthBox.Text =
                    (_viewModel.Crop.Width / multiplier)
                    .ToString(
                        "0.##",
                        CultureInfo.CurrentCulture);

                UnitHeightBox.Text =
                    (_viewModel.Crop.Height / multiplier)
                    .ToString(
                        "0.##",
                        CultureInfo.CurrentCulture);
            }
            finally
            {
                _updatingControls =
                    wasUpdating;
            }

            UpdateCalculatedPixels();
        }


        private void UpdateCalculatedPixels()
        {
            if (_isInitializing)
                return;

            if (CalculatedPixels is null)
                return;

            if (_source is null)
            {
                CalculatedPixels.Text =
                    string.Empty;

                return;
            }

            CalculatedPixels.Text =
                $"Calculated: " +
                $"{_viewModel.Crop.Width:0} × " +
                $"{_viewModel.Crop.Height:0} pixels";
        }


        private double GetDpi()
        {
            if (_isInitializing ||
                DpiBox is null)
            {
                return 300.0;
            }

            if (TryNumber(
                    DpiBox.Text,
                    out double dpi) &&
                dpi > 0)
            {
                return dpi;
            }

            return 300.0;
        }


        // ============================================================
        // SAVE CROP
        // ============================================================

        private async void CropSave_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_source is null)
            {
                MessageBox.Show(
                    this,
                    "Open or paste an image before saving a crop.",
                    Title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            string sourceExtension = _sourceExtension ?? ".png";
            string sourceName = _sourceName ?? "Clipboard Image";
            string suggestedName = $"{sourceName}-crop{sourceExtension}";

            string? path =
                FileDialogService.SaveImage(
                    suggestedName,
                    sourceExtension);

            if (path is null)
                return;

            try
            {
                BusyIndicator.Visibility =
                    Visibility.Visible;

                CropSaveButton.IsEnabled =
                    false;

                Mouse.OverrideCursor =
                    Cursors.Wait;

                var crop =
                    _viewModel.Crop;

                int quality = 95;

                if (TryNumber(
                        QualityBox.Text,
                        out double requestedQuality))
                {
                    quality =
                        (int)Math.Round(
                            requestedQuality);
                }

                quality =
                    Math.Clamp(
                        quality,
                        1,
                        100);

                BitmapSource source =
                    _source;

                await Task.Run(
                    () =>
                    {
                        CropService.Save(
                            source,
                            crop,
                            path,
                            quality);
                    });

                var saved =
                    CropMath.ToPixelRect(
                        crop,
                        source.PixelWidth,
                        source.PixelHeight);

                MessageBox.Show(
                    this,
                    $"Saved {saved.Width} × {saved.Height} pixel crop.",
                    Title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ShowError(
                    "Unable to save crop",
                    ex);
            }
            finally
            {
                BusyIndicator.Visibility =
                    Visibility.Collapsed;

                CropSaveButton.IsEnabled =
                    true;

                Mouse.OverrideCursor =
                    null;
            }
        }


        // ============================================================
        // KEYBOARD SHORTCUTS
        // ============================================================

        private void Window_PreviewKeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (_isInitializing)
                return;

            if (Keyboard.Modifiers == ModifierKeys.Control &&
                e.Key == Key.V &&
                !IsEditableTextBoxFocused())
            {
                PasteImageFromClipboard();
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers ==
                    ModifierKeys.Control &&
                e.Key == Key.O)
            {
                Open_Click(
                    this,
                    e);

                e.Handled = true;

                return;
            }

            if (Keyboard.Modifiers ==
                    ModifierKeys.Control &&
                e.Key == Key.S)
            {
                CropSave_Click(
                    this,
                    e);

                e.Handled = true;

                return;
            }

            if (Keyboard.Modifiers ==
                    ModifierKeys.Control &&
                e.Key == Key.D0)
            {
                FitImage();

                e.Handled = true;

                return;
            }

            if (Keyboard.Modifiers ==
                    ModifierKeys.Control &&
                e.Key == Key.D1)
            {
                SetZoom(1.0);

                e.Handled = true;

                return;
            }

            if (e.Key == Key.R &&
                Keyboard.Modifiers ==
                    ModifierKeys.None)
            {
                Reset_Click(
                    this,
                    e);

                e.Handled = true;

                return;
            }

            if (e.Key == Key.Escape)
            {
                CropLayer.CancelOperation();

                e.Handled = true;

                return;
            }

            if (_source is null)
                return;

            int step =
                Keyboard.Modifiers ==
                ModifierKeys.Shift
                    ? 10
                    : 1;

            double dx =
                e.Key switch
                {
                    Key.Left => -step,
                    Key.Right => step,
                    _ => 0
                };

            double dy =
                e.Key switch
                {
                    Key.Up => -step,
                    Key.Down => step,
                    _ => 0
                };

            if (dx != 0 ||
                dy != 0)
            {
                SetCrop(
                    CropMath.Move(
                        _viewModel.Crop,
                        dx,
                        dy,
                        _source.PixelWidth,
                        _source.PixelHeight));

                e.Handled = true;
            }
        }


        // ============================================================
        // DRAG & DROP
        // ============================================================

        private void Window_DragOver(
            object sender,
            DragEventArgs e)
        {
            if (e.Data.GetDataPresent(
                    DataFormats.FileDrop))
            {
                e.Effects =
                    DragDropEffects.Copy;
            }
            else
            {
                e.Effects =
                    DragDropEffects.None;
            }

            e.Handled = true;
        }


        private void Window_Drop(
            object sender,
            DragEventArgs e)
        {
            if (e.Data.GetData(
                    DataFormats.FileDrop)
                is not string[] files)
            {
                return;
            }

            if (files.Length == 0)
                return;

            LoadImageFromFile(
                files[0]);
        }


        private static bool IsEditableTextBoxFocused() =>
            Keyboard.FocusedElement is TextBox textBox &&
            !textBox.IsReadOnly;


        // ============================================================
        // CROP MODEL HELPERS
        // ============================================================

        private void SetCrop(
            CropRect crop)
        {
            if (_source is null)
                return;

            crop =
                CropMath.Clamp(
                    crop,
                    _source.PixelWidth,
                    _source.PixelHeight);

            _viewModel.Crop =
                crop;

            CropLayer.Crop =
                crop;

            UpdateControlsFromModel();
        }


        private static bool TryNumber(
            string text,
            out double value)
        {
            if (!double.TryParse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.CurrentCulture,
                    out value))
            {
                /*
                 * Also accept dot-decimal input on machines whose
                 * regional settings use another decimal separator.
                 */
                if (!double.TryParse(
                        text,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out value))
                {
                    return false;
                }
            }

            return !double.IsNaN(value) &&
                   !double.IsInfinity(value) &&
                   value >= 0;
        }


        private void ShowError(
            string title,
            Exception error)
        {
            MessageBox.Show(
                this,
                $"{title}.\n\n{error.Message}",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
