using System.IO;
using System.IO.Compression;
using System.Linq;
using PrecisionImageCropper.Models;

namespace PrecisionImageCropper.Services;

public sealed record ZipExportProgress(int Current, int Total, string FileName);

public sealed class ZipExportException : Exception
{
    public ZipExportException(string fileName, Exception innerException)
        : base($"Unable to export \"{fileName}\".", innerException) => FileName = fileName;

    public string FileName { get; }
}

public static class BatchZipExportService
{
    public static Task ExportAsync(
        IReadOnlyList<BatchImageItem> items,
        string outputPath,
        IProgress<ZipExportProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Export(items, outputPath, progress, cancellationToken), cancellationToken);

    private static void Export(
        IReadOnlyList<BatchImageItem> items,
        string outputPath,
        IProgress<ZipExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0) throw new InvalidOperationException("There are no images to export.");
        if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("The ZIP output path cannot be empty.", nameof(outputPath));

        var destination = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(destination) ?? throw new InvalidOperationException("The ZIP output folder is invalid.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");

        try
        {
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var file = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false))
            {
                for (var index = 0; index < items.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var item = items[index];
                    var entryName = GetUniqueEntryName(item.OutputFileName, usedNames);
                    progress?.Report(new ZipExportProgress(index + 1, items.Count, entryName));
                    try
                    {
                        ImageRenderService.WriteZipEntry(item, archive, entryName);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        throw new ZipExportException(item.OriginalFileName, ex);
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, destination, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try { File.Delete(temporaryPath); } catch { }
            }
        }
    }

    internal static string GetUniqueEntryName(string candidate, ISet<string> usedNames)
    {
        var safeName = SanitizeFileName(candidate);
        var stem = Path.GetFileNameWithoutExtension(safeName);
        var extension = Path.GetExtension(safeName);
        var result = safeName;
        var suffix = 2;
        while (!usedNames.Add(result))
            result = $"{stem}-{suffix++}{extension}";
        return result;
    }

    private static string SanitizeFileName(string? value)
    {
        var fileName = Path.GetFileName(value ?? string.Empty);
        if (string.IsNullOrWhiteSpace(fileName)) return "image.png";
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(fileName.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "image.png" : safe;
    }
}
