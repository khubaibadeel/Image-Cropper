using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using PrecisionImageCropper.Models;

namespace PrecisionImageCropper.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private BitmapSource? _image;
    private CropRect _crop = new();
    private double _zoom = 1;
    private bool _isBusy;
    public event PropertyChangedEventHandler? PropertyChanged;
    public BitmapSource? Image { get => _image; set => Set(ref _image, value); }
    public int ImageWidth { get; private set; }
    public int ImageHeight { get; private set; }
    public CropRect Crop { get => _crop.Clone(); set { _crop = value.Clone(); NotifyCrop(); } }
    public double Zoom { get => _zoom; set { if (Set(ref _zoom, value)) OnPropertyChanged(nameof(ZoomText)); } }
    public string ZoomText => $"{Zoom * 100:0}%";
    public bool IsBusy { get => _isBusy; set => Set(ref _isBusy, value); }
    public string WidthText => Crop.Width.ToString("0.##");
    public string HeightText => Crop.Height.ToString("0.##");
    public string XText => Crop.X.ToString("0.##");
    public string YText => Crop.Y.ToString("0.##");
    public string CropSizeText => $"{Crop.Width:0} × {Crop.Height:0} px";
    public string ImageSizeText => ImageWidth == 0 ? "No image loaded" : $"Original: {ImageWidth:N0} × {ImageHeight:N0} px";
    public void Load(BitmapSource image) { Image = image; ImageWidth = image.PixelWidth; ImageHeight = image.PixelHeight; OnPropertyChanged(nameof(ImageWidth)); OnPropertyChanged(nameof(ImageHeight)); OnPropertyChanged(nameof(ImageSizeText)); }
    public void NotifyCrop() { OnPropertyChanged(nameof(Crop)); OnPropertyChanged(nameof(WidthText)); OnPropertyChanged(nameof(HeightText)); OnPropertyChanged(nameof(XText)); OnPropertyChanged(nameof(YText)); OnPropertyChanged(nameof(CropSizeText)); }
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; OnPropertyChanged(name); return true; }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
