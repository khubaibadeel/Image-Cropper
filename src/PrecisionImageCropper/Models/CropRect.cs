namespace PrecisionImageCropper.Models;

/// <summary>Crop geometry in oriented, original source-image pixels.</summary>
public sealed class CropRect
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public CropRect() { }
    public CropRect(double x, double y, double width, double height) => (X, Y, Width, Height) = (x, y, width, height);
    public CropRect Clone() => new(X, Y, Width, Height);
}
