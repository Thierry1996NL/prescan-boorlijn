using System.Diagnostics;
using System.IO;

namespace Borevexa.Prescan.App.Services;

public static class StartupCacheCleanupService
{
    public static void CleanTemporaryCaches()
    {
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Borevexa",
            "PrescanNative");

        DeleteDirectory(Path.Combine(baseDir, "WebView2UserData"));
        DeleteDirectory(Path.Combine(baseDir, "WebView2"));
        DeleteOldDirectoryContents(Path.Combine(baseDir, "DocumentPreview"), TimeSpan.FromDays(7));
        DeleteOldDirectoryContents(Path.Combine(baseDir, "ReportLocks"), TimeSpan.FromHours(12));
        DeleteFileIfExists(Path.Combine(baseDir, "map-debug.log"));
        DeleteTempReportPreviewFiles();
    }

    private static void DeleteTempReportPreviewFiles()
    {
        Try(() =>
        {
            var tempDir = Path.GetTempPath();
            foreach (var file in Directory.EnumerateFiles(tempDir, "borevexa-rapportpreview-*.png", SearchOption.TopDirectoryOnly))
            {
                DeleteFileIfExists(file);
            }
        });
    }

    private static void DeleteDirectory(string path)
    {
        Try(() =>
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        });
    }

    private static void DeleteDirectoryContents(string path)
    {
        Try(() =>
        {
            if (!Directory.Exists(path)) return;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly))
            {
                DeleteFileIfExists(file);
            }

            foreach (var directory in Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly))
            {
                DeleteDirectory(directory);
            }
        });
    }

    private static void DeleteOldDirectoryContents(string path, TimeSpan maxAge)
    {
        Try(() =>
        {
            if (!Directory.Exists(path)) return;
            var cutoff = DateTime.UtcNow - maxAge;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                Try(() =>
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                });
            }

            foreach (var directory in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories).OrderByDescending(item => item.Length))
            {
                Try(() =>
                {
                    if (!Directory.EnumerateFileSystemEntries(directory).Any())
                    {
                        Directory.Delete(directory, recursive: false);
                    }
                });
            }
        });
    }

    private static void DeleteFileIfExists(string path)
    {
        Try(() =>
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        });
    }

    private static void Try(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Startup cache cleanup skipped item: {ex.Message}");
        }
    }
}
