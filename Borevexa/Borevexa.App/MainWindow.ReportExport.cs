using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Borevexa.App.Services;
using Borevexa.Core.Models;

using Borevexa.Core.Services;

namespace Borevexa.App;

public partial class MainWindow
{
    private void RenderFinalReportPanel(int stepNumber)
    {
        if (stepNumber != ReportStepNumber)
        {
            StepFinalReportPanel.Visibility = Visibility.Collapsed;
            return;
        }

        StepFinalReportPanel.Visibility = Visibility.Visible;
        if (_selectedProject is null)
        {
            StepFinalReportStatusText.Text = "Geen project actief.";
            StepFinalReportExportHistoryText.Text = "Nog geen exportgeschiedenis.";
            StepFinalReportText.Text = "Open of maak eerst een project.";
            return;
        }

        var signature = BuildFinalReportPanelSignature();
        if (string.Equals(_lastFinalReportPanelSignature, signature, StringComparison.Ordinal))
        {
            return;
        }

        _lastFinalReportPanelSignature = signature;
        RefreshFinalReportExportUi();
        var exportJson = _projects.GetStepData(_selectedProject.Id, ReportStepNumber, "eindrapport_export") ??
                         _projects.GetStepData(_selectedProject.Id, 10, "eindrapport_export");
        if (TryReadReportExportPath(exportJson, out var path))
        {
            StepFinalReportStatusText.Text = $"Laatste export: {path}";
        }
        else
        {
            StepFinalReportStatusText.Text = "Genereer de rapportdata opnieuw of exporteer direct naar PDF.";
        }

        StepFinalReportText.Text = "Live preview staat uit voor snelheid. Exporteren bouwt het volledige PDF-rapport op uit de opgeslagen rapportpreviews.";
    }

    private string BuildFinalReportPanelSignature()
    {
        return string.Join(";",
            _selectedProject?.Id.ToString("N") ?? "geen-project",
            _reportUiDataVersion.ToString(CultureInfo.InvariantCulture),
            _projectFiles.Count.ToString(CultureInfo.InvariantCulture));
    }

    private void RefreshFinalReportExportUi()
    {
        if (_selectedProject is null)
        {
            FinalReportExportButton.Content = "Concept exporteren";
            StepFinalReportExportHistoryText.Text = "Nog geen exportgeschiedenis.";
            return;
        }

        var quality = _reportQuality.EvaluateProject(_selectedProject.Id, ReportContractCatalog.GetAll());
        FinalReportExportButton.Content = quality.IsReady ? "Definitief exporteren" : "Concept exporteren";
        StepFinalReportExportHistoryText.Text = BuildFinalReportExportHistoryText();
    }

    private string BuildFinalReportExportHistoryText()
    {
        if (_selectedProject is null) return "Nog geen exportgeschiedenis.";
        var json = _projects.GetStepData(_selectedProject.Id, ReportStepNumber, "eindrapport_export_history");
        if (string.IsNullOrWhiteSpace(json)) return "Nog geen exportgeschiedenis.";

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
            {
                return "Nog geen exportgeschiedenis.";
            }

            var lines = document.RootElement.EnumerateArray()
                .Take(4)
                .Select(item =>
                {
                    var exportedAt = JsonText(item, "exportedAt", "");
                    var version = JsonText(item, "version", "-");
                    var status = JsonText(item, "status", "-");
                    var path = JsonText(item, "path", "");
                    var file = string.IsNullOrWhiteSpace(path) ? "-" : System.IO.Path.GetFileName(path);
                    var stamp = DateTimeOffset.TryParse(exportedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                        ? parsed.ToString("dd-MM HH:mm", CultureInfo.CurrentCulture)
                        : exportedAt;
                    return $"{stamp} - {version} - {status} - {file}";
                });

            return "Exportgeschiedenis: " + string.Join(" | ", lines);
        }
        catch
        {
            return "Exportgeschiedenis kon niet worden gelezen.";
        }
    }

