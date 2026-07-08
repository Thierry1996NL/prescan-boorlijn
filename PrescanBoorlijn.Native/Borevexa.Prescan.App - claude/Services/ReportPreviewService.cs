using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Borevexa.Prescan.Core.Models;
using Borevexa.Prescan.Core.Services;

namespace Borevexa.Prescan.App.Services;

public sealed class ReportPreviewService
{
    public const string LiveMapPreviewDataKey = ReportDataKeys.LiveMapPreview;
    public const string ReportLockDataKey = ReportDataKeys.ReportLock;
    private const int MinimumMapCaptureWidth = 320;
    private const int MinimumMapCaptureHeight = 220;
    private const int MinimumSampledColors = 8;
    private const double MaximumDominantColorRatio = 0.88;

    // A properly rendered slippy map (aerial/BGT tiles + cables) always yields a
    // rich colour histogram. A GPU-degraded (blank) capture only contains the flat
    // background plus a few vector overlays, so it collapses to very few clusters.
    // Measured: good live-map capture ~270 clusters, blank ~39.
    private const int MinimumLiveMapColorClusters = 64;

    private readonly ProjectRepository _projects;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _liveMapCaptureRunning;

    public ReportPreviewService(ProjectRepository projects, JsonSerializerOptions? jsonOptions = null)
    {
        _projects = projects;
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    }

    public async Task<string> CaptureLiveMapForReportPreviewAsync(
        Guid projectId,
        int stepNumber,
        CoreWebView2 webView,
        object metadata,
        Action<Exception>? onError = null,
        bool force = false,
        string? variantKey = null)
    {
        if (_liveMapCaptureRunning)
        {
            if (!force) return "";
            for (var attempt = 0; attempt < 20 && _liveMapCaptureRunning; attempt++)
            {
                await Task.Delay(50);
            }
            if (_liveMapCaptureRunning) return "";
        }

        _liveMapCaptureRunning = true;
        var tempPath = "";
        try
        {
            // WebView2 calls can hang indefinitely when the renderer is stuck; without a
            // timeout the _liveMapCaptureRunning flag then stays set and silently blocks
            // every future report capture until the app restarts.
            await WithTimeout(
                webView.ExecuteScriptAsync("window.borevexaMap && window.borevexaMap.handleMessage({ type: 'reportCaptureMode', enabled: true })"),
                TimeSpan.FromSeconds(6),
                "reportCaptureMode");
            var readinessJson = await WithTimeout(
                PrepareWebMapForReportCaptureAsync(webView),
                TimeSpan.FromSeconds(10),
                "prepareReportCapture");

            var dir = GetLiveMapCaptureDirectory();
            Directory.CreateDirectory(dir);
            var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            var variant = NormalizeLiveMapVariantKey(variantKey);
            var variantSegment = string.IsNullOrWhiteSpace(variant) ? "" : $"-{variant}";
            var path = Path.Combine(dir, $"project-{projectId}-step-{stepNumber}-live-map{variantSegment}-{stamp}.png");
            var stablePath = GetStableLiveMapCapturePath(dir, projectId.ToString(), stepNumber, variant);
            tempPath = path + ".tmp";

            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await WithTimeout(
                    webView.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream),
                    TimeSpan.FromSeconds(10),
                    "CapturePreviewAsync");
            }

            var validation = ValidateMapCapture(tempPath);
            if (!validation.IsUsable)
            {
                TryDeleteFile(tempPath);
                tempPath = "";
                onError?.Invoke(new InvalidOperationException($"Rapportkaartcapture afgekeurd: {validation.Reason}"));
                return "";
            }

            // Reject GPU-degraded (blank) captures that slip past the generic egaal-check:
            // the basemap/BGT tiles failed to paint, leaving only the vector overlays.
            // These would otherwise overwrite a previously good report image.
            if (validation.SampledColors < MinimumLiveMapColorClusters)
            {
                TryDeleteFile(tempPath);
                tempPath = "";
                onError?.Invoke(new InvalidOperationException(
                    $"Rapportkaartcapture afgekeurd: kaart onvolledig gerenderd ({validation.SampledColors} kleurclusters, ondergrond/BGT ontbreekt)."));
                return "";
            }

