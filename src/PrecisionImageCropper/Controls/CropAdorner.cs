using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PrecisionImageCropper.Models;
using PrecisionImageCropper.Utilities;

namespace PrecisionImageCropper.Controls;

/// <summary>Fast visual/input layer. It only changes source-pixel geometry; it never edits bitmap data.</summary>
public sealed class CropAdorner : FrameworkElement
{
    private enum DragMode { None, Create, Move, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }
    private DragMode _mode;
    private Point _startPoint;
    private CropRect _startCrop = new();
    private CropRect _crop = new();
    private double _sourceWidth;
    private double _sourceHeight;
    private double _scale = 1;

    public event EventHandler<CropRect>? CropChanged;
    public event EventHandler? CropOperationStarted;
    public event EventHandler<CropRect>? CropOperationCompleted;
    public CropRect Crop { get => _crop.Clone(); set { _crop = value.Clone(); InvalidateVisual(); } }
    public double SourceWidth { get => _sourceWidth; set { _sourceWidth = value; InvalidateVisual(); } }
    public double SourceHeight { get => _sourceHeight; set { _sourceHeight = value; InvalidateVisual(); } }
    public double Scale { get => _scale; set { _scale = Math.Max(.001, value); InvalidateVisual(); } }
    public double? AspectRatio { get; set; }

