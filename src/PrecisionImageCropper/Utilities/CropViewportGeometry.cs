using System.Windows;

namespace PrecisionImageCropper.Utilities;

/// <summary>
/// Calculates display-only image placement in a crop viewport. The offset never
/// participates in source-pixel crop geometry.
/// </summary>
public readonly record struct CropViewportLayout(
    double Scale, double ImageWidth, double ImageHeight,
    double HostWidth, double HostHeight, double ImageLeft, double ImageTop)
{
    public Point DisplayToSource(Point displayPoint) =>
        new((displayPoint.X - ImageLeft) / Scale, (displayPoint.Y - ImageTop) / Scale);

    public Point SourceToDisplay(Point sourcePoint) =>
        new(ImageLeft + sourcePoint.X * Scale, ImageTop + sourcePoint.Y * Scale);
}

public static class CropViewportGeometry
{
    public static CropViewportLayout Calculate(double viewportWidth, double viewportHeight,
        double sourceWidth, double sourceHeight, double zoom, double comfortablePadding = 28)
    {
        var safeViewportWidth = Math.Max(1, viewportWidth);
        var safeViewportHeight = Math.Max(1, viewportHeight);
        var safeSourceWidth = Math.Max(1, sourceWidth);
        var safeSourceHeight = Math.Max(1, sourceHeight);
        var availableWidth = Math.Max(1, safeViewportWidth - comfortablePadding);
        var availableHeight = Math.Max(1, safeViewportHeight - comfortablePadding);
        var fitScale = Math.Min(availableWidth / safeSourceWidth, availableHeight / safeSourceHeight);
        var scale = Math.Max(.001, fitScale * Math.Max(.001, zoom));
        var imageWidth = safeSourceWidth * scale;
        var imageHeight = safeSourceHeight * scale;
        var hostWidth = Math.Max(safeViewportWidth, imageWidth);
        var hostHeight = Math.Max(safeViewportHeight, imageHeight);
        return new CropViewportLayout(scale, imageWidth, imageHeight, hostWidth, hostHeight,
            Math.Max(0, (hostWidth - imageWidth) / 2), Math.Max(0, (hostHeight - imageHeight) / 2));
    }

    // Keep the same nearest-pixel policy used by recent crop-size persistence.
    public static int NormalizePixel(double value) => Math.Max(0, (int)Math.Round(value));
}
