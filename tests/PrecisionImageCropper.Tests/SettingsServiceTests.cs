using System.IO;
using PrecisionImageCropper.Services;
using Xunit;

namespace PrecisionImageCropper.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public void Recent_history_is_limited_deduplicated_and_persists_across_service_instances()
    {
        var root = Path.Combine(Path.GetTempPath(), $"PrecisionImageCropper-tests-{Guid.NewGuid():N}");
        var settingsPath = Path.Combine(root, "settings.json");
        try
        {
            var settings = new SettingsService(settingsPath);
            for (var index = 1; index <= 12; index++)
                settings.RecordOpenedFile(Path.Combine(root, $"file-{index}.png"));
            for (var index = 1; index <= 7; index++)
                settings.RecordFolder(Path.Combine(root, $"folder-{index}"));

            Assert.Equal(10, settings.RecentFiles.Count);
            Assert.Equal(Path.GetFullPath(Path.Combine(root, "file-12.png")), settings.RecentFiles[0]);
            Assert.Equal(5, settings.RecentFolders.Count);
            Assert.Equal(Path.GetFullPath(Path.Combine(root, "folder-7")), settings.RecentFolders[0]);

            settings.RecordOpenedFile(Path.Combine(root, "file-8.png"));
            Assert.Equal(Path.GetFullPath(Path.Combine(root, "file-8.png")), settings.RecentFiles[0]);
            Assert.Equal(10, settings.RecentFiles.Count);

            var restored = new SettingsService(settingsPath);
            Assert.Equal(settings.RecentFiles, restored.RecentFiles);
            Assert.Equal(settings.RecentFolders, restored.RecentFolders);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Removing_stale_recent_entries_persists_the_removal()
    {
        var root = Path.Combine(Path.GetTempPath(), $"PrecisionImageCropper-tests-{Guid.NewGuid():N}");
        var settingsPath = Path.Combine(root, "settings.json");
        var imagePath = Path.Combine(root, "image.png");
        try
        {
            var settings = new SettingsService(settingsPath);
            settings.RecordOpenedFile(imagePath);
            settings.RemoveRecentFile(imagePath);

            var restored = new SettingsService(settingsPath);
            Assert.Empty(restored.RecentFiles);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
