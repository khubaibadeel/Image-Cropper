using System.IO;
using System.Linq;
using System.Text.Json;

namespace PrecisionImageCropper.Services;

public sealed record RecentCropSize(int Width, int Height)
{
    public override string ToString() => $"{Width:N0} × {Height:N0}";
}

/// <summary>Stores lightweight per-user UI history outside the application directory.</summary>
public sealed class SettingsService
{
    private const int RecentFileLimit = 10;
    private const int RecentFolderLimit = 5;
    private const int RecentCropSizeLimit = 5;
    private readonly string _settingsPath;
    private SettingsData _settings;

    public SettingsService(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PrecisionImageCropper",
            "settings.json");
        _settings = Load();
    }

    public IReadOnlyList<string> RecentFiles => _settings.RecentFiles.AsReadOnly();
    public IReadOnlyList<string> RecentFolders => _settings.RecentFolders.AsReadOnly();
    public IReadOnlyList<RecentCropSize> RecentCropSizes => _settings.RecentCropSizes.AsReadOnly();

    public void RecordOpenedFile(string path)
    {
        var canonicalPath = Canonicalize(path);
        if (string.IsNullOrWhiteSpace(canonicalPath)) return;

        MoveToFront(_settings.RecentFiles, canonicalPath, RecentFileLimit);
        var folder = Path.GetDirectoryName(canonicalPath);
        if (!string.IsNullOrWhiteSpace(folder))
            MoveToFront(_settings.RecentFolders, folder, RecentFolderLimit);
        Save();
    }

    public void RecordFolder(string path)
    {
        var canonicalPath = Canonicalize(path);
        if (string.IsNullOrWhiteSpace(canonicalPath)) return;

        MoveToFront(_settings.RecentFolders, canonicalPath, RecentFolderLimit);
        Save();
    }

    public void RemoveRecentFile(string path)
    {
        if (RemoveMatching(_settings.RecentFiles, path)) Save();
    }

    public void RemoveRecentFolder(string path)
    {
        if (RemoveMatching(_settings.RecentFolders, path)) Save();
    }

    public void RecordCropSize(double width, double height)
    {
        var roundedWidth = (int)Math.Round(width);
        var roundedHeight = (int)Math.Round(height);
        if (roundedWidth < 1 || roundedHeight < 1) return;

        _settings.RecentCropSizes.RemoveAll(size =>
            size.Width == roundedWidth && size.Height == roundedHeight);
        _settings.RecentCropSizes.Insert(0, new RecentCropSize(roundedWidth, roundedHeight));
        if (_settings.RecentCropSizes.Count > RecentCropSizeLimit)
            _settings.RecentCropSizes.RemoveRange(RecentCropSizeLimit, _settings.RecentCropSizes.Count - RecentCropSizeLimit);
        Save();
    }

    private SettingsData Load()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return new SettingsData();
            var json = File.ReadAllText(_settingsPath);
            var loaded = JsonSerializer.Deserialize<SettingsData>(json) ?? new SettingsData();
            loaded.RecentFiles ??= [];
            loaded.RecentFolders ??= [];
            loaded.RecentCropSizes ??= [];
            Normalize(loaded.RecentFiles, RecentFileLimit);
            Normalize(loaded.RecentFolders, RecentFolderLimit);
            loaded.RecentCropSizes = loaded.RecentCropSizes
                .Where(size => size.Width > 0 && size.Height > 0)
                .Distinct()
                .Take(RecentCropSizeLimit)
                .ToList();
            return loaded;
        }
        catch
        {
            return new SettingsData();
        }
    }

    private void Save()
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (string.IsNullOrWhiteSpace(directory)) return;

        try
        {
            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(directory, $"settings-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_settings));
            File.Move(temporaryPath, _settingsPath, true);
        }
        catch
        {
            // Recent-item persistence must never block an import or edit operation.
        }
    }

    private static void MoveToFront(List<string> entries, string value, int limit)
    {
        entries.RemoveAll(existing => string.Equals(existing, value, StringComparison.OrdinalIgnoreCase));
        entries.Insert(0, value);
        if (entries.Count > limit) entries.RemoveRange(limit, entries.Count - limit);
    }

    private static bool RemoveMatching(List<string> entries, string value)
    {
        var count = entries.Count;
        entries.RemoveAll(existing => string.Equals(existing, value, StringComparison.OrdinalIgnoreCase));
        return entries.Count != count;
    }

    private static void Normalize(List<string> entries, int limit)
    {
        var distinct = entries
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
        entries.Clear();
        entries.AddRange(distinct);
    }

    private static string? Canonicalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { return Path.GetFullPath(path); }
        catch { return null; }
    }

    private sealed class SettingsData
    {
        public List<string> RecentFiles { get; set; } = [];
        public List<string> RecentFolders { get; set; } = [];
        public List<RecentCropSize> RecentCropSizes { get; set; } = [];
    }
}
