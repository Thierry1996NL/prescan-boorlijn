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

// Boorlijn (stap 3): intekenen, opslaan en visualiseren van de boortrace.
// Fase 3-opsplitsing (07-07-2026) van MainWindow.xaml.cs; gedrag ongewijzigd.

public partial class MainWindow
{
    private void AddStepFourTraceRibbon()
    {
        var card = new Border
        {
            Background = Brush("#F8FAFB"),
            BorderBrush = Brush("#DEE6EA"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Boorlijn tekenen",
            Foreground = Brush("#3F4750"),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6)
        });

        LoadTraceVisualSettings();
        var buttons = new UniformGrid { Columns = 2, Rows = 5 };
        AddTraceRibbonButton(buttons, "+ Nieuwe lijn", "Nieuwe boorlijn", true);
        AddTraceRibbonButton(buttons, "Bewerken", "Start tekenmodus", false);
        AddTraceRibbonButton(buttons, "Kaart verplaatsen", "Boorlijn handmodus", false);
        AddTraceRibbonButton(buttons, "Zoom boorlijn", "Zoom naar boorlijn", false);
        AddTraceRibbonButton(buttons, "Verschuiven", "Verschuif lijn", false);
        AddTraceRibbonButton(buttons, _traceSmoothBore ? "Boorlijn hoekig" : "Boorlijn vloeiend", "Boorlijn trace vloeiend", false);
        AddTraceRibbonButton(buttons, "Wissel richting", "Wissel richting", false);
        AddTraceRibbonButton(buttons, "Verwijderen", "Verwijder trace", false);
        AddTraceRibbonButton(buttons, "Opslaan", "Boorlijn opslaan", true);
        panel.Children.Add(buttons);

