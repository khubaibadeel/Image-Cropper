namespace PrecisionImageCropper.Models;

/// <summary>
/// A lightweight, non-destructive image edit recipe. It intentionally contains
/// no bitmap data, so history is inexpensive even for large source images.
/// </summary>
public sealed class ImageEditState
{
    public ImageEditState(
        CropRect crop,
        string aspectRatio,
        bool aspectLocked,
        double customAspectRatioWidth,
        double customAspectRatioHeight,
        int netRotation,
        bool horizontalFlip,
        bool verticalFlip)
    {
        Crop = crop.Clone();
        AspectRatio = aspectRatio;
        AspectLocked = aspectLocked;
        CustomAspectRatioWidth = customAspectRatioWidth;
        CustomAspectRatioHeight = customAspectRatioHeight;
        NetRotation = netRotation;
        HorizontalFlip = horizontalFlip;
        VerticalFlip = verticalFlip;
    }

    public CropRect Crop { get; }
    public string AspectRatio { get; }
    public bool AspectLocked { get; }
    public double CustomAspectRatioWidth { get; }
    public double CustomAspectRatioHeight { get; }
    public int NetRotation { get; }
    public bool HorizontalFlip { get; }
    public bool VerticalFlip { get; }

    public bool IsEquivalentTo(ImageEditState other) =>
        Math.Abs(Crop.X - other.Crop.X) < .01 &&
        Math.Abs(Crop.Y - other.Crop.Y) < .01 &&
        Math.Abs(Crop.Width - other.Crop.Width) < .01 &&
        Math.Abs(Crop.Height - other.Crop.Height) < .01 &&
        string.Equals(AspectRatio, other.AspectRatio, StringComparison.Ordinal) &&
        AspectLocked == other.AspectLocked &&
        Math.Abs(CustomAspectRatioWidth - other.CustomAspectRatioWidth) < .01 &&
        Math.Abs(CustomAspectRatioHeight - other.CustomAspectRatioHeight) < .01 &&
        NetRotation == other.NetRotation &&
        HorizontalFlip == other.HorizontalFlip &&
        VerticalFlip == other.VerticalFlip;
}
