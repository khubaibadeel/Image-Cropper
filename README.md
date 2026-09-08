# Precision Image Cropper

A local Windows 10/11 WPF image cropper. Crop geometry is retained in original, correctly oriented image pixels; the canvas is only a scaled preview.

## Run

```powershell
dotnet restore
dotnet build
dotnet run --project src/PrecisionImageCropper
```

## Tests

```powershell
dotnet test
```

## Publish a standalone Windows executable

```powershell
dotnet publish src/PrecisionImageCropper/PrecisionImageCropper.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Supported input types are JPG, PNG, BMP, and TIFF. PNG output retains transparency. JPEG quality is user-adjustable and defaults to 95. The original image is always read-only; Save As creates a separate output file.
