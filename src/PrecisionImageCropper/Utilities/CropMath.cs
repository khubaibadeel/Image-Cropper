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
        var left = Math.Clamp((int)Math.Floor(crop.X), 0, imageWidth - 1);
        var top = Math.Clamp((int)Math.Floor(crop.Y), 0, imageHeight - 1);
        var right = Math.Clamp((int)Math.Ceiling(crop.X + crop.Width), left + 1, imageWidth);
        var bottom = Math.Clamp((int)Math.Ceiling(crop.Y + crop.Height), top + 1, imageHeight);
        return new Int32Rect(left, top, right - left, bottom - top);
    }
}
