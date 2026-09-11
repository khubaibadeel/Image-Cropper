using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using PrecisionImageCropper.Utilities;

namespace PrecisionImageCropper.Models;

public enum ImageImportSource
{
    File,
    Clipboard
}

/// <summary>
/// Owns the complete, independent edit state for one image in the batch.
/// The full-resolution source is deliberately represented by a path, not a
/// retained BitmapSource; callers load it only when they need to render it.
/// </summary>
public sealed class BatchImageItem : INotifyPropertyChanged
{
    private BitmapSource? _thumbnail;
    private bool _isThumbnailLoading = true;
    private CropRect _cropRectangle;
    private string _selectedAspectRatio = "FreeForm";
    private double _customAspectRatioWidth = 1;
    private double _customAspectRatioHeight = 1;
    private int _netRotation;
    private bool _horizontalFlip;
    private bool _verticalFlip;
    private string? _statusMessage;

    public BatchImageItem(
        string sourceDataPath,
        string? originalFilePath,
        string originalFileName,
        string originalExtension,
        int originalWidth,
        int originalHeight,
        ImageImportSource importSource)
    {
        Id = Guid.NewGuid();
        SourceDataPath = sourceDataPath;
        OriginalFilePath = originalFilePath;
        OriginalFileName = originalFileName;
        OriginalExtension = string.IsNullOrWhiteSpace(originalExtension) ? ".png" : originalExtension;
        OriginalWidth = originalWidth;
        OriginalHeight = originalHeight;
        ImportSource = importSource;
        // A newly queued image is unedited. The legacy crop editor may choose a
        // smaller interactive selection later, but batch state starts with the
        // full source rectangle so HasCrop and output dimensions are truthful.
        _cropRectangle = new CropRect(0, 0, originalWidth, originalHeight);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; }
    public string SourceDataPath { get; }
    public string? OriginalFilePath { get; }
    public string OriginalFileName { get; }
    public string OriginalExtension { get; }
    public int OriginalWidth { get; }
    public int OriginalHeight { get; }
    public ImageImportSource ImportSource { get; }

    public BitmapSource? Thumbnail
    {
        get => _thumbnail;
        set => Set(ref _thumbnail, value);
    }

    public bool IsThumbnailLoading
    {
        get => _isThumbnailLoading;
        set => Set(ref _isThumbnailLoading, value);
    }

    public CropRect CropRectangle
    {
        get => _cropRectangle.Clone();
        set
        {
            _cropRectangle = CropMath.Clamp(value, OriginalWidth, OriginalHeight);
            NotifyEditState();
        }
    }

    public string SelectedAspectRatio
    {
        get => _selectedAspectRatio;
        set => Set(ref _selectedAspectRatio, value ?? "FreeForm");
    }

    public double CustomAspectRatioWidth
    {
        get => _customAspectRatioWidth;
        set => Set(ref _customAspectRatioWidth, value > 0 ? value : 1);
    }

    public double CustomAspectRatioHeight
    {
        get => _customAspectRatioHeight;
        set => Set(ref _customAspectRatioHeight, value > 0 ? value : 1);
    }

    public int NetRotation
    {
        get => _netRotation;
        set
        {
            var normalized = ((value % 360) + 360) % 360;
            if (normalized is not (0 or 90 or 180 or 270))
                throw new ArgumentOutOfRangeException(nameof(value), "Rotation must be 0, 90, 180, or 270 degrees.");

            if (Set(ref _netRotation, normalized))
                NotifyEditState();
        }
    }

    public bool HorizontalFlip
    {
        get => _horizontalFlip;
        set
        {
            if (Set(ref _horizontalFlip, value))
                NotifyEditState();
        }
    }

    public bool VerticalFlip
    {
        get => _verticalFlip;
        set
        {
            if (Set(ref _verticalFlip, value))
                NotifyEditState();
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        set => Set(ref _statusMessage, value);
    }

    public bool HasCrop =>
        Math.Abs(_cropRectangle.X) > 0.01 ||
        Math.Abs(_cropRectangle.Y) > 0.01 ||
        Math.Abs(_cropRectangle.Width - OriginalWidth) > 0.01 ||
        Math.Abs(_cropRectangle.Height - OriginalHeight) > 0.01;

    public bool HasRotation => NetRotation != 0;
    public bool HasHorizontalFlip => HorizontalFlip;
    public bool HasVerticalFlip => VerticalFlip;
    public bool HasUnsavedChanges => HasCrop || HasRotation || HasHorizontalFlip || HasVerticalFlip;
    public bool IsEdited => HasUnsavedChanges;
    public string OriginalDimensionsText => $"{OriginalWidth:N0} × {OriginalHeight:N0} px";
    public int OutputWidth => NetRotation is 90 or 270 ? CropHeight : CropWidth;
    public int OutputHeight => NetRotation is 90 or 270 ? CropWidth : CropHeight;
    public string OutputDimensionsText => $"{OutputWidth:N0} × {OutputHeight:N0} px";
    public string EditStateText => IsEdited ? "Edited" : "Original";
    public string OutputFileName => GetSuggestedOutputFileName();

    public string GetSuggestedOutputFileName()
    {
        var stem = Path.GetFileNameWithoutExtension(OriginalFileName);
        return $"{stem}{(HasUnsavedChanges ? "-edited" : "-copy")}{OriginalExtension}";
    }

    private int CropWidth => Math.Max(1, (int)Math.Round(_cropRectangle.Width));
    private int CropHeight => Math.Max(1, (int)Math.Round(_cropRectangle.Height));

    private void NotifyEditState()
    {
        OnPropertyChanged(nameof(CropRectangle));
        OnPropertyChanged(nameof(HasCrop));
        OnPropertyChanged(nameof(HasRotation));
        OnPropertyChanged(nameof(HasHorizontalFlip));
        OnPropertyChanged(nameof(HasVerticalFlip));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(IsEdited));
        OnPropertyChanged(nameof(EditStateText));
        OnPropertyChanged(nameof(OutputWidth));
        OnPropertyChanged(nameof(OutputHeight));
        OnPropertyChanged(nameof(OutputDimensionsText));
        OnPropertyChanged(nameof(OutputFileName));
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