    public CropAdorner()
    {
        Focusable = true;
        Cursor = Cursors.Cross;
        SnapsToDevicePixels = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (_sourceWidth <= 0 || _sourceHeight <= 0 || _crop.Width <= 0 || _crop.Height <= 0) return;
        var all = new Rect(0, 0, ActualWidth, ActualHeight);
        var selection = CoordinateConverter.ImageToDisplay(_crop, _scale);
        // Transparent (rather than null) creates a complete crop-body hit target.
        // Grid lines below are only drawn, so they can never own input.
        dc.DrawRectangle(Brushes.Transparent, null, all);
        var mask = new GeometryGroup { FillRule = FillRule.EvenOdd };
        mask.Children.Add(new RectangleGeometry(all));
        mask.Children.Add(new RectangleGeometry(selection));
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(145, 0, 0, 0)), null, mask);
        dc.DrawRectangle(null, new Pen(Brushes.White, 1.5), selection);
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(130, 255, 255, 255)), 1);
        for (var division = 1; division <= 2; division++)
        {
            var x = selection.Left + selection.Width * division / 3;
            var y = selection.Top + selection.Height * division / 3;
            dc.DrawLine(gridPen, new Point(x, selection.Top), new Point(x, selection.Bottom));
            dc.DrawLine(gridPen, new Point(selection.Left, y), new Point(selection.Right, y));
        }
        foreach (var point in CropInteractionGeometry.HandlePoints(selection))
            dc.DrawRectangle(Brushes.White, new Pen(Brushes.Black, 1), new Rect(point.X - 3.5, point.Y - 3.5, 7, 7));
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || _sourceWidth <= 0) return;
        Focus();
        _startPoint = e.GetPosition(this);
        _startCrop = _crop.Clone();
        _mode = GetDragMode(_startPoint);
        CaptureMouse();
        CropOperationStarted?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var point = e.GetPosition(this);
        if (_mode == DragMode.None)
        {
            Cursor = CursorFor(GetDragMode(point));
            return;
        }
        var dx = (point.X - _startPoint.X) / _scale;
        var dy = (point.Y - _startPoint.Y) / _scale;
        var result = _startCrop.Clone();
        if (_mode == DragMode.Move)
            result = CropMath.Move(result, dx, dy, _sourceWidth, _sourceHeight);
        else if (_mode == DragMode.Create)
            result = CreateAt(point);
        else if (AspectRatio is > 0)
            result = ResizeWithRatio(dx, dy);
        else
            result = ResizeFree(dx, dy);
        SetCrop(result);
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (_mode == DragMode.None || e.ChangedButton != MouseButton.Left) return;
        _mode = DragMode.None;
        ReleaseMouseCapture();
        CropOperationCompleted?.Invoke(this, _crop.Clone());
        e.Handled = true;
    }

    public bool CancelOperation()
    {
        if (_mode == DragMode.None) return false;
        _mode = DragMode.None;
        _crop = _startCrop.Clone();
        ReleaseMouseCapture();
        InvalidateVisual();
        CropChanged?.Invoke(this, _crop.Clone());
        CropOperationCompleted?.Invoke(this, _crop.Clone());
        return true;
    }

    private CropRect CreateAt(Point point)
    {
        var x0 = Math.Clamp(_startPoint.X / _scale, 0, _sourceWidth);
        var y0 = Math.Clamp(_startPoint.Y / _scale, 0, _sourceHeight);
        var x1 = Math.Clamp(point.X / _scale, 0, _sourceWidth);
        var y1 = Math.Clamp(point.Y / _scale, 0, _sourceHeight);
        var crop = new CropRect(Math.Min(x0, x1), Math.Min(y0, y1), Math.Abs(x1 - x0), Math.Abs(y1 - y0));
        if (AspectRatio is > 0 && crop.Width > 0 && crop.Height > 0)
        {
            var ratio = AspectRatio.Value;
            var width = Math.Min(crop.Width, crop.Height * ratio);
            var height = width / ratio;
            crop.Width = width; crop.Height = height;
            if (x1 < x0) crop.X = x0 - width;
            if (y1 < y0) crop.Y = y0 - height;
        }
        return CropMath.Clamp(crop, _sourceWidth, _sourceHeight);
    }

    private CropRect ResizeFree(double dx, double dy)
    {
        var c = _startCrop.Clone();
        switch (_mode)
        {
            case DragMode.Left: c.X += dx; c.Width -= dx; break;
            case DragMode.Right: c.Width += dx; break;
            case DragMode.Top: c.Y += dy; c.Height -= dy; break;
            case DragMode.Bottom: c.Height += dy; break;
            case DragMode.TopLeft: c.X += dx; c.Width -= dx; c.Y += dy; c.Height -= dy; break;
            case DragMode.TopRight: c.Width += dx; c.Y += dy; c.Height -= dy; break;
            case DragMode.BottomLeft: c.X += dx; c.Width -= dx; c.Height += dy; break;
            case DragMode.BottomRight: c.Width += dx; c.Height += dy; break;
        }
        // Preserve the opposing edge when the requested size reaches the minimum.
        if (c.Width < CropMath.MinSize) { if (_mode is DragMode.Left or DragMode.TopLeft or DragMode.BottomLeft) c.X = _startCrop.X + _startCrop.Width - CropMath.MinSize; c.Width = CropMath.MinSize; }
        if (c.Height < CropMath.MinSize) { if (_mode is DragMode.Top or DragMode.TopLeft or DragMode.TopRight) c.Y = _startCrop.Y + _startCrop.Height - CropMath.MinSize; c.Height = CropMath.MinSize; }
        return CropMath.Clamp(c, _sourceWidth, _sourceHeight);
    }

    private CropRect ResizeWithRatio(double dx, double dy)
    {
        var ratio = AspectRatio!.Value;
        bool fromLeft = _mode is DragMode.Left or DragMode.TopLeft or DragMode.BottomLeft;
        bool fromTop = _mode is DragMode.Top or DragMode.TopLeft or DragMode.TopRight;
        bool horizontalOnly = _mode is DragMode.Left or DragMode.Right;
        bool verticalOnly = _mode is DragMode.Top or DragMode.Bottom;
        double anchorX = fromLeft ? _startCrop.X + _startCrop.Width : _startCrop.X;
        double anchorY = fromTop ? _startCrop.Y + _startCrop.Height : _startCrop.Y;
        double sx = fromLeft ? -1 : 1;
        double sy = fromTop ? -1 : 1;

        double currentW = Math.Max(CropMath.MinSize, _startCrop.Width + (fromLeft ? -dx : dx));
        double currentH = Math.Max(CropMath.MinSize, _startCrop.Height + (fromTop ? -dy : dy));

        double requestedW;
        if (horizontalOnly)
        {
            requestedW = currentW;
        }
        else if (verticalOnly)
        {
            requestedW = currentH * ratio;
        }
        else
        {
            // Corner drag: follow the axis with the larger movement relative to ratio
            if (Math.Abs(dx) >= Math.Abs(dy) * ratio)
                requestedW = currentW;
            else
                requestedW = currentH * ratio;
        }

        double width = Math.Max(CropMath.MinSize, requestedW);
        var maxW = sx > 0 ? _sourceWidth - anchorX : anchorX;
        var maxH = sy > 0 ? _sourceHeight - anchorY : anchorY;
        width = Math.Min(width, Math.Min(maxW, maxH * ratio));
        width = Math.Max(CropMath.MinSize, width);
        var height = width / ratio;
        var x = sx > 0 ? anchorX : anchorX - width;
        var y = sy > 0 ? anchorY : anchorY - height;
        if (horizontalOnly) { y = _startCrop.Y + (_startCrop.Height - height) / 2; y = Math.Clamp(y, 0, _sourceHeight - height); }
        if (verticalOnly) { x = _startCrop.X + (_startCrop.Width - width) / 2; x = Math.Clamp(x, 0, _sourceWidth - width); }
        return CropMath.Clamp(new CropRect(x, y, width, height), _sourceWidth, _sourceHeight);
    }

    private DragMode GetDragMode(Point point)
    {
        var r = CoordinateConverter.ImageToDisplay(_crop, _scale);
        return CropInteractionGeometry.GetRegion(r, point) switch
        {
            CropPointerRegion.TopLeft => DragMode.TopLeft,
            CropPointerRegion.Top => DragMode.Top,
            CropPointerRegion.TopRight => DragMode.TopRight,
            CropPointerRegion.Right => DragMode.Right,
            CropPointerRegion.BottomRight => DragMode.BottomRight,
            CropPointerRegion.Bottom => DragMode.Bottom,
            CropPointerRegion.BottomLeft => DragMode.BottomLeft,
            CropPointerRegion.Left => DragMode.Left,
            CropPointerRegion.Move => DragMode.Move,
            _ => DragMode.Create
        };
    }

    private static Cursor CursorFor(DragMode mode) => mode switch { DragMode.Left or DragMode.Right => Cursors.SizeWE, DragMode.Top or DragMode.Bottom => Cursors.SizeNS, DragMode.TopLeft or DragMode.BottomRight => Cursors.SizeNWSE, DragMode.TopRight or DragMode.BottomLeft => Cursors.SizeNESW, DragMode.Move => Cursors.SizeAll, _ => Cursors.Cross };
    private void SetCrop(CropRect crop) { _crop = crop; InvalidateVisual(); CropChanged?.Invoke(this, crop.Clone()); }
}
