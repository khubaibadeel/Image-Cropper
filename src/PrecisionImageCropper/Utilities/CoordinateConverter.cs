using System.Windows;
using PrecisionImageCropper.Models;

namespace PrecisionImageCropper.Utilities;

public static class CoordinateConverter
{
    public static double DisplayToImageX(double displayX, double displayWidth, double imageWidth) => displayWidth <= 0 ? 0 : displayX * imageWidth / displayWidth;
    public static double DisplayToImageY(double displayY, double displayHeight, double imageHeight) => displayHeight <= 0 ? 0 : displayY * imageHeight / displayHeight;
    public static double ImageToDisplayX(double imageX, double displayWidth, double imageWidth) => imageWidth <= 0 ? 0 : imageX * displayWidth / imageWidth;
    public static double ImageToDisplayY(double imageY, double displayHeight, double imageHeight) => imageHeight <= 0 ? 0 : imageY * displayHeight / imageHeight;
    public static Rect ImageToDisplay(CropRect crop, double scale) => new(crop.X * scale, crop.Y * scale, crop.Width * scale, crop.Height * scale);
}
