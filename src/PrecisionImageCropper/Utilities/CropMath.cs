using System.Windows;
using PrecisionImageCropper.Models;

namespace PrecisionImageCropper.Utilities;

public static class CropMath
{
    public const double MinSize = 10;

    public static CropRect Clamp(CropRect crop, double imageWidth, double imageHeight, double minimum = MinSize)
    {
        var minW = Math.Min(minimum, imageWidth);
        var minH = Math.Min(minimum, imageHeight);
        crop.Width = Math.Clamp(crop.Width, minW, imageWidth);
        crop.Height = Math.Clamp(crop.Height, minH, imageHeight);
        crop.X = Math.Clamp(crop.X, 0, Math.Max(0, imageWidth - crop.Width));
        crop.Y = Math.Clamp(crop.Y, 0, Math.Max(0, imageHeight - crop.Height));
        return crop;
    }

    public static CropRect Move(CropRect crop, double deltaX, double deltaY, double imageWidth, double imageHeight)
    {
        crop.X += deltaX; crop.Y += deltaY;
        return Clamp(crop, imageWidth, imageHeight);
    }

    public static CropRect Default(double imageWidth, double imageHeight)
    {
        var width = Math.Max(MinSize, imageWidth * .8);
        var height = Math.Max(MinSize, imageHeight * .8);
        return Clamp(new CropRect((imageWidth - width) / 2, (imageHeight - height) / 2, width, height), imageWidth, imageHeight);
    }

    public static CropRect ApplyRatio(CropRect crop, double ratio, double imageWidth, double imageHeight)
    {
        if (ratio <= 0) return Clamp(crop, imageWidth, imageHeight);
        var width = Math.Min(crop.Width, crop.Height * ratio);
        var height = width / ratio;
        if (width < MinSize || height < MinSize) { width = Math.Max(MinSize, width); height = width / ratio; }
        if (width > imageWidth) { width = imageWidth; height = width / ratio; }
        if (height > imageHeight) { height = imageHeight; width = height * ratio; }
        crop.Width = width; crop.Height = height;
        return Clamp(crop, imageWidth, imageHeight);
    }

    public static Int32Rect ToPixelRect(CropRect crop, int imageWidth, int imageHeight)
    {
        // Round once, at export time. This keeps an entered 1200 × 800 crop exactly
        // 1200 × 800 pixels while display/drag math remains continuous and drift-free.
        var left = Math.Clamp((int)Math.Round(crop.X), 0, Math.Max(0, imageWidth - 1));
        var top = Math.Clamp((int)Math.Round(crop.Y), 0, Math.Max(0, imageHeight - 1));
        var width = Math.Clamp(Math.Max(1, (int)Math.Round(crop.Width)), 1, Math.Max(1, imageWidth - left));
        var height = Math.Clamp(Math.Max(1, (int)Math.Round(crop.Height)), 1, Math.Max(1, imageHeight - top));
        return new Int32Rect(left, top, width, height);
    }
}
