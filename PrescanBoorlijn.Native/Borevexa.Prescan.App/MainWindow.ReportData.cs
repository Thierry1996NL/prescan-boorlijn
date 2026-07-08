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
using Borevexa.Prescan.App.Models;
using Borevexa.Prescan.App.Reports.Blocks;
using Borevexa.Prescan.App.Services;
using Borevexa.Prescan.Cad;
using Borevexa.Prescan.Core.Models;
using Borevexa.Prescan.Core.Services;
using Borevexa.Prescan.Geo;
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

namespace Borevexa.Prescan.App;

// Rapportdata: opbouw en opslag van stap-rapportdata, payloads en snapshots.
// Fase 3-opsplitsing (07-07-2026) van MainWindow.xaml.cs; gedrag ongewijzigd.

public partial class MainWindow
{
    private void AppendStepReportSection(StringBuilder builder, int stepNumber)
    {
        // Oude vrije rapporttekst wordt niet meer aan het eindrapport toegevoegd.
    }

    private string BuildAllStepReportRefreshSignature()
    {
        if (_selectedProject is null) return "geen-project";

        return string.Join(";",
            _selectedProject.Id.ToString("N"),
            _reportUiDataVersion.ToString(CultureInfo.InvariantCulture),
            _projectFiles.Count.ToString(CultureInfo.InvariantCulture),
            _selectedBroModelType,
            _selectedMachineId ?? "",
            _profilePoints.Count.ToString(CultureInfo.InvariantCulture),
            _boringItems.Count.ToString(CultureInfo.InvariantCulture));
    }

    private object BuildEnvironmentReportDataPayload()
    {
        var analysis = BuildParcelOwnerAnalysis();
        var segments = analysis.Segments.Select(segment =>
        {
            var risk = AssessParcelRisk(segment);
            return new
            {
                segment.Start,
                segment.End,
                segment.Length,
                segment.CadastralMunicipality,
                segment.Section,
                segment.ParcelNumber,
                segment.CadastralObjectId,
                segment.BgtHolderCode,
                segment.BgtHolderCategory,
                segment.BgtHolderName,
                segment.ZroStatus,
                risk.Level,
                risk.Reason,
                action = SuggestedAction(segment),
                tracePath = segment.TracePath.Select(point => new { point.X, point.Y }).ToArray(),
                parcelRing = segment.Parcel?.Ring.Select(point => new { point.X, point.Y }).ToArray() ?? []
            };
        }).ToList();

        return new
        {
            traceLength = GetReportProfileTotal(analysis.TraceRows),
            segmentCount = analysis.Segments.Count,
            crossedParcelCount = CountCrossedParcels(analysis.Segments),
            segments
        };
    }

    private object BuildImportReportDataPayload(IReadOnlyList<TracePointRow> traceRows, IReadOnlyList<ProjectMapLayer> layers, IReadOnlyList<ProjectDocumentEntry> docs)
    {
        var crossings = BuildKlicPlanCrossings(traceRows, layers);
        return new
        {
            files = _projectFiles.Select(file => new
            {
                file.FileType,
                file.DisplayName,
                file.LocalPath,
                file.SizeBytes
            }),
            documents = docs.Select(doc => new
            {
                doc.Type,
                doc.Name,
                doc.LocalPath,
                doc.SizeKb
            }),
            layers = layers.Select(layer => new
            {
                layer.Id,
                layer.Type,
                layer.Name,
                layer.Color,
                geometryCount = layer.FeatureCollection.Features.Count
            }),
            klicCrossings = crossings.Select(crossing => new
            {
                crossing.Code,
                crossing.Distance,
                crossing.X,
                crossing.Y,
                crossing.ThemeLabel,
                crossing.Theme,
                crossing.Color,
                crossing.NetworkContent,
                crossing.NetworkOperator,
                crossing.NetworkContact,
                crossing.DataSummary,
                crossing.CrossingContent
            }),
            klicContactPdfAvailable = docs.Any(doc => doc.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        };
    }

    private string BuildReportDataStatusText()
    {
        if (_selectedProject is null) return "-";
        var qualityText = BuildReportQualityStatusText();
        var snapshotJson = _projects.GetStepData(_selectedProject.Id, ReportStepNumber, ReportSnapshotDataKey);
        if (string.IsNullOrWhiteSpace(snapshotJson)) return $"Nog geen rapport-snapshot · {qualityText}";
        try
        {
            using var document = JsonDocument.Parse(snapshotJson);
            var root = document.RootElement;
            var files = root.TryGetProperty("projectFiles", out var filesElement) ? filesElement.GetInt32() : 0;
            var maps = root.TryGetProperty("lockedMaps", out var mapsElement) ? mapsElement.GetInt32() : 0;
            var segments = root.TryGetProperty("parcelSegments", out var parcelElement) ? parcelElement.GetInt32() : 0;
            var stepData = root.TryGetProperty("stepReportData", out var stepDataElement) ? stepDataElement.GetInt32() : 0;
            var substeps = root.TryGetProperty("stepReportSubsteps", out var substepElement) ? substepElement.GetInt32() : 0;
            var version = root.TryGetProperty("reportContractVersion", out var versionElement) && versionElement.TryGetInt32(out var versionNumber)
                ? $"v{versionNumber}"
                : "v?";
            return $"Snapshot {version} actueel · {stepData} staprapport(en), {substeps} substap(pen), {files} bestand(en), {maps} kaart(en), {segments} perceelsegment(en) · {qualityText}";
        }
        catch
        {
            return $"Snapshot ongeldig · opnieuw opslaan · {qualityText}";
        }
    }

