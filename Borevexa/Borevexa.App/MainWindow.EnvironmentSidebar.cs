using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using Borevexa.App.Models;

namespace Borevexa.App;

public partial class MainWindow
{
    private void AddEnvironmentAnalysisResults(Panel parent)
    {
        if (_selectedProject is null)
        {
            parent.Children.Add(CreateEnvironmentMessage("Geen project actief."));
            return;
        }

        var analysis = BuildParcelOwnerAnalysis();
        var total = analysis.TraceLength > 0 ? analysis.TraceLength : Math.Max(1, _selectedProject.BoreLengthMeters);
        var segments = analysis.Segments;
        var bgtImports = _projectFiles.Count(file => file.FileType.Contains("BGT", StringComparison.OrdinalIgnoreCase));
        var kadasterImports = _projectFiles.Count(file => file.FileType.Contains("BAG", StringComparison.OrdinalIgnoreCase) || file.FileType.Contains("Kadaster", StringComparison.OrdinalIgnoreCase));

        var summary = new StackPanel();
        summary.Children.Add(new TextBlock { Text = "Analyse resultaat", Foreground = Brush("#071422"), FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });
        summary.Children.Add(new TextBlock
        {
            Text = $"Boorlijn: {total:N1} m\nKadaster/BAG imports: {kadasterImports}\nBGT imports: {bgtImports}\nSegmenten: {segments.Count}",
            Foreground = Brush("#334155"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap
        });
        summary.Children.Add(new TextBlock
        {
            Text = "Bronhouder is beheerder/aanleverende partij BGT, niet automatisch eigenaar of ZRO. ZRO blijft handmatige controle.",
            Foreground = Brush("#7F99AC"),
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        });
        parent.Children.Add(new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("#D7E8FA"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 10, 0, 0),
            Child = summary
        });

        if (segments.Count == 0)
        {
            parent.Children.Add(CreateEnvironmentMessage("Geen perceelsegmenten gevonden. Controleer of de Kadaster/BAG-zip met kadastralekaart_perceel.gml en een opgeslagen boorlijn aanwezig zijn."));
            return;
        }

        parent.Children.Add(new TextBlock
        {
            Text = "Segmenten per perceel",
            Foreground = Brush("#315B7E"),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 12, 0, 6)
        });

