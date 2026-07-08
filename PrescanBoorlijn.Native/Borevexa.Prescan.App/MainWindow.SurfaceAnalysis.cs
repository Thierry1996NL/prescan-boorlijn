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

// Oppervlakteanalyse (stap 4): BGT-segmentberekening langs de boorlijn,
// analyse-acties, panelen en oppervlaktefilters.
// Fase 3-opsplitsing (07-07-2026) van MainWindow.xaml.cs; gedrag ongewijzigd.

public partial class MainWindow
{
    private IReadOnlyList<BgtSurfaceSegment> GetBgtSurfaceSegments(double profileTotal)
    {
        if (_mapOverlayStates.TryGetValue("bgt", out var bgtVisible) && !bgtVisible)
        {
            return [];
        }

        var traceRows = GetTraceRowsForProfile();
        if (traceRows.Count < 2 || profileTotal <= 0) return [];

        var traceDistances = BuildTraceDistances(traceRows);
        if (traceDistances.Count < 2 || traceDistances[^1] <= 0) return [];

        // The geometric polygon analysis does an exact point-in-polygon test against
        // the actual BGT surface polygons, so it matches the BGT precisely. The
        // MapLibre "rendered" sampling is only an approximation: it queries rendered
        // pixels and votes across a perpendicular band, which in ditch-rich areas
        // (e.g. polders) picks up neighbouring water and grossly over-reports it.
        // Trust the polygon analysis whenever it has solid coverage of the boorlijn.
        var polygonSegments = BuildBgtSurfaceSegmentsFromProjectPolygons(profileTotal, traceRows, traceDistances);
        if (polygonSegments.Count > 0 && KnownSurfaceFraction(polygonSegments, profileTotal) >= 0.6)
        {
            return polygonSegments;
        }

        var mapSegments = BuildCurrentMapBgtSurfaceSegments(profileTotal);
        var savedSegments = ReadSavedBgtSurfaceSegments(profileTotal);

        var bestCurrent = ChooseBestBgtSurfaceSegments(mapSegments, polygonSegments, profileTotal);
        return ChooseBestBgtSurfaceSegments(bestCurrent, savedSegments, profileTotal);
    }

    private IReadOnlyList<BgtSurfaceSegment> BuildBgtSurfaceSegmentsFromProjectPolygons(
        double profileTotal,
        IReadOnlyList<TracePointRow> traceRows,
        IReadOnlyList<double> traceDistances)
    {
        _projectFiles = _selectedProject is null ? _projectFiles : _projects.GetProjectFiles(_selectedProject.Id);
        var polygons = BuildBgtSurfacePolygons();
        if (polygons.Count == 0)
        {
            return [];
        }

        var sampleStep = Math.Max(0.75, Math.Min(2.0, profileTotal / 80));
        var samples = new List<BgtSurfaceSample>();
        for (var distance = 0d; distance <= profileTotal + 0.001; distance += sampleStep)
        {
            var clampedDistance = Math.Min(profileTotal, distance);
            var traceDistance = traceDistances[^1] * clampedDistance / profileTotal;
            var point = InterpolateTracePoint(traceRows, traceDistances, traceDistance);
            // Actuele BGT-terreindelen bedekken de grond zonder overlap (historische
            // versies worden al bij het parsen weggefilterd), dus een punt ligt in
            // precies één echt oppervlak. Beperk tot echte oppervlakteklassen; bij
            // onverhoopte overlap wint het kleinste vlak — dat is dan het meest
            // specifieke (een sloot in een polder, niet andersom). Grote echte
            // waterlopen (kanalen van tientallen hectaren) tellen gewoon mee.
            var polygon = polygons
                .Where(candidate => BgtSurfaceContains(candidate, point.X, point.Y))
                .Where(candidate => IsBgtSurfaceVisible(candidate.Label))
                .Where(candidate => IsRealBgtSurface(candidate.Label))
                .OrderBy(candidate => candidate.Area)
                .FirstOrDefault();
            samples.Add(polygon is null
                ? new BgtSurfaceSample(clampedDistance, "Onbekend", "#F8FAFC")
                : new BgtSurfaceSample(clampedDistance, polygon.Label, polygon.Color));
        }

        return BuildBgtSurfaceSegmentsFromSamples(samples, profileTotal);
    }

    private IReadOnlyList<BgtSurfaceSegment> BuildCurrentMapBgtSurfaceSegments(double profileTotal)
    {
        if (_mapBgtSurfaceSamples.Count < 2 || profileTotal <= 0)
        {
            return [];
        }

        return BuildBgtSurfaceSegmentsFromSamples(
            _mapBgtSurfaceSamples.Where(sample => IsBgtSurfaceVisible(sample.Label)).ToList(),
            profileTotal);
    }