    private object? BuildStepReportDataPayload(int stepNumber, bool refreshProjectFiles = true)
    {
        if (_selectedProject is null) return null;

        if (refreshProjectFiles)
        {
            _projectFiles = _projects.GetProjectFiles(_selectedProject.Id);
        }

        var traceRows = GetTraceRowsForProfile();
        var traceDistances = traceRows.Count >= 2 ? BuildTraceDistances(traceRows) : [];
        var traceLength = traceRows.Count >= 2
            ? traceDistances[^1]
            : Math.Max(1, _selectedProject.BoreLengthMeters);
        var reportLocation = BuildReportProjectLocation(BuildTraceLocationContext(traceRows));
        var reportBoreLength = GetReportBoreLengthMeters(traceRows, traceLength);
        var metadata = ReadProjectHeaderMetadata();
        List<ProjectMapLayer>? cachedLayers = null;
        List<ProjectDocumentEntry>? cachedDocs = null;
        IReadOnlyList<ProjectMapLayer> GetReportLayers() => cachedLayers ??= BuildProjectMapLayers(_projectFiles);
        IReadOnlyList<ProjectDocumentEntry> GetReportDocs() => cachedDocs ??= BuildProjectDocumentEntries(_projectFiles).ToList();
        var title = _workspaces.TryGetValue(stepNumber, out var workspace) ? workspace.Title : $"Stap {stepNumber}";

        object content = stepNumber switch
        {
            0 => new
            {
                cover = true,
                preface = true,
                contents = true,
                project = _selectedProject.Name,
                location = reportLocation
            },
            1 => new
            {
                project = new
                {
                    _selectedProject.Name,
                    Datum = metadata.ReportDate,
                    ProjectnummerIntern = metadata.InternalProjectNumber,
                    ProjectnummerExtern = metadata.ExternalProjectNumber,
                    _selectedProject.Client,
                    Location = reportLocation,
                    _selectedProject.Status,
                    BoreLengthMeters = reportBoreLength,
                    _selectedProject.DiameterMillimeters,
                    _selectedProject.Material
                },
                boring = BuildReportBoringSummary(),
                selectedMachineId = _selectedMachineId,
                drillingTechnique = _selectedDrillingTechnique
            },
            2 => BuildImportReportDataPayload(traceRows, GetReportLayers(), GetReportDocs()),
            3 => new
            {
                traceLength,
                pointCount = traceRows.Count,
                points = traceRows.Select(row => new
                {
                    row.Index,
                    row.Role,
                    row.X,
                    row.Y,
                    distance = row.Index > 0 && row.Index <= traceDistances.Count ? traceDistances[row.Index - 1] : 0
                }),
                mapLocked = IsMapReportLocked(stepNumber),
                mapStateAvailable = !string.IsNullOrWhiteSpace(GetCurrentMapStateJson(stepNumber))
            },
            4 => BuildSurfaceReportDataPayload(traceLength),
            5 => BuildEnvironmentReportDataPayload(),
            6 => BuildUndergroundReportDataPayload(traceRows, traceLength, GetActiveUndergroundModelType()),
            7 => BuildProfileReportDataPayload(traceLength),
            8 => new
            {
                selectedMachineId = _selectedMachineId,
                selectedMachine = Machines.FirstOrDefault(machine => machine.Id == _selectedMachineId),
                placements = ReadMachinePlacementRows(),
                technique = _selectedDrillingTechnique,
                dimensions = new { _machineLengthMeters, _machineWidthMeters, _borePitLengthMeters, _borePitWidthMeters }
            },
            9 => new
            {
                status = "Sonderingen nog niet vastgelegd",
                note = "Leg sonderingen vast zodra deze bron beschikbaar is; daarna worden ze als rapportdata opgenomen."
            },
            var n when n == ReportStepNumber => new
            {
                status = "Rapportgenerator",
                snapshotAvailable = !string.IsNullOrWhiteSpace(_projects.GetStepData(_selectedProject.Id, ReportStepNumber, ReportSnapshotDataKey))
            },
            var n when n == ThreeDStepNumber => new { includedInReport = false, reason = "3D export is aparte output." },
            var n when n == WorkDrawingStepNumber => new { includedInReport = false, reason = "Werktekening is aparte output." },
            _ => new { status = GetStepCompletenessText(stepNumber) }
        };
        var substeps = BuildStepReportSubsteps(stepNumber, content, traceRows, traceLength, GetReportLayers, GetReportDocs);

        return new
        {
            schema = "borevexa.step-report-data.v3",
            generatedAt = DateTimeOffset.Now,
            projectId = _selectedProject.Id,
            stepNumber,
            displayNumber = DisplayStepNumber(stepNumber),
            title,
            status = GetStepCompletenessText(stepNumber),
            sourceKeys = BuildStepReportSourceKeys(stepNumber),
            substeps
        };
    }

    private static string[] BuildStepReportSourceKeys(int stepNumber) =>
        ReportContractCatalog.GetStepSourceKeys(stepNumber).ToArray();