        foreach (var segment in segments.Take(20))
        {
            var selected = EnvironmentSegmentKey(segment).Equals(_selectedEnvironmentSegmentKey, StringComparison.OrdinalIgnoreCase);
            var body = new StackPanel();
            body.Children.Add(new TextBlock
            {
                Text = $"{segment.Start:N1} - {segment.End:N1} m  ({segment.Length:N1} m)",
                Foreground = Brush("#071422"),
                FontSize = 12,
                FontWeight = FontWeights.Bold
            });
            body.Children.Add(new TextBlock
            {
                Text = $"Perceel: {segment.CadastralMunicipality} {segment.Section} {segment.ParcelNumber}\nObject ID: {segment.CadastralObjectId}\nBronhouder: {segment.BgtHolderCode} · {segment.BgtHolderCategory} · {segment.BgtHolderName}\nZRO: {segment.ZroStatus}",
                Foreground = Brush("#334155"),
                FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });
            var card = new Border
            {
                Background = selected ? Brush("#FFF7ED") : Brush("#FBFCFD"),
                BorderBrush = selected ? Brush("#F97316") : Brush("#DEE6EA"),
                BorderThickness = selected ? new Thickness(2) : new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(9),
                Margin = new Thickness(0, 0, 0, 7),
                Child = body,
                Cursor = Cursors.Hand,
                Tag = segment,
                ToolTip = "Klik om dit perceel op de kaart te markeren"
            };
            card.MouseLeftButtonUp += EnvironmentSegment_OnClick;
            parent.Children.Add(card);
        }
    }

    private void EnvironmentSegment_OnClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ParcelOwnerSegment segment }) return;

        _selectedEnvironmentSegmentKey = EnvironmentSegmentKey(segment);
        RenderEnvironmentAnalysisSidebarPanel(showResults: true);
        HighlightEnvironmentSegment(segment);
        e.Handled = true;
    }

    private void HighlightEnvironmentSegment(ParcelOwnerSegment segment)
    {
        var features = new List<GeoJsonFeature>();
        if (segment.Parcel is not null)
        {
            features.Add(new GeoJsonFeature(
                CadastralParcelToGeoJsonGeometry(segment.Parcel),
                new Dictionary<string, object>
                {
                    ["highlightKind"] = "parcel",
                    ["color"] = "#F97316"
                }));
        }
        else
        {
            var parcelFeature = FindCadastralFeature(segment);
            if (parcelFeature is not null)
            {
                var properties = new Dictionary<string, object>(parcelFeature.Properties, StringComparer.OrdinalIgnoreCase)
                {
                    ["highlightKind"] = "parcel",
                    ["color"] = "#F97316"
                };
                features.Add(new GeoJsonFeature(parcelFeature.Geometry, properties));
            }
        }

        var traceSegmentFeatures = BuildEnvironmentTraceSegmentFeatures(segment);
        foreach (var traceSegment in traceSegmentFeatures)
        {
            features.Add(traceSegment);
        }

        if (features.Count == 0)
        {
            OutputText.Text = "Geen geometrie gevonden om dit perceel te markeren.";
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            type = "environmentHighlight",
            label = $"{segment.Start:N1} - {segment.End:N1} m",
            geojson = new GeoJsonFeatureCollection(features)
        }, JsonOptions);
        SendMapMessage(payload);
        OutputText.Text = $"Perceel gemarkeerd: {segment.CadastralMunicipality} {segment.Section} {segment.ParcelNumber}\nSegment {segment.Start:N1} - {segment.End:N1} m.";
    }

    private GeoJsonFeature? FindCadastralFeature(ParcelOwnerSegment segment)
    {
        _projectFiles = _selectedProject is null ? _projectFiles : _projects.GetProjectFiles(_selectedProject.Id);
        var targetId = NormalizeFeatureKey(segment.CadastralObjectId);
        return BuildProjectMapLayers(_projectFiles)
            .Where(IsBagOrKadasterLayer)
            .SelectMany(layer => layer.FeatureCollection.Features)
            .Where(IsCadastralParcelFeature)
            .FirstOrDefault(feature =>
            {
                if (!string.IsNullOrWhiteSpace(targetId) && targetId != "-")
                {
                    var featureId = NormalizeFeatureKey(FirstFeatureString(feature, "Identificatie", "objectId", "kadaster.identificatie"));
                    if (featureId.Equals(targetId, StringComparison.OrdinalIgnoreCase)) return true;
                }

                return GetFeatureString(feature, "Kadastrale gemeente", "-").Equals(segment.CadastralMunicipality, StringComparison.OrdinalIgnoreCase) &&
                       GetFeatureString(feature, "Sectie", "-").Equals(segment.Section, StringComparison.OrdinalIgnoreCase) &&
                       GetFeatureString(feature, "Perceelnummer", "-").Equals(segment.ParcelNumber, StringComparison.OrdinalIgnoreCase);
            });
    }

    private static GeoJsonGeometry CadastralParcelToGeoJsonGeometry(CadastralParcelPolygon parcel)
    {
        var rings = new List<object>
        {
            parcel.Ring.Select(point => RdToWgs84(point.X, point.Y)).ToList()
        };
        foreach (var hole in parcel.Holes)
        {
            rings.Add(hole.Select(point => RdToWgs84(point.X, point.Y)).ToList());
        }

        return new GeoJsonGeometry("Polygon", rings);
    }

    private IReadOnlyList<GeoJsonFeature> BuildEnvironmentTraceSegmentFeatures(ParcelOwnerSegment segment)
    {
        if (segment.TracePath.Count >= 2)
        {
            var segmentCoordinates = segment.TracePath.Select(point => RdToWgs84(point.X, point.Y)).ToList();
            var midPoint = segment.TracePath[Math.Clamp(segment.TracePath.Count / 2, 0, segment.TracePath.Count - 1)];
            return
            [
                new GeoJsonFeature(
                    new GeoJsonGeometry("LineString", segmentCoordinates),
                    new Dictionary<string, object>
                    {
                        ["highlightKind"] = "traceSegment",
                        ["color"] = "#E11D48"
                    }),
                new GeoJsonFeature(
                    new GeoJsonGeometry("Point", RdToWgs84(midPoint.X, midPoint.Y)),
                    new Dictionary<string, object>
                    {
                        ["highlightKind"] = "traceMidpoint",
                        ["label"] = $"{segment.Start:N1} - {segment.End:N1} m",
                        ["color"] = "#F97316"
                    })
            ];
        }

        var traceRows = GetTraceRowsForProfile();
        if (traceRows.Count < 2) return [];

        var traceDistances = BuildTraceDistances(traceRows);
        if (traceDistances.Count < 2 || traceDistances[^1] <= 0) return [];

        var traceTotal = traceDistances[^1];
        var start = Math.Clamp(segment.Start, 0, traceTotal);
        var end = Math.Clamp(segment.End, 0, traceTotal);
        if (end <= start) return [];

        var coordinates = new List<double[]>
        {
            RdToWgs84Point(InterpolateTracePoint(traceRows, traceDistances, start))
        };
        for (var i = 1; i < traceRows.Count - 1; i++)
        {
            if (traceDistances[i] > start && traceDistances[i] < end)
            {
                coordinates.Add(RdToWgs84(traceRows[i].X, traceRows[i].Y));
            }
        }
        coordinates.Add(RdToWgs84Point(InterpolateTracePoint(traceRows, traceDistances, end)));

        var mid = InterpolateTracePoint(traceRows, traceDistances, (start + end) / 2);
        return
        [
            new GeoJsonFeature(
                new GeoJsonGeometry("LineString", coordinates),
                new Dictionary<string, object>
                {
                    ["highlightKind"] = "traceSegment",
                    ["color"] = "#E11D48",
                    ["label"] = $"{segment.Start:N1} - {segment.End:N1} m"
                }),
            new GeoJsonFeature(
                new GeoJsonGeometry("Point", RdToWgs84Point(mid)),
                new Dictionary<string, object>
                {
                    ["highlightKind"] = "segmentMarker",
                    ["color"] = "#E11D48",
                    ["label"] = $"{segment.Start:N1} - {segment.End:N1} m"
                })
        ];
    }

    private static double[] RdToWgs84Point(TracePointRow point) => RdToWgs84(point.X, point.Y);

    private static string EnvironmentSegmentKey(ParcelOwnerSegment segment) =>
        $"{segment.Start:F2}|{segment.End:F2}|{NormalizeFeatureKey(segment.CadastralObjectId)}|{segment.BgtHolderCode}";

    private static string NormalizeFeatureKey(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : Regex.Replace(value.Trim(), @"\s+", "").ToUpperInvariant();

    private static Border CreateEnvironmentMessage(string text) => new()
    {
        Background = Brushes.White,
        BorderBrush = Brush("#D7E8FA"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(10),
        Margin = new Thickness(0, 10, 0, 0),
        Child = new TextBlock
        {
            Text = text,
            Foreground = Brush("#587080"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap
        }
    };

}