            File.Move(tempPath, path);
            File.Copy(path, stablePath, true);
            tempPath = "";
            CleanupOldLiveMapCaptures(dir, projectId.ToString(), stepNumber);

            _projects.SaveStepData(projectId, stepNumber, BuildLiveMapPreviewDataKey(variant), JsonSerializer.Serialize(new
            {
                capturedAt = DateTimeOffset.Now,
                stepNumber,
                variant,
                imagePath = stablePath,
                capturedImagePath = path,
                capture = new
                {
                    readiness = TryParseJsonElement(readinessJson),
                    validation.Width,
                    validation.Height,
                    validation.SampleCount,
                    validation.SampledColors,
                    validation.DominantColorRatio,
                    validation.Reason
                },
                metadata
            }, _jsonOptions));

            return stablePath;
        }
        catch (Exception exception)
        {
            onError?.Invoke(exception);
            return "";
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }

            try
            {
                await WithTimeout(
                    webView.ExecuteScriptAsync("window.borevexaMap && window.borevexaMap.handleMessage({ type: 'reportCaptureMode', enabled: false })"),
                    TimeSpan.FromSeconds(6),
                    "reportCaptureMode uitschakelen");
            }
            catch
            {
                // Best-effort restore; a failed cleanup must not block the app.
            }

            _liveMapCaptureRunning = false;
        }
    }

    private static async Task WithTimeout(Task task, TimeSpan timeout, string description)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        if (completed != task)
        {
            throw new TimeoutException($"Kaartcapture stap '{description}' duurde langer dan {timeout.TotalSeconds:0}s.");
        }

        await task;
    }

    private static async Task<T> WithTimeout<T>(Task<T> task, TimeSpan timeout, string description)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        if (completed != task)
        {
            throw new TimeoutException($"Kaartcapture stap '{description}' duurde langer dan {timeout.TotalSeconds:0}s.");
        }

        return await task;
    }

    public static MapCaptureValidation ValidateMapCapture(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return MapCaptureValidation.Failed("Bestand ontbreekt.");
        }

        try
        {
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.FirstOrDefault();
            if (frame is null)
            {
                return MapCaptureValidation.Failed("PNG bevat geen afbeeldingsframe.");
            }

            BitmapSource source = frame.Format == PixelFormats.Bgra32
                ? frame
                : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);

            var width = source.PixelWidth;
            var height = source.PixelHeight;
            if (width < MinimumMapCaptureWidth || height < MinimumMapCaptureHeight)
            {
                return MapCaptureValidation.Failed($"Afbeelding is te klein ({width}x{height}px).", width, height);
            }

            var stride = width * 4;
            var pixels = new byte[stride * height];
            source.CopyPixels(pixels, stride, 0);

            var colors = new Dictionary<int, int>();
            var sampleCount = 0;
            var stepX = Math.Max(1, width / 80);
            var stepY = Math.Max(1, height / 60);

            for (var y = 0; y < height; y += stepY)
            {
                for (var x = 0; x < width; x += stepX)
                {
                    var offset = y * stride + x * 4;
                    var b = Quantize(pixels[offset]);
                    var g = Quantize(pixels[offset + 1]);
                    var r = Quantize(pixels[offset + 2]);
                    var a = pixels[offset + 3] < 32 ? 0 : 255;
                    var key = a << 24 | r << 16 | g << 8 | b;
                    colors[key] = colors.TryGetValue(key, out var count) ? count + 1 : 1;
                    sampleCount++;
                }
            }

            var dominantRatio = sampleCount == 0 ? 1 : colors.Values.Max() / (double)sampleCount;
            if (colors.Count < MinimumSampledColors)
            {
                return MapCaptureValidation.Failed($"Te weinig beeldvariatie ({colors.Count} kleurclusters).", width, height, sampleCount, colors.Count, dominantRatio);
            }

            if (dominantRatio > MaximumDominantColorRatio)
            {
                return MapCaptureValidation.Failed($"Afbeelding lijkt grotendeels egaal ({dominantRatio:P0} dominante kleur).", width, height, sampleCount, colors.Count, dominantRatio);
            }

            return new MapCaptureValidation(true, width, height, sampleCount, colors.Count, dominantRatio, "Bruikbare live kaartcapture.");
        }
        catch (Exception exception)
        {
            return MapCaptureValidation.Failed($"PNG-validatie mislukt: {exception.Message}");
        }
    }

    public string GetLiveMapReportPreviewImagePath(Guid projectId, int stepNumber, Func<int, string> lockedPathProvider, string? variantKey = null)
    {
        var variant = NormalizeLiveMapVariantKey(variantKey);
        if (string.IsNullOrWhiteSpace(variant))
        {
            var lockedPath = lockedPathProvider(stepNumber);
            if (!string.IsNullOrWhiteSpace(lockedPath)) return lockedPath;
        }

        var json = _projects.GetStepData(projectId, stepNumber, BuildLiveMapPreviewDataKey(variant));
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                var path = root.TryGetProperty("imagePath", out var imageElement) ? imageElement.GetString() ?? "" : "";
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) return path;
            }
            catch
            {
                // Fall back to the latest capture on disk. Project data may lag one UI refresh behind.
            }
        }

        return FindNewestLiveMapCapture(projectId, stepNumber, variant);
    }

    public bool HasLiveMapReportPreview(Guid projectId, int stepNumber) =>
        !string.IsNullOrWhiteSpace(GetLiveMapReportPreviewImagePath(projectId, stepNumber, _ => ""));

    public string? GetReportLockJson(Guid projectId, int stepNumber)
    {
        return _projects.GetStepData(projectId, stepNumber, ReportLockDataKey);
    }

    public string? GetReportLockJson(Guid projectId, int stepNumber, string? contextKey, bool includeLegacyFallback = true)
    {
        var scopedDataKey = BuildReportLockDataKey(contextKey);
        if (!scopedDataKey.Equals(ReportLockDataKey, StringComparison.OrdinalIgnoreCase))
        {
            var scoped = _projects.GetStepData(projectId, stepNumber, scopedDataKey);
            if (!string.IsNullOrWhiteSpace(scoped)) return scoped;
        }

        return includeLegacyFallback ? GetReportLockJson(projectId, stepNumber) : null;
    }

    public void SaveReportLockJson(Guid projectId, int stepNumber, string json)
    {
        _projects.SaveStepData(projectId, stepNumber, ReportLockDataKey, json);
    }

    public void SaveReportLockJson(Guid projectId, int stepNumber, string json, string? contextKey)
    {
        var scopedDataKey = BuildReportLockDataKey(contextKey);
        if (!scopedDataKey.Equals(ReportLockDataKey, StringComparison.OrdinalIgnoreCase))
        {
            _projects.SaveStepData(projectId, stepNumber, scopedDataKey, json);
        }

        _projects.SaveStepData(projectId, stepNumber, ReportLockDataKey, json);
    }

    public bool IsMapReportLocked(Guid projectId, int stepNumber)
    {
        var json = GetReportLockJson(projectId, stepNumber);
        return IsMapReportLockJsonLocked(json);
    }

    public bool IsMapReportLocked(Guid projectId, int stepNumber, string? contextKey, bool includeLegacyFallback = true)
    {
        var json = GetReportLockJson(projectId, stepNumber, contextKey, includeLegacyFallback);
        return IsMapReportLockJsonLocked(json);
    }

    private static bool IsMapReportLockJsonLocked(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("locked", out var locked) && locked.ValueKind == JsonValueKind.True;
        }
        catch
        {
            return false;
        }
    }

    public static string BuildReportLockDataKey(string? contextKey)
    {
        var normalized = MapStateService.NormalizeContextKey(contextKey);
        return string.IsNullOrWhiteSpace(normalized) ? ReportLockDataKey : $"{ReportLockDataKey}_{normalized}";
    }

    public static void SaveFrameworkElementAsPng(FrameworkElement element, string path, double scale = 1d)
    {
        scale = Math.Clamp(scale, 1d, 4d);
        element.UpdateLayout();
        var width = Math.Max(1, element.ActualWidth);
        var height = Math.Max(1, element.ActualHeight);
        if (width < 2 || height < 2)
        {
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var desired = element.DesiredSize;
            width = Math.Max(width, desired.Width);
            height = Math.Max(height, desired.Height);
            element.Arrange(new Rect(0, 0, width, height));
            element.UpdateLayout();
        }

        var render = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(width * scale)),
            Math.Max(1, (int)Math.Ceiling(height * scale)),
            96 * scale,
            96 * scale,
            PixelFormats.Pbgra32);
        render.Render(element);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(render));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static async Task<string> PrepareWebMapForReportCaptureAsync(CoreWebView2 webView)
    {
        try
        {
            return await webView.ExecuteScriptAsync(
                """
                (async () => {
                  if (window.borevexaMap && window.borevexaMap.prepareReportCapture) {
                    return await window.borevexaMap.prepareReportCapture();
                  }
                  if (window.borevexaMap && window.borevexaMap.handleMessage) {
                    window.borevexaMap.handleMessage({ type: 'reportCaptureMode', enabled: true });
                  }
                  await new Promise(resolve => setTimeout(resolve, 450));
                  return { ready: false, reason: 'fallback' };
                })()
                """);
        }
        catch
        {
            await Task.Delay(450);
            return "";
        }
    }

    private static JsonElement? TryParseJsonElement(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    private static int Quantize(byte value) => value / 16;

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); } catch { }
    }

    private static string GetLiveMapCaptureDirectory()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Borevexa", "PrescanNative", "ReportLiveMaps");
    }

    public static string BuildLiveMapPreviewDataKey(string? variantKey)
    {
        var variant = NormalizeLiveMapVariantKey(variantKey);
        return string.IsNullOrWhiteSpace(variant)
            ? LiveMapPreviewDataKey
            : $"{LiveMapPreviewDataKey}_{variant}";
    }

    public static string NormalizeLiveMapVariantKey(string? variantKey)
    {
        if (string.IsNullOrWhiteSpace(variantKey)) return "";

        var builder = new System.Text.StringBuilder();
        foreach (var character in variantKey.Trim())
        {
            if (char.IsLetterOrDigit(character) || character is '-' or '_')
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static void CleanupOldLiveMapCaptures(string directory, string projectId, int stepNumber)
    {
        try
        {
            var prefix = $"project-{projectId}-step-{stepNumber}-";
            var stablePrefix = $"{prefix}report-";
            var files = Directory.GetFiles(directory, $"{prefix}*.png")
                .Select(path => new FileInfo(path))
                .Where(file => !file.Name.StartsWith(stablePrefix, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Skip(12)
                .ToList();
            foreach (var file in files)
            {
                try { file.Delete(); } catch { }
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static string GetStableLiveMapCapturePath(string directory, string projectId, int stepNumber, string? variantKey = null)
    {
        var variant = NormalizeLiveMapVariantKey(variantKey);
        var suffix = string.IsNullOrWhiteSpace(variant) ? "current" : variant;
        return Path.Combine(directory, $"project-{projectId}-step-{stepNumber}-report-{suffix}.png");
    }

    private static string FindNewestLiveMapCapture(Guid projectId, int stepNumber, string? variantKey = null)
    {
        try
        {
            var directory = GetLiveMapCaptureDirectory();
            if (!Directory.Exists(directory)) return "";

            var variant = NormalizeLiveMapVariantKey(variantKey);
            var stablePath = GetStableLiveMapCapturePath(directory, projectId.ToString(), stepNumber, variant);
            if (File.Exists(stablePath)) return stablePath;

            var prefix = $"project-{projectId}-step-{stepNumber}-";
            var pattern = string.IsNullOrWhiteSpace(variant)
                ? $"{prefix}*.png"
                : $"{prefix}*{variant}*.png";
            return Directory.GetFiles(directory, pattern)
                .Select(path => new FileInfo(path))
                .Where(file => file.Exists && file.Length > 0)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => file.FullName)
                .FirstOrDefault() ?? "";
        }
        catch
        {
            return "";
        }
    }
}

public sealed record MapCaptureValidation(
    bool IsUsable,
    int Width,
    int Height,
    int SampleCount,
    int SampledColors,
    double DominantColorRatio,
    string Reason)
{
    public static MapCaptureValidation Failed(
        string reason,
        int width = 0,
        int height = 0,
        int sampleCount = 0,
        int sampledColors = 0,
        double dominantColorRatio = 1) =>
        new(false, width, height, sampleCount, sampledColors, dominantColorRatio, reason);
}
