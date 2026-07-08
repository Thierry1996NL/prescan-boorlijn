using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using Borevexa.Prescan.Core.Models;
using Borevexa.Prescan.Core.Services;

namespace Borevexa.Prescan.App.Services;

public sealed record ReportExportResult(
    string HtmlPath,
    string? ImagePath,
    string ManifestPath,
    DateTimeOffset ExportedAt,
    string Format,
    string VersionLabel);

internal sealed record StoredExportHistoryItem(
    DateTimeOffset ExportedAt,
    string Kind,
    string Version,
    string Status,
    string Path,
    string? ImagePath,
    string? ManifestPath);

public sealed class ReportExportService(ProjectRepository projects, JsonSerializerOptions? jsonOptions = null)
{
    private readonly JsonSerializerOptions _jsonOptions = jsonOptions ?? new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public ReportExportResult ExportFinalReportVectorPdf(
        PrescanProject project,
        int reportStepNumber,
        string html,
        ReportQualitySummary quality,
        bool openAfterExport)
    {
        var exportedAt = DateTimeOffset.Now;
        var exportDir = GetExportDirectory();
        Directory.CreateDirectory(exportDir);

        var basePath = Path.Combine(exportDir, BuildFinalReportBaseName(project.Name, exportedAt, quality));
        var htmlPath = basePath + ".html";
        var pdfPath = basePath + ".pdf";
        var manifestPath = basePath + ".manifest.json";

        File.WriteAllText(htmlPath, html, Encoding.UTF8);
        var pdfCreated = TryPrintHtmlToPdfWithEdge(htmlPath, pdfPath);

        var result = new ReportExportResult(
            htmlPath,
            pdfCreated ? pdfPath : null,
            manifestPath,
            exportedAt,
            pdfCreated ? "pdf-vector" : "html-vector",
            BuildVersionLabel(exportedAt, quality));

        WriteManifest(manifestPath, "eindrapport", project, reportStepNumber, null, "alle-substap-previews-vector", "Eindrapportage uit vector rapportpreviews", result, quality);
        SaveExportRecord(project.Id, reportStepNumber, result, quality);
        AppendExportHistory(project.Id, reportStepNumber, "eindrapport_export_history", "Eindrapport", result, quality);

        if (openAfterExport)
        {
            Process.Start(new ProcessStartInfo(pdfCreated ? pdfPath : htmlPath) { UseShellExecute = true });
        }

        return result;
    }

    public ReportExportResult ExportPreviewPage(
        PrescanProject project,
        int stepNumber,
        string? substepNumber,
        string sectionName,
        string title,
        string html,
        string pngPath,
        ReportQualitySummary quality,
        bool openAfterExport)
    {
        var exportedAt = DateTimeOffset.Now;
        var exportDir = GetExportDirectory();
        Directory.CreateDirectory(exportDir);

        var basePath = Path.Combine(exportDir, BuildPreviewBaseName(project.Name, stepNumber, substepNumber, sectionName, exportedAt, quality));
        var htmlPath = basePath + ".html";
        var targetPngPath = basePath + ".png";
        var manifestPath = basePath + ".manifest.json";

        File.Copy(pngPath, targetPngPath, overwrite: true);
        File.WriteAllText(htmlPath, html.Replace("__BOREVEXA_PREVIEW_IMAGE__", new Uri(targetPngPath).AbsoluteUri), Encoding.UTF8);

        var result = new ReportExportResult(
            htmlPath,
            targetPngPath,
            manifestPath,
            exportedAt,
            "html-preview",
            BuildVersionLabel(exportedAt, quality));

        WriteManifest(manifestPath, "rapportpreview", project, stepNumber, substepNumber, sectionName, title, result, quality);
        SavePreviewExportRecord(project.Id, stepNumber, substepNumber, sectionName, title, result, quality);
        AppendExportHistory(project.Id, stepNumber, BuildPreviewExportHistoryKey(substepNumber), "Rapportpreview", result, quality);

        if (openAfterExport)
        {
            Process.Start(new ProcessStartInfo(htmlPath) { UseShellExecute = true });
        }

        return result;
    }