    private object[] BuildStepReportSubsteps(
        int stepNumber,
        object content,
        IReadOnlyList<TracePointRow> traceRows,
        double traceLength,
        Func<IReadOnlyList<ProjectMapLayer>> getLayers,
        Func<IReadOnlyList<ProjectDocumentEntry>> getDocs)
    {
        if (_selectedProject is null) return [];

        var definitions = StepReportCatalog.GetSubsteps(stepNumber);
        var dataDefinitions = definitions.Where(definition => !definition.IsChapterIntroduction).ToList();
        PrescanSubstep Def(int index)
        {
            var source = dataDefinitions.Count > 0 ? dataDefinitions : definitions;
            return source[Math.Min(index, source.Count - 1)];
        }

        object Sub(int index, string status, object data, params string[] sourceKeys) =>
            CreateStepReportSubstepPayload(Def(index), status, data, sourceKeys);
        var surfaceSegmentsForReport = stepNumber == 4
            ? GetBgtSurfaceSegments(traceLength)
            : Array.Empty<BgtSurfaceSegment>();
        var surfaceMeasuredLengthForReport = surfaceSegmentsForReport.Sum(segment => Math.Max(0, segment.Length));
        var surfaceAnalysisGeneratedForReport = stepNumber == 4 &&
                                                !string.IsNullOrWhiteSpace(
                                                    _projects.GetStepData(_selectedProject.Id, 4, "surface_analysis_generated"));
        var ahnSurfaceProfileRowsForReport = stepNumber == 4
            ? GetAhnSurfaceProfileRows(traceLength)
            : Array.Empty<(double Distance, double Surface, double BoreNap)>();
        var reportStartSettingsForReport = stepNumber == 0
            ? ReadReportStartSettings()
            : ReportStartSettings.Default;
        var startParcelCountForReport = stepNumber == 0
            ? CountCrossedParcels(BuildParcelOwnerAnalysis().Segments)
            : 0;
        var klicCrossingsForReport = stepNumber is 2 or 3
            ? BuildKlicPlanCrossings(traceRows, getLayers())
            : Array.Empty<KlicPlanCrossing>();
        var klicEvZonesForReport = stepNumber == 3
            ? BuildKlicEvZones(traceRows, getLayers())
            : Array.Empty<KlicEvZone>();
        var parcelAnalysisForReport = stepNumber == 5
            ? BuildParcelOwnerAnalysis()
            : null;

        return stepNumber switch
        {
            0 =>
            [
                Sub(0, "Voorblad beschikbaar", new
                {
                    project = _selectedProject.Name,
                    _selectedProject.Client,
                    location = BuildReportProjectLocation(BuildTraceLocationContext(traceRows)),
                    _selectedProject.Status,
                    traceLength,
                    generatedAt = DateTimeOffset.Now,
                    coverTitle = reportStartSettingsForReport.CoverTitle,
                    coverSubtitle = reportStartSettingsForReport.CoverSubtitle,
                    coverRevision = reportStartSettingsForReport.CoverRevision,
                    coverNote = reportStartSettingsForReport.CoverNote
                }, "project_info"),
                Sub(1, "Voorwoord beschikbaar", new
                {
                    documentType = "Automatisch gegenereerde prescan",
                    generatedAt = DateTimeOffset.Now,
                    traceLength,
                    documentCount = getDocs().Count,
                    layerCount = getLayers().Count,
                    crossedParcelCount = startParcelCountForReport,
                    forewordText = reportStartSettingsForReport.ForewordText,
                    forewordScope = reportStartSettingsForReport.ForewordScope
                }, ReportSnapshotDataKey),
                Sub(2, "Inhoudsopgave beschikbaar", new
                {
                    contentsTitle = reportStartSettingsForReport.ContentsTitle,
                    contentsIntro = reportStartSettingsForReport.ContentsIntro,
                    includeAppendices = reportStartSettingsForReport.IncludeAppendices,
                    chapters = BuildReportContentsEntries(startParcelCountForReport, reportStartSettingsForReport.IncludeAppendices)
                        .Select(entry => new { entry.Page, entry.Title, entry.Description })
                }, ReportSnapshotDataKey)
            ],
            1 =>
            [
                Sub(0, GetStepCompletenessText(1), new
                {
                    _selectedProject.Name,
                    _selectedProject.Client,
                    Location = BuildReportProjectLocation(BuildTraceLocationContext(traceRows)),
                    _selectedProject.Status,
                    BoreLengthMeters = GetReportBoreLengthMeters(traceRows, traceLength),
                    _selectedProject.DiameterMillimeters,
                    _selectedProject.Material
                }, "project_info"),
                Sub(1, _boringItems.Count == 0 ? "Geen losse inhoudsitems" : $"{_boringItems.Count} item(s)", new
                {
                    items = _boringItems.Select(item => new
                    {
                        item.Type,
                        item.Dn,
                        item.Label,
                        item.OutsideDiameter,
                        item.Color,
                        contents = item.Contents.Select(child => new { child.Label, child.OutsideDiameter, child.Color })
                    })
                }, "boring_config"),
                Sub(2, _boringItems.Count == 0 ? "Geen boringconfiguratie vastgelegd" : "Berekening beschikbaar", new
                {
                    boring = BuildReportBoringSummary(),
                    fillFactor = FillFactor,
                    boringFactor = BoringFactor,
                    selectedMachineId = _selectedMachineId
                }, "boring_config"),
                Sub(3, _boringItems.Count == 0
                    ? "Geen boringconfiguratie vastgelegd"
                    : string.IsNullOrWhiteSpace(_selectedMachineId) ? "Machine nog kiezen" : "Machine gekozen",
                    BuildMachineChoiceReportDataPayload(), "boring_config")
            ],
            2 =>
            [
                Sub(0, _projectFiles.Count == 0 ? "Geen bestanden" : $"{_projectFiles.Count} bestand(en)", new
                {
                    files = _projectFiles.Select(file => new { file.FileType, file.DisplayName, file.LocalPath, file.SizeBytes })
                }, "project_files"),
                Sub(1, getDocs().Count == 0 ? "Geen documenten" : $"{getDocs().Count} document(en)", new
                {
                    documents = getDocs().Select(doc => new { doc.Type, doc.Name, doc.LocalPath, doc.SizeKb })
                }, "project_files"),
                Sub(2, $"{getLayers().Count} laag/lagen", new
                {
                    layers = getLayers().Select(layer => new { layer.Id, layer.Type, layer.Name, layer.Color, geometryCount = layer.FeatureCollection.Features.Count }),
                    klicCrossings = klicCrossingsForReport
                }, "project_files", "map_state")
            ],
            3 =>
            [
                Sub(0, traceRows.Count >= 2 ? "Boorlijn beschikbaar" : "Boorlijn ontbreekt", new
                {
                    traceLength,
                    pointCount = traceRows.Count,
                    points = traceRows.Select(row => new { row.Index, row.Role, row.X, row.Y }),
                    baseLayer = _selectedMapBaseLayer,
                    mapLocked = IsMapReportLocked(stepNumber),
                    mapStateAvailable = !string.IsNullOrWhiteSpace(GetCurrentMapStateJson(stepNumber))
                }, "boortrace_geojson", "report_lock", "map_state"),
                Sub(1, klicCrossingsForReport.Count == 0 ? "Geen KLIC kruisingen" : $"{klicCrossingsForReport.Count} kruising(en)", new
                {
                    crossings = klicCrossingsForReport,
                    klicThemes = _klicThemeStates,
                    klicLayerCount = getLayers().Count(IsKlicLayer),
                    bufferEnabled = _mapOverlayStates.TryGetValue("klicBuffer", out var klicBufferVisible) && klicBufferVisible,
                    klicVisible = _mapOverlayStates.TryGetValue("klic", out var klicVisible) && klicVisible
                }, "project_files", "map_state"),
                Sub(2, klicEvZonesForReport.Count == 0 ? "Geen EV-zones nabij boorlijn" : $"{klicEvZonesForReport.Count} EV-zone(s)", new
                {
                    analysisAvailable = traceRows.Count >= 2,
                    traceLength,
                    evZones = klicEvZonesForReport,
                    zones = klicEvZonesForReport,
                    searchBufferMeters = KlicEvZoneSearchBufferMeters,
                    klicLayerCount = getLayers().Count(IsKlicLayer),
                    klicVisible = _mapOverlayStates.TryGetValue("klic", out var klicEvVisible) && klicEvVisible,
                    explanation = "Een EV-zone is een gebied of aanduiding uit de KLIC-levering waarvoor eisvoorzorgsmaatregelen gelden. Bij werkzaamheden nabij zo'n zone moeten de voorwaarden uit de KLIC-informatie en de aanwijzingen van de netbeheerder worden opgevolgd voordat de uitvoering start."
                }, "klic_ev_zones", "project_files", "map_state")
            ],
            4 =>
            [
                Sub(0, surfaceSegmentsForReport.Count == 0 ? "Geen segmenten" : $"{surfaceSegmentsForReport.Count} segment(en)", new
                {
                    traceLength,
                    measuredLength = surfaceMeasuredLengthForReport,
                    generated = surfaceAnalysisGeneratedForReport,
                    mapLocked = IsMapReportLocked(4),
                    segments = surfaceSegmentsForReport
                }, "surface_analysis_generated", "report_lock"),
                Sub(1, surfaceSegmentsForReport.Count == 0 ? "Geen oppervlakteprofiel" : $"{surfaceSegmentsForReport.Count} segment(en) in oppervlakteprofiel", new
                {
                    traceLength,
                    measuredLength = surfaceMeasuredLengthForReport,
                    segments = surfaceSegmentsForReport,
                    filters = _bgtSurfaceStates,
                    generated = surfaceAnalysisGeneratedForReport,
                    mapLocked = IsMapReportLocked(4)
                }, "surface_analysis_generated", "report_lock"),
                Sub(2, ahnSurfaceProfileRowsForReport.Count < 2 ? "Geen AHN4 maaiveldprofiel" : $"{ahnSurfaceProfileRowsForReport.Count} maaiveldpunt(en)", new
                {
                    traceLength,
                    profilePointCount = ahnSurfaceProfileRowsForReport.Count,
                    minSurface = ahnSurfaceProfileRowsForReport.Count == 0 ? (double?)null : ahnSurfaceProfileRowsForReport.Min(row => row.Surface),
                    maxSurface = ahnSurfaceProfileRowsForReport.Count == 0 ? (double?)null : ahnSurfaceProfileRowsForReport.Max(row => row.Surface),
                    mapLocked = IsMapReportLocked(4),
                    points = ahnSurfaceProfileRowsForReport.Select(row => new { row.Distance, row.Surface, row.BoreNap })
                }, "surface_analysis_generated", "boortrace_geojson", "report_lock")
            ],
            5 =>
            [
                Sub(0, parcelAnalysisForReport is null || parcelAnalysisForReport.Segments.Count == 0 ? "Geen percelen" : $"{parcelAnalysisForReport.Segments.Count} segment(en)", content, "environment_analysis"),
                Sub(1, "ZRO en omgevingsinformatie beschikbaar", content, "project_files", "environment_analysis")
            ],
            6 =>
            [
                Sub(0, GetBroSoundings(BroDgmModelType).Count == 0 ? "BRO DGM nog niet geladen" : $"{GetBroSoundings(BroDgmModelType).Count} DGM-datasetpunt(en)",
                    BuildUndergroundReportDataPayload(traceRows, traceLength, BroDgmModelType),
                    "map_state", "bro_dgm", "boortrace_geojson"),
                Sub(1, GetBroSoundings(BroRegisModelType).Count == 0 ? "REGIS II nog niet geladen" : $"{GetBroSoundings(BroRegisModelType).Count} REGIS-datasetpunt(en)",
                    BuildUndergroundReportDataPayload(traceRows, traceLength, BroRegisModelType),
                    "map_state", "bro_regis", "boortrace_geojson"),
                Sub(2, "BRO Geomorfologie 2025-01 kaartlaag",
                    BuildUndergroundReportDataPayload(traceRows, traceLength, BroGeomorphologyModelType),
                    "map_state", "bro_geomorfologie", "boortrace_geojson"),
                Sub(3, "BRO Bodemkaart 2025-01 kaartlaag",
                    BuildUndergroundReportDataPayload(traceRows, traceLength, BroSoilMapModelType),
                    "map_state", "bro_bodemkaart", "boortrace_geojson"),
                Sub(4, "BRO Grondwaterspiegeldiepte GHG 2025-01 kaartlaag",
                    BuildUndergroundReportDataPayload(traceRows, traceLength, BroGroundwaterGhgModelType),
                    "map_state", "bro_grondwaterspiegel_ghg", "boortrace_geojson"),
                Sub(5, "BRO Grondwaterspiegeldiepte GLG 2025-01 kaartlaag",
                    BuildUndergroundReportDataPayload(traceRows, traceLength, BroGroundwaterGlgModelType),
                    "map_state", "bro_grondwaterspiegel_glg", "boortrace_geojson"),
                Sub(6, "BRO Grondwaterspiegeldiepte GVG 2025-01 kaartlaag",
                    BuildUndergroundReportDataPayload(traceRows, traceLength, BroGroundwaterGvgModelType),
                    "map_state", "bro_grondwaterspiegel_gvg", "boortrace_geojson"),
                Sub(7, "BRO Grondwatertrappen Gt 2025-01 kaartlaag",
                    BuildUndergroundReportDataPayload(traceRows, traceLength, BroGroundwaterGtModelType),
                    "map_state", "bro_grondwatertrappen", "boortrace_geojson"),
                Sub(8, "BRO Grondwaterspiegeldiepte modeldocumentatie 2025-01 kaartlaag",
                    BuildUndergroundReportDataPayload(traceRows, traceLength, BroGroundwaterDocumentationModelType),
                    "map_state", "bro_grondwaterspiegel_modeldocumentatie", "boortrace_geojson")
            ],
            7 =>
            [
                Sub(0, _profilePoints.Count >= 2 ? $"{_profilePoints.Count} profielpunt(en)" : "Profiel ontbreekt", new { points = _profilePoints }, "diepteprofiel_3d"),
            ],
            8 =>
            [
                Sub(0, string.IsNullOrWhiteSpace(_selectedMachineId) ? "Machine nog kiezen" : "Machine ingetekend", new
                {
                    selectedMachineId = _selectedMachineId,
                    selectedMachine = Machines.FirstOrDefault(machine => machine.Id == _selectedMachineId),
                    placements = ReadMachinePlacementRows(),
                    technique = _selectedDrillingTechnique,
                    dimensions = new { _machineLengthMeters, _machineWidthMeters, _borePitLengthMeters, _borePitWidthMeters }
                }, "machine_placements", "boring_config")
            ],
            9 =>
            [
                Sub(0, "Sonderingen nog niet vastgelegd", content, "sonderingen")
            ],
            var n when n == ReportStepNumber =>
            [
                Sub(0, BuildReportDataStatusText(), new { snapshot = TryReadReportSnapshot(out var snapshot) ? snapshot.Clone() : (JsonElement?)null }, ReportSnapshotDataKey),
                Sub(1, "Automatische conclusie", content, ReportSnapshotDataKey),
                Sub(2, BuildLastReportGeneratedText(), content, "eindrapport_export")
            ],
            _ => definitions.Select(definition => CreateStepReportSubstepPayload(definition, GetStepCompletenessText(stepNumber), content, BuildStepReportSourceKeys(stepNumber))).ToArray()
        };
    }

