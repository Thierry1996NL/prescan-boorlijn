using System.Runtime.CompilerServices;
using System.Text;

namespace Borevexa.Core.Services;

/// <summary>
/// Centrale, altijd-veilige logger voor fouten die de app bewust opvangt.
/// Fase 5 (07-07-2026): voorheen had de codebase ~100 lege catch-blokken waardoor
/// fouten geluidloos verdwenen; die roepen nu <see cref="Swallowed"/> aan zodat elk
/// opgeslokt probleem terug te vinden is in %LOCALAPPDATA%\Borevexa\Logs\app.log.
/// Loggen mag nooit de app verstoren: alle fouten binnen de logger zelf worden genegeerd,
/// en dezelfde fout op dezelfde plek wordt maximaal eens per 30 seconden geschreven.
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, DateTime> RecentEntries = new(StringComparer.Ordinal);
    private static readonly TimeSpan ThrottleWindow = TimeSpan.FromSeconds(30);
    private const long MaxLogBytes = 5 * 1024 * 1024;

    public static void Swallowed(
        Exception exception,
        [CallerMemberName] string caller = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int line = 0)
    {
        Write("SWALLOWED", $"{exception.GetType().Name}: {exception.Message}", caller, filePath, line);
    }

    public static void Warn(
        string message,
        [CallerMemberName] string caller = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int line = 0)
    {
        Write("WARN", message, caller, filePath, line);
    }

    private static void Write(string level, string message, string caller, string filePath, int line)
    {
        try
        {
            var fileName = Path.GetFileName(filePath);
            var throttleKey = $"{fileName}:{line}:{message}";
            lock (Gate)
            {
                var now = DateTime.UtcNow;
                if (RecentEntries.TryGetValue(throttleKey, out var lastWritten) && now - lastWritten < ThrottleWindow)
                {
                    return;
                }

                RecentEntries[throttleKey] = now;
                if (RecentEntries.Count > 500)
                {
                    RecentEntries.Clear();
                }

                var path = GetLogPath();
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxLogBytes)
                {
                    try { File.Delete(path); } catch { /* rotatie is best-effort */ }
                }

                File.AppendAllText(
                    path,
                    $"[{DateTimeOffset.Now:O}] {level} {fileName}:{line} {caller}: {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Loggen mag de app nooit laten falen.
        }
    }

    private static string GetLogPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Borevexa",
            "Logs",
            "app.log");
}