    public ReportExportResult ExportPreviewPages(
        PrescanProject project,
        int stepNumber,
        string? substepNumber,
        string sectionName,
        string title,
        string html,
        IReadOnlyList<string> pngPaths,
        ReportQualitySummary quality,
        bool openAfterExport)
    {
        if (pngPaths.Count == 0)
        {
            throw new InvalidOperationException("Geen rapportpreview-pagina's beschikbaar voor export.");
        }

        var exportedAt = DateTimeOffset.Now;
        var exportDir = GetExportDirectory();
        Directory.CreateDirectory(exportDir);

        var basePath = Path.Combine(exportDir, BuildPreviewBaseName(project.Name, stepNumber, substepNumber, sectionName, exportedAt, quality));
        var htmlPath = basePath + ".html";
        var manifestPath = basePath + ".manifest.json";
        var targetPngPaths = pngPaths
            .Select((pngPath, index) =>
            {
                var targetPath = $"{basePath}-pagina-{index + 1}.png";
                File.Copy(pngPath, targetPath, overwrite: true);
                return targetPath;
            })
            .ToArray();

        var pageImages = string.Join(
            Environment.NewLine,
            targetPngPaths.Select((pngPath, index) =>
            {
                var uri = new Uri(pngPath).AbsoluteUri;
                var sheetClass = IsLandscapePreviewImage(pngPath) ? "sheet landscape" : "sheet";
                return $"<main class=\"{sheetClass}\"><img src=\"{uri}\" alt=\"Rapportpreview {index + 1}\"></main>";
            }));
        File.WriteAllText(
            htmlPath,
            html.Replace("__BOREVEXA_PREVIEW_PAGES__", pageImages)
                .Replace("__BOREVEXA_PREVIEW_IMAGE__", new Uri(targetPngPaths[0]).AbsoluteUri),
            Encoding.UTF8);

        var result = new ReportExportResult(
            htmlPath,
            targetPngPaths[0],
            manifestPath,
            exportedAt,
            "html-preview",
            BuildVersionLabel(exportedAt, quality));

        WriteManifest(manifestPath, "rapportpreview", project, stepNumber, substepNumber, sectionName, title, result, quality);
        SavePreviewExportRecord(project.Id, stepNumber, substepNumber, sectionName, title, result, quality);
        AppendExportHistory(project.Id, stepNumber, BuildPreviewExportHistoryKey(substepNumber), "Rapportpreview", result, quality);

        if (openAfterExport)
        {
            Process.Start(new ProcessStartInfo(htmlPath) { UseShellExecute = true });
        }

        return result;
    }

    private static bool IsLandscapePreviewImage(string pngPath)
    {
        try
        {
            using var stream = File.OpenRead(pngPath);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.FirstOrDefault();
            return frame is not null && frame.PixelWidth > frame.PixelHeight;
        }
        catch
        {
            return false;
        }
    }

    public static string GetExportDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Borevexa", "PrescanNative", "Exports");

    private void SaveExportRecord(Guid projectId, int reportStepNumber, ReportExportResult result, ReportQualitySummary quality)
    {
        projects.SaveStepData(projectId, reportStepNumber, ReportDataKeys.ReportExport, JsonSerializer.Serialize(new
        {
            exportedAt = result.ExportedAt,
            path = result.HtmlPath,
            pdfPath = result.Format.Contains("pdf", StringComparison.OrdinalIgnoreCase) ? result.ImagePath : null,
            manifestPath = result.ManifestPath,
            format = result.Format,
            version = result.VersionLabel,
            reportStatus = quality.StatusLabel,
            quality = new
            {
                quality.TotalIssues,
                quality.HighIssues,
                quality.MediumIssues,
                quality.LowIssues,
                quality.IsReady
            }
        }, _jsonOptions));
    }

    private void SavePreviewExportRecord(
        Guid projectId,
        int stepNumber,
        string? substepNumber,
        string sectionName,
        string title,
        ReportExportResult result,
        ReportQualitySummary quality)
    {
        projects.SaveStepData(projectId, stepNumber, BuildPreviewExportKey(substepNumber), JsonSerializer.Serialize(new
        {
            exportedAt = result.ExportedAt,
            path = result.HtmlPath,
            imagePath = result.ImagePath,
            manifestPath = result.ManifestPath,
            format = result.Format,
            version = result.VersionLabel,
            stepNumber,
            substepNumber,
            sectionName,
            title,
            reportStatus = quality.StatusLabel,
            quality = new
            {
                quality.TotalIssues,
                quality.HighIssues,
                quality.MediumIssues,
                quality.LowIssues,
                quality.IsReady
            }
        }, _jsonOptions));
    }