    private object BuildSurfaceReportDataPayload(double traceLength)
    {
        var segments = GetBgtSurfaceSegments(traceLength);
        return new
        {
            traceLength,
            generated = !string.IsNullOrWhiteSpace(_projects.GetStepData(_selectedProject!.Id, 4, "surface_analysis_generated") ??
                                                   _projects.GetStepData(_selectedProject.Id, 5, "surface_analysis_generated")),
            mapLocked = IsMapReportLocked(4),
            segments = segments.Select(segment => new
            {
                segment.Label,
                segment.Start,
                segment.End,
                segment.Length,
                segment.Color
            })
        };
    }

    private int CountSavedStepReportSubsteps(int stepNumber)
    {
        if (_selectedProject is null) return 0;
        var json = _projects.GetStepData(_selectedProject.Id, stepNumber, StepReportDataKey);
        if (string.IsNullOrWhiteSpace(json)) return 0;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("substeps", out var substeps) && substeps.ValueKind == JsonValueKind.Array
                ? substeps.GetArrayLength()
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private IEnumerable<UIElement> CreateStepReportPreviewPages(int stepNumber)
    {
        if (_selectedProject is null) yield break;

        SaveStepReportDataForStep(stepNumber);
        TryReadStepReportRoot(stepNumber, out var root);
        foreach (var substep in StepReportCatalog.GetSubsteps(stepNumber))
        {
            foreach (var page in CreateInlineSubstepReportPagesCore(stepNumber, substep, root, expandFinalConclusionPreview: false))
            {
                yield return page;
            }
        }
    }

    private static object CreateStepReportSubstepPayload(PrescanSubstep definition, string status, object data, IReadOnlyList<string> sourceKeys) => new
    {
        number = definition.Number,
        displayNumber = string.IsNullOrWhiteSpace(definition.DisplayNumber) ? definition.Number : definition.DisplayNumber,
        title = definition.Title,
        reportSectionTitle = definition.ReportSectionTitle,
        description = definition.Description,
        status,
        ready = !status.Contains("ontbreekt", StringComparison.OrdinalIgnoreCase) &&
                !status.Contains("nog", StringComparison.OrdinalIgnoreCase) &&
                !status.Contains("geen", StringComparison.OrdinalIgnoreCase),
        sourceKeys,
        data
    };

    private static string HumanizeReportDataKey(string key) => key switch
    {
        "traceLength" => "tracelengte",
        "pointCount" => "punten",
        "points" => "punten",
        "files" => "bestanden",
        "documents" => "documenten",
        "layers" => "filters",
        "klicCrossings" or "crossings" => "KLIC-kruisingen",
        "segments" => "segmenten",
        "selectedSoundings" => "geselecteerde bronpunten",
        "soundings" => "bronpunten",
        "selectedMachineId" => "gekozen machine",
        "drillingTechnique" => "boortechniek",
        "profilePointCount" => "profielpunten",
        "placements" => "plaatsingen",
        "mapLocked" => "kaart opgeslagen voor rapportage",
        "mapStateAvailable" => "kaartstatus beschikbaar",
        "project_info" => "projectinformatie",
        "boring_config" => "boringconfiguratie",
        "map_state" => "kaartstatus",
        "boortrace_geojson" => "boorlijn",
        "report_lock" => "rapportkaart",
        "report_snapshot" => "rapportsnapshot",
        "surface_analysis_generated" => "oppervlakteanalyse",
        "machine_placements" => "machineplaatsingen",
        "bro_dgm" => "BRO DGM",
        "bro_regis" => "REGIS II",
        "bro_geomorfologie" => "geomorfologie",
        "bro_bodemkaart" => "bodemkaart",
        "bro_grondwaterspiegeldiepte" => "grondwaterspiegeldiepte",
        _ => Regex.Replace(Regex.Replace(key, "_+", " "), "([a-z])([A-Z])", "$1 $2").ToLower(CultureInfo.CurrentCulture)
    };

    private static bool IsSemanticallySameStepReportData(string? existingJson, string nextJson)
    {
        if (string.IsNullOrWhiteSpace(existingJson))
        {
            return false;
        }

        return string.Equals(
            NormalizeVolatileReportJson(existingJson),
            NormalizeVolatileReportJson(nextJson),
            StringComparison.Ordinal);
    }

    private string ReadStepReportSection(int stepNumber)
    {
        if (_selectedProject is null) return "";
        var json = _projects.GetStepData(_selectedProject.Id, stepNumber, ReportSectionDataKey);
        if (string.IsNullOrWhiteSpace(json)) return "";

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("text", out var textElement)
                ? textElement.GetString() ?? ""
                : json;
        }
        catch
        {
            return json;
        }
    }

