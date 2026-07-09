using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Xml.Linq;
using Borevexa.App.Models;
using Borevexa.App.Reports.Blocks;
using Borevexa.App.Services;
using Borevexa.Cad;
using Borevexa.Core.Models;
using Borevexa.Core.Services;
using Borevexa.Geo;
using Docnet.Core;
using Docnet.Core.Converters;
using Docnet.Core.Models;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingImageFormat = System.Drawing.Imaging.ImageFormat;
using DrawingImageLockMode = System.Drawing.Imaging.ImageLockMode;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using DrawingRectangle = System.Drawing.Rectangle;
using NtsCoordinate = NetTopologySuite.Geometries.Coordinate;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;
using NtsGeometryFactory = NetTopologySuite.Geometries.GeometryFactory;
using NtsLineString = NetTopologySuite.Geometries.LineString;
using NtsLinearRing = NetTopologySuite.Geometries.LinearRing;
using NtsPolygon = NetTopologySuite.Geometries.Polygon;
using UglyToad.PdfPig;

namespace Borevexa.App;

// Kaart-rapportagepijplijn: captures, rapportlocks, kaartstaat en render-herstel.
// Fase 3-opsplitsing (07-07-2026) van MainWindow.xaml.cs; gedrag ongewijzigd.

public partial class MainWindow
{
    private bool IsMapReportLocked(int stepNumber)
    {
        if (_selectedProject is null || !IsMapWorkspaceStep(stepNumber)) return false;
        var runtime = GetCurrentMapWorkspaceRuntime(stepNumber);
        var includeLegacyFallback = !runtime.SuppressLegacyFallback;
        return _reportPreview.IsMapReportLocked(_selectedProject.Id, stepNumber, runtime.ContextKey, includeLegacyFallback);
    }

    private bool IsCurrentMapReportLocked() => _selectedStep is not null && IsMapReportLocked(_selectedStep.Number);

    private bool BlockIfCurrentMapReportLocked()
    {
        if (!IsCurrentMapReportLocked()) return false;
        _mapLocked = true;
        UpdateMapLockButton();
        SendMapMessage(JsonSerializer.Serialize(new { type = "mapLock", locked = true }, JsonOptions));
        OutputText.Text = "Opgeslagen voor rapportage\n\nDeze GIS kaart is bevroren en wordt gebruikt in de rapportage.";
        return true;
    }

    private void UpdateMapReportLockButton()
    {
        if (_selectedStep is null || !IsMapWorkspaceStep(_selectedStep.Number)) return;
        var locked = IsMapReportLocked(_selectedStep.Number);
        ApplyMapReportLockButtonState(HeaderMapSaveButton, locked);
        ApplyMapReportLockButtonState(MapToolbarSaveButton, locked);
        _mapLocked = locked;
    }

    private void ApplyMapReportLockButtonState(Button button, bool locked)
    {
        button.Content = locked ? "Opgeslagen voor rapportage" : "Opslaan voor rapportage";
        button.ToolTip = locked ? "Deze kaart is opgeslagen voor de rapportage" : "Sla deze kaart op voor de rapportage";
        button.IsEnabled = true;
        button.Background = locked ? Brush("#F3F4F6") : Brush("#334155");
        button.Foreground = locked ? Brush("#15803D") : Brushes.White;
        button.BorderBrush = locked ? Brush("#BBF7D0") : Brush("#334155");
    }