    private void RenderStepTenReport(bool reloadSources = true)
    {
        ReportPreviewTitleText.Text = "Eindrapportage export";
        ReportExportHintText.Text = "Preview is uitgeschakeld voor snelheid. Genereer opnieuw actualiseert de rapportdata; exporteren bouwt de volledige PDF.";
        ReportPartEditorPanel.Visibility = Visibility.Collapsed;
        StepTenReportContent.Children.Clear();
        StepTenPreviewSurface.Visibility = Visibility.Collapsed;
        if (_selectedProject is null)
        {
            ReportProjectCodeText.Text = "Geen project";
            ReportProjectLocationText.Text = "";
            ReportGeneratedText.Text = "";
            return;
        }

        var context = BuildReportStartContext();
        ReportProjectCodeText.Text = _selectedProject.Name;
        ReportProjectLocationText.Text = context.ReportLocation;
        ReportGeneratedText.Text = $"Laatst geopend: {DateTime.Now:dd MMMM yyyy}";
        RefreshFinalReportExportUi();
    }

    private void RefreshFinalReportSourcesFromStepData(bool saveCurrentInputs)
    {
        if (_selectedProject is null) return;

        if (saveCurrentInputs)
        {
            SaveProjectSnapshot();
            if (_loadedBoringConfigProjectId == _selectedProject.Id)
            {
                SaveBoringConfigSnapshot();
            }
        }

        _projectFiles = _projects.GetProjectFiles(_selectedProject.Id);
        _mapLayerBuilder.ClearCache();
        ClearEnvironmentAnalysisCache();

        _currentBoreTraceJson = GetStoredBoreTraceJson();
        _currentBoreTracePoints = [];
        _currentMachinePlacementsJson = GetStoredMachinePlacementsJson();
        _profilePoints = [];
        _profileScreenMetrics = null;

        _loadedBoringConfigProjectId = null;
        _boringItems.Clear();
        _selectedMachineId = null;
        _selectedDrillingTechnique = "walkover";
        EnsureBoringConfigLoaded(seedDefaultWhenMissing: false);
        RemoveLegacyDemoBoringConfigForReport();
    }

    private string RegenerateFinalReportPreview()
    {
        if (_selectedProject is null) return "Geen project actief.";

        RefreshFinalReportSourcesFromStepData(saveCurrentInputs: true);
        RefreshAllStepReportData();
        SaveReportSnapshotFromCurrentStepData();
        RenderStepTenReport(reloadSources: false);
        StepFinalReportPanel.Visibility = Visibility.Visible;
        StepFinalReportStatusText.Text = $"Eindrapport opnieuw opgebouwd uit stapdata: {DateTime.Now:dd-MM-yyyy HH:mm}";
        StepFinalReportText.Text = "Rapportdata is opnieuw opgebouwd. De live preview is uitgeschakeld; exporteren bouwt de volledige rapportage op als PDF.";
        RefreshFinalReportExportUi();
        RefreshWorkflowReportStatus(ReportStepNumber);
        return "Eindrapport opnieuw gegenereerd\n\nDe rapportdata is opnieuw opgebouwd uit de laatst opgeslagen project-, import-, boorlijn-, profiel-, omgevings- en machinegegevens. De live preview in stap 10 is uitgeschakeld om de app sneller te houden.";
    }