    private void RefreshAllStepReportData(bool force = false)
    {
        if (_selectedProject is null) return;

        _projectFiles = _projects.GetProjectFiles(_selectedProject.Id);
        var signature = BuildAllStepReportRefreshSignature();
        if (!force && string.Equals(_lastAllStepReportRefreshSignature, signature, StringComparison.Ordinal))
        {
            return;
        }

        foreach (var workspace in _workspaces.Values.OrderBy(workspace => workspace.StepNumber))
        {
            if (IsHiddenWorkflowStep(workspace.StepNumber)) continue;
            SaveStepReportDataForStep(workspace.StepNumber, refreshProjectFiles: false);
        }

        _lastAllStepReportRefreshSignature = BuildAllStepReportRefreshSignature();
    }

    private void RenderStepReportPreview(int stepNumber)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            RenderFinalReportPanel(stepNumber);
            StepTenPreviewSurface.Visibility = Visibility.Visible;
            StepTenReportContent.Children.Clear();

            var pages = CreateStepReportPreviewPages(stepNumber).ToList();
            if (pages.Count == 0)
            {
                pages.Add(GetReportPreviewPage(stepNumber));
            }
            foreach (var page in pages)
            {
                StepTenReportContent.Children.Add(page);
            }

            var title = _workspaces.TryGetValue(stepNumber, out var workspace)
                ? workspace.Title
                : "Rapportonderdeel";
            ReportPreviewTitleText.Text = $"Stap {stepNumber} - rapportpreview";
            ReportExportHintText.Text = "PDF-achtige weergave van dit subonderdeel. De tekst hieronder komt terug in het eindrapport.";
            ReportPartEditorPanel.Visibility = Visibility.Collapsed;
            ReportPartEditorTitle.Text = $"Rapportdata stap {stepNumber}: {title}";
            ReportPreviewPartText.Text = "";
            ReportPreviewPartText.IsEnabled = false;
        }
        finally
        {
            stopwatch.Stop();
            LogPerformanceTimingIfSlow($"Rapportpreview stap {stepNumber}", stopwatch.Elapsed, 120);
        }
    }

    private void SaveReportSnapshotFromCurrentStepData()
    {
        if (_selectedProject is null) return;

        _projectFiles = _projects.GetProjectFiles(_selectedProject.Id);
        var layers = BuildProjectMapLayers(_projectFiles);
        var docs = BuildProjectDocumentEntries(_projectFiles).ToList();
        var traceRows = GetTraceRowsForProfile();
        EnsureProfilePoints();
        var parcelAnalysis = BuildParcelOwnerAnalysis();
        var total = _profilePoints.Count >= 2
            ? Math.Max(1, _profilePoints[^1].Distance)
            : traceRows.Count >= 2
                ? BuildTraceDistances(traceRows)[^1]
                : Math.Max(1, _selectedProject.BoreLengthMeters);
        var surfaceSegments = GetBgtSurfaceSegments(total);
        var lockedMapSteps = _workspaces.Keys
            .Where(IsMapWorkspaceStep)
            .Where(IsMapReportLocked)
            .OrderBy(step => step)
            .ToArray();
        var stepStatuses = _workspaces.Values
            .OrderBy(workspace => workspace.StepNumber)
            .Where(workspace => !IsHiddenWorkflowStep(workspace.StepNumber))
            .Select(workspace => new
            {
                stepNumber = workspace.StepNumber,
                displayNumber = DisplayStepNumber(workspace.StepNumber),
                workspace.Title,
                status = GetStepCompletenessText(workspace.StepNumber),
                savedAt = ReadStepSavedAt(workspace.StepNumber),
                reportDataAvailable = _selectedProject is not null && !string.IsNullOrWhiteSpace(_projects.GetStepData(_selectedProject.Id, workspace.StepNumber, StepReportDataKey)),
                mapLocked = IsMapWorkspaceStep(workspace.StepNumber) && IsMapReportLocked(workspace.StepNumber)
            })
            .ToArray();

        var snapshot = new
        {
            generatedAt = DateTimeOffset.Now,
            projectId = _selectedProject.Id,
            projectName = _selectedProject.Name,
            reportVersion = ReportSchemaVersion,
            reportContractVersion = ReportSchemaVersion,
            projectFiles = _projectFiles.Count,
            documents = docs.Count,
            mapLayers = layers.Count,
            tracePoints = traceRows.Count,
            traceLength = total,
            profilePoints = _profilePoints.Count,
            surfaceSegments = surfaceSegments.Count,
            parcelSegments = parcelAnalysis.Segments.Count,
            stepReportData = stepStatuses.Count(step => step.reportDataAvailable),
            lockedMaps = lockedMapSteps.Length,
            stepReportSubsteps = stepStatuses.Sum(step => CountSavedStepReportSubsteps(step.stepNumber)),
            lockedMapSteps,
            selectedMachineId = _selectedMachineId,
            drillingTechnique = _selectedDrillingTechnique,
            stepStatuses
        };
        SaveSelectedProjectStepData(ReportStepNumber, ReportSnapshotDataKey, JsonSerializer.Serialize(snapshot, JsonOptions));
    }

    private void SaveStepReportDataForStep(int stepNumber, bool refreshProjectFiles = true)
    {
        if (_selectedProject is null) return;
        var payload = BuildStepReportDataPayload(stepNumber, refreshProjectFiles);
        if (payload is null) return;
        try
        {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            if (IsSemanticallySameStepReportData(_projects.GetStepData(_selectedProject.Id, stepNumber, StepReportDataKey), json))
            {
                return;
            }

            SaveSelectedProjectStepData(stepNumber, StepReportDataKey, json);
        }
        catch (InvalidOperationException exception)
        {
            SaveSelectedProjectStepData(stepNumber, StepReportDataKey, JsonSerializer.Serialize(new
            {
                schema = "borevexa.step-report-data.v3",
                generatedAt = DateTimeOffset.Now,
                projectId = _selectedProject.Id,
                stepNumber,
                displayNumber = DisplayStepNumber(stepNumber),
                title = _workspaces.TryGetValue(stepNumber, out var workspace) ? workspace.Title : $"Stap {stepNumber}",
                status = "Rapportdata tijdelijk hersteld",
                sourceKeys = BuildStepReportSourceKeys(stepNumber),
                error = exception.Message
            }, JsonOptions));
        }
    }

    private bool TryBuildStepReportContentFromSubsteps(int stepNumber, JsonElement root, out JsonElement content)
    {
        content = default;
        var hasAnySubstep = root.TryGetProperty("substeps", out var substeps) && substeps.ValueKind == JsonValueKind.Array && substeps.GetArrayLength() > 0;
        if (!hasAnySubstep) return false;

        TryGetSubstepData(root, $"{stepNumber}.1", out var first);
        TryGetSubstepData(root, $"{stepNumber}.2", out var second);
        TryGetSubstepData(root, $"{stepNumber}.3", out var third);
        TryGetSubstepData(root, $"{stepNumber}.4", out var fourth);
        TryGetSubstepData(root, $"{stepNumber}.5", out var fifth);

        content = stepNumber switch
        {
            0 => JsonSerializer.SerializeToElement(new
            {
                project = JsonText(first, "project", _selectedProject?.Name ?? "-"),
                location = JsonText(first, "location", _selectedProject?.Location ?? "-")
            }, JsonOptions),
            1 => JsonSerializer.SerializeToElement(new
            {
                project = first,
                boring = JsonProperty(third, "boring") ?? default,
                selectedMachineId = JsonText(fourth, "selectedMachineId", JsonText(third, "selectedMachineId", _selectedMachineId ?? "")),
                selectedMachine = JsonProperty(fourth, "selectedMachine") ?? default,
                drillingTechnique = JsonText(fourth, "drillingTechnique", _selectedDrillingTechnique),
                aiAnalysis = JsonText(fifth, "analysis", "")
            }, JsonOptions),
            2 => JsonSerializer.SerializeToElement(new
            {
                files = JsonArray(first, "files").ToArray(),
                documents = JsonArray(second, "documents").ToArray(),
                layers = JsonArray(third, "layers").ToArray(),
                klicCrossings = JsonArray(third, "klicCrossings").ToArray(),
                klicContactPdfAvailable = JsonArray(second, "documents").Any(document => JsonText(document, "name", "").EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            }, JsonOptions),
            3 => JsonSerializer.SerializeToElement(new
            {
                traceLength = JsonDouble(first, "traceLength"),
                pointCount = JsonInt(first, "pointCount"),
                points = JsonArray(first, "points").ToArray(),
                mapLocked = JsonProperty(first, "mapLocked") is { ValueKind: JsonValueKind.True },
                mapStateAvailable = JsonProperty(first, "mapStateAvailable") is { ValueKind: JsonValueKind.True },
                klicCrossings = JsonArray(second, "crossings").ToArray()
            }, JsonOptions),
            4 => JsonSerializer.SerializeToElement(new
            {
                traceLength = JsonDouble(first, "traceLength"),
                generated = JsonProperty(second, "generated") is { ValueKind: JsonValueKind.True },
                mapLocked = JsonProperty(second, "mapLocked") is { ValueKind: JsonValueKind.True },
                segments = JsonArray(first, "segments").ToArray()
            }, JsonOptions),
            5 => first.ValueKind == JsonValueKind.Object ? first : second,
            6 => first.ValueKind == JsonValueKind.Object ? first : JsonSerializer.SerializeToElement(new { status = "BRO/DINOloket kaartdataset controleren" }, JsonOptions),
            7 => JsonSerializer.SerializeToElement(new
            {
                traceLength = JsonDouble(root, "traceLength"),
                profilePointCount = JsonArray(first, "points").Count(),
                lowestNap = JsonArray(first, "points").Select(point => JsonDouble(point, "nap", double.NaN)).Where(double.IsFinite).DefaultIfEmpty(double.NaN).Min(),
                points = JsonArray(first, "points").ToArray()
            }, JsonOptions),
            8 => JsonSerializer.SerializeToElement(new
            {
                selectedMachineId = JsonText(first, "selectedMachineId", ""),
                selectedMachine = JsonProperty(first, "selectedMachine") ?? default,
                placements = JsonArray(first, "placements").ToArray()
            }, JsonOptions),
            9 => first.ValueKind == JsonValueKind.Object ? first : JsonSerializer.SerializeToElement(new { note = "Sonderingen zijn nog niet ingericht in de rapportdata." }, JsonOptions),
            var n when n == ReportStepNumber => first.ValueKind == JsonValueKind.Object ? first : JsonSerializer.SerializeToElement(new { status = "Rapportgenerator" }, JsonOptions),
            _ => first.ValueKind == JsonValueKind.Object ? first : JsonSerializer.SerializeToElement(new { status = GetStepCompletenessText(stepNumber) }, JsonOptions)
        };
        return true;
    }

    private bool TryReadReportSnapshot(out JsonElement snapshot)
    {
        snapshot = default;
        if (_selectedProject is null) return false;
        var json = _projects.GetStepData(_selectedProject.Id, ReportStepNumber, ReportSnapshotDataKey);
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            snapshot = document.RootElement.Clone();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryReadStepReportContent(int stepNumber, out JsonElement content)
    {
        content = default;
        if (_selectedProject is null) return false;
        var json = _projects.GetStepData(_selectedProject.Id, stepNumber, StepReportDataKey);
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("content", out var element))
            {
                content = element.Clone();
                return true;
            }

            return TryBuildStepReportContentFromSubsteps(stepNumber, document.RootElement, out content);
        }
        catch
        {
            return false;
        }
    }

    private bool TryReadStepReportRoot(int stepNumber, out JsonElement root)
    {
        root = default;
        if (_selectedProject is null) return false;
        var json = _projects.GetStepData(_selectedProject.Id, stepNumber, StepReportDataKey);
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