    private void CaptureMapLockState(string message)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            if (document.RootElement.TryGetProperty("locked", out var locked))
            {
                _mapLocked = locked.GetBoolean();
                UpdateMapLockButton();
            }
        }
        catch (System.Exception swallowedException)
        {
            // Lock state is UI-only; ignore malformed browser messages.
            AppLog.Swallowed(swallowedException);
        }
    }

    private async System.Threading.Tasks.Task SaveAndLockMapForReportAsync(int stepNumber)
    {
        if (_selectedProject is null) return;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (IsBorelineStep(stepNumber)) RequestBoreTraceSave();
            // "Opslaan voor rapportage" op stap 7 vergrendelde tot nu toe alleen het
            // kaartbeeld — de dieptepunten (dwarsprofiel/segmenten) werden alleen
            // opgeslagen via de losse "Sla dieptepunten op"-knop. Live-bewerkingen (bv.
            // via de nieuwe klik-op-de-grafiek-tools) konden zo ongemerkt verloren gaan
            // bij een herstart, terwijl de kaart al als "definitief" was vastgezet.
            if (stepNumber == ProfileStepNumber) SaveDepthProfile();

            SaveProjectSnapshot();
            SaveMapStateForStep(stepNumber, false);
            SendMapMessage("{\"type\":\"save\"}");
            OutputText.Text = "Opslaan voor rapportage\n\nKaartbeeld wordt opgeslagen voor de rapportage...";
            var workspace = GetCurrentMapWorkspaceRuntime(stepNumber, GetActiveMapReportVariantKey(stepNumber)).Definition;
            var stepThreeCaptures = (PrimaryPath: "", CoverOsmMapPath: "", BagMapPath: "", PhotoMapPath: "", KlicBagMapPath: "", KlicPhotoMapPath: "");
            var imagePath = "";
            if (workspace.HasMultiVariantReportCapture)
            {
                stepThreeCaptures = await CaptureStepThreeLiveMapForReportWithRetryAsync(stepNumber);
                imagePath = stepThreeCaptures.PrimaryPath;
            }
            else
            {
                // Zelfde robuustheid als de multi-variant flow: capture met retry in
                // plaats van een eenmalige poging, zodat een trage tegel-load geen leeg
                // rapportbeeld geeft.
                imagePath = await CaptureCurrentStepThreeMapWithRetryAsync(stepNumber, GetActiveMapReportVariantKey(stepNumber));
            }

            if (string.IsNullOrWhiteSpace(imagePath))
            {
                // Nooit vergrendelen zonder bruikbaar kaartbeeld; anders blijft de
                // rapportage stilletjes op een oud beeld staan.
                OutputText.Text = "Kaart niet opgeslagen voor rapportage\n\nDe live kaartcapture is nog niet bruikbaar. Wacht tot de kaart volledig geladen is en probeer 'Opslaan voor rapportage' opnieuw.";
                RefreshWorkflowReportStatus(stepNumber);
                return;
            }

            if (!workspace.HasMultiVariantReportCapture)
            {
                // Bevries het vergrendelde beeld in een eigen bestand (de multi-variant
                // flow heeft al eigen variantbestanden); report-current.png rolt door met
                // elke nieuwe capture en mag nooit het "vastgezette" rapportbeeld zijn.
                imagePath = FreezeLockedMapImage(stepNumber, imagePath);
            }

            var analysis = stepNumber == 4 ? BuildSurfaceAnalysisReportText() : "";

            _mapLocked = true;
            var payload = JsonSerializer.Serialize(new
            {
                locked = true,
                lockedAt = DateTimeOffset.Now,
                stepNumber,
                imagePath,
                imagePaths = workspace.HasMultiVariantReportCapture
                    ? new
                    {
                        coverOsm = stepThreeCaptures.CoverOsmMapPath,
                        pdokBag = stepThreeCaptures.BagMapPath,
                        pdokFoto = stepThreeCaptures.PhotoMapPath,
                        klicPdokBag = stepThreeCaptures.KlicBagMapPath,
                        klicPdokFoto = stepThreeCaptures.KlicPhotoMapPath
                    }
                    : null,
                baseLayer = _selectedMapBaseLayer,
                overlays = _mapOverlayStates,
                klicThemes = _klicThemeStates,
                bgtSurfaces = _bgtSurfaceStates,
                projectLayerVisibility = _projectLayerStates,
                camera = _lastMapCamera,
                mapContextKey = GetCurrentMapStateContextKey(stepNumber),
                substepNumber = _selectedSubstep?.Number,
                substepDisplayNumber = _selectedSubstep?.DisplayNumber,
                substepTitle = _selectedSubstep?.Title,
                analysis
            }, JsonOptions);
            _reportPreview.SaveReportLockJson(_selectedProject.Id, stepNumber, payload, GetCurrentMapStateContextKey(stepNumber));
            SendMapMessage(JsonSerializer.Serialize(new { type = "mapLock", locked = true }, JsonOptions));
            UpdateMapLockButton();
            RefreshWorkflowReportStatus(stepNumber);
            SaveStepReportDataForStep(stepNumber);
            RefreshInlineReportPreviewIfVisible();
            OutputText.Text = $"Kaart opgeslagen voor rapportage\n\nStap {stepNumber} gebruikt deze kaartafbeelding in de rapportage{(string.IsNullOrWhiteSpace(imagePath) ? "" : $" ({imagePath})")}.";
        }
        finally
        {
            stopwatch.Stop();
            LogPerformanceTimingIfSlow($"Kaart opslaan voor rapportage stap {stepNumber}", stopwatch.Elapsed, 200);
        }
    }

    private void UnlockMapForReport(int stepNumber)
    {
        if (_selectedProject is null) return;
        var payload = JsonSerializer.Serialize(new
        {
            locked = false,
            unlockedAt = DateTimeOffset.Now,
            stepNumber
        }, JsonOptions);
        _reportPreview.SaveReportLockJson(_selectedProject.Id, stepNumber, payload, GetCurrentMapStateContextKey(stepNumber));
        // Repair any leftover pollution from the (now-fixed) SaveReportLockJson bug that
        // used to always overwrite the shared, unscoped legacy lock key regardless of
        // which substep triggered it — a stale "locked" flag there could make an
        // unrelated substep of this step appear locked even after being unlocked here.
        // Explicitly clearing it on every unlock is a safe, idempotent no-op once the
        // legacy key is already clean.
        _reportPreview.SaveReportLockJson(_selectedProject.Id, stepNumber, payload);
        _mapLocked = false;
        SendMapMessage(JsonSerializer.Serialize(new { type = "mapLock", locked = false }, JsonOptions));
        UpdateMapLockButton();
        RefreshWorkflowReportStatus(stepNumber);
        OutputText.Text = $"Kaart ontgrendeld voor rapportage\n\nStap {stepNumber} kan weer worden aangepast. Zet hem opnieuw vast zodra de kaart en analyse definitief zijn.";
    }

    // Kopieert een capture naar een vast, per-stap-EN-substap vergrendeld bestand zodat
    // het "vastgezette" rapportbeeld niet meebeweegt met latere live captures. Zonder de
    // context-key in de bestandsnaam delen alle substeps van dezelfde stap (bv. 4.1 en
    // 4.3) letterlijk hetzelfde bestand en overschrijft vastzetten in de ene substap het
    // vastgezette beeld van de andere.
    private string FreezeLockedMapImage(int stepNumber, string imagePath)
    {
        if (_selectedProject is null || string.IsNullOrWhiteSpace(imagePath) || !System.IO.File.Exists(imagePath))
        {
            return imagePath;
        }

        try
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Borevexa",
                "ReportLocks");
            Directory.CreateDirectory(dir);
            var contextSuffix = MapStateService.NormalizeContextKey(GetCurrentMapStateContextKey(stepNumber));
            var fileNameSuffix = string.IsNullOrWhiteSpace(contextSuffix) ? "" : $"-{contextSuffix}";
            var frozenPath = System.IO.Path.Combine(dir, $"project-{_selectedProject.Id}-step-{stepNumber}{fileNameSuffix}-kaart.png");
            System.IO.File.Copy(imagePath, frozenPath, true);
            return frozenPath;
        }
        catch (Exception exception)
        {
            AppendMapDiagnostic($"Vastzetten rapportkaartbeeld stap {stepNumber} mislukt: {exception.Message}");
            return imagePath;
        }
    }

    // Werkt de vergrendelde rapportkaart bij met een nieuw (bevroren) beeld, voor elke
    // kaartstap met een enkelvoudige lock. Multi-variant stappen (stap 3) zijn
    // uitgezonderd: die lock bestaat uit meerdere variantbeelden (BAG/foto/KLIC) en
    // wordt alleen via "Opslaan voor rapportage" vernieuwd — een enkel imagePath zou
    // die varianten overschrijven.
    private void SaveReportLockWithImage(int stepNumber, string imagePath)
    {
        if (_selectedProject is null || string.IsNullOrWhiteSpace(imagePath)) return;
        if (GetCurrentMapWorkspaceRuntime(stepNumber, null).Definition.HasMultiVariantReportCapture) return;

        imagePath = FreezeLockedMapImage(stepNumber, imagePath);

        var payload = JsonSerializer.Serialize(new
        {
            locked = true,
            lockedAt = DateTimeOffset.Now,
            stepNumber,
            imagePath,
            baseLayer = _selectedMapBaseLayer,
            overlays = _mapOverlayStates,
            klicThemes = _klicThemeStates,
            bgtSurfaces = _bgtSurfaceStates,
            projectLayerVisibility = _projectLayerStates,
            camera = _lastMapCamera,
            mapContextKey = GetCurrentMapStateContextKey(stepNumber),
            substepNumber = _selectedSubstep?.Number,
            substepDisplayNumber = _selectedSubstep?.DisplayNumber,
            substepTitle = _selectedSubstep?.Title,
            analysis = stepNumber == 4 ? BuildSurfaceAnalysisReportText() : ""
        }, JsonOptions);
        _reportPreview.SaveReportLockJson(_selectedProject.Id, stepNumber, payload, GetCurrentMapStateContextKey(stepNumber));
        RefreshWorkflowReportStatus(stepNumber);
    }

    private string? GetReportLockJson(int stepNumber)
    {
        if (_selectedProject is null) return null;

        var runtime = GetCurrentMapWorkspaceRuntime(stepNumber, GetActiveMapReportVariantKey(stepNumber));
        return _reportPreview.GetReportLockJson(_selectedProject.Id, stepNumber, runtime.ContextKey, !runtime.SuppressLegacyFallback);
    }

    private string GetLockedReportMapImagePath(int stepNumber, string? variantKey = null)
    {
        if (_selectedProject is null) return "";
        var json = GetReportLockJson(stepNumber);
        if (string.IsNullOrWhiteSpace(json)) return "";
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("locked", out var locked) || locked.ValueKind != JsonValueKind.True) return "";
            var variant = ReportPreviewService.NormalizeLiveMapVariantKey(variantKey);
            if (!string.IsNullOrWhiteSpace(variant))
            {
                var variantProperty = variant switch
                {
                    StepThreeCoverOsmReportMapVariant => "coverOsm",
                    StepThreeReportMapBagVariant => "pdokBag",
                    StepThreeReportMapPhotoVariant => "pdokFoto",
                    StepThreeKlicReportMapBagVariant => "klicPdokBag",
                    StepThreeKlicReportMapPhotoVariant => "klicPdokFoto",
                    _ => variant
                };
                if (root.TryGetProperty("imagePaths", out var imagePaths) &&
                    imagePaths.ValueKind == JsonValueKind.Object &&
                    imagePaths.TryGetProperty(variantProperty, out var variantImageElement))
                {
                    var variantImagePath = variantImageElement.GetString() ?? "";
                    return !string.IsNullOrWhiteSpace(variantImagePath) && System.IO.File.Exists(variantImagePath)
                        ? variantImagePath
                        : "";
                }

                return "";
            }

            var imagePath = root.TryGetProperty("imagePath", out var imageElement) ? imageElement.GetString() ?? "" : "";
            return !string.IsNullOrWhiteSpace(imagePath) && System.IO.File.Exists(imagePath) ? imagePath : "";
        }
        catch
        {
            return "";
        }
    }

    private string GetFirstAvailableLockedReportMapImagePath(IReadOnlyList<int> stepNumbers)
    {
        foreach (var stepNumber in stepNumbers)
        {
            var path = GetLockedReportMapImagePath(stepNumber);
            if (!string.IsNullOrWhiteSpace(path)) return path;
        }

        return "";
    }

    private void QueueLiveMapReportCapture(int stepNumber)
    {
        var runtime = GetCurrentMapWorkspaceRuntime(stepNumber, GetActiveMapReportVariantKey(stepNumber));
        _reportMapCapture.QueueLiveMapCapture(stepNumber, StepThreeMapView.CoreWebView2, refreshPreview: true, runtime.ScopedReportVariantKey);
    }

    private async Task<string> CaptureLiveMapForReportPreviewAsync(int stepNumber, bool refreshPreview, bool force = false, string? variantKey = null)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var runtime = GetCurrentMapWorkspaceRuntime(stepNumber, variantKey);
            if (runtime.Definition.SendsTraceBeforeCapture)
            {
                SendTraceStateToMap(reportStepNumber: stepNumber);
            }

            if (runtime.Definition.SendsSurfaceAnalysisBeforeCapture)
            {
                SendMapMessage(JsonSerializer.Serialize(new { type = "surfaceAnalysisRequest" }, JsonOptions));
            }

            // De dieptepunten (S/2/3/E) moeten ook op de vastgezette kaartcapture van
            // stap 7 te zien zijn, niet alleen in de Dwarsprofiel-grafiek.
            if (stepNumber == ProfileStepNumber)
            {
                SendProfilePointLabelsToMap();
            }

            return await _reportMapCapture.CaptureLiveMapAsync(stepNumber, StepThreeMapView.CoreWebView2, refreshPreview, force, runtime.ScopedReportVariantKey);
        }
        finally
        {
            stopwatch.Stop();
            LogPerformanceTimingIfSlow($"Kaartcapture stap {stepNumber} {variantKey ?? "standaard"}", stopwatch.Elapsed, 200);
        }
    }

    // Guarantees the report shows the map 1-op-1 at the moment the user looks at it:
    // called right before opening a report preview or exporting, so the freshest
    // kaartbeeld is captured first (blank/mid-animation frames are rejected and then
    // the previous good capture simply stays in place).
    private async Task EnsureFreshMapReportCaptureAsync()
    {
        if (_selectedProject is null || _selectedStep is null) return;
        var stepNumber = _selectedStep.Number;
        if (!IsMapWorkspaceStep(stepNumber)) return;
        if (!_mapLibreLoaded || StepThreeMapView?.CoreWebView2 is null) return;
        if (StepThreeMapFrame.Visibility != Visibility.Visible) return;

        try
        {
            await CaptureLiveMapForReportPreviewAsync(stepNumber, refreshPreview: false, force: true);
        }
        catch (Exception exception)
        {
            AppendMapDiagnostic($"Verse rapportcapture voor preview/export mislukt: {exception.Message}");
        }
    }

    private string? GetActiveMapReportVariantKey(int stepNumber)
    {
        if (stepNumber != 6) return null;
        return GetSubsurfaceMapReportVariantKey(GetActiveUndergroundModelType());
    }

    private string? GetCurrentLiveMapVariantKey(int stepNumber, string? variantKey)
    {
        return GetCurrentMapWorkspaceRuntime(stepNumber, variantKey).ScopedReportVariantKey;
    }

    private object BuildLiveMapCaptureMetadata(int stepNumber)
    {
        var runtime = GetCurrentMapWorkspaceRuntime(stepNumber, GetActiveMapReportVariantKey(stepNumber));
        return new
        {
            baseLayer = _selectedMapBaseLayer,
            overlays = _mapOverlayStates,
            klicThemes = _klicThemeStates,
            bgtSurfaces = _bgtSurfaceStates,
            projectLayerVisibility = _projectLayerStates,
            camera = _lastMapCamera,
            mapScale = _workDrawingScale,
            workspace = new
            {
                runtime.Definition.StepNumber,
                purpose = runtime.Definition.Purpose.ToString(),
                runtime.ContextKey,
                runtime.NormalizedContextKey,
                runtime.ActiveReportVariantKey,
                runtime.ScopedReportVariantKey,
                runtime.HasScopedContext
            },
            substepNumber = _selectedSubstep?.Number,
            substepDisplayNumber = _selectedSubstep?.DisplayNumber,
            substepTitle = _selectedSubstep?.Title
        };
    }

    private void OnLiveMapCaptureCompleted(int stepNumber, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            SaveStepReportDataForStep(stepNumber);
            RefreshWorkflowReportStatus(stepNumber);

            if (_selectedStep?.Number == stepNumber &&
                _selectedReportPreviewStepNumber is null)
            {
                RefreshInlineReportPreviewIfVisible();
            }

            // Keep an open Rapportpreview window in sync with the freshly captured
            // map image; otherwise it keeps showing the previous kaartbeeld.
            if (IsReportPreviewWindowOpen())
            {
                RefreshReportPreviewWindow();
            }
        });
    }

    private async System.Threading.Tasks.Task<(string PrimaryPath, string CoverOsmMapPath, string BagMapPath, string PhotoMapPath, string KlicBagMapPath, string KlicPhotoMapPath)> CaptureStepThreeLiveMapForReportWithRetryAsync(int stepNumber)
    {
        var originalBaseLayer = _selectedMapBaseLayer;
        var originalOverlays = new Dictionary<string, bool>(_mapOverlayStates, StringComparer.OrdinalIgnoreCase);

        try
        {
            var coverOsmMapPath = await CaptureStepThreeLiveMapVariantWithRetryAsync(
                stepNumber,
                StepThreeCoverOsmReportMapVariant,
                "osm",
                new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                {
                    ["baseMap"] = true,
                    ["parcels"] = false,
                    ["buildings"] = false,
                    ["addresses"] = false,
                    ["boreTrace"] = true,
                    ["boreTraceNumbers"] = true,
                    ["boreTraceLengths"] = false,
                    ["boreTraceInfo"] = true
                });
            var bagMapPath = await CaptureStepThreeLiveMapVariantWithRetryAsync(
                stepNumber,
                StepThreeReportMapBagVariant,
                "pdok-brt",
                new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                {
                    ["baseMap"] = true,
                    ["parcels"] = true,
                    ["buildings"] = true,
                    ["addresses"] = false,
                    ["boreTrace"] = true,
                    ["boreTraceNumbers"] = true,
                    ["boreTraceLengths"] = true,
                    ["boreTraceInfo"] = true
                });
            var photoMapPath = await CaptureStepThreeLiveMapVariantWithRetryAsync(
                stepNumber,
                StepThreeReportMapPhotoVariant,
                "pdok-aerial",
                new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                {
                    ["baseMap"] = true,
                    ["parcels"] = false,
                    ["buildings"] = false,
                    ["addresses"] = false,
                    ["boreTrace"] = true,
                    ["boreTraceNumbers"] = true,
                    ["boreTraceLengths"] = true,
                    ["boreTraceInfo"] = true
                });
            var klicBagMapPath = await CaptureStepThreeLiveMapVariantWithRetryAsync(
                stepNumber,
                StepThreeKlicReportMapBagVariant,
                "pdok-brt",
                new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                {
                    ["baseMap"] = true,
                    ["parcels"] = true,
                    ["buildings"] = false,
                    ["addresses"] = false,
                    ["boreTrace"] = true,
                    ["boreTraceNumbers"] = false,
                    ["boreTraceLengths"] = false,
                    ["boreTraceInfo"] = false,
                    ["klic"] = true,
                    ["klicBuffer"] = true
                });
            var klicPhotoMapPath = await CaptureStepThreeLiveMapVariantWithRetryAsync(
                stepNumber,
                StepThreeKlicReportMapPhotoVariant,
                "pdok-aerial",
                new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                {
                    ["baseMap"] = true,
                    ["parcels"] = false,
                    ["buildings"] = false,
                    ["addresses"] = false,
                    ["boreTrace"] = true,
                    ["boreTraceNumbers"] = false,
                    ["boreTraceLengths"] = false,
                    ["boreTraceInfo"] = false,
                    ["klic"] = true,
                    ["klicBuffer"] = true
                });

            var primaryPath = !string.IsNullOrWhiteSpace(bagMapPath) ? bagMapPath : photoMapPath;
            if (!string.IsNullOrWhiteSpace(primaryPath))
            {
                return (primaryPath, coverOsmMapPath, bagMapPath, photoMapPath, klicBagMapPath, klicPhotoMapPath);
            }
        }
        finally
        {
            await RestoreStepThreeMapAfterReportVariantCaptureAsync(originalBaseLayer, originalOverlays);
        }

        var fallbackPath = await CaptureCurrentStepThreeMapWithRetryAsync(stepNumber, variantKey: null);
        return (fallbackPath, "", "", "", "", "");
    }

    private async System.Threading.Tasks.Task<string> CaptureStepThreeLiveMapVariantWithRetryAsync(
        int stepNumber,
        string variantKey,
        string baseLayer,
        IReadOnlyDictionary<string, bool> overlays)
    {
        var forceKlicMode = overlays.TryGetValue("klic", out var klicVisible) && klicVisible;
        var previousForceKlicMode = _forceStepThreeKlicMapForReportCapture;
        _forceStepThreeKlicMapForReportCapture = forceKlicMode;
        try
        {
            await ApplyStepThreeReportMapVariantAsync(baseLayer, overlays);
            var imagePath = await CaptureCurrentStepThreeMapWithRetryAsync(stepNumber, variantKey);
            if (!string.IsNullOrWhiteSpace(imagePath)) return imagePath;
        }
        finally
        {
            _forceStepThreeKlicMapForReportCapture = previousForceKlicMode;
        }

        AppendMapDiagnostic($"Live kaartcapture ({variantKey}) viel terug op zichtbare WebView-capture.");
        return await CaptureVisibleStepThreeMapForReportAsync(stepNumber, variantKey);
    }

    private async System.Threading.Tasks.Task<string> CaptureCurrentStepThreeMapWithRetryAsync(int stepNumber, string? variantKey)
    {
        var delays = new[] { 450, 750, 1100, 1500 };
        foreach (var delay in delays)
        {
            await System.Threading.Tasks.Task.Delay(delay);
            var imagePath = await CaptureLiveMapForReportPreviewAsync(stepNumber, refreshPreview: true, force: true, variantKey);
            if (!string.IsNullOrWhiteSpace(imagePath)) return imagePath;
        }

        AppendMapDiagnostic($"Live kaartcapture voor rapportpreview niet opgeslagen ({variantKey ?? "actueel"}): WebView gaf geen bruikbaar kaartbeeld. Schermcapture-fallback is uitgeschakeld om foutieve rapportbeelden te voorkomen.");
        return "";
    }

    private async System.Threading.Tasks.Task<string> CaptureStepSixSubsurfaceWmsReportMapAsync(string modelType)
    {
        modelType = NormalizeBroModelType(modelType);
        if (!IsBroWmsMapLayer(modelType)) return "";

        await ApplyStepSixSubsurfaceWmsReportMapVariantAsync(modelType);
        return await CaptureCurrentStepThreeMapWithRetryAsync(6, GetSubsurfaceMapReportVariantKey(modelType));
    }

    private async System.Threading.Tasks.Task ApplyStepSixSubsurfaceWmsReportMapVariantAsync(string modelType)
    {
        modelType = NormalizeBroModelType(modelType);
        var activeOverlay = BroWmsOverlayKey(modelType);
        if (string.IsNullOrWhiteSpace(activeOverlay)) return;

        _selectedBroModelType = modelType;
        foreach (var overlayKey in BroWmsOverlayKeys())
        {
            var visible = overlayKey.Equals(activeOverlay, StringComparison.OrdinalIgnoreCase);
            _mapOverlayStates[overlayKey] = visible;
            SendMapMessage(JsonSerializer.Serialize(new { type = "overlay", id = overlayKey, visible }, JsonOptions));
        }

        SendTraceStateToMap(reportStepNumber: 6);
        SendProjectLayersToMap();
        await System.Threading.Tasks.Task.Delay(2400);
    }

    private async System.Threading.Tasks.Task ApplyStepThreeReportMapVariantAsync(
        string baseLayer,
        IReadOnlyDictionary<string, bool> overlays)
    {
        SendMapMessage(JsonSerializer.Serialize(new { type = "base", id = baseLayer }, JsonOptions));
        foreach (var overlay in overlays)
        {
            SendMapMessage(JsonSerializer.Serialize(new { type = "overlay", id = overlay.Key, visible = overlay.Value }, JsonOptions));
        }

        if (!_suppressProjectLayerSend)
        {
            SendProjectLayersToMap();
        }
        SendTraceStateToMap();
        if (overlays.TryGetValue("klic", out var klicVisible) && klicVisible)
        {
            SendKlicCrossingLabelsToMap();
        }
        else
        {
            ClearKlicCrossingLabelsFromMap();
        }
        await System.Threading.Tasks.Task.Delay(1800);
    }

    private async System.Threading.Tasks.Task RestoreStepThreeMapAfterReportVariantCaptureAsync(
        string baseLayer,
        IReadOnlyDictionary<string, bool> overlays)
    {
        _selectedMapBaseLayer = baseLayer;
        foreach (var overlay in overlays)
        {
            _mapOverlayStates[overlay.Key] = overlay.Value;
        }

        SendMapMessage(JsonSerializer.Serialize(new { type = "base", id = baseLayer }, JsonOptions));
        foreach (var overlay in overlays)
        {
            SendMapMessage(JsonSerializer.Serialize(new { type = "overlay", id = overlay.Key, visible = overlay.Value }, JsonOptions));
        }

        if (!_suppressProjectLayerSend)
        {
            SendProjectLayersToMap();
        }
        SendTraceStateToMap();
        ClearKlicCrossingLabelsFromMap();
        await System.Threading.Tasks.Task.Delay(350);
    }

    private async System.Threading.Tasks.Task<string> CaptureVisibleStepThreeMapForReportAsync(int stepNumber, string? variantKey = null)
    {
        if (_selectedProject is null || stepNumber != 3) return "";
        if (StepThreeMapView.ActualWidth < 320 || StepThreeMapView.ActualHeight < 220) return "";

        try
        {
            if (StepThreeMapView.CoreWebView2 is not null)
            {
                await StepThreeMapView.CoreWebView2.ExecuteScriptAsync("window.borevexaMap && window.borevexaMap.handleMessage({ type: 'reportCaptureMode', enabled: true })");
                await System.Threading.Tasks.Task.Delay(260);
            }

            StepThreeMapView.UpdateLayout();
            var point = StepThreeMapView.PointToScreen(new Point(0, 0));
            var width = Math.Max(1, (int)Math.Round(StepThreeMapView.ActualWidth));
            var height = Math.Max(1, (int)Math.Round(StepThreeMapView.ActualHeight));
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Borevexa",
                "ReportLiveMaps");
            Directory.CreateDirectory(dir);

            var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            var variant = ReportPreviewService.NormalizeLiveMapVariantKey(variantKey);
            var variantSegment = string.IsNullOrWhiteSpace(variant) ? "" : $"-{variant}";
            var stableSuffix = string.IsNullOrWhiteSpace(variant) ? "current" : variant;
            var path = System.IO.Path.Combine(dir, $"project-{_selectedProject.Id}-step-{stepNumber}-visible-map{variantSegment}-{stamp}.png");
            var stablePath = System.IO.Path.Combine(dir, $"project-{_selectedProject.Id}-step-{stepNumber}-report-{stableSuffix}.png");
            using (var bitmap = new System.Drawing.Bitmap(width, height))
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(
                    (int)Math.Round(point.X),
                    (int)Math.Round(point.Y),
                    0,
                    0,
                    new System.Drawing.Size(width, height));
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }

            var validation = ReportPreviewService.ValidateMapCapture(path);
            if (!validation.IsUsable)
            {
                try { File.Delete(path); } catch (System.Exception swallowedException)
        {
            AppLog.Swallowed(swallowedException);
        }
                AppendMapDiagnostic($"Zichtbare kaartcapture voor rapportpreview afgekeurd: {validation.Reason}");
                return "";
            }

            File.Copy(path, stablePath, true);
            SaveSelectedProjectStepData(stepNumber, ReportPreviewService.BuildLiveMapPreviewDataKey(variant), JsonSerializer.Serialize(new
            {
                capturedAt = DateTimeOffset.Now,
                stepNumber,
                variant,
                imagePath = stablePath,
                capturedImagePath = path,
                capture = new
                {
                    source = "visible-webview",
                    validation.Width,
                    validation.Height,
                    validation.SampleCount,
                    validation.SampledColors,
                    validation.DominantColorRatio,
                    validation.Reason
                },
                metadata = BuildLiveMapCaptureMetadata(stepNumber)
            }, JsonOptions));

            AppendMapDiagnostic($"Zichtbare kaartcapture opgeslagen voor rapportpreview: {stablePath}");
            return stablePath;
        }
        catch (Exception exception)
        {
            AppendMapDiagnostic($"Zichtbare kaartcapture voor rapportpreview mislukt: {exception}");
            return "";
        }
        finally
        {
            try
            {
                if (StepThreeMapView.CoreWebView2 is not null)
                {
                    await StepThreeMapView.CoreWebView2.ExecuteScriptAsync("window.borevexaMap && window.borevexaMap.handleMessage({ type: 'reportCaptureMode', enabled: false })");
                }
            }
            catch (System.Exception swallowedException)
            {
                // Best-effort restore; capture failure must not leave the app unusable.
                AppLog.Swallowed(swallowedException);
            }
        }
    }

    // After the render surface is reclaimed, grab a fresh report capture so the
    // rapportage matches what is now on screen. The capture itself waits for the
    // basemap/BGT tiles to finish painting and rejects any blank frame, so we only
    // need to give the recovered map a moment before queuing it.
    private async void RefreshReportCaptureForCurrentMapStep()
    {
        if (_selectedStep is null || !IsMapWorkspaceStep(_selectedStep.Number)) return;
        if (StepThreeMapFrame.Visibility != Visibility.Visible) return;

        var stepNumber = _selectedStep.Number;
        // Korte settle zodat het herstelde kaartbeeld (na "Ververs kaart") eerst
        // volledig gerenderd is; daarna het standaard capture-pad.
        await Task.Delay(650);
        if (_selectedStep?.Number == stepNumber && StepThreeMapFrame.Visibility == Visibility.Visible)
        {
            await CaptureLiveMapForReportPreviewAsync(stepNumber, refreshPreview: true, force: true);
        }
    }

    private string GetLiveMapReportPreviewImagePath(int stepNumber, string? variantKey = null)
    {
        if (_selectedProject is null) return "";
        var lockedVariantPath = GetLockedReportMapImagePath(stepNumber, variantKey);
        if (!string.IsNullOrWhiteSpace(lockedVariantPath)) return lockedVariantPath;

        var runtime = GetCurrentMapWorkspaceRuntime(stepNumber, variantKey);
        var path = _reportPreview.GetLiveMapReportPreviewImagePath(_selectedProject.Id, stepNumber, _ => "", runtime.ScopedReportVariantKey);
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) return path;

        if (runtime.SuppressLegacyFallback)
        {
            return "";
        }

        return FindNewestLiveMapCaptureForSelectedProject(stepNumber, variantKey);
    }

    private string FindNewestLiveMapCaptureForSelectedProject(int stepNumber, string? variantKey = null)
    {
        if (_selectedProject is null) return "";

        try
        {
            var directory = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Borevexa",
                "ReportLiveMaps");
            if (!Directory.Exists(directory)) return "";

            var variant = ReportPreviewService.NormalizeLiveMapVariantKey(variantKey);
            var prefix = $"project-{_selectedProject.Id}-step-{stepNumber}-";

            string FindNewest(string pattern) =>
                Directory.GetFiles(directory, pattern)
                    .Select(filePath => new FileInfo(filePath))
                    .Where(file => file.Exists && file.Length > 0)
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .Select(file => file.FullName)
                    .FirstOrDefault() ?? "";

            if (!string.IsNullOrWhiteSpace(variant))
            {
                var stableVariantPath = System.IO.Path.Combine(directory, $"{prefix}report-{variant}.png");
                if (File.Exists(stableVariantPath)) return stableVariantPath;

                var newestVariant = FindNewest($"{prefix}*{variant}*.png");
                if (!string.IsNullOrWhiteSpace(newestVariant)) return newestVariant;

                return "";
            }

            var stableCurrentPath = System.IO.Path.Combine(directory, $"{prefix}report-current.png");
            if (File.Exists(stableCurrentPath)) return stableCurrentPath;

            return FindNewest($"{prefix}*.png");
        }
        catch
        {
            return "";
        }
    }

    private void SaveMapStateForStep(int stepNumber, bool updateOutput)
    {
        if (_selectedProject is null) return;

        // Fase 1 kaartstaat-dieet: deze methode draait bij elke pan/zoom (debounced)
        // en bij elke stapwissel. Hij bevat daarom uitsluitend de staat die bij het
        // inladen ook echt wordt teruggelezen (camera, lagen, filters, schaal).
        // Voorheen gingen hier ook een verse GetProjectFiles-read, alle kaartlagen en
        // een 762KB documentIndex in mee: ~966KB synchroon naar SQLite per interactie,
        // terwijl niets daarvan ooit werd teruggelezen. Dat veroorzaakte de vastlopers.
        var runtime = GetCurrentMapWorkspaceRuntime(stepNumber, GetActiveMapReportVariantKey(stepNumber));
        var state = new
        {
            savedAt = DateTimeOffset.Now,
            stepNumber,
            mapContextKey = runtime.ContextKey,
            mapWorkspace = new
            {
                purpose = runtime.Definition.Purpose.ToString(),
                runtime.NormalizedContextKey,
                runtime.ActiveReportVariantKey,
                runtime.ScopedReportVariantKey,
                runtime.SuppressLegacyFallback
            },
            substepNumber = _selectedSubstep?.Number,
            substepDisplayNumber = _selectedSubstep?.DisplayNumber,
            substepTitle = _selectedSubstep?.Title,
            baseLayer = _selectedMapBaseLayer,
            overlays = _mapOverlayStates,
            klicThemes = _klicThemeStates,
            bgtSurfaces = _bgtSurfaceStates,
            projectLayerVisibility = _projectLayerStates,
            mapScale = _workDrawingScale,
            camera = _lastMapCamera
        };

        _mapState.SaveStepMapState(_selectedProject.Id, stepNumber, state, runtime.ContextKey);
        if (updateOutput)
        {
            OutputText.Text = $"Alles opgeslagen\n\nOnderlegger: {_selectedMapBaseLayer}\nOverlays: {_mapOverlayStates.Count(kv => kv.Value)} actief\nBestanden: {_projectFiles.Count}";
        }
    }

    // Draait bij laag-, filter- en KLIC-thema-toggles. Bewust GEEN rapportage-capture
    // hier: dat is kaartbediening, geen rapportmoment. Captures gebeuren bij preview
    // openen, PDF-export en "Opslaan voor rapportage" (zie EnsureFreshMapReportCaptureAsync).
    private void SaveCurrentMapStateAfterLayerChange()
    {
        if (_selectedProject is null || _selectedStep is null || !IsMapWorkspaceStep(_selectedStep.Number)) return;

        CancelPendingMapStatePersistence();
        SaveMapStateForStep(_selectedStep.Number, false);
        SaveStepReportDataForStep(_selectedStep.Number);
        RefreshInlineReportPreviewIfVisible();
    }

    // Na een bewuste kaartactie met animatie (uitlijnen, fitTrace): wacht tot de
    // animatie klaar is, sla de kaartstand op en vernieuw het rapportbeeld. Uniform
    // voor alle kaartstappen; alleen stappen met live capture-ondersteuning capturen,
    // en een bestaande (niet-stap-3) lock krijgt het verse beeld mee.
    private async Task RefreshMapStateAndReportAfterMapAnimationAsync(int stepNumber)
    {
        await Task.Delay(900);
        if (_selectedProject is null || _selectedStep?.Number != stepNumber) return;

        SaveMapStateForStep(stepNumber, false);
        SaveStepReportDataForStep(stepNumber);

        var runtime = GetCurrentMapWorkspaceRuntime(stepNumber, GetActiveMapReportVariantKey(stepNumber));
        if (runtime.Definition.SupportsLiveCapture)
        {
            var imagePath = await CaptureLiveMapForReportPreviewAsync(stepNumber, refreshPreview: true, force: true);
            if (!string.IsNullOrWhiteSpace(imagePath) &&
                !runtime.Definition.HasMultiVariantReportCapture &&
                IsMapReportLocked(stepNumber))
            {
                SaveReportLockWithImage(stepNumber, imagePath);
            }
        }
        RefreshInlineReportPreviewIfVisible();
    }

    private void CaptureLiveMapState(string message)
    {
        if (_selectedProject is null || _selectedStep is null) return;
        if (!IsMapWorkspaceStep(_selectedStep.Number) && _selectedStep.Number != WorkDrawingStepNumber) return;

        CaptureMapCamera(message);
        QueueMapStatePersistence(_selectedStep.Number);
    }

    private void QueueMapStatePersistence(int stepNumber)
    {
        _pendingMapStateStepNumber = stepNumber;
        _mapStatePersistenceTimer.Stop();
        _mapStatePersistenceTimer.Start();
    }

    private void MapStatePersistenceTimer_OnTick(object? sender, EventArgs e)
    {
        _mapStatePersistenceTimer.Stop();
        if (_pendingMapStateStepNumber is not int stepNumber)
        {
            return;
        }

        _pendingMapStateStepNumber = null;
        PersistMapStateAfterInteraction(stepNumber, refreshReportPreview: false);
    }

    private void PersistMapStateAfterInteraction(int stepNumber, bool refreshReportPreview)
    {
        if (_selectedProject is null || _selectedStep is null || _selectedStep.Number != stepNumber) return;
        if (!IsMapWorkspaceStep(stepNumber) && stepNumber != WorkDrawingStepNumber) return;

        SaveMapStateForStep(stepNumber, false);
        SaveStepReportDataForStep(stepNumber);

        // Bewust GEEN rapportage-capture per pan/zoom: een capture kost 3-13 seconden
        // (repaint-bursts, PNG-encode/validatie, previews verversen) en maakte de kaart
        // traag tijdens gewoon gebruik. Het rapport blijft toch 1-op-1: er wordt vers
        // gecaptured op de momenten die tellen — preview openen, PDF-export en
        // "Opslaan voor rapportage" (EnsureFreshMapReportCaptureAsync / SaveAndLock).

        if (refreshReportPreview)
        {
            RefreshInlineReportPreviewIfVisible();
        }
    }

    private void CancelPendingMapStatePersistence()
    {
        _mapStatePersistenceTimer.Stop();
        _pendingMapStateStepNumber = null;
    }

    private void CaptureMapCamera(string message)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (root.TryGetProperty("camera", out var camera) && camera.ValueKind == JsonValueKind.Object)
            {
                _lastMapCamera = camera.Clone();
            }
        }
        catch (System.Exception swallowedException)
        {
            // Camera capture is best-effort; map filters and files can still be saved.
            AppLog.Swallowed(swallowedException);
        }
    }

    private void SaveStepThreeState() => SaveMapStateForStep(_selectedStep?.Number ?? 3, true);

    private void ApplyStoredMapStateForCurrentStep(bool restoreCameraToMap = true)
    {
        if (_selectedProject is null || _selectedStep is null) return;

        var json = GetCurrentMapStateJson(_selectedStep.Number);
        if (string.IsNullOrWhiteSpace(json))
        {
            if (TryApplyReportLockMapStateForCurrentStep(restoreCamera: restoreCameraToMap))
            {
                return;
            }

            _lastMapCamera = null;
            ApplyDefaultMapStateForCurrentStep();
            if (restoreCameraToMap) SendDefaultCameraToMapForCurrentStep();
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            _selectedMapBaseLayer = MapStateService.ReadBaseLayer(root, _selectedMapBaseLayer);
            MapStateService.ApplyBooleanDictionary(root, "overlays", _mapOverlayStates);
            MapStateService.ApplyBooleanDictionary(root, "klicThemes", _klicThemeStates);
            MapStateService.ApplyBooleanDictionary(root, "bgtSurfaces", _bgtSurfaceStates);
            MapStateService.ApplyBooleanDictionary(root, "projectLayerVisibility", _projectLayerStates);

            _lastMapCamera = MapStateService.ReadCamera(root) ?? _lastMapCamera;
            if (_selectedStep.Number == 4 && !IsAhn4HeightSubstep())
            {
                _gisLayerState.NormalizeSurfaceAnalysisMapState();
                _mapBgtSurfaceSamples = [];
            }
            else if (_selectedStep.Number == ProfileStepNumber)
            {
                _gisLayerState.NormalizeProfileMapState();
            }

            // ApplyStoredMapStateForCurrentStep is called both on genuine step navigation
            // (where the live MapLibre camera needs to be moved to match the persisted
            // state) and from render-surface recovery after a tab switch/resize (where the
            // camera never actually left its position and restoring it here would just
            // snap the map back to the last *saved* checkpoint, discarding any live pan
            // that happened since). Only the former should pass restoreCameraToMap: true.
            if (restoreCameraToMap && _mapLibreLoaded && _lastMapCamera is JsonElement cameraToRestore)
            {
                SendMapMessage(JsonSerializer.Serialize(new { type = "restoreCamera", camera = cameraToRestore }, JsonOptions));
            }
        }
        catch
        {
            _lastMapCamera = null;
            ApplyDefaultMapStateForCurrentStep();
            if (restoreCameraToMap) SendDefaultCameraToMapForCurrentStep();
            // Invalid legacy map-state should not block opening the map.
        }
    }

    private bool TryApplyReportLockMapStateForCurrentStep(bool restoreCamera)
    {
        if (_selectedStep is null) return false;

        var json = GetReportLockJson(_selectedStep.Number);
        if (string.IsNullOrWhiteSpace(json)) return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("locked", out var locked) || locked.ValueKind != JsonValueKind.True)
            {
                return false;
            }

            _selectedMapBaseLayer = MapStateService.ReadBaseLayer(root, _selectedMapBaseLayer);
            MapStateService.ApplyBooleanDictionary(root, "overlays", _mapOverlayStates);
            MapStateService.ApplyBooleanDictionary(root, "klicThemes", _klicThemeStates);
            MapStateService.ApplyBooleanDictionary(root, "bgtSurfaces", _bgtSurfaceStates);
            MapStateService.ApplyBooleanDictionary(root, "projectLayerVisibility", _projectLayerStates);

            if (_selectedStep.Number == ProfileStepNumber)
            {
                _gisLayerState.NormalizeProfileMapState();
            }

            var camera = MapStateService.ReadCamera(root);
            if (camera is not JsonElement cameraElement) return false;

            _lastMapCamera = cameraElement.Clone();
            if (restoreCamera && _mapLibreLoaded)
            {
                SendMapMessage(JsonSerializer.Serialize(new { type = "restoreCamera", camera = _lastMapCamera }, JsonOptions));
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private void ApplyDefaultMapStateForCurrentStep()
    {
        if (_selectedStep?.Number == ProfileStepNumber)
        {
            _gisLayerState.ApplyProfileMapDefaults();
            return;
        }

        if (_selectedStep?.Number != 4) return;

        if (IsAhn4HeightSubstep())
        {
            _gisLayerState.ApplyAhn4HeightMapDefaults();
        }
        else
        {
            _gisLayerState.ApplySurfaceAnalysisMapDefaults();
        }

        _mapBgtSurfaceSamples = [];
    }

    private bool IsAhn4HeightSubstep() =>
        _selectedStep?.Number == 4 && string.Equals(_selectedSubstep?.Number, "4.3", StringComparison.OrdinalIgnoreCase);

    private void SendDefaultCameraToMapForCurrentStep()
    {
        if (!_mapLibreLoaded || _selectedStep is null) return;
        if (!IsMapWorkspaceStep(_selectedStep.Number) && _selectedStep.Number != WorkDrawingStepNumber) return;

        SendMapMessage("{\"type\":\"zoomToBoreline\"}");
    }

    private void SendStoredMapStateToMap()
    {
        if (_selectedProject is null || _selectedStep is null || !_mapLibreLoaded) return;

        var json = GetCurrentMapStateJson(_selectedStep.Number);
        if (string.IsNullOrWhiteSpace(json))
        {
            if (TryApplyReportLockMapStateForCurrentStep(restoreCamera: true))
            {
                return;
            }

            _lastMapCamera = null;
            SendDefaultCameraToMapForCurrentStep();
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var camera = MapStateService.ReadCamera(root);
            if (camera is JsonElement cameraElement)
            {
                _lastMapCamera = cameraElement.Clone();
                SendMapMessage(JsonSerializer.Serialize(new { type = "restoreCamera", camera = _lastMapCamera }, JsonOptions));
            }
            else
            {
                _lastMapCamera = null;
                SendDefaultCameraToMapForCurrentStep();
            }
        }
        catch
        {
            _lastMapCamera = null;
            SendDefaultCameraToMapForCurrentStep();
            // Ignore invalid legacy map state.
        }
    }

    private void SyncFullMapStateToMap()
    {
        if (!_mapLibreLoaded || StepThreeMapView.CoreWebView2 is null)
        {
            QueueMapSync();
            return;
        }

        _gisMap.ClearPendingSync();
        SendProjectLayersToMap();
        SendAllFilterStatesToMap();
        SendProfileModeToMap();
        SendMachineStateToMap();
        SendStoredMapStateToMap();
        SendMapMessage(JsonSerializer.Serialize(new { type = "mapLock", locked = _mapLocked }, JsonOptions));
    }

    // A plain resize does not reclaim a WebView2 render surface that was lost while the
    // map was collapsed by another tab; only a *genuine* size change does (which is why
    // navigating to another step recovers it). This forces a real one-frame size jiggle
    // of the map, then restores the layout and re-syncs the map content.
    private void RecoverMapRender(Action? afterRecovered = null)
    {
        if (_mapRecoveryRunning || !_mapLibreLoaded || StepThreeMapView?.CoreWebView2 is null) return;
        if (StepThreeMapFrame.Visibility != Visibility.Visible) return;
        _mapRecoveryRunning = true;

        var restoreHeight = StepThreePanel.Height;
        var restoreMax = StepThreePanel.MaxHeight;
        var hadExplicitHeight = !double.IsNaN(restoreHeight) && restoreHeight > 160;
        var baseHeight = hadExplicitHeight ? restoreHeight : StepThreePanel.ActualHeight;

        void RestorePanel()
        {
            StepThreePanel.Height = hadExplicitHeight ? restoreHeight : double.NaN;
            StepThreePanel.MaxHeight = restoreMax;
        }

        // A lost WebView2 render surface / dropped WebGL context is only reclaimed by a
        // *genuine* size change that the browser actually observes as two distinct
        // frames. Two back-to-back layout passes get coalesced, so we shrink now, wait a
        // real interval on a timer, then restore — forcing the browser to re-present and
        // restore the GL context, after which the map repaints.
        if (double.IsNaN(baseHeight) || baseHeight < 200)
        {
            // Cannot jiggle a size we don't control; fall back to a repaint burst.
            SendProjectLayersToMap();
            SendTraceStateToMap();
            ApplyStoredMapStateForCurrentStep(restoreCameraToMap: false);
            SendMapMessage("{\"type\":\"recover\"}");
            _mapRecoveryRunning = false;
            afterRecovered?.Invoke();
            return;
        }

        StepThreePanel.Height = baseHeight - 40;
        StepThreePanel.MaxHeight = baseHeight - 40;
        SendMapMessage("{\"type\":\"resize\"}");

        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(220)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_mapLibreLoaded && StepThreeMapView?.CoreWebView2 is not null && StepThreeMapFrame.Visibility == Visibility.Visible)
            {
                RestorePanel();
                SendProjectLayersToMap();
                SendTraceStateToMap();
                ApplyStoredMapStateForCurrentStep();
                SendMapMessage("{\"type\":\"recover\"}");
            }
            else
            {
                RestorePanel();
            }
            _mapRecoveryRunning = false;
            afterRecovered?.Invoke();
        };
        timer.Start();
    }

    // WebView2 can render blank/white after its host element has been collapsed and
    // shown again (which happens when switching between the GIS-kaart and the
    // Oppervlakteprofiel/Dwarsprofiel tabs). Nudging MapLibre with a resize once the
    // element is laid out again reclaims the render surface.
    private void RefreshMapRender()
    {
        if (!_mapLibreLoaded || StepThreeMapView?.CoreWebView2 is null) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_mapLibreLoaded &&
                StepThreeMapView?.CoreWebView2 is not null &&
                StepThreeMapFrame.Visibility == Visibility.Visible)
            {
                SendMapMessage("{\"type\":\"resize\"}");
            }
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // Flicker-free repaint nudge: tells MapLibre to re-present once the map element is
    // visible again. Deferred to DispatcherPriority.Loaded so the map container already
    // has its real pixel size, then sent twice (immediately and after a short delay) to
    // catch a WebGL context that restores a beat after the canvas becomes visible.
    private void NudgeMapRenderIfVisible()
    {
        if (!_mapLibreLoaded || StepThreeMapView?.CoreWebView2 is null) return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_mapLibreLoaded ||
                StepThreeMapView?.CoreWebView2 is null ||
                StepThreeMapFrame.Visibility != Visibility.Visible)
            {
                return;
            }

            SendMapMessage("{\"type\":\"recover\"}");
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void MapRefresh_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_mapLibreLoaded || StepThreeMapView?.CoreWebView2 is null)
        {
            _mapInitializationStarted = false;
            _ = EnsureMapLibreLoadedAsync();
            if (OutputText is not null) OutputText.Text = "Kaart wordt geladen...";
            return;
        }

        RecoverMapRender(afterRecovered: RefreshReportCaptureForCurrentMapStep);
        if (OutputText is not null) OutputText.Text = "Kaart ververst.";
    }
}