    private async void FinalReportExport_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ConfirmFullReportQualityBeforeExport())
        {
            return;
        }

        await RunUiBackgroundOperationAsync(
            "Eindrapport exporteren naar PDF...",
            () => ExportFinalReportHtmlAsync(openAfterExport: true));
    }

    private async void InlineReportPreviewPdfExport_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ConfirmReportQualityBeforeExport())
        {
            return;
        }

        // Vers kaartbeeld vastleggen zodat de export exact het huidige kaartbeeld bevat.
        await EnsureFreshMapReportCaptureAsync();

        await RunUiBackgroundOperationAsync(
            "Rapportpreview exporteren...",
            () => ExportCurrentInlineReportPreviewHtmlAsync(openAfterExport: true));
    }

    private bool ConfirmReportQualityBeforeExport()
    {
        if (_selectedProject is null || _selectedStep is null)
        {
            return true;
        }

        var exportChapter = _reportPreviewWindowScope == ReportPreviewWindowScope.Chapter;
        var summary = exportChapter || _selectedSubstep is null
            ? _reportQuality.EvaluateStep(_selectedProject.Id, _selectedStep.Number)
            : _reportQuality.EvaluateSubstep(_selectedProject.Id, _selectedStep.Number, _selectedSubstep.Number);

        if (summary.IsReady)
        {
            return true;
        }

        return ConfirmQualitySummaryForExport(
            summary,
            "Rapportcontrole",
            "Dit rapportonderdeel is nog niet rapportklaar.");
    }

    private bool ConfirmFullReportQualityBeforeExport()
    {
        if (_selectedProject is null)
        {
            return true;
        }

        var summary = _reportQuality.EvaluateProject(_selectedProject.Id, ReportContractCatalog.GetAll());
        if (summary.IsReady)
        {
            return true;
        }

        return ConfirmQualitySummaryForExport(
            summary,
            "Rapportcontrole eindrapport",
            "Het eindrapport is nog niet rapportklaar.");
    }

    private static bool ConfirmQualitySummaryForExport(ReportQualitySummary summary, string title, string intro)
    {
        var issueText = BuildReportQualityActions(summary);
        if (summary.HighIssues > 0)
        {
            MessageBox.Show(
                $"{intro}\n\n{BuildReportQualityHeadline(summary)}\n{issueText}\n\nExport is geblokkeerd zolang er hoge aandachtspunten zijn.",
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Stop);
            return false;
        }

        var result = MessageBox.Show(
            $"{intro}\n\n{BuildReportQualityHeadline(summary)}\n{issueText}\n\nToch exporteren als concept?",
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        return result == MessageBoxResult.Yes;
    }

    private async void FinalReportRegenerate_OnClick(object sender, RoutedEventArgs e)
    {
        await RunUiBackgroundOperationAsync(
            "Rapportdata verversen...",
            async () =>
            {
                await Dispatcher.Yield(DispatcherPriority.Background);
                return RegenerateFinalReportPreview();
            });
    }

    private string ExportCurrentInlineReportPreviewHtml(bool openAfterExport)
    {
        if (_selectedProject is null || _selectedStep is null) return "Geen project of processtap actief.";

        SaveStepReportDataForStep(_selectedStep.Number);
        RenderInlineReportPreview();
        InlineReportPreviewContent.UpdateLayout();

        var previewPages = InlineReportPreviewContent.Children
            .OfType<FrameworkElement>()
            .ToList();
        if (previewPages.Count == 0)
        {
            return "Geen rapportpreview beschikbaar om te exporteren.";
        }

        var exportChapter = _reportPreviewWindowScope == ReportPreviewWindowScope.Chapter;
        var sectionName = !exportChapter && _selectedSubstep is not null
            ? $"substap-{DisplaySubstepNumber(_selectedSubstep).Replace('.', '-')}"
            : $"stap-{DisplayStepNumber(_selectedStep.Number).Replace('.', '-')}";
        var title = !exportChapter && _selectedSubstep is not null
            ? DisplayReportSectionTitle(_selectedSubstep)
            : $"{DisplayStepNumber(_selectedStep.Number)} {_selectedStep.Title}";
        var quality = !exportChapter && _selectedSubstep is not null
            ? _reportQuality.EvaluateSubstep(_selectedProject.Id, _selectedStep.Number, _selectedSubstep.Number)
            : _reportQuality.EvaluateStep(_selectedProject.Id, _selectedStep.Number);
        var tempPngPaths = new List<string>();

        try
        {
            for (var index = 0; index < previewPages.Count; index++)
            {
                var tempPngPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"borevexa-rapportpreview-{Guid.NewGuid():N}-pagina-{index + 1}.png");
                ReportPreviewService.SaveFrameworkElementAsPng(previewPages[index], tempPngPath, ReportPreviewExportScale);
                tempPngPaths.Add(tempPngPath);
            }

            var html = BuildInlinePreviewExportHtml(title, previewPages.Count);
            var result = _reportExport.ExportPreviewPages(
                _selectedProject,
                _selectedStep.Number,
                _selectedSubstep?.Number,
                sectionName,
                title,
                html,
                tempPngPaths,
                quality,
                openAfterExport);
            RefreshWorkflowReportStatus(_selectedStep.Number);
            return result.Format == "pdf-preview"
                ? $"Rapportpreview geexporteerd als PDF ({result.VersionLabel})\n\nPDF:\n{result.ImagePath}\n\nHTML:\n{result.HtmlPath}\n\n{previewPages.Count} A4-pagina('s) opgenomen. Alleen de huidige processtap/substap is opgenomen."
                : $"Rapportpreview geexporteerd ({result.VersionLabel})\n\nHTML:\n{result.HtmlPath}\n\nManifest:\n{result.ManifestPath}\n\n{previewPages.Count} A4-pagina('s) opgenomen. Alleen de huidige processtap/substap is opgenomen. Kon geen PDF genereren (Microsoft Edge niet gevonden) - gebruik in de browser: Afdrukken -> Opslaan als PDF.";
        }
        catch (Exception exception)
        {
            return $"Rapportpreview exporteren is niet gelukt:\n{exception.Message}";
        }
        finally
        {
            foreach (var tempPngPath in tempPngPaths)
            {
                try { System.IO.File.Delete(tempPngPath); } catch (System.Exception swallowedException)
        {
            AppLog.Swallowed(swallowedException);
        }
            }
        }
    }

    private async Task<string> ExportCurrentInlineReportPreviewHtmlAsync(bool openAfterExport)
    {
        if (_selectedProject is null || _selectedStep is null) return "Geen project of processtap actief.";

        SaveStepReportDataForStep(_selectedStep.Number);
        RenderInlineReportPreview();
        InlineReportPreviewContent.UpdateLayout();

        var previewPages = InlineReportPreviewContent.Children
            .OfType<FrameworkElement>()
            .ToList();
        if (previewPages.Count == 0)
        {
            return "Geen rapportpreview beschikbaar om te exporteren.";
        }

        var sectionName = _selectedSubstep is not null
            ? $"substap-{DisplaySubstepNumber(_selectedSubstep).Replace('.', '-')}"
            : $"stap-{DisplayStepNumber(_selectedStep.Number).Replace('.', '-')}";
        var title = _selectedSubstep is not null
            ? DisplayReportSectionTitle(_selectedSubstep)
            : $"{DisplayStepNumber(_selectedStep.Number)} {_selectedStep.Title}";
        var quality = _selectedSubstep is not null
            ? _reportQuality.EvaluateSubstep(_selectedProject.Id, _selectedStep.Number, _selectedSubstep.Number)
            : _reportQuality.EvaluateStep(_selectedProject.Id, _selectedStep.Number);
        var tempPngPaths = new List<string>();
        var project = _selectedProject;
        var stepNumber = _selectedStep.Number;
        var substepNumber = _selectedSubstep?.Number;

        try
        {
            for (var index = 0; index < previewPages.Count; index++)
            {
                var tempPngPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"borevexa-rapportpreview-{Guid.NewGuid():N}-pagina-{index + 1}.png");
                ReportPreviewService.SaveFrameworkElementAsPng(previewPages[index], tempPngPath, ReportPreviewExportScale);
                tempPngPaths.Add(tempPngPath);
            }

            var html = BuildInlinePreviewExportHtml(title, previewPages.Count);
            var result = await Task.Run(() => _reportExport.ExportPreviewPages(
                project,
                stepNumber,
                substepNumber,
                sectionName,
                title,
                html,
                tempPngPaths,
                quality,
                openAfterExport));
            RefreshWorkflowReportStatus(stepNumber);
            return $"Rapportpreview geexporteerd ({result.VersionLabel})\n\nHTML:\n{result.HtmlPath}\n\nManifest:\n{result.ManifestPath}\n\n{previewPages.Count} A4-pagina('s) opgenomen. Alleen de huidige processtap/substap is opgenomen. Gebruik in de browser: Afdrukken -> Opslaan als PDF.";
        }
        catch (Exception exception)
        {
            return $"Rapportpreview exporteren is niet gelukt:\n{exception.Message}";
        }
        finally
        {
            foreach (var tempPngPath in tempPngPaths)
            {
                try { System.IO.File.Delete(tempPngPath); } catch (System.Exception swallowedException)
        {
            AppLog.Swallowed(swallowedException);
        }
            }
        }
    }

    private static string BuildInlinePreviewExportHtml(string title, int pageCount = 1)
    {
        var encodedTitle = System.Net.WebUtility.HtmlEncode(title);
        var encodedHint = System.Net.WebUtility.HtmlEncode(pageCount == 1
            ? "Alleen de huidige rapportpreview wordt afgedrukt."
            : $"{pageCount} A4-pagina's van de huidige rapportpreview worden afgedrukt.");
        return $$$"""
<!doctype html>
<html lang="nl">
<head>
<meta charset="utf-8">
<title>Rapportpreview - {{{encodedTitle}}}</title>
<style>
@page{size:A4;margin:0}
@page landscape{size:A4 landscape;margin:0}
body{margin:0;background:#e8eef2;font-family:Arial,Segoe UI,sans-serif;color:#101820}
.toolbar{max-width:297mm;margin:14px auto}
.print{background:#2f3a45;color:white;border:0;border-radius:1px;padding:10px 16px;font-weight:700}
.hint{margin-left:10px;color:#748494;font-size:12px}
.sheet{position:relative;width:210mm;height:297mm;margin:14px auto;background:white;box-sizing:border-box;page-break-after:always;overflow:hidden}
.sheet.landscape{page:landscape;width:297mm;height:210mm}
.sheet:last-child{page-break-after:auto}
.sheet img{display:block;width:100%;height:100%;object-fit:contain}
@media print{body{background:white}.toolbar{display:none}.sheet{margin:0;box-shadow:none}}
</style>
</head>
<body>
<div class="toolbar"><button class="print" onclick="window.print()">Exporteer naar PDF</button><span class="hint">{{{encodedHint}}}</span></div>
__BOREVEXA_PREVIEW_PAGES__
</body>
</html>
""";
    }

    private string ExportFinalReportHtml(bool openAfterExport)
    {
        if (_selectedProject is null) return "Geen project actief.";

        RefreshFinalReportSourcesFromStepData(saveCurrentInputs: true);
        RefreshAllStepReportData();
        SaveReportSnapshotFromCurrentStepData();
        var quality = _reportQuality.EvaluateProject(_selectedProject.Id, ReportContractCatalog.GetAll());

        try
        {
            var vectorPages = RenderFinalReportSubstepPreviewPagesToVectorHtml(refreshStepData: false);
            var html = BuildFinalReportVectorBundleHtml(_selectedProject.Name, vectorPages);
            var result = _reportExport.ExportFinalReportVectorPdf(_selectedProject, ReportStepNumber, html, quality, openAfterExport);
            RefreshWorkflowReportStatus(ReportStepNumber);
            RefreshFinalReportExportUi();
            var pdfLine = string.IsNullOrWhiteSpace(result.ImagePath) ? "PDF: niet automatisch aangemaakt; open de HTML en print naar PDF." : $"PDF:\n{result.ImagePath}";
            return $"Eindrapportage geexporteerd ({result.VersionLabel})\n\n{pdfLine}\n\nHTML-bron:\n{result.HtmlPath}\n\nManifest:\n{result.ManifestPath}\n\n{vectorPages.Count} vector rapportpreview-pagina('s) opgenomen. Tekst, tabellen, lijnen en labels blijven vector/selecteerbaar; kaartbeelden en luchtfoto's blijven rasterbronnen.";
        }
        catch (Exception exception)
        {
            RefreshWorkflowReportStatus(ReportStepNumber);
            RefreshFinalReportExportUi();
            return $"Eindrapportage exporteren is niet gelukt:\n{exception.Message}";
        }
    }

    private async Task<string> ExportFinalReportHtmlAsync(bool openAfterExport)
    {
        if (_selectedProject is null) return "Geen project actief.";

        RefreshFinalReportSourcesFromStepData(saveCurrentInputs: true);
        RefreshAllStepReportData();
        SaveReportSnapshotFromCurrentStepData();
        var project = _selectedProject;
        var quality = _reportQuality.EvaluateProject(project.Id, ReportContractCatalog.GetAll());

        try
        {
            var vectorPages = RenderFinalReportSubstepPreviewPagesToVectorHtml(refreshStepData: false);
            var html = BuildFinalReportVectorBundleHtml(project.Name, vectorPages);
            StepFinalReportStatusText.Text = $"PDF wordt opgebouwd... {vectorPages.Count} pagina('s)";
            var result = await Task.Run(() => _reportExport.ExportFinalReportVectorPdf(project, ReportStepNumber, html, quality, openAfterExport));
            RefreshWorkflowReportStatus(ReportStepNumber);
            RefreshFinalReportExportUi();
            var pdfLine = string.IsNullOrWhiteSpace(result.ImagePath) ? "PDF: niet automatisch aangemaakt; open de HTML en print naar PDF." : $"PDF:\n{result.ImagePath}";
            return $"Eindrapportage geexporteerd ({result.VersionLabel})\n\n{pdfLine}\n\nHTML-bron:\n{result.HtmlPath}\n\nManifest:\n{result.ManifestPath}\n\n{vectorPages.Count} vector rapportpreview-pagina('s) opgenomen. Tekst, tabellen, lijnen en labels blijven vector/selecteerbaar; kaartbeelden en luchtfoto's blijven rasterbronnen.";
        }
        catch (Exception exception)
        {
            RefreshWorkflowReportStatus(ReportStepNumber);
            RefreshFinalReportExportUi();
            return $"Eindrapportage exporteren is niet gelukt:\n{exception.Message}";
        }
    }

    private IReadOnlyList<string> RenderFinalReportSubstepPreviewPagesToVectorHtml(bool refreshStepData = true)
    {
        if (_selectedProject is null) return [];

        var pages = new List<string>();
        foreach (var page in CreateCustomerFinalReportPreviewPages(refreshStepData).OfType<FrameworkElement>())
        {
            pages.Add(BuildVectorReportPageHtml(page, pages.Count + 1));
        }

        return pages;
    }

    private IEnumerable<(int StepNumber, PrescanSubstep Substep)> EnumerateFinalReportSubsteps()
    {
        foreach (var stepNumber in _workspaces.Keys.OrderBy(step => step))
        {
            if (stepNumber > ReportStepNumber) continue;
            foreach (var substep in StepReportCatalog.GetSubsteps(stepNumber))
            {
                if (ShouldIncludeSubstepInCustomerFinalReport(substep))
                {
                    yield return (stepNumber, substep);
                }
            }
        }
    }

    private static bool ShouldIncludeSubstepInCustomerFinalReport(PrescanSubstep substep)
    {
        if (substep.StepNumber < ReportStepNumber) return true;
        return substep.IsChapterIntroduction ||
               string.Equals(substep.Number, "10.2", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildFinalReportVectorBundleHtml(string projectName, IReadOnlyList<string> pageHtml)
    {
        var encodedTitle = System.Net.WebUtility.HtmlEncode($"Eindrapportage - {projectName}");
        var encodedHint = System.Net.WebUtility.HtmlEncode($"{pageHtml.Count} A4-pagina's uit de rapportpreviews worden als vector-PDF opgebouwd.");
        return $$$"""
<!doctype html>
<html lang="nl">
<head>
<meta charset="utf-8">
<title>{{{encodedTitle}}}</title>
<style>
@page{size:A4;margin:0}
@page landscape{size:A4 landscape;margin:0}
body{margin:0;background:#e8eef2;font-family:Arial,Segoe UI,sans-serif;color:#101820}
.toolbar{max-width:297mm;margin:14px auto}
.print{background:#2f3a45;color:white;border:0;border-radius:1px;padding:10px 16px;font-weight:700}
.hint{margin-left:10px;color:#748494;font-size:12px}
.sheet{position:relative;width:210mm;height:297mm;margin:14px auto;background:white;box-sizing:border-box;page-break-after:always;overflow:hidden}
.sheet.landscape{page:landscape;width:297mm;height:210mm}
.sheet:last-child{page-break-after:auto}
.vitem{position:absolute;box-sizing:border-box;overflow:hidden}
.vinner{position:absolute;box-sizing:border-box}
.vtext{white-space:pre-wrap;line-height:1.25;overflow:hidden;overflow-wrap:break-word;word-break:normal}
.vimage{object-fit:contain}
.vsvg{position:absolute;overflow:visible}
@media print{body{background:white}.toolbar{display:none}.sheet{margin:0;box-shadow:none}}
</style>
</head>
<body>
<div class="toolbar"><button class="print" onclick="window.print()">Open / print PDF</button><span class="hint">{{{encodedHint}}}</span></div>
{{{string.Join(Environment.NewLine, pageHtml)}}}
</body>
</html>
""";
    }
}