        panel.Children.Add(new TextBlock
        {
            Text = "Gebruik Kaart verplaatsen om rustig te pannen. Gebruik Bewerken voor punten; sleep punten om ze te verplaatsen. Rechtermuisklik verwijdert een punt.",
            Foreground = Brush("#8FA6B2"),
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 7, 0, 0)
        });
        AddTracePointsTable(panel);

        card.Child = panel;
        StepSpecificRibbonPanel.Children.Add(card);
    }

    private void AddTracePointsTable(Panel parent)
    {
        parent.Children.Add(new TextBlock
        {
            Text = "Punten - RD coordinaten",
            Foreground = Brush("#5F7785"),
            FontSize = 10.5,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 10, 0, 4)
        });

        _tracePointsTablePanel = new StackPanel();
        parent.Children.Add(_tracePointsTablePanel);
        RenderTracePointsTable();
    }

    private void AddTraceRibbonButton(Panel parent, string label, string action, bool primary)
    {
        var button = new Button
        {
            Content = label,
            Tag = action,
            Height = 30,
            Margin = new Thickness(0, 0, 5, 5),
            Padding = new Thickness(4, 0, 4, 0),
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Background = primary ? Brush("#3F4750") : Brushes.White,
            Foreground = primary ? Brushes.White : Brush("#071422"),
            BorderBrush = primary ? Brush("#3F4750") : Brush("#DEE6EA"),
            BorderThickness = new Thickness(1)
        };
        button.Click += StepAction_OnClick;
        parent.Children.Add(button);
    }

    private static void AddTraceTableCell(Grid row, string text, int column, bool header)
    {
        var cell = new TextBlock
        {
            Text = text,
            Foreground = header ? Brush("#5F7785") : Brush("#071422"),
            FontSize = 9.5,
            FontWeight = header ? FontWeights.Bold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 2, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(cell, column);
        row.Children.Add(cell);
    }

    private static IReadOnlyList<ReportMapRecipe> BuildFixedScaleTraceRecipes(
        int stepNumber,
        string title,
        string purpose,
        string baseMap,
        string layerSet,
        IReadOnlyList<TracePointRow> traceRows,
        int scaleDenominator,
        int maxSheets)
    {
        if (traceRows.Count < 2)
        {
            return [new ReportMapRecipe($"step{stepNumber}-fixed-empty", stepNumber, title, purpose, baseMap, layerSet, scaleDenominator, "fixed-scale-trace", 760, 330, true, false, false, null, null)];
        }

        var distances = BuildTraceDistances(traceRows);
        var total = distances.Count > 0 ? distances[^1] : 0;
        if (total <= 0)
        {
            return [new ReportMapRecipe($"step{stepNumber}-fixed-single", stepNumber, title, purpose, baseMap, layerSet, scaleDenominator, "fixed-scale-trace", 760, 330, true, false, false, null, null)];
        }

        var visibleMeters = Math.Max(12, 700 / ReportPixelsPerMeterForScale(scaleDenominator));
        var overlap = Math.Min(10, visibleMeters * 0.18);
        var step = Math.Max(visibleMeters - overlap, visibleMeters * 0.65);
        var result = new List<ReportMapRecipe>();
        var start = 0d;
        var sheet = 1;
        while (start < total && result.Count < maxSheets)
        {
            var end = Math.Min(total, start + visibleMeters);
            var suffix = total > visibleMeters ? $" blad {sheet}" : "";
            result.Add(new ReportMapRecipe(
                $"step{stepNumber}-fixed-{sheet}",
                stepNumber,
                $"{title}{suffix}",
                $"{purpose} Uitsnede {start:N0}-{end:N0} m.",
                baseMap,
                layerSet,
                scaleDenominator,
                "fixed-scale-trace",
                760,
                330,
                true,
                false,
                false,
                start,
                end));
            if (end >= total) break;
            start += step;
            sheet++;
        }

        return result;
    }

    private static string BuildReportLocationMapTraceDetailsHtml(IReadOnlyList<TracePointRow> traceRows)
    {
        if (traceRows.Count < 2) return "";
        var distances = BuildTraceDistances(traceRows);
        var start = traceRows[0];
        var end = traceRows[^1];
        static string H(string value) => System.Net.WebUtility.HtmlEncode(value);
        var lengthText = H($"{distances[^1]:N1} m");
        var startText = H($"X {start.X:N2} / Y {start.Y:N2}");
        var endText = H($"X {end.X:N2} / Y {end.Y:N2}");
        return $"""
<div class="mapbox" style="margin-top:-12px;background:#fbfcfd">
<div class="maptitle">Boorlijngegevens</div>
<table class="kv">
<tbody>
<tr><th>Boorlijnlengte</th><td>{lengthText}</td></tr>
<tr><th>Beginpunt RD</th><td>{startText}</td></tr>
<tr><th>Eindpunt RD</th><td>{endText}</td></tr>
</tbody>
</table>
</div>
""";
    }

    private static List<double> BuildTraceDistances(IReadOnlyList<TracePointRow> traceRows)
    {
        if (traceRows.Count == 0) return [];
        var distances = new List<double> { 0 };
        for (var i = 1; i < traceRows.Count; i++)
        {
            var previous = traceRows[i - 1];
            var current = traceRows[i];
            distances.Add(distances[^1] + Math.Sqrt(Math.Pow(current.X - previous.X, 2) + Math.Pow(current.Y - previous.Y, 2)));
        }

        return distances;
    }

    private ReportLocationContext BuildTraceLocationContext(IReadOnlyList<TracePointRow> traceRows)
    {
        var declaredPlace = GetDeclaredReportPlace();
        var declaredLocation = GetDeclaredReportLocation();
        var midpoint = GetTraceMidpointWgs(traceRows);
        if (midpoint is null)
        {
            return new ReportLocationContext(declaredLocation, "", declaredPlace, "", "");
        }

        var fallback = $"{declaredLocation} (circa {midpoint.Lat:N5}, {midpoint.Lon:N5})";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Borevexa/1.0 (report reverse geocoding)");
            var url = $"https://nominatim.openstreetmap.org/reverse?format=jsonv2&lat={midpoint.Lat.ToString(CultureInfo.InvariantCulture)}&lon={midpoint.Lon.ToString(CultureInfo.InvariantCulture)}&zoom=18&addressdetails=1";
            var json = http.GetStringAsync(url).GetAwaiter().GetResult();
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var displayName = root.TryGetProperty("display_name", out var displayElement) ? displayElement.GetString() ?? "" : "";
            var road = "";
            var place = "";
            var houseNumber = "";
            if (root.TryGetProperty("address", out var address))
            {
                road = ReadAddressPart(address, "road", "pedestrian", "cycleway", "footway", "path");
                houseNumber = ReadAddressPart(address, "house_number");
                place = ReadAddressPart(address, "village", "town", "city", "hamlet", "municipality", "county");
            }

            var summaryParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(road)) summaryParts.Add(string.IsNullOrWhiteSpace(houseNumber) ? road : $"{road} {houseNumber}");
            if (!string.IsNullOrWhiteSpace(place)) summaryParts.Add(place);
            var summary = summaryParts.Count > 0 ? string.Join(", ", summaryParts) : ShortReportCell(displayName, 90);
            if (string.IsNullOrWhiteSpace(summary)) summary = fallback;
            summary = ReconcileReportLocationSummary(summary, road, houseNumber, place, declaredPlace, fallback);
            return new ReportLocationContext(summary, road, place, houseNumber, displayName);
        }
        catch
        {
            return new ReportLocationContext(fallback, "", "", "", "");
        }
    }

    private void CaptureCurrentBoreTrace(string message)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            _currentBoreTraceJson = root.TryGetProperty("geojson", out var geojsonElement) && geojsonElement.ValueKind == JsonValueKind.Object
                ? geojsonElement.GetRawText()
                : null;
            _currentBoreTracePoints = ReadTracePointRows(root);
            _profileScreenMetrics = null;
            _mapBgtSurfaceSamples = [];
            ClearEnvironmentAnalysisCache();
            RenderTracePointsTable();
        }
        catch
        {
            _currentBoreTraceJson = null;
            _currentBoreTracePoints = [];
            _mapBgtSurfaceSamples = [];
            ClearEnvironmentAnalysisCache();
            RenderTracePointsTable();
        }
    }

    private string ClearBoreTrace()
    {
        if (_selectedProject is null) return "Geen project actief.";
        if (IsSelectedStepThreeKlicSubstep()) return "Deze KLIC-controlesubstap is alleen-lezen. Verwijder of wijzig de boorlijn in 3.1 Boorlijn ingetekend.";
        SaveBoreTraceGeoJson("null");
        _currentBoreTraceJson = null;
        _currentBoreTracePoints = [];
        ClearEnvironmentAnalysisCache();
        RenderTracePointsTable();
        SendMapMessage("{\"type\":\"traceClear\"}");
        return "Boorlijn verwijderd\n\nDe trace is uit stap Boorlijn gewist en lokaal opgeslagen.";
    }

    private static RdPoint ClosestPointOnRdTrace(RdPoint point, IReadOnlyList<RdPoint> tracePoints)
    {
        var best = tracePoints[0];
        var bestDistance = double.MaxValue;
        for (var i = 1; i < tracePoints.Count; i++)
        {
            var a = tracePoints[i - 1];
            var b = tracePoints[i];
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var lengthSquared = dx * dx + dy * dy;
            if (lengthSquared <= 0.0001) continue;

            var t = Math.Clamp(((point.X - a.X) * dx + (point.Y - a.Y) * dy) / lengthSquared, 0d, 1d);
            var candidate = new RdPoint(a.X + dx * t, a.Y + dy * t);
            var distance = Distance(point, candidate);
            if (distance >= bestDistance) continue;

            best = candidate;
            bestDistance = distance;
        }

        return best;
    }

    private UIElement CreateCoverBoreTraceMapBlock()
    {
        const double mapWidth = 724;
        const double mapHeight = 318;
        var frame = new Grid
        {
            Width = mapWidth,
            Height = mapHeight,
            ClipToBounds = true,
            Background = Brush("#F8FAFB"),
            Clip = new RectangleGeometry(new Rect(0, 0, mapWidth, mapHeight), 10, 10)
        };

        var coverMapPath = GetLiveMapReportPreviewImagePath(3, StepThreeCoverOsmReportMapVariant);
        if (!string.IsNullOrWhiteSpace(coverMapPath) && File.Exists(coverMapPath))
        {
            var image = new Image
            {
                Width = mapWidth,
                Height = mapHeight,
                Stretch = Stretch.UniformToFill,
                SnapsToDevicePixels = true,
                ClipToBounds = true
            };
            if (TryApplyLocalImageSource(image, coverMapPath))
            {
                frame.Children.Add(image);
            }
        }

        if (frame.Children.Count == 0)
        {
            var traceRows = NormalizeTraceRowsToRd(GetTraceRowsForProfile());
            frame.Children.Add(new Viewbox
            {
                Width = mapWidth,
                Height = mapHeight,
                Stretch = Stretch.UniformToFill,
                Child = CreateReportOpenStreetMapCanvas("Boorlijn op OpenStreetMap", traceRows)
            });
        }

        return new Border
        {
            Width = mapWidth,
            Background = Brushes.White,
            BorderBrush = Brush("#D7E8FA"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 58, 0, 0),
            Child = frame
        };
    }

    private void DrawBoreLine(StepWorkspace workspace)
    {
        AddLine(70, 300, 560, 150, "#007A5A", 5, [8, 3]);
        AddCircle(70, 300, 9, "#007A5A", "#007A5A");
        AddCircle(560, 150, 9, "#007A5A", "#007A5A");

        if (workspace.StepNumber is 7 or 8 or 9)
        {
            AddCircle(312, 226, 7, "#FFFFFF", "#E85D04", 3);
            AddText("analysepunt", 324, 214, "#E85D04", 10, FontWeights.SemiBold);
        }
    }

    private void EnsureStoredBoreTraceLoaded()
    {
        if (_selectedProject is null || _currentBoreTracePoints.Count >= 2) return;

        var traceJson = GetStoredBoreTraceJson();
        if (string.IsNullOrWhiteSpace(traceJson))
        {
            _currentBoreTraceJson = null;
            _currentBoreTracePoints = [];
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(traceJson);
            var points = ReadTracePointRows(document.RootElement);
            if (points.Count == 0)
            {
                _currentBoreTraceJson = null;
                _currentBoreTracePoints = [];
                return;
            }

            _currentBoreTraceJson = traceJson;
            _currentBoreTracePoints = points;
        }
        catch
        {
            _currentBoreTraceJson = null;
            _currentBoreTracePoints = [];
        }
    }

    private static IEnumerable<RdPoint> EnumerateTraceSamplePoints(IReadOnlyList<TracePointRow> traceRows)
    {
        foreach (var row in traceRows)
        {
            yield return new RdPoint(row.X, row.Y);
        }

        for (var i = 1; i < traceRows.Count; i++)
        {
            var previous = traceRows[i - 1];
            var current = traceRows[i];
            yield return new RdPoint((previous.X + current.X) / 2d, (previous.Y + current.Y) / 2d);
        }
    }

    private bool FeatureCrossesTrace(GeoJsonFeature feature, IReadOnlyList<TracePointRow> traceRows)
    {
        foreach (var rdLine in EnumerateFeatureGeometryLines(feature).Select(line => line.Where(IsValidRdPoint).ToList()))
        {
            if (rdLine.Count < 2) continue;
            for (var klicIndex = 1; klicIndex < rdLine.Count; klicIndex++)
            {
                var klicA = rdLine[klicIndex - 1];
                var klicB = rdLine[klicIndex];
                for (var traceIndex = 1; traceIndex < traceRows.Count; traceIndex++)
                {
                    var traceA = traceRows[traceIndex - 1];
                    var traceB = traceRows[traceIndex];
                    if (TrySegmentIntersection(
                            traceA.X, traceA.Y, traceB.X, traceB.Y,
                            klicA.X, klicA.Y, klicB.X, klicB.Y,
                            out _))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static string FormatTraceRole(string? role) => role switch
    {
        "intrede" => "Intrede",
        "uittrede" => "Uittrede",
        _ => "Tussenpunt"
    };

    private static string FormatTraceStatusMessage(string message)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            var points = root.TryGetProperty("points", out var pointsElement) && pointsElement.TryGetInt32(out var pointCount) ? pointCount : 0;
            var length = root.TryGetProperty("length", out var lengthElement) && lengthElement.TryGetDouble(out var lengthMeters) ? lengthMeters : 0;
            return $"Boorlijn bijgewerkt\n\nPunten: {points}\nLengte: {length:N1} m\nKlik op 'Boorlijn opslaan' om de trace lokaal vast te leggen.";
        }
        catch
        {
            return $"Boorlijn bijgewerkt\n\n{message}";
        }
    }

    private IReadOnlyList<TracePointRow> GetReportLocationMapTraceRows(IReadOnlyList<TracePointRow> fallbackTraceRows)
    {
        EnsureProfilePoints();
        if (_profileAlignedToMap && _profilePoints.Count >= 2 && _profilePoints.All(point => IsValidRdPoint(new RdPoint(point.X, point.Y))))
        {
            return _profilePoints
                .OrderBy(point => point.Distance)
                .Select((point, index) => new TracePointRow(
                    index + 1,
                    index == 0 ? "Intrede" : index == _profilePoints.Count - 1 ? "Uittrede" : "Tussenpunt",
                    point.X,
                    point.Y))
                .ToList();
        }

        return fallbackTraceRows;
    }

    private string? GetStoredBoreTraceJson()
    {
        if (_selectedProject is null) return null;
        var json = _projects.GetStepData(_selectedProject.Id, 3, "boortrace_geojson") ??
                   _projects.GetStepData(_selectedProject.Id, 4, "boortrace_geojson") ??
                   _projects.GetStepData(_selectedProject.Id, 5, "boortrace_geojson");
        if (string.IsNullOrWhiteSpace(json) || json.Trim().Equals("null", StringComparison.OrdinalIgnoreCase)) return null;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.TryGetProperty("type", out var type) && type.GetString() == "LineString")
            {
                return root.GetRawText();
            }

            if (ReadTracePointRows(root).Count >= 2)
            {
                return root.GetRawText();
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static LonLat? GetTraceMidpointWgs(IReadOnlyList<TracePointRow> traceRows)
    {
        if (traceRows.Count < 2) return null;
        var distances = BuildTraceDistances(traceRows);
        if (distances.Count < 2 || distances[^1] <= 0) return null;
        var midpoint = InterpolateTracePoint(traceRows, distances, distances[^1] / 2d);
        if (!LooksLikeRd(midpoint.X, midpoint.Y)) return null;
        var wgs = RdToWgs84(midpoint.X, midpoint.Y);
        return new LonLat(wgs[0], wgs[1]);
    }

    private static TracePointRow InterpolateTracePoint(IReadOnlyList<TracePointRow> traceRows, IReadOnlyList<double> distances, double targetDistance)
    {
        if (traceRows.Count == 0) return new TracePointRow(1, "Dieptepunt", 0, 0);
        if (targetDistance <= 0) return traceRows[0];
        if (targetDistance >= distances[^1]) return traceRows[^1];

        for (var i = 1; i < distances.Count; i++)
        {
            if (targetDistance > distances[i]) continue;
            var previousDistance = distances[i - 1];
            var segmentLength = Math.Max(0.001, distances[i] - previousDistance);
            var ratio = (targetDistance - previousDistance) / segmentLength;
            var previous = traceRows[i - 1];
            var next = traceRows[i];
            return new TracePointRow(0, "Dieptepunt", previous.X + (next.X - previous.X) * ratio, previous.Y + (next.Y - previous.Y) * ratio);
        }

        return traceRows[^1];
    }

    private static bool IsBoreTraceEditAction(string action) =>
        action is "Nieuwe boorlijn"
            or "Start tekenmodus"
            or "Boorlijn opslaan"
            or "Verschuif lijn"
            or "Boorlijn trace vloeiend"
            or "Wissel richting"
            or "Verwijder trace";

    private static bool IsBoreTraceReportMapSubstep(int stepNumber, string substepNumber) =>
        stepNumber == 3 && string.Equals(substepNumber, "3.1", StringComparison.OrdinalIgnoreCase);

    private static bool IsBorelineDisplayStep(int? stepNumber) => stepNumber is 3 or 4;

    private static bool IsBorelineStep(int? stepNumber) => stepNumber is 3;

    private void LoadTraceVisualSettings()
    {
        if (_selectedProject is null || _traceVisualSettingsProjectId == _selectedProject.Id) return;
        _traceVisualSettingsProjectId = _selectedProject.Id;
        _traceSmoothBore = false;
        var json = _projects.GetStepData(_selectedProject.Id, 4, "boortrace_visual_settings");
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.TryGetProperty("smooth", out var smooth) && smooth.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                _traceSmoothBore = smooth.GetBoolean();
            }
        }
        catch
        {
            _traceSmoothBore = false;
        }
    }

    private static IReadOnlyList<TracePointRow> NormalizeTraceRowsToRd(IReadOnlyList<TracePointRow> rows)
    {
        return rows.Select(row =>
        {
            if (LooksLikeRd(row.X, row.Y)) return row;
            if (LooksLikeLonLat(row.X, row.Y))
            {
                var rd = Wgs84ToRd(row.X, row.Y);
                return row with { X = rd.X, Y = rd.Y };
            }

            return row;
        }).ToList();
    }

    private static TraceProjection ProjectPointOnTrace(double x, double y, IReadOnlyList<TracePointRow> traceRows, IReadOnlyList<double> distances)
    {
        var bestDistance = 0d;
        var bestOffset = double.MaxValue;
        for (var i = 1; i < traceRows.Count; i++)
        {
            var a = traceRows[i - 1];
            var b = traceRows[i];
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var lengthSquared = dx * dx + dy * dy;
            if (lengthSquared <= 0.0001) continue;

            var t = Math.Max(0, Math.Min(1, ((x - a.X) * dx + (y - a.Y) * dy) / lengthSquared));
            var px = a.X + dx * t;
            var py = a.Y + dy * t;
            var offset = Math.Sqrt(Math.Pow(x - px, 2) + Math.Pow(y - py, 2));
            if (offset >= bestOffset) continue;

            bestOffset = offset;
            bestDistance = distances[i - 1] + Math.Sqrt(lengthSquared) * t;
        }

        return new TraceProjection(bestDistance, bestOffset);
    }

    private static KlicPlanPoint ProjectPointOnTraceSigned(RdPoint point, IReadOnlyList<TracePointRow> traceRows, IReadOnlyList<double> distances)
    {
        var bestStation = 0d;
        var bestOffset = 0d;
        var bestDistance = double.MaxValue;
        for (var i = 1; i < traceRows.Count; i++)
        {
            var a = traceRows[i - 1];
            var b = traceRows[i];
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var lengthSquared = dx * dx + dy * dy;
            if (lengthSquared <= 0.0001) continue;
            var length = Math.Sqrt(lengthSquared);
            var t = Math.Clamp(((point.X - a.X) * dx + (point.Y - a.Y) * dy) / lengthSquared, 0, 1);
            var px = a.X + dx * t;
            var py = a.Y + dy * t;
            var distance = Math.Sqrt(Math.Pow(point.X - px, 2) + Math.Pow(point.Y - py, 2));
            if (distance >= bestDistance) continue;
            var signed = ((point.X - px) * (-dy / length)) + ((point.Y - py) * (dx / length));
            bestDistance = distance;
            bestOffset = signed;
            bestStation = distances[i - 1] + length * t;
        }

        return new KlicPlanPoint(bestStation, bestOffset);
    }

    private static IReadOnlyList<TracePointRow> ReadTracePointRows(JsonElement root)
    {
        if (root.TryGetProperty("geojson", out var geoJsonElement) && geoJsonElement.ValueKind == JsonValueKind.Object)
        {
            var rowsFromGeoJson = ReadTracePointRowsFromLineString(geoJsonElement);
            if (rowsFromGeoJson.Count >= 2) return rowsFromGeoJson;
        }

        var directRowsFromGeoJson = ReadTracePointRowsFromLineString(root);
        if (directRowsFromGeoJson.Count >= 2) return directRowsFromGeoJson;

        if (!root.TryGetProperty("rdPoints", out var pointsElement) || pointsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var rows = new List<TracePointRow>();
        foreach (var pointElement in pointsElement.EnumerateArray())
        {
            var index = pointElement.TryGetProperty("index", out var indexElement) && indexElement.TryGetInt32(out var pointIndex)
                ? pointIndex
                : rows.Count + 1;
            var role = pointElement.TryGetProperty("role", out var roleElement)
                ? FormatTraceRole(roleElement.GetString())
                : "Tussenpunt";
            var x = pointElement.TryGetProperty("x", out var xElement) && xElement.TryGetDouble(out var xValue) ? xValue : 0;
            var y = pointElement.TryGetProperty("y", out var yElement) && yElement.TryGetDouble(out var yValue) ? yValue : 0;
            rows.Add(new TracePointRow(index, role, x, y));
        }

        return rows;
    }

    private static IReadOnlyList<TracePointRow> ReadTracePointRowsFromLineString(JsonElement lineStringElement)
    {
        if (!lineStringElement.TryGetProperty("type", out var typeElement) ||
            !string.Equals(typeElement.GetString(), "LineString", StringComparison.OrdinalIgnoreCase) ||
            !lineStringElement.TryGetProperty("coordinates", out var coordinatesElement) ||
            coordinatesElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var rows = new List<TracePointRow>();
        foreach (var coordinateElement in coordinatesElement.EnumerateArray())
        {
            if (coordinateElement.ValueKind != JsonValueKind.Array || coordinateElement.GetArrayLength() < 2) continue;
            var rd = CoordinateToRdPoint(coordinateElement[0].GetDouble(), coordinateElement[1].GetDouble());
            if (!IsValidRdPoint(rd)) continue;

            var index = rows.Count + 1;
            rows.Add(new TracePointRow(index, index == 1 ? "Intrede" : "Tussenpunt", rd.X, rd.Y));
        }

        if (rows.Count >= 2) rows[^1] = rows[^1] with { Role = "Uittrede" };
        return rows;
    }

    private async System.Threading.Tasks.Task RefreshTraceStateAfterMapLayerSyncAsync()
    {
        await System.Threading.Tasks.Task.Delay(180);
        if (!_mapLibreLoaded || StepThreeMapView.CoreWebView2 is null) return;
            SendTraceStateToMap();
            if (_selectedStep?.Number == 6) SendBroSoundingsToMap();
            await System.Threading.Tasks.Task.Delay(420);
            if (!_mapLibreLoaded || StepThreeMapView.CoreWebView2 is null) return;
            SendTraceStateToMap();
            if (_selectedStep?.Number == 6) SendBroSoundingsToMap();
    }

    private void RenderBoreTraceSidebarPanel()
    {
        EnsureStoredBoreTraceLoaded();

        BoreTraceSidebarPanel.Children.Add(new TextBlock
        {
            Text = "Boorlijn tekenen",
            Foreground = Brush("#071422"),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        LoadTraceVisualSettings();
        var buttons = new UniformGrid { Columns = 2, Rows = 5, Margin = new Thickness(0, 0, 0, 8) };
        AddTraceRibbonButton(buttons, "+ Nieuwe lijn", "Nieuwe boorlijn", true);
        AddTraceRibbonButton(buttons, "Bewerken", "Start tekenmodus", false);
        AddTraceRibbonButton(buttons, "Kaart verplaatsen", "Boorlijn handmodus", false);
        AddTraceRibbonButton(buttons, "Zoom boorlijn", "Zoom naar boorlijn", false);
        AddTraceRibbonButton(buttons, "Verschuiven", "Verschuif lijn", false);
        AddTraceRibbonButton(buttons, _traceSmoothBore ? "Boorlijn hoekig" : "Boorlijn vloeiend", "Boorlijn trace vloeiend", false);
        AddTraceRibbonButton(buttons, "Wissel richting", "Wissel richting", false);
        AddTraceRibbonButton(buttons, "Verwijderen", "Verwijder trace", false);
        AddTraceRibbonButton(buttons, "Opslaan", "Boorlijn opslaan", true);
        BoreTraceSidebarPanel.Children.Add(buttons);

        BoreTraceSidebarPanel.Children.Add(new TextBlock
        {
            Text = "Gebruik Kaart verplaatsen om rustig te pannen. Gebruik Bewerken voor punten; sleep punten om ze te verplaatsen. Rechtermuisklik verwijdert een punt.",
            Foreground = Brush("#587080"),
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        });

        AddTracePointsTable(BoreTraceSidebarPanel);
        SendTraceStateToMap();
    }

    private void RenderTracePointsTable()
    {
        if (_tracePointsTablePanel is null) return;
        _tracePointsTablePanel.Children.Clear();

        var header = new Grid { Height = 22, Background = Brush("#F1F5F7") };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition());
        AddTraceTableCell(header, "#", 0, true);
        AddTraceTableCell(header, "Rol", 1, true);
        AddTraceTableCell(header, "X RD", 2, true);
        AddTraceTableCell(header, "Y RD", 3, true);
        _tracePointsTablePanel.Children.Add(header);

        if (_currentBoreTracePoints.Count == 0)
        {
            _tracePointsTablePanel.Children.Add(new TextBlock
            {
                Text = "Nog geen punten.",
                Foreground = Brush("#8FA6B2"),
                FontSize = 10.5,
                Margin = new Thickness(0, 5, 0, 0)
            });
            return;
        }

        foreach (var point in _currentBoreTracePoints)
        {
            var row = new Grid { Height = 22, Background = point.Role == "Intrede" ? Brush("#F3F4F6") : point.Role == "Uittrede" ? Brush("#FEF2F2") : Brushes.White };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition());
            AddTraceTableCell(row, point.Index.ToString(CultureInfo.InvariantCulture), 0, false);
            AddTraceTableCell(row, point.Role, 1, false);
            AddTraceTableCell(row, point.X.ToString("N3", CultureInfo.CurrentCulture), 2, false);
            AddTraceTableCell(row, point.Y.ToString("N3", CultureInfo.CurrentCulture), 3, false);
            _tracePointsTablePanel.Children.Add(row);
        }
    }

    private string RequestBoreTraceSave()
    {
        if (!IsBorelineStep(_selectedStep?.Number)) return "Boorlijn opslaan is alleen beschikbaar in stap Boorlijn.";
        if (IsSelectedStepThreeKlicSubstep()) return "Deze KLIC-controlesubstap is alleen-lezen. Sla de boorlijn op in 3.1 Boorlijn ingetekend.";
        if (_selectedProject is not null &&
            _currentBoreTracePoints.Count >= 2 &&
            !string.IsNullOrWhiteSpace(_currentBoreTraceJson))
        {
            SaveBoreTraceGeoJson(_currentBoreTraceJson);
            _profileScreenMetrics = null;
            ClearEnvironmentAnalysisCache();
            RenderTracePointsTable();
            RefreshWorkflowReportStatus(_selectedStep?.Number ?? 3);
            SaveStepReportDataForStep(3);
            return $"Boorlijn opgeslagen\n\nProject: {_selectedProject.Name}\nPunten: {_currentBoreTracePoints.Count}\nLengte: {TraceLengthMeters(_currentBoreTracePoints):N1} m\nLokale database: stap Boorlijn / boortrace_geojson";
        }

        SendMapMessage("{\"type\":\"traceSaveRequest\"}");
        return "Boorlijn opslaan aangevraagd\n\nDe kaart stuurt de actuele trace terug naar de lokale database.";
    }

    private void SaveBoreTraceFromMap(string message)
    {
        if (_selectedProject is null) return;

        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            var geojson = root.TryGetProperty("geojson", out var geojsonElement) && geojsonElement.ValueKind == JsonValueKind.Object
                ? geojsonElement.GetRawText()
                : "null";
            var points = root.TryGetProperty("points", out var pointsElement) && pointsElement.TryGetInt32(out var pointCount) ? pointCount : 0;
            var length = root.TryGetProperty("length", out var lengthElement) && lengthElement.TryGetDouble(out var lengthMeters) ? lengthMeters : 0;

            SaveBoreTraceGeoJson(geojson);
            _currentBoreTraceJson = geojson == "null" ? null : geojson;
            _currentBoreTracePoints = ReadTracePointRows(root);
            _profileScreenMetrics = null;
            ClearEnvironmentAnalysisCache();
            RenderTracePointsTable();
            RefreshWorkflowReportStatus(_selectedStep?.Number ?? 3);
            SaveStepReportDataForStep(3);
            OutputText.Text = $"Boorlijn opgeslagen\n\nProject: {_selectedProject.Name}\nPunten: {points}\nLengte: {length:N1} m\nLokale database: stap Boorlijn / boortrace_geojson";
        }
        catch (Exception exception)
        {
            OutputText.Text = $"Boorlijn opslaan mislukt\n\n{exception.Message}";
        }
    }

    private void SaveBoreTraceGeoJson(string geojson)
    {
        if (_selectedProject is null) return;
        SaveSelectedProjectStepData(3, "boortrace_geojson", geojson);
        SaveSelectedProjectStepData(4, "boortrace_geojson", geojson);
    }

    private void SaveTraceVisualSettings()
    {
        if (_selectedProject is null) return;
        var payload = JsonSerializer.Serialize(new
        {
            smooth = _traceSmoothBore,
            savedAt = DateTimeOffset.Now
        }, JsonOptions);
        SaveSelectedProjectStepData(4, "boortrace_visual_settings", payload);
    }

    private void SendTraceStateToMap(bool drawing = false, int? reportStepNumber = null)
    {
        if (!_mapLibreLoaded || StepThreeMapView.CoreWebView2 is null)
        {
            QueueMapSync();
            return;
        }

        var stepNumber = reportStepNumber ?? _selectedStep?.Number;
        var workDrawingStep = stepNumber == WorkDrawingStepNumber;
        var surfaceAnalysisStep = stepNumber == 4;
        var undergroundStep = stepNumber == 6;
        var undergroundPointOnlyMap = undergroundStep && IsBroPointDatasetModel(GetActiveUndergroundModelType());
        var visible = workDrawingStep || surfaceAnalysisStep || undergroundStep || (_mapOverlayStates.TryGetValue("boreTrace", out var traceVisible) && traceVisible);
        var enabled = (IsBorelineDisplayStep(stepNumber) ||
                       (stepNumber is > 4 and <= ThreeDStepNumber && stepNumber != ReportStepNumber) ||
                       stepNumber == WorkDrawingStepNumber) &&
                      visible;
        var showNumbers = !undergroundPointOnlyMap && (workDrawingStep || surfaceAnalysisStep || undergroundStep || (_mapOverlayStates.TryGetValue("boreTraceNumbers", out var numbersVisible) && numbersVisible));
        var showLengths = !undergroundPointOnlyMap && (workDrawingStep || surfaceAnalysisStep || undergroundStep || (_mapOverlayStates.TryGetValue("boreTraceLengths", out var lengthsVisible) && lengthsVisible));
        var showInfo = !undergroundPointOnlyMap && (workDrawingStep || surfaceAnalysisStep || undergroundStep || (_mapOverlayStates.TryGetValue("boreTraceInfo", out var infoVisible) && infoVisible));
        var traceJson = enabled ? (_currentBoreTraceJson ?? GetStoredBoreTraceJson()) : null;
        var boringDiameterMm = GetBoringDiameterMillimeters();
        var boringInfoJson = JsonSerializer.Serialize(BuildBoringMapInfo(), JsonOptions);
        LoadTraceVisualSettings();
        var editableDrawing = IsBorelineStep(stepNumber) && drawing;
        var payload = $"{{\"type\":\"traceState\",\"enabled\":{enabled.ToString().ToLowerInvariant()},\"drawing\":{editableDrawing.ToString().ToLowerInvariant()},\"showNumbers\":{showNumbers.ToString().ToLowerInvariant()},\"showLengths\":{showLengths.ToString().ToLowerInvariant()},\"showInfo\":{showInfo.ToString().ToLowerInvariant()},\"smooth\":{_traceSmoothBore.ToString().ToLowerInvariant()},\"boringDiameterMm\":{boringDiameterMm},\"boringInfo\":{boringInfoJson},\"geojson\":{(string.IsNullOrWhiteSpace(traceJson) ? "null" : traceJson)}}}";
            _gisMap.TrySendJson(
            StepThreeMapView.CoreWebView2,
            payload,
            exception => AppendMapDiagnostic($"Boorlijnstatus naar kaart sturen mislukt: {exception.Message}"));
    }

    private static IReadOnlyList<Point> SmoothReportTrace(IReadOnlyList<Point> points)
    {
        if (points.Count < 3) return points;
        var result = new List<Point>();
        static int Clamp(int index, int count) => Math.Max(0, Math.Min(count - 1, index));
        static double Catmull(double p0, double p1, double p2, double p3, double t)
        {
            var t2 = t * t;
            var t3 = t2 * t;
            return 0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3);
        }

        for (var i = 0; i < points.Count - 1; i++)
        {
            var p0 = points[Clamp(i - 1, points.Count)];
            var p1 = points[i];
            var p2 = points[i + 1];
            var p3 = points[Clamp(i + 2, points.Count)];
            var distance = Math.Sqrt(Math.Pow(p2.X - p1.X, 2) + Math.Pow(p2.Y - p1.Y, 2));
            var steps = Math.Max(8, Math.Min(24, (int)Math.Ceiling(distance / 18d)));
            for (var step = 0; step < steps; step++)
            {
                if (i > 0 && step == 0) continue;
                var t = step / (double)steps;
                result.Add(new Point(
                    Catmull(p0.X, p1.X, p2.X, p3.X, t),
                    Catmull(p0.Y, p1.Y, p2.Y, p3.Y, t)));
            }
        }

        result.Add(points[^1]);
        return result;
    }

    private string StartBoreTraceMode()
    {
        if (!IsBorelineStep(_selectedStep?.Number)) return "Tekenmodus is alleen beschikbaar in stap Boorlijn.";
        if (IsSelectedStepThreeKlicSubstep()) return "Deze KLIC-controlesubstap is alleen-lezen. Pas de boorlijn aan in 3.1 Boorlijn ingetekend.";
        SendTraceStateToMap(drawing: true);
        return "Tekenmodus gestart\n\nKlik op de kaart om punten toe te voegen. Sleep punten om ze te verplaatsen. Gebruik Kaart verplaatsen om de kaart zelf te pannen.";
    }

    private string ToggleSmoothTrace()
    {
        LoadTraceVisualSettings();
        _traceSmoothBore = !_traceSmoothBore;
        SaveTraceVisualSettings();
        SendTraceStateToMap();
        StepSpecificRibbonPanel.Children.Clear();
        BoreTraceSidebarPanel.Children.Clear();
        RenderBoreTraceSidebarPanel();
        return _traceSmoothBore
            ? "Boorlijn vloeiend\n\nDe kaart tekent de boorlijn nu als een vloeiende curve. De tussenpunten blijven opgeslagen en komen terug zodra je weer hoekig kiest."
            : "Boorlijn hoekig\n\nDe kaart toont weer alle oorspronkelijke knikpunten en rechte segmenten.";
    }

    private static double TraceLengthMeters(IReadOnlyList<TracePointRow> points)
    {
        if (points.Count < 2) return 0;
        var total = 0d;
        for (var index = 1; index < points.Count; index++)
        {
            var previous = points[index - 1];
            var current = points[index];
            total += Math.Sqrt(Math.Pow(current.X - previous.X, 2) + Math.Pow(current.Y - previous.Y, 2));
        }
        return total;
    }
}