    private IReadOnlyList<BgtSurfaceSegment> ReadSavedBgtSurfaceSegments(double profileTotal)
    {
        if (_selectedProject is null || profileTotal <= 0)
        {
            return [];
        }

        var json = _projects.GetStepData(_selectedProject.Id, 4, "surface_analysis_generated") ??
                   _projects.GetStepData(_selectedProject.Id, 5, "surface_analysis_generated");
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var segmentsElement = JsonProperty(root, "segments");
            if (segmentsElement is not { ValueKind: JsonValueKind.Array })
            {
                return [];
            }

            var result = new List<BgtSurfaceSegment>();
            foreach (var segmentElement in segmentsElement.Value.EnumerateArray())
            {
                var label = JsonText(segmentElement, "label", "");
                if (string.IsNullOrWhiteSpace(label) ||
                    !IsBgtSurfaceVisible(label))
                {
                    continue;
                }

                var start = Math.Clamp(JsonDouble(segmentElement, "start", 0), 0, profileTotal);
                var end = Math.Clamp(JsonDouble(segmentElement, "end", start), 0, profileTotal);
                var length = JsonDouble(segmentElement, "length", 0);
                if (end <= start && length > 0)
                {
                    end = Math.Clamp(start + length, 0, profileTotal);
                }

                if (end <= start)
                {
                    continue;
                }

                var color = JsonText(segmentElement, "color", BgtSurfaceColorForLabel(label));
                result.Add(new BgtSurfaceSegment(start, end, label, color));
            }

            return result
                .OrderBy(segment => segment.Start)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<BgtSurfaceSegment> ChooseBestBgtSurfaceSegments(
        IReadOnlyList<BgtSurfaceSegment> mapSegments,
        IReadOnlyList<BgtSurfaceSegment> polygonSegments,
        double profileTotal)
    {
        if (mapSegments.Count == 0) return polygonSegments;
        if (polygonSegments.Count == 0) return mapSegments;

        var mapDominant = DominantSurfaceFraction(mapSegments, profileTotal);
        var polygonDominant = DominantSurfaceFraction(polygonSegments, profileTotal);
        var mapKnown = KnownSurfaceFraction(mapSegments, profileTotal);
        var polygonKnown = KnownSurfaceFraction(polygonSegments, profileTotal);
        var mapVariety = SurfaceVariety(mapSegments);
        var polygonVariety = SurfaceVariety(polygonSegments);

        if (!ContainsSurface(mapSegments, "water") && ContainsSurface(polygonSegments, "water"))
        {
            return polygonSegments;
        }

        if (ContainsSurface(polygonSegments, "water") && TotalSurfaceLength(polygonSegments, "water") > TotalSurfaceLength(mapSegments, "water") + 0.5)
        {
            return polygonSegments;
        }

        if (!ContainsSurface(mapSegments, "asfalt") && ContainsSurface(polygonSegments, "asfalt"))
        {
            return polygonSegments;
        }

        if (ContainsSurface(polygonSegments, "asfalt") && TotalSurfaceLength(polygonSegments, "asfalt") > TotalSurfaceLength(mapSegments, "asfalt") + 0.5)
        {
            return polygonSegments;
        }

        if (mapVariety <= 1 && mapDominant > 0.90 && polygonVariety > 1 && polygonKnown >= mapKnown - 0.10)
        {
            return polygonSegments;
        }

        if (mapDominant > 0.76 && polygonVariety > mapVariety && polygonDominant < mapDominant - 0.08)
        {
            return polygonSegments;
        }

        if (mapSegments.Count <= 2 && polygonSegments.Count >= mapSegments.Count + 2 && polygonKnown >= mapKnown - 0.05)
        {
            return polygonSegments;
        }

        return mapSegments;
    }

    private static double DominantSurfaceFraction(IReadOnlyList<BgtSurfaceSegment> segments, double profileTotal)
    {
        if (segments.Count == 0 || profileTotal <= 0) return 1;
        return segments
            .GroupBy(segment => NormalizeBgtSurfaceKey(segment.Label))
            .Max(group => group.Sum(segment => segment.Length)) / profileTotal;
    }

    private static double KnownSurfaceFraction(IReadOnlyList<BgtSurfaceSegment> segments, double profileTotal)
    {
        if (segments.Count == 0 || profileTotal <= 0) return 0;
        return segments
            .Where(segment => !segment.Label.Equals("Onbekend", StringComparison.OrdinalIgnoreCase))
            .Sum(segment => segment.Length) / profileTotal;
    }

    private static int SurfaceVariety(IReadOnlyList<BgtSurfaceSegment> segments) =>
        segments.Select(segment => NormalizeBgtSurfaceKey(segment.Label)).Distinct(StringComparer.OrdinalIgnoreCase).Count();

    private static bool ContainsSurface(IReadOnlyList<BgtSurfaceSegment> segments, string surface)
    {
        return segments.Any(segment => NormalizeBgtSurfaceKey(segment.Label).Equals(surface, StringComparison.OrdinalIgnoreCase));
    }

    private static double TotalSurfaceLength(IReadOnlyList<BgtSurfaceSegment> segments, string surface)
    {
        return segments
            .Where(segment => NormalizeBgtSurfaceKey(segment.Label).Equals(surface, StringComparison.OrdinalIgnoreCase))
            .Sum(segment => segment.Length);
    }

    private static IReadOnlyList<BgtSurfaceSegment> BuildBgtSurfaceSegmentsFromSamples(IReadOnlyList<BgtSurfaceSample> sourceSamples, double profileTotal)
    {
        var samples = sourceSamples
            .Where(sample => double.IsFinite(sample.Distance) && sample.Distance >= 0 && sample.Distance <= profileTotal + 0.001)
            .OrderBy(sample => sample.Distance)
            .ToList();
        if (samples.Count == 0) return [];
        if (samples[0].Distance > 0.001)
        {
            samples.Insert(0, samples[0] with { Distance = 0 });
        }
        if (samples[^1].Distance < profileTotal)
        {
            samples.Add(samples[^1] with { Distance = profileTotal });
        }

        var segments = new List<BgtSurfaceSegment>();
        var start = 0d;
        var current = samples[0];
        for (var i = 1; i < samples.Count; i++)
        {
            var sample = samples[i];
            if (sample.Label.Equals(current.Label, StringComparison.OrdinalIgnoreCase) && sample.Color.Equals(current.Color, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var end = Math.Max(start, (samples[i - 1].Distance + sample.Distance) / 2);
            if (end - start >= 0.5)
            {
                segments.Add(new BgtSurfaceSegment(start, end, current.Label, current.Color));
            }

            start = end;
            current = sample;
        }

        if (profileTotal - start >= 0.5)
        {
            segments.Add(new BgtSurfaceSegment(start, profileTotal, current.Label, current.Color));
        }

        return segments.Where(segment => !segment.Label.Equals("Onbekend", StringComparison.OrdinalIgnoreCase) || segment.Length >= 3).ToList();
    }

    private IReadOnlyList<BgtSurfacePolygon> BuildBgtSurfacePolygons()
    {
        if (_mapOverlayStates.TryGetValue("bgt", out var bgtVisible) && !bgtVisible)
        {
            return [];
        }

        var polygons = new List<BgtSurfacePolygon>();
        foreach (var layer in BuildProjectMapLayers(_projectFiles)
                     .Where(layer => layer.Type.Equals("BGT", StringComparison.OrdinalIgnoreCase))
                     .Where(layer => IsProjectLayerVisible(layer.Id)))
        {
            foreach (var feature in layer.FeatureCollection.Features)
            {
                foreach (var coordinateRings in EnumeratePolygonCoordinateRings(feature.Geometry))
                {
                    var rings = coordinateRings
                        .Select(ring => ring.Select(ToRdPoint).Where(point => point.X > 0 && point.Y > 0).ToList())
                        .Where(ring => ring.Count >= 4)
                        .ToList();

                    if (rings.Count == 0)
                    {
                        continue;
                    }

                    var outerRing = rings[0];
                    var holes = rings.Skip(1).ToList();
                    var area = PolygonArea(outerRing) - holes.Sum(PolygonArea);
                    polygons.Add(new BgtSurfacePolygon(outerRing, holes, BgtSurfaceLabel(feature), BgtSurfaceColor(feature), Math.Abs(area)));
                }
            }
        }

        return polygons
            // Geen bovengrens op oppervlakte: echte waterlopen/kanalen zijn soms
            // tientallen hectaren en moeten meetellen. Thematische gebieden worden
            // al op label gefilterd (IsRealBgtSurface) en historische versies bij
            // het parsen (eindRegistratie).
            .Where(polygon => polygon.Ring.Count >= 4 && polygon.Area > 0.5)
            .ToList();
    }

    private static string BgtSurfaceLabel(GeoJsonFeature feature)
    {
        var objectType = GetFeatureString(feature, "objectType", GetFeatureString(feature, "theme", "BGT"));
        var sourceName = GetFeatureString(feature, "sourceName", "");
        var bgtType = GetFeatureString(feature, "bgtType", "");
        var value = objectType.ToLowerInvariant();
        var function = GetFeatureString(feature, "Functie", "");
        var functionPlus = GetFeatureString(feature, "Functie plus", "");
        var physical = GetFeatureString(feature, "Fysiek voorkomen", "");
        var physicalPlus = GetFeatureString(feature, "Fysiek voorkomen plus", "");
        var classValue = GetFeatureString(feature, "Klasse", "");
        var functionValue = $"{function} {functionPlus}".Replace('_', ' ').Replace('-', ' ').ToLowerInvariant();
        var physicalValue = $"{physical} {physicalPlus} {classValue}".Replace('_', ' ').Replace('-', ' ').ToLowerInvariant();
        var combined = $"{value} {sourceName} {bgtType} {function} {functionPlus} {physical} {physicalPlus} {classValue}"
            .Replace('_', ' ')
            .Replace('-', ' ')
            .ToLowerInvariant();
        var typeValue = $"{objectType} {bgtType} {sourceName}"
            .Replace('_', ' ')
            .Replace('-', ' ')
            .ToLowerInvariant();
        var isExplicitWater = typeValue.Contains("waterdeel") ||
                              typeValue.Contains("waterbody") ||
                              typeValue.Contains("waterloop");
        var isGreen = functionValue.Contains("berm") ||
                      functionValue.Contains("groenvoorziening") ||
                      functionValue.Contains("plantsoen") ||
                      functionValue.Contains("vegetatie") ||
                      physicalValue.Contains("groenvoorziening") ||
                      physicalValue.Contains("gras") ||
                      physicalValue.Contains("kruid") ||
                      physicalValue.Contains("bos");
        var isPaved = functionValue.Contains("rijbaan") ||
                      functionValue.Contains("fietspad") ||
                      functionValue.Contains("voetpad") ||
                      physicalValue.Contains("asfalt") ||
                      physicalValue.Contains("gesloten verharding");

        if (combined.Contains("spoor")) return "spoor";
        if (isExplicitWater || functionValue.Contains("water") || physicalValue.Contains("water")) return "water";
        if (isPaved) return "asfalt";
        if (isGreen) return "groenstrook";
        if (combined.Contains(" water ")) return "water";
        if (combined.Contains("begroeid") || combined.Contains("vegetatie") || combined.Contains("groen") || combined.Contains("plantcover")) return "groenstrook";
        if (combined.Contains("onbegroeid") || combined.Contains("open verharding") || combined.Contains("zand") || combined.Contains("gravel")) return "onverhard";
        if (combined.Contains("pand") || combined.Contains("gebouw") || combined.Contains("building")) return "bebouwing";
        if (combined.Contains("ondersteunend wegdeel")) return "groenstrook";
        if (combined.Contains("wegdeel") || combined.Contains("road") || combined.Contains("traffic")) return "asfalt";
        return objectType.Replace('_', ' ');
    }

    private bool IsBgtSurfaceVisible(string label)
    {
        var key = NormalizeBgtSurfaceKey(label);
        return _gisLayerState.IsBgtSurfaceVisible(key);
    }

    private static string NormalizeBgtSurfaceKey(string label)
    {
        var value = (label ?? string.Empty).Trim().Replace('_', ' ').Replace('-', ' ').ToLowerInvariant();
        if (value.Contains("water")) return "water";
        if (value.Contains("berm")) return "groenstrook";
        if (value.Contains("asfalt") || value.Contains("wegdeel") || value.Contains("verharding") || value.Contains("rijbaan")) return "asfalt";
        if (value.Contains("groen") || value.Contains("begroeid") || value.Contains("vegetatie") || value.Contains("plantcover") ||
            value.Contains("gras") || value.Contains("kruid") || value.Contains("bos")) return "groenstrook";
        if (value.Contains("onverhard") || value.Contains("zand") || value.Contains("gravel")) return "onverhard";
        if (value.Contains("bebouwing") || value.Contains("pand") || value.Contains("gebouw") || value.Contains("building")) return "bebouwing";
        if (value.Contains("spoor")) return "spoor";
        return "overig";
    }

    private static string BgtSurfaceColor(GeoJsonFeature feature)
    {
        var label = BgtSurfaceLabel(feature);
        return label switch
        {
            "water" => "#7DD3FC",
            "asfalt" => "#CBD5E1",
            "spoor" => "#9CA3AF",
            "groenstrook" => "#86EFAC",
            "onverhard" => "#FDE68A",
            "bebouwing" => "#D1D5DB",
            _ => GetFeatureString(feature, "color", "#E2E8F0")
        };
    }

    private static string BgtSurfaceColorForLabel(string label) =>
        NormalizeBgtSurfaceKey(label) switch
        {
            "water" => "#7DD3FC",
            "asfalt" => "#CBD5E1",
            "spoor" => "#9CA3AF",
            "groenstrook" => "#86EFAC",
            "onverhard" => "#FDE68A",
            "bebouwing" => "#D1D5DB",
            _ => "#E2E8F0"
        };

    private static int BgtSurfacePriority(string label)
    {
        return label.ToLowerInvariant() switch
        {
            "asfalt" => 100,
            "water" => 90,
            "spoor" => 85,
            "bebouwing" => 80,
            "onverhard" => 60,
            "groenstrook" => 40,
            _ => 10
        };
    }

    // Only the physical BGT surface (terreindeel) classes tile the ground and belong
    // in the surface profile. Thematic/administrative areas such as "functioneelgebied"
    // normalise to "overig" and must be ignored, otherwise a huge functional-area
    // polygon (or a parser fragment) would swamp the real surface classification.
    private static bool IsRealBgtSurface(string label) =>
        NormalizeBgtSurfaceKey(label) is "water" or "asfalt" or "spoor" or "groenstrook" or "onverhard" or "bebouwing";

    private static double BgtSurfaceRank(BgtSurfacePolygon polygon)
    {
        var areaScore = Math.Log10(Math.Max(1, polygon.Area));
        var priorityScore = BgtSurfacePriority(polygon.Label) * 0.03;
        return areaScore - priorityScore;
    }

    private static bool BgtSurfaceContains(BgtSurfacePolygon polygon, double x, double y)
    {
        if (!PointInPolygon(x, y, polygon.Ring)) return false;
        return !polygon.Holes.Any(hole => PointInPolygon(x, y, hole));
    }

    private static IReadOnlyList<BgtSurfaceSample> ReadMapBgtSurfaceSamples(JsonElement root)
    {
        if (!root.TryGetProperty("bgtSurfaceSamples", out var samplesElement) || samplesElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var samples = new List<BgtSurfaceSample>();
        foreach (var sampleElement in samplesElement.EnumerateArray())
        {
            var distance = sampleElement.TryGetProperty("distance", out var distanceElement) && distanceElement.TryGetDouble(out var distanceValue)
                ? distanceValue
                : double.NaN;
            var label = sampleElement.TryGetProperty("label", out var labelElement)
            ? labelElement.GetString() ?? "Onbekend"
                : "Onbekend";
            var color = sampleElement.TryGetProperty("color", out var colorElement)
            ? colorElement.GetString() ?? "#F8FAFC"
                : "#F8FAFC";

            if (double.IsFinite(distance))
            {
                samples.Add(new BgtSurfaceSample(distance, label, color));
            }
        }

        return samples.OrderBy(sample => sample.Distance).ToList();
    }

    // De oppervlakteanalyse loopt over de ECHTE boorlijn (geometrische lengte van de
    // ingetekende trace), niet over de as van het dwarsprofiel. De profielpunten
    // beslaan vaak maar een deel van de lijn (bv. 54,6 m van een 102,7 m boorlijn),
    // waardoor de analyse anders de tweede helft — met bijv. een slootkruising —
    // volledig zou missen.
    private double GetSurfaceAnalysisTraceLength()
    {
        var traceRows = GetTraceRowsForProfile();
        if (traceRows.Count >= 2)
        {
            var distances = BuildTraceDistances(traceRows);
            if (distances.Count >= 2 && distances[^1] > 0)
            {
                return distances[^1];
            }
        }

        EnsureProfilePoints();
        if (_profilePoints.Count >= 2)
        {
            return Math.Max(1, _profilePoints[^1].Distance);
        }

        return Math.Max(1, _selectedProject?.BoreLengthMeters ?? 1);
    }

    private string ExecuteSurfaceAnalysis()
    {
        if (_selectedProject is null) return "Geen project actief.";
        EnsureProfilePoints();

        // The BGT surface analysis is computed geometrically from the BGT polygons
        // (exact point-in-polygon along the boorlijn) and no longer depends on a live
        // map response. Keep the map trace/layers in sync for other views, but render
        // the result immediately so the analysis also works while the GIS map tab is
        // hidden (e.g. on the Oppervlakteprofiel tab).
        _surfaceAnalysisMetricsRefreshPending = false;
        SendProjectLayersToMap();
        SendTraceStateToMap();

        var total = GetSurfaceAnalysisTraceLength();
        var segments = GetBgtSurfaceSegments(total);
        SaveSurfaceAnalysisSnapshot(total, segments);
        AppendMapDiagnostic(
            $"Oppervlakteanalyse: boorlijn {total:N1} m, {segments.Count} segment(en): " +
            string.Join(", ", segments.Select(segment => $"{segment.Label} {segment.Start:N1}-{segment.End:N1}m")));

        // If the GIS map is visible, refresh its report capture too so the map in the
        // rapportage matches the current view. When the map tab is hidden the capture
        // is skipped (it can't render), and the map keeps its last saved snapshot.
        if (_selectedStep?.Number == 4 && StepThreeMapFrame.Visibility == Visibility.Visible)
        {
            SaveMapStateForStep(4, false);
            QueueLiveMapReportCapture(4);
        }

        if (_selectedStep?.Number == 4)
        {
            ActivateWorkflowPart(4, "oppervlakte");
            RenderSurfaceAnalysisPanel();
        }

        ApplyStepThreeLayoutBounds();
        RefreshWorkflowReportStatus(4);
        if (_selectedReportPreviewStepNumber == 4)
        {
            RenderStepReportPreview(4);
        }

        // Push the fresh analysis straight into any open rapportage (inline preview
        // and the substap/hoofdstuk preview window) so it updates the moment the
        // analysis runs.
        SaveStepReportDataForStep(4);
        RefreshInlineReportPreviewIfVisible();
        if (IsReportPreviewWindowOpen())
        {
            RefreshReportPreviewWindow();
        }

        if (segments.Count == 0)
        {
            return "Oppervlakteanalyse\n\nGeen BGT-oppervlaktesegmenten gevonden langs de boorlijn. Controleer of de BGT-import zichtbaar is en of de boorlijn is opgeslagen.";
        }

        var summary = string.Join(", ", segments
            .GroupBy(segment => NormalizeBgtSurfaceKey(segment.Label))
            .OrderByDescending(group => group.Sum(segment => segment.Length))
            .Select(group => $"{group.Sum(segment => segment.Length):N1} m {group.Key}"));
        return $"Oppervlakteanalyse uitgevoerd\n\n{segments.Count} segment(en) over {total:N1} m boorlijn: {summary}.";
    }

    private void RenderSurfaceAnalysisPanel()
    {
        if (_selectedProject is null) return;
        EnsureProfilePoints();
        var total = GetSurfaceAnalysisTraceLength();
        var segments = GetBgtSurfaceSegments(total);
        SurfaceAnalysisBarCanvas.Children.Clear();
        SurfaceAnalysisTablePanel.Children.Clear();
        UpdateSurfaceAnalysisSummaryBar(segments, total);
        SurfaceAnalysisSummaryText.Text = segments.Count == 0
            ? "Geen BGT-oppervlaktesegmenten gevonden langs de boorlijn."
            : $"{segments.Count} segment(en) over {total:N1} m boorlijn - zelfde bron als het dwarsprofiel.";
        DrawSurfaceAnalysisBar(SurfaceAnalysisBarCanvas, segments, total);
        SurfaceAnalysisTablePanel.Children.Add(CreateReportSurfaceSegmentTable(segments));
    }

    private void DrawSurfaceAnalysisBar(Canvas canvas, IReadOnlyList<BgtSurfaceSegment> segments, double total)
    {
        var panelWidth = StepSurfaceAnalysisPanel.ActualWidth;
        canvas.Width = Math.Max(520, panelWidth > 120 ? panelWidth - 44 : 860);
        // Hoog genoeg voor: titel, de balk, de as-regel (incl. labels van korte
        // segmenten) en een volledige (niet-afgesneden) legenda-regel.
        canvas.Height = 104;
        AddCanvasRect(canvas, 0, 0, canvas.Width, canvas.Height, "#FFFFFF", "#E5E7EB", 0.8);
        AddCanvasText(canvas, "BGT oppervlakteprofiel", 10, 8, "#334155", 11, FontWeights.Bold);
        const double left = 28;
        const double top = 34;
        var width = Math.Max(320, canvas.Width - left - 32);
        const double height = 24;
        AddCanvasRect(canvas, left, top, width, height, "#F8FAFC", "#CBD5E1", 1);

        if (segments.Count == 0)
        {
            AddCanvasText(canvas, "Geen BGT-profiel beschikbaar", left + 10, top + 5, "#8FA6B2", 11, FontWeights.Normal);
            return;
        }

        var axisY = top + height + 7;
        AddCanvasText(canvas, "0 m", left, axisY, "#64748B", 9.5, FontWeights.Normal);
        AddCanvasText(canvas, $"{total:N1} m", left + width - 54, axisY, "#64748B", 9.5, FontWeights.Normal);

        // Segmenten die te smal zijn voor een label in de balk krijgen hun lengte
        // onder de balk (op de as-regel), met een aanwijslijntje naar het segment.
        // Links en rechts blijven de vaste asmaten (0 m / totaal) vrij.
        var previousAxisLabelEnd = left + 30;
        foreach (var segment in segments)
        {
            var x = left + Math.Clamp(segment.Start / Math.Max(1, total), 0, 1) * width;
            var w = Math.Max(2, Math.Clamp(segment.Length / Math.Max(1, total), 0, 1) * width);
            AddCanvasRect(canvas, x, top, Math.Min(w, left + width - x), height, segment.Color, "#FFFFFF", 0.8);
            if (w > 46)
            {
                var label = w > 110 ? $"{segment.Length:N0} m {segment.Label}" : $"{segment.Length:N0} m";
                AddCanvasText(canvas, label, x + 5, top + 5, "#071422", 9.5, FontWeights.Bold);
            }
            else
            {
                var label = $"{segment.Length:N1} m";
                var estimatedWidth = label.Length * 5.5;
                var labelX = Math.Clamp(x + w / 2 - estimatedWidth / 2, previousAxisLabelEnd + 6, left + width - 58 - estimatedWidth);
                AddCanvasText(canvas, label, labelX, axisY, "#334155", 9, FontWeights.SemiBold);
                AddCanvasRect(canvas, x + w / 2, top + height, 1, 4, "#94A3B8", "#94A3B8", 0);
                previousAxisLabelEnd = labelX + estimatedWidth;
            }
        }

        var legendX = left;
        var legendY = top + height + 26;
        foreach (var item in segments
                     .GroupBy(segment => segment.Label)
                     .Select(group => new { Label = group.Key, Color = group.First().Color, Length = group.Sum(segment => segment.Length) })
                     .OrderByDescending(item => item.Length)
                     .Take(5))
        {
            AddCanvasRect(canvas, legendX, legendY + 2, 9, 9, item.Color, "#CBD5E1", 0.6);
            AddCanvasText(canvas, $"{item.Label} {item.Length:N1} m", legendX + 14, legendY, "#475569", 9, FontWeights.Normal);
            legendX += Math.Min(170, 66 + item.Label.Length * 6);
            if (legendX > canvas.Width - 170)
            {
                break;
            }
        }
    }

    private void UpdateSurfaceAnalysisSummaryBar()
    {
        if (_selectedStep?.Number != 4)
        {
            SurfaceAnalysisSummaryBar.Visibility = Visibility.Collapsed;
            return;
        }

        if (_selectedProject is null)
        {
            SurfaceAnalysisSummaryBar.Visibility = Visibility.Collapsed;
            return;
        }

        EnsureProfilePoints();
        var total = GetSurfaceAnalysisTraceLength();
        UpdateSurfaceAnalysisSummaryBar(GetBgtSurfaceSegments(total), total);
    }

    private void UpdateSurfaceAnalysisSummaryBar(IReadOnlyList<BgtSurfaceSegment> segments, double total)
    {
        if (_selectedStep?.Number != 4)
        {
            SurfaceAnalysisSummaryBar.Visibility = Visibility.Collapsed;
            return;
        }

        var summary = segments.Count == 0
            ? "Nog geen BGT-oppervlaktesegmenten gevonden. Klik op Analyse uitvoeren nadat BGT zichtbaar is."
            : string.Join(" | ",
                segments
                    .GroupBy(segment => segment.Label)
                    .Select(group => $"{group.Sum(segment => segment.Length):N1} m {group.Key}")
                    .Take(6));
        var suffix = segments.Select(segment => segment.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 6 ? " | ..." : "";
        SurfaceAnalysisSummaryBarText.Text = $"Boorlijn {total:N1} m | {segments.Count} segment(en) | {summary}{suffix}";
        SurfaceAnalysisSummaryBar.Visibility = Visibility.Visible;
    }

    private void SaveSurfaceAnalysisSnapshot(double total, IReadOnlyList<BgtSurfaceSegment> segments)
    {
        if (_selectedProject is null) return;

        var payload = JsonSerializer.Serialize(new
        {
            generatedAt = DateTimeOffset.Now,
            source = "borevexa-bgt-surface-analysis",
            traceLength = total,
            segments = segments.Select(segment => new
            {
                segment.Label,
                segment.Start,
                segment.End,
                segment.Length,
                segment.Color
            })
        }, JsonOptions);
        SaveSelectedProjectStepData(4, "surface_analysis_generated", payload);
    }

    private bool ShouldShowSurfaceAnalysisPanel()
    {
        if (_selectedProject is null || _selectedStep?.Number != 4) return false;
        if (IsMapReportLocked(4)) return true;
        return !string.IsNullOrWhiteSpace(_projects.GetStepData(_selectedProject.Id, 4, "surface_analysis_generated") ??
                                          _projects.GetStepData(_selectedProject.Id, 5, "surface_analysis_generated"));
    }

    private string BuildSurfaceAnalysisReportText()
    {
        EnsureProfilePoints();
        var total = GetSurfaceAnalysisTraceLength();
        var segments = GetBgtSurfaceSegments(total);
        var builder = new StringBuilder();
        builder.AppendLine($"BGT oppervlakteprofiel opgeslagen voor rapportage op {DateTime.Now:dd-MM-yyyy HH:mm}.");
        if (segments.Count == 0)
        {
            builder.AppendLine("Geen BGT-oppervlaktesegmenten beschikbaar.");
            return builder.ToString().Trim();
        }

        foreach (var segment in segments)
        {
            builder.AppendLine($"- {segment.Label}: {segment.Start:N1}-{segment.End:N1} m ({segment.Length:N1} m)");
        }
        return builder.ToString().Trim();
    }

    private void AddStepFiveBgtSidebarTools()
    {
        var card = new Border
        {
            Background = Brush("#F8FAFB"),
            BorderBrush = Brush("#DEE6EA"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7),
            Margin = new Thickness(5, 0, 5, 10)
        };

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Analyse langs boorlijn",
            Foreground = Brush("#3F4750"),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var buttons = new UniformGrid { Columns = 1, Rows = 2 };
        AddBgtRibbonButton(buttons, "Boorlijn horizontaal", "Boorlijn horizontaal", true);
        AddBgtRibbonButton(buttons, "Analyse uitvoeren", "Analyse uitvoeren", true);
        panel.Children.Add(buttons);

        panel.Children.Add(new TextBlock
        {
            Text = "Draai de kaart zodat de boorlijn horizontaal ligt, of voer de BGT-oppervlakteanalyse uit langs de opgeslagen boorlijn. De balk, segmenten en rapportage worden meteen bijgewerkt.",
            Foreground = Brush("#7F99AC"),
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        });

        card.Child = panel;
        SurfaceAnalysisSidebarPanel.Children.Add(card);
    }

    private string AlignMapToHorizontalBorelineForStepFour(bool runAnalysis)
    {
        if (_selectedStep?.Number != 4) return "Boorlijn uitlijnen is alleen beschikbaar in stap 4.";

        EnsureStoredBoreTraceLoaded();
        if (_currentBoreTracePoints.Count < 2)
        {
            return "Boorlijn niet beschikbaar\n\nTeken en sla eerst minimaal een intrede- en uittredepunt op in stap 3.1.";
        }

        // The sidebar button can be clicked while the Oppervlakteprofiel tab is active;
        // switch to the GIS-kaart tab first so the rotation is actually visible.
        if (StepThreeMapFrame.Visibility != Visibility.Visible)
        {
            ActivateWorkflowPart(4, "kaart");
        }

        _mapOverlayStates["boreTrace"] = true;
        _mapOverlayStates["boreTraceNumbers"] = true;
        SendAllFilterStatesToMap();
        SendTraceStateToMap(drawing: false);
        // Fit the whole boorlijn horizontally in view (no forced werktekening scale);
        // the report capture follows the map view 1-op-1.
        SendMapMessage("{\"type\":\"fitTrace\",\"fit\":\"bounds\"}");

        var analysisResult = runAnalysis ? ExecuteSurfaceAnalysis() : "";
        _ = RefreshMapStateAndReportAfterMapAnimationAsync(_selectedStep.Number);

        var alignmentText = "Kaart uitgelijnd\n\nDe GIS-kaart is gedraaid zodat de volledige boorlijn horizontaal in beeld staat. De rapportage neemt dit kaartbeeld 1-op-1 over.";
        return runAnalysis && !string.IsNullOrWhiteSpace(analysisResult)
            ? $"{analysisResult}\n\n{alignmentText}"
            : alignmentText;
    }

    private void BgtSurfaceFilter_OnClick(object sender, RoutedEventArgs e)
    {
        if (BlockIfCurrentMapReportLocked()) return;
        if ((sender as Button)?.Tag is not string surface) return;

        var visible = _gisLayerState.ToggleBgtSurface(surface);
        _mapBgtSurfaceSamples = [];
        _suppressProjectLayerSend = true;
        try
        {
            RenderStepThree(force: true);
        }
        finally
        {
            _suppressProjectLayerSend = false;
        }
        SendBgtSurfaceFiltersToMap();
        SendTraceStateToMap();
        SendProfileModeToMap();
        SaveCurrentMapStateAfterLayerChange();

        if (_selectedStep?.Number == ProfileStepNumber || _selectedStep?.Number == WorkDrawingStepNumber)
        {
            RenderProfilePanel();
        }
    }

    private static bool IsSurfaceAnalysisOverlay(string overlayId) =>
        overlayId.Equals("baseMap", StringComparison.OrdinalIgnoreCase) ||
        overlayId.Equals("parcels", StringComparison.OrdinalIgnoreCase) ||
        overlayId.Equals("bgt", StringComparison.OrdinalIgnoreCase) ||
        overlayId.Equals("bagImport", StringComparison.OrdinalIgnoreCase) ||
        overlayId.Equals("klic", StringComparison.OrdinalIgnoreCase) ||
        overlayId.Equals("klicBuffer", StringComparison.OrdinalIgnoreCase) ||
        overlayId.StartsWith("boreTrace", StringComparison.OrdinalIgnoreCase);

    private void SendBgtSurfaceFiltersToMap()
    {
        var payload = JsonSerializer.Serialize(new { type = "bgtSurfaceFilter", surfaces = _bgtSurfaceStates }, JsonOptions);
        SendMapMessage(payload);
    }

    private void AddBgtSurfaceToggles(Panel parent)
    {
        foreach (var surface in _bgtSurfaceStates.Keys.ToList())
        {
            _gisSidebar.AddLayerToggle(
                parent,
                BgtSurfaceFilterLabel(surface),
                surface,
                "BGT filter",
                _bgtSurfaceStates[surface],
                new Thickness(22, 0, 0, 0),
                BgtSurfaceFilter_OnClick,
                10.5);
        }
    }

    private static string BgtSurfaceFilterLabel(string surface) => GisSidebarBuilder.BgtSurfaceFilterLabel(surface);
}