    private void WriteManifest(
        string manifestPath,
        string kind,
        PrescanProject project,
        int stepNumber,
        string? substepNumber,
        string sectionName,
        string title,
        ReportExportResult result,
        ReportQualitySummary quality)
    {
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new
        {
            kind,
            project = new
            {
                project.Id,
                project.Name,
                project.Client,
                project.Location,
                project.Status
            },
            stepNumber,
            substepNumber,
            sectionName,
            title,
            exportedAt = result.ExportedAt,
            version = result.VersionLabel,
            format = result.Format,
            htmlPath = result.HtmlPath,
            imagePath = result.ImagePath,
            pdfPath = result.Format.Contains("pdf", StringComparison.OrdinalIgnoreCase) ? result.ImagePath : null,
            reportStatus = quality.StatusLabel,
            quality = new
            {
                quality.TotalIssues,
                quality.HighIssues,
                quality.MediumIssues,
                quality.LowIssues,
                quality.IsReady
            }
        }, _jsonOptions), Encoding.UTF8);
    }

    private static bool TryPrintHtmlToPdfWithEdge(string htmlPath, string pdfPath)
    {
        var edgePath = FindEdgeExecutable();
        if (string.IsNullOrWhiteSpace(edgePath) || !File.Exists(edgePath)) return false;

        var userDataDir = Path.Combine(Path.GetTempPath(), $"borevexa-edge-pdf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(userDataDir);
        try
        {
            var sourceUri = new Uri(htmlPath).AbsoluteUri;
            var startInfo = new ProcessStartInfo(edgePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                Arguments = $"--headless --disable-gpu --no-first-run --user-data-dir=\"{userDataDir}\" --print-to-pdf=\"{pdfPath}\" --print-to-pdf-no-header \"{sourceUri}\""
            };

            using var process = Process.Start(startInfo);
            if (process is null) return false;
            if (!process.WaitForExit(60000))
            {
                try { process.Kill(entireProcessTree: true); } catch (System.Exception swallowedException)
        {
            AppLog.Swallowed(swallowedException);
        }
                return false;
            }

            return File.Exists(pdfPath) && new FileInfo(pdfPath).Length > 1024;
        }
        catch
        {
            return false;
        }
        finally
        {
            try { Directory.Delete(userDataDir, recursive: true); } catch (System.Exception swallowedException)
        {
            AppLog.Swallowed(swallowedException);
        }
        }
    }

    private static string? FindEdgeExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Edge", "Application", "msedge.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private void AppendExportHistory(Guid projectId, int stepNumber, string key, string kind, ReportExportResult result, ReportQualitySummary quality)
    {
        var history = ReadExportHistory(projectId, stepNumber, key);
        history.Insert(0, new StoredExportHistoryItem(
            result.ExportedAt,
            kind,
            result.VersionLabel,
            quality.StatusLabel,
            result.HtmlPath,
            result.ImagePath,
            result.ManifestPath));

        projects.SaveStepData(projectId, stepNumber, key, JsonSerializer.Serialize(history.Take(25).ToList(), _jsonOptions));
    }

    private List<StoredExportHistoryItem> ReadExportHistory(Guid projectId, int stepNumber, string key)
    {
        var json = projects.GetStepData(projectId, stepNumber, key);
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<StoredExportHistoryItem>>(json, _jsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string BuildFinalReportBaseName(string projectName, DateTimeOffset exportedAt, ReportQualitySummary quality)
    {
        var safeName = Regex.Replace(projectName, @"[^A-Za-z0-9_\-]+", "-").Trim('-');
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "prescan";

        var status = quality.IsReady ? "definitief" : "concept";
        return $"eindrapport-{safeName}-{status}-{exportedAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}";
    }

    private static string BuildPreviewBaseName(string projectName, int stepNumber, string? substepNumber, string sectionName, DateTimeOffset exportedAt, ReportQualitySummary quality)
    {
        var safeName = Regex.Replace(projectName, @"[^A-Za-z0-9_\-]+", "-").Trim('-');
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "prescan";

        var safeSection = Regex.Replace(sectionName, @"[^A-Za-z0-9_\-]+", "-").Trim('-');
        if (string.IsNullOrWhiteSpace(safeSection)) safeSection = "rapportpreview";

        var status = quality.IsReady ? "definitief" : "concept";
        var substep = string.IsNullOrWhiteSpace(substepNumber) ? $"stap-{stepNumber}" : $"substap-{substepNumber}";
        return $"rapportpreview-{safeName}-{substep}-{safeSection}-{status}-{exportedAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}";
    }

    private static string BuildPreviewExportKey(string? substepNumber) =>
        string.IsNullOrWhiteSpace(substepNumber)
            ? "rapportpreview_export"
            : $"rapportpreview_export_{substepNumber.Replace('.', '_')}";

    private static string BuildPreviewExportHistoryKey(string? substepNumber) =>
        string.IsNullOrWhiteSpace(substepNumber)
            ? "rapportpreview_export_history"
            : $"rapportpreview_export_history_{substepNumber.Replace('.', '_')}";

    private static string BuildVersionLabel(DateTimeOffset exportedAt, ReportQualitySummary quality)
    {
        var status = quality.IsReady ? "DEF" : "CONCEPT";
        return $"{status}-{exportedAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}";
    }
}
