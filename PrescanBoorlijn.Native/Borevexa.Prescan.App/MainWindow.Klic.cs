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

// KLIC (stap 3.3): kabels/leidingen, kruisingen, thema's en documenten.
// Fase 3-opsplitsing (07-07-2026) van MainWindow.xaml.cs; gedrag ongewijzigd.

public partial class MainWindow
{
    private void AddKlicCrossingReportMedia(Panel panel)
    {
        if (_selectedProject is null)
        {
            return;
        }

        var crossings = GetCurrentKlicPlanCrossings();

        panel.Children.Add(CreateReportSubheading("KLIC kruisingen"));
        panel.Children.Add(CreateReportKlicPlanCrossingTable(crossings));
        panel.Children.Add(CreateReportNote(crossings.Count == 0
            ? "Er zijn nog geen KLIC-kruisingen met de boorlijn gevonden. Controleer of KLIC-lagen en themafilters zichtbaar zijn in de blauwe KLIC-zijbalk."
            : "De KLIC-kruisingen worden automatisch bepaald op basis van de vastgelegde boorlijn en de gekoppelde KLIC-lagen. De kaartbeelden staan op de KLIC-kaartbijlage."));
    }

    private void AddKlicThemeToggles(Panel parent)
    {
        if (_selectedProject is null) return;

        _projectFiles = _projects.GetProjectFiles(_selectedProject.Id);
        var klicFiles = _projectFiles.Where(IsKlicProjectFile);
        var themes = BuildProjectMapLayers(klicFiles)
            .SelectMany(layer => layer.FeatureCollection.Features)
            .Select(feature => feature.Properties.TryGetValue("theme", out var theme) ? theme?.ToString() : null)
            .Where(theme => !string.IsNullOrWhiteSpace(theme))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(theme => theme)
            .ToArray();

        foreach (var theme in themes)
        {
            _gisLayerState.EnsureKlicTheme(theme!);

            _gisSidebar.AddLayerToggle(
                parent,
                KlicThemeLabel(theme!),
                theme!,
                "KLIC thema",
                _klicThemeStates[theme!],
                new Thickness(22, 0, 0, 0),
                KlicTheme_OnClick,
                10.5);
        }
    }

    private void AddStepThreeKlicLayerControls()
    {
        if (_selectedProject is null) return;

        _projectFiles = _projects.GetProjectFiles(_selectedProject.Id);
        var uniqueFiles = UniqueProjectFiles(_projectFiles).ToArray();
        SyncProjectLayerStates(uniqueFiles);

        StepThreeImportsPanel.Children.Add(new TextBlock
        {
            Text = "KLIC",
            Foreground = Brush("#071422"),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(5, 0, 5, 8)
        });
        StepThreeImportsPanel.Children.Add(new TextBlock
        {
            Text = "Controleer de KLIC-lagen langs de vastgelegde boorlijn. De boorlijn is hier alleen zichtbaar en kan alleen in 3.1 worden aangepast.",
            Foreground = Brush("#587080"),
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(5, 0, 5, 8)
        });

        AddStepThreeCheckboxToPanel(StepThreeImportsPanel, "KLIC lagen", "klic", new Thickness(5, 0, 0, 0));
        AddStepThreeCheckboxToPanel(StepThreeImportsPanel, "KLIC bufferzone 1 m links/rechts", "klicBuffer", new Thickness(22, 0, 0, 4));
        AddKlicThemeToggles(StepThreeImportsPanel);

        var klicFiles = uniqueFiles
            .Where(IsKlicProjectFile)
            .OrderBy(file => file.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        StepThreeImportsPanel.Children.Add(new Border
        {
            BorderBrush = Brush("#D7E8FA"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(5, 8, 5, 8)
        });

        StepThreeImportsPanel.Children.Add(new TextBlock
        {
            Text = "KLIC bronlagen",
            Foreground = Brush("#587080"),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(5, 0, 5, 6)
        });

        if (klicFiles.Length == 0)
        {
            StepThreeImportsPanel.Children.Add(new TextBlock
            {
                Text = "Geen KLIC-bestanden gekoppeld. Importeer de KLIC-levering in stap 2.1.",
                Foreground = Brush("#8FA6B2"),
                FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(5, 0, 5, 0)
            });
            return;
        }

        foreach (var file in klicFiles)
        {
            var layerId = ProjectFileLayerId(file);
            _gisSidebar.AddLayerToggle(
                StepThreeImportsPanel,
                ProjectFileLayerLabel(file),
                layerId,
                ProjectFileTypeLabel(file),
                IsProjectLayerVisible(layerId),
                new Thickness(22, 0, 0, 0),
                ProjectLayer_OnClick,
                10.5);
        }
    }

    private static string BuildKlicCrossingCableText(KlicPlanCrossing crossing)
    {
        return BuildKlicCrossingCableText(
            crossing.CrossingContent,
            crossing.NetworkContent,
            crossing.DataSummary,
            crossing.ThemeLabel);
    }

    private static string BuildKlicCrossingCableText(string crossingContent, string networkContent, string dataSummary, string themeLabel)
    {
        var items = BuildSimpleKlicCableList(crossingContent, themeLabel);
        if (items.Count == 0) items = BuildSimpleKlicCableList(networkContent, themeLabel);
        if (items.Count == 0) items = BuildSimpleKlicCableList(dataSummary, themeLabel);

        return items.Count == 0
            ? themeLabel
            : string.Join("\n", items.Select(item => $"- {item}"));
    }

    private static string BuildKlicDataSummary(GeoJsonFeature feature, IReadOnlyDictionary<string, object>? details)
    {
        var values = new List<string>();

        void Add(string label, string value, int maxLength = 80)
        {
            value = CleanKlicReportValue(value);
            if (string.IsNullOrWhiteSpace(value)) return;
            var entry = $"{label}: {TruncateText(value, maxLength)}";
            if (!values.Contains(entry, StringComparer.OrdinalIgnoreCase)) values.Add(entry);
        }

        string Find(params string[] fragments)
        {
            foreach (var source in new[] { details, feature.Properties })
            {
                if (source is null) continue;
                foreach (var pair in source)
                {
                    if (fragments.All(fragment => pair.Key.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
                    {
                        var value = pair.Value?.ToString() ?? "";
                        if (!string.IsNullOrWhiteSpace(value)) return value;
                    }
                }
            }

            return "";
        }

        var status = CleanKlicReportValue(Find("currentStatus"));
        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("functional", StringComparison.OrdinalIgnoreCase) && !status.Equals("functioneel", StringComparison.OrdinalIgnoreCase))
        {
            Add("Status", status);
        }

        var fictitious = CleanKlicReportValue(Find("fictitious"));
        if (fictitious.Equals("ja", StringComparison.OrdinalIgnoreCase))
        {
            Add("Ligging", "fictief");
        }

        Add("Sinds", Find("validFrom"));
        Add("Tot", Find("validTo"));

        var reference = FirstNonEmpty(Find("utilityLinkId"), Find("localId"));
        if (!string.IsNullOrWhiteSpace(reference))
        {
            Add("Ref", reference.Split('.').LastOrDefault(part => !string.IsNullOrWhiteSpace(part)) ?? reference, 42);
        }

        return values.Count == 0 ? "-" : TruncateText(string.Join("; ", values), 260);
    }

    private static string BuildKlicEvMeasureText(GeoJsonFeature feature, IReadOnlyDictionary<string, object>? details, string themeLabel)
    {
        var values = new List<string>();

        void Add(string value, int maxLength = 240)
        {
            value = CleanKlicReportValue(value);
            if (string.IsNullOrWhiteSpace(value)) return;
            value = TruncateText(value, maxLength);
            if (!values.Contains(value, StringComparer.OrdinalIgnoreCase)) values.Add(value);
        }

        Add(FindKlicDetailValue(feature, details, ["voorzorg"]));
        Add(FindKlicDetailValue(feature, details, ["eis"]));
        Add(FindKlicDetailValue(feature, details, ["maatregel"]));
        Add(FindKlicDetailValue(feature, details, ["omschrijving"]));
        Add(FindKlicDetailValue(feature, details, ["description"]));
        Add(FindKlicDetailValue(feature, details, ["toelichting"]));
        Add(ReadKlicDetailOrFallback(feature, details, "networkContent", "", 240));

        return values.Count == 0
            ? $"Eisvoorzorgsmaatregel gekoppeld aan {themeLabel}. Raadpleeg de KLIC-levering en stem werkzaamheden af met de netbeheerder."
            : string.Join("; ", values.Take(4));
    }

    private IReadOnlyList<KlicEvZone> BuildKlicEvZones(IReadOnlyList<TracePointRow> traceRows, IReadOnlyList<ProjectMapLayer> layers)
    {
        if (traceRows.Count < 2) return [];
        var traceDistances = BuildTraceDistances(traceRows);
        if (traceDistances.Count < 2 || traceDistances[^1] <= 0) return [];

        var result = new List<KlicEvZone>();
        foreach (var layer in layers.Where(IsKlicLayer))
        {
            if (_projectLayerStates.TryGetValue(layer.Id, out var layerVisible) && !layerVisible)
            {
                continue;
            }

            foreach (var feature in layer.FeatureCollection.Features)
            {
                var details = GetFeatureDetailProperties(feature);
                if (!IsKlicEvZoneFeature(feature, layer, details)) continue;

                var relation = FindKlicEvZoneRelation(feature, traceRows, traceDistances);
                if (relation is null) continue;
                if (!relation.Intersects && !relation.ContainsTrace && relation.Proximity > KlicEvZoneSearchBufferMeters)
                {
                    continue;
                }

                var theme = GetFeatureProperty(feature, "theme");
                if (string.IsNullOrWhiteSpace(theme)) theme = "overig";
                var themeLabel = KlicThemeLabel(theme);
                var index = result.Count + 1;
                result.Add(new KlicEvZone(
                    $"EV{index}",
                    Math.Round(relation.Station, 2),
                    Math.Round(relation.Offset, 2),
                    Math.Round(relation.X, 2),
                    Math.Round(relation.Y, 2),
                    Math.Round(relation.Proximity, 2),
                    themeLabel,
                    theme,
                    KlicThemeColor(theme),
                    layer.Name,
                    feature.Geometry.Type,
                    ReadKlicDetailOrFallback(feature, details, "networkContent", themeLabel, 2000),
                    ResolveKlicNetworkOperator(feature, details),
                    ResolveKlicNetworkContact(feature, details),
                    BuildKlicEvMeasureText(feature, details, themeLabel),
                    BuildKlicDataSummary(feature, details)));
            }
        }

        return result
            .GroupBy(zone => $"{zone.LayerName}|{zone.Theme}|{Math.Round(zone.Distance, 1)}|{zone.NetworkOperator}|{zone.Measure}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(zone => zone.Distance)
            .ThenBy(zone => zone.ProximityMeters)
            .Take(60)
            .Select((zone, index) => zone with { Code = $"EV{index + 1}" })
            .ToList();
    }

    private IReadOnlyList<KlicPlanCrossing> BuildKlicPlanCrossings(IReadOnlyList<TracePointRow> traceRows, IReadOnlyList<ProjectMapLayer> layers)
    {
        if (traceRows.Count < 2) return [];
        var traceDistances = BuildTraceDistances(traceRows);
        if (traceDistances.Count < 2 || traceDistances[^1] <= 0) return [];

        var result = new List<KlicPlanCrossing>();
        foreach (var layer in layers.Where(IsKlicLayer))
        {
            if (_projectLayerStates.TryGetValue(layer.Id, out var layerVisible) && !layerVisible)
            {
                continue;
            }

            foreach (var feature in layer.FeatureCollection.Features)
            {
                var theme = GetFeatureProperty(feature, "theme");
                if (string.IsNullOrWhiteSpace(theme)) theme = "overig";
                if (_klicThemeStates.TryGetValue(theme, out var themeVisible) && !themeVisible)
                {
                    continue;
                }

                var details = GetFeatureDetailProperties(feature);
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
                            if (!TrySegmentIntersection(
                                    traceA.X, traceA.Y, traceB.X, traceB.Y,
                                    klicA.X, klicA.Y, klicB.X, klicB.Y,
                                    out var traceRatio))
                            {
                                continue;
                            }

                            var segmentLength = Math.Max(0.001, traceDistances[traceIndex] - traceDistances[traceIndex - 1]);
                            var station = traceDistances[traceIndex - 1] + segmentLength * traceRatio;
                            var x = traceA.X + (traceB.X - traceA.X) * traceRatio;
                            var y = traceA.Y + (traceB.Y - traceA.Y) * traceRatio;
                            var projected = ProjectPointOnTraceSigned(new RdPoint(x, y), traceRows, traceDistances);
                            var index = result.Count + 1;
                            result.Add(new KlicPlanCrossing(
                                $"K{index}",
                                Math.Round(station, 2),
                                Math.Round(projected.Offset, 2),
                                Math.Round(x, 2),
                                Math.Round(y, 2),
                                KlicThemeLabel(theme),
                                theme,
                                KlicThemeColor(theme),
                                ReadKlicDetailOrFallback(feature, details, "networkContent", KlicThemeLabel(theme), 2000),
                                ResolveKlicNetworkOperator(feature, details),
                                ResolveKlicNetworkContact(feature, details),
                                BuildKlicDataSummary(feature, details),
                                BuildSpecificKlicCrossingContent(feature, details, theme, KlicThemeLabel(theme))));
                        }
                    }
                }
            }
        }

        return result
            .GroupBy(crossing => $"{crossing.Theme}|{Math.Round(crossing.Distance, 1)}|{crossing.NetworkContent}|{crossing.NetworkOperator}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(crossing => crossing.Distance)
            .Take(120)
            .Select((crossing, index) => crossing with { Code = $"K{index + 1}" })
            .ToList();
    }

    private IReadOnlyList<KlicPlanLine> BuildKlicPlanLines(IReadOnlyList<TracePointRow> traceRows, IReadOnlyList<ProjectMapLayer> layers)
    {
        if (traceRows.Count < 2) return [];
        var distances = BuildTraceDistances(traceRows);
        return layers
            .Where(IsKlicLayer)
            .SelectMany(layer => layer.FeatureCollection.Features.SelectMany(feature =>
            {
                var theme = GetFeatureProperty(feature, "theme");
                if (string.IsNullOrWhiteSpace(theme)) theme = "overig";
                if (!FeatureCrossesTrace(feature, traceRows)) return [];
                return EnumerateFeatureGeometryLines(feature).Select(line => new KlicPlanLine(
                    KlicThemeColor(theme),
                    line.Select(point => ProjectPointOnTraceSigned(point, traceRows, distances)).ToList()));
            }))
            .Where(line => line.Points.Count >= 2)
            .ToList();
    }

    private IReadOnlyList<KlicSituationLine> BuildKlicSituationLines(IReadOnlyList<TracePointRow> traceRows, IReadOnlyList<ProjectMapLayer> layers)
    {
        if (traceRows.Count < 2) return [];
        return layers
            .Where(IsKlicLayer)
            .SelectMany(layer => layer.FeatureCollection.Features.SelectMany(feature =>
            {
                if (!FeatureCrossesTrace(feature, traceRows)) return [];
                var theme = GetFeatureProperty(feature, "theme");
                if (string.IsNullOrWhiteSpace(theme)) theme = "overig";
                return EnumerateFeatureGeometryLines(feature)
                    .Select(line => line.Where(IsValidRdPoint).ToList())
                    .Where(line => line.Count >= 2)
                    .Select(line => new KlicSituationLine(KlicThemeColor(theme), KlicThemeLabel(theme), line));
            }))
            .ToList();
    }

    private static IReadOnlyList<string> BuildKlicThemeKeywords(string theme, string themeLabel)
    {
        var text = $"{theme} {themeLabel}".ToLowerInvariant();
        var keywords = new List<string>();
        void Add(string value)
        {
            if (!keywords.Contains(value, StringComparer.OrdinalIgnoreCase)) keywords.Add(value);
        }

        if (text.Contains("gas")) Add("gas");
        if (text.Contains("water")) Add("water");
        if (text.Contains("data") || text.Contains("telecom")) { Add("data"); Add("telecom"); Add("kabel"); }
        if (text.Contains("laagspanning")) { Add("laagspanning"); Add("elektriciteit"); Add("kabel"); }
        if (text.Contains("middenspanning")) { Add("middenspanning"); Add("elektriciteit"); Add("kabel"); }
        if (text.Contains("riool")) Add("riool");
        if (text.Contains("warmte")) Add("warmte");
        if (keywords.Count == 0) Add(themeLabel.Replace("KLIC", "", StringComparison.OrdinalIgnoreCase).Trim());
        return keywords.Where(keyword => !string.IsNullOrWhiteSpace(keyword)).ToList();
    }

    private static IReadOnlyList<string> BuildSimpleKlicCableList(string source, string themeLabel)
    {
        source = Regex.Replace(source ?? "", @"[ \t]+", " ").Trim();
        if (string.IsNullOrWhiteSpace(source)) return [];

        var rawParts = Regex.Split(source, @"\s*(?:\||\r?\n)\s*")
            .Select(CleanKlicCableDescription)
            .Where(IsUsefulKlicCableDescription)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (rawParts.Count == 0)
        {
            var cleaned = CleanKlicCableDescription(source);
            if (IsUsefulKlicCableDescription(cleaned)) rawParts.Add(cleaned);
        }

        var keywords = BuildKlicThemeKeywords("", themeLabel);
        var relevant = rawParts
            .Where(part => keywords.Count == 0 || keywords.Any(keyword => part.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (relevant.Count == 0) relevant = rawParts;

        return relevant
            .Select(part => TruncateText(part, 120))
            .Take(6)
            .ToList();
    }

    private static string BuildSpecificKlicCrossingContent(GeoJsonFeature feature, IReadOnlyDictionary<string, object>? details, string theme, string themeLabel)
    {
        var items = new List<string>();

        void AddItem(string value, int maxLength = 140)
        {
            value = CleanKlicCableDescription(value);
            if (string.IsNullOrWhiteSpace(value)) return;
            if (!IsUsefulKlicCableDescription(value)) return;
            value = TruncateText(value, maxLength);
            if (!items.Contains(value, StringComparer.OrdinalIgnoreCase)) items.Add(value);
        }

        var rawContent = FirstNonEmpty(GetDetailProperty(details, "networkContent"), GetFeatureProperty(feature, "networkContent"));
        foreach (var part in SelectKlicCrossingContentParts(rawContent, theme, themeLabel))
        {
            AddItem(part);
        }

        var composed = ComposeKlicTechnicalDescription(feature, details, themeLabel);
        AddItem(composed);

        var status = FindKlicDetailValue(feature, details, ["currentStatus"], ["status"]);
        status = CleanKlicReportValue(status);
        if (!status.Equals("functional", StringComparison.OrdinalIgnoreCase) &&
            !status.Equals("functioneel", StringComparison.OrdinalIgnoreCase))
        {
            AddItem($"Status {status}", 80);
        }

        var reference = FirstNonEmpty(
            FindKlicDetailValue(feature, details, ["utilityLinkId"]),
            FindKlicDetailValue(feature, details, ["localId"]));
        if (!string.IsNullOrWhiteSpace(reference))
        {
            AddItem($"KLIC-ref {reference.Split('.').LastOrDefault(part => !string.IsNullOrWhiteSpace(part)) ?? reference}", 54);
        }

        return items.Count == 0
            ? themeLabel
            : string.Join("\n", items.Take(6));
    }

    private static string CleanKlicCableDescription(string value)
    {
        value = CleanKlicReportValue(value);
        if (string.IsNullOrWhiteSpace(value)) return "";
        value = RemoveKlicManagerFragments(value);
        value = Regex.Replace(value, @"^(Kruisende\s+leiding/kabel|Leiding/kabel|Kabels?/leidingen|Leidingen|Kabelbed|Netbeheerder)\s*:\s*", "", RegexOptions.IgnoreCase).Trim();
        value = Regex.Replace(value, @"^\d+\s+kabels?/leidingen\s*", "", RegexOptions.IgnoreCase).Trim();
        value = NormalizeKlicTechnicalText(value);
        value = Regex.Replace(value, @"\s+", " ").Trim(' ', '-', ':', ',');
        return value;
    }

    private static string CleanKlicNetworkOperator(string value)
    {
        value = CleanKlicReportValue(value);
        if (string.IsNullOrWhiteSpace(value)) return "";
        if (value.Equals("nl.imkl", StringComparison.OrdinalIgnoreCase)) return "";
        if (value.Equals("imkl", StringComparison.OrdinalIgnoreCase)) return "";
        if (value.Contains("namespace", StringComparison.OrdinalIgnoreCase)) return "";
        if (value.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return "";
        if (value.Contains("IMKLValue", StringComparison.OrdinalIgnoreCase)) return "";
        return value;
    }

    private static string CleanKlicReportValue(string value)
    {
        value = Regex.Replace(value.Trim(), @"\s+", " ");
        if (string.IsNullOrWhiteSpace(value)) return "";
        if (value.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase)) return "";
        if (value.Equals("true", StringComparison.OrdinalIgnoreCase)) return "ja";
        if (value.Equals("false", StringComparison.OrdinalIgnoreCase)) return "nee";
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date))
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        value = value.Split('/', '#').LastOrDefault(part => !string.IsNullOrWhiteSpace(part)) ?? value;
        value = value
            .Replace("ConditionOfFacilityValue", "", StringComparison.OrdinalIgnoreCase)
            .Replace("ConditionOfFacility", "", StringComparison.OrdinalIgnoreCase)
            .Replace("http:", "", StringComparison.OrdinalIgnoreCase)
            .Replace("https:", "", StringComparison.OrdinalIgnoreCase)
            .Trim('_', '-', '/', ' ');
        return value;
    }

    private void ClearKlicCrossingLabelsFromMap()
    {
        if (!_mapLibreLoaded || StepThreeMapView.CoreWebView2 is null) return;
        SendMapMessage(JsonSerializer.Serialize(new { type = "klicCrossingLabels", labels = Array.Empty<object>() }, JsonOptions));
    }

    private static string ComposeKlicTechnicalDescription(GeoJsonFeature feature, IReadOnlyDictionary<string, object>? details, string themeLabel)
    {
        var type = FirstNonEmpty(
            FindKlicDetailValue(feature, details, ["objectType"]),
            themeLabel);
        type = NormalizeKlicTechnicalText(CleanKlicReportValue(type));

        var parts = new List<string>();
        void Add(string value)
        {
            value = NormalizeKlicTechnicalText(CleanKlicReportValue(value));
            if (string.IsNullOrWhiteSpace(value)) return;
            if (!parts.Contains(value, StringComparer.OrdinalIgnoreCase)) parts.Add(value);
        }

        var diameter = FirstNonEmpty(
            FindKlicDetailValue(feature, details, ["pipeDiameter"]),
            FindKlicDetailValue(feature, details, ["diameter"]),
            FindKlicDetailValue(feature, details, ["breedte"]),
            FindKlicDetailValue(feature, details, ["width"]));
        diameter = FormatKlicDiameter(diameter);
        Add(diameter);

        Add(FindKlicDetailValue(feature, details, ["buismateriaalType"]));
        Add(FindKlicDetailValue(feature, details, ["telecommunicationsCableMaterialType"]));
        Add(FindKlicDetailValue(feature, details, ["material"]));
        Add(FindKlicDetailValue(feature, details, ["materiaal"]));

        var voltage = FirstNonEmpty(
            FindKlicDetailValue(feature, details, ["operatingVoltage"]),
            FindKlicDetailValue(feature, details, ["nominalVoltage"]),
            FindKlicDetailValue(feature, details, ["voltage"]),
            FindKlicDetailValue(feature, details, ["spanning"]));
        voltage = FormatKlicVoltage(voltage);
        Add(voltage);

        var pressure = FindKlicDetailValue(feature, details, ["pressure"]);
        pressure = FormatKlicPressure(pressure);
        Add(pressure);

        Add(FindKlicDetailValue(feature, details, ["waterType"]));
        Add(FindKlicDetailValue(feature, details, ["sewerWaterType"]));
        Add(FindKlicDetailValue(feature, details, ["oilGasChemicalsProductType"]));
        Add(FindKlicDetailValue(feature, details, ["label"]));

        return parts.Count == 0
            ? ""
            : $"{type}: {string.Join(", ", parts.Take(5))}";
    }

    private static bool ContainsKlicManagerInfo(string value) =>
        value.Contains("beheerder", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("beheerobject", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("LEI-PERS", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("email", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("e-mail", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("telefoon", StringComparison.OrdinalIgnoreCase) ||
        value.Contains('@');

    private void DrawKlicOverlay()
    {
        AddPolyline("#7B00AA", 3, [42, 258, 188, 230, 344, 200, 540, 168, 720, 140]);
        AddPolyline("#00CCFF", 3, [28, 310, 196, 280, 356, 242, 552, 226, 734, 206]);
        AddPolyline("#000080", 3, [72, 112, 228, 132, 410, 138, 610, 120, 742, 128]);
        AddPolyline("#FFFF00", 4, [115, 72, 250, 116, 408, 172, 560, 224, 716, 280]);
        AddPolyline("#00AA44", 3, [32, 376, 170, 340, 320, 326, 492, 350, 690, 334]);
        AddText("KLIC LS", 356, 188, "#7B00AA", 10, FontWeights.SemiBold);
        AddText("KLIC water", 602, 104, "#000080", 10, FontWeights.SemiBold);
    }

    private void EnsureStepThreeKlicMapDefaults()
    {
        _mapOverlayStates["baseMap"] = true;
        _mapOverlayStates["parcels"] = true;
        _mapOverlayStates["boreTrace"] = true;
        _mapOverlayStates["buildings"] = false;
        _mapOverlayStates["addresses"] = false;
        _mapOverlayStates["bgt"] = false;
        _mapOverlayStates["bagImport"] = false;
        _mapOverlayStates["designImport"] = false;
        _mapOverlayStates["customImport"] = false;

        if (!_stepThreeKlicDefaultsApplied)
        {
            _mapOverlayStates["klic"] = true;
            _mapOverlayStates["klicBuffer"] = true;
            _stepThreeKlicDefaultsApplied = true;
        }
    }

    private static string FindKlicDetailValue(GeoJsonFeature feature, IReadOnlyDictionary<string, object>? details, params string[][] keyFragments)
    {
        foreach (var source in new[] { details, feature.Properties })
        {
            if (source is null) continue;
            foreach (var fragments in keyFragments)
            {
                var pair = source.FirstOrDefault(item => fragments.All(fragment => item.Key.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
                var value = pair.Value?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
        }

        return "";
    }

    private KlicEvZoneRelation? FindKlicEvZoneRelation(GeoJsonFeature feature, IReadOnlyList<TracePointRow> traceRows, IReadOnlyList<double> traceDistances)
    {
        KlicEvZoneRelation? best = null;

        void Consider(double station, double offset, double x, double y, double proximity, bool intersects, bool containsTrace)
        {
            proximity = Math.Abs(proximity);
            if (best is not null && proximity >= best.Proximity) return;
            best = new KlicEvZoneRelation(station, offset, x, y, proximity, intersects, containsTrace);
        }

        foreach (var rdLine in EnumerateFeatureGeometryLines(feature).Select(line => line.Where(IsValidRdPoint).ToList()))
        {
            if (rdLine.Count == 1)
            {
                var projected = ProjectPointOnTraceSigned(rdLine[0], traceRows, traceDistances);
                Consider(projected.Station, projected.Offset, rdLine[0].X, rdLine[0].Y, projected.Offset, intersects: false, containsTrace: false);
                continue;
            }

            if (rdLine.Count < 2) continue;

            if (IsClosedRing(rdLine))
            {
                foreach (var sample in EnumerateTraceSamplePoints(traceRows))
                {
                    if (!PointInRing(sample, rdLine)) continue;
                    var projected = ProjectPointOnTraceSigned(sample, traceRows, traceDistances);
                    Consider(projected.Station, 0, sample.X, sample.Y, 0, intersects: true, containsTrace: true);
                    break;
                }
            }

            for (var klicIndex = 1; klicIndex < rdLine.Count; klicIndex++)
            {
                var klicA = rdLine[klicIndex - 1];
                var klicB = rdLine[klicIndex];
                for (var traceIndex = 1; traceIndex < traceRows.Count; traceIndex++)
                {
                    var traceA = traceRows[traceIndex - 1];
                    var traceB = traceRows[traceIndex];
                    var segmentLength = Math.Max(0.001, traceDistances[traceIndex] - traceDistances[traceIndex - 1]);
                    if (TrySegmentIntersection(
                            traceA.X, traceA.Y, traceB.X, traceB.Y,
                            klicA.X, klicA.Y, klicB.X, klicB.Y,
                            out var traceRatio))
                    {
                        var station = traceDistances[traceIndex - 1] + segmentLength * traceRatio;
                        var x = traceA.X + (traceB.X - traceA.X) * traceRatio;
                        var y = traceA.Y + (traceB.Y - traceA.Y) * traceRatio;
                        Consider(station, 0, x, y, 0, intersects: true, containsTrace: false);
                        continue;
                    }

                    var closest = ClosestPointBetweenSegments(traceA, traceB, klicA, klicB);
                    var closestStation = traceDistances[traceIndex - 1] + segmentLength * closest.TraceRatio;
                    var closestX = traceA.X + (traceB.X - traceA.X) * closest.TraceRatio;
                    var closestY = traceA.Y + (traceB.Y - traceA.Y) * closest.TraceRatio;
                    Consider(closestStation, closest.Offset, closestX, closestY, closest.Offset, intersects: false, containsTrace: false);
                }
            }
        }

        return best;
    }

    private static string FormatKlicDiameter(string value)
    {
        value = CleanKlicReportValue(value);
        if (string.IsNullOrWhiteSpace(value) || value.Equals("0", StringComparison.OrdinalIgnoreCase)) return "";
        value = NormalizeKlicTechnicalText(value);
        if (value.Contains("\u00D8", StringComparison.Ordinal) || value.Contains("DN", StringComparison.OrdinalIgnoreCase)) return value;
        return Regex.IsMatch(value, @"^\d+([\,\.]\d+)?$")
            ? $"\u00D8{value}"
            : value;
    }

    private static string FormatKlicPressure(string value)
    {
        value = CleanKlicReportValue(value);
        if (string.IsNullOrWhiteSpace(value) || value.Equals("0", StringComparison.OrdinalIgnoreCase)) return "";
        return Regex.IsMatch(value, @"^\d+([\,\.]\d+)?$")
            ? $"{value} bar"
            : NormalizeKlicTechnicalText(value);
    }

    private static string FormatKlicVoltage(string value)
    {
        value = CleanKlicReportValue(value);
        if (string.IsNullOrWhiteSpace(value) || value.Equals("0", StringComparison.OrdinalIgnoreCase)) return "";
        return Regex.IsMatch(value, @"^\d+([\,\.]\d+)?$")
            ? $"{value} V"
            : NormalizeKlicTechnicalText(value);
    }

    private IReadOnlyList<KlicPlanCrossing> GetCurrentKlicPlanCrossings()
    {
        if (_selectedProject is null) return [];
        var traceRows = GetTraceRowsForProfile();
        if (traceRows.Count < 2) return [];

        _projectFiles = _projects.GetProjectFiles(_selectedProject.Id);
        var layers = BuildProjectMapLayers(_projectFiles);
        SyncProjectLayerStates(_projectFiles);
        SyncKlicThemeStates(layers);
        return BuildKlicPlanCrossings(traceRows, layers);
    }

    private static bool IsKlicEvZoneFeature(GeoJsonFeature feature, ProjectMapLayer layer, IReadOnlyDictionary<string, object>? details)
    {
        var featureText = string.Join(" ", feature.Properties.Select(item => $"{item.Key} {item.Value}"));
        var detailText = details is null ? "" : string.Join(" ", details.Select(item => $"{item.Key} {item.Value}"));
        var haystack = $"{layer.Type} {layer.Name} {feature.Geometry.Type} {featureText} {detailText}";
        return ContainsAny(
            haystack,
            "AanduidingEisVoorzorgsmaatregel",
            "EisVoorzorgsmaatregel",
            "eis voorzorgsmaatregel",
            "eis-voorzorgsmaatregel",
            "Voorzorgsmaatregel",
            "voorzorgsmaatregel",
            "voorzorgsmaatregelen",
            "EV zone",
            "EV-zone");
    }

    private static bool IsKlicLayer(ProjectMapLayer layer) =>
        layer.Type.Contains("KLIC", StringComparison.OrdinalIgnoreCase) ||
        layer.Name.Contains("KLIC", StringComparison.OrdinalIgnoreCase);

    private static bool IsKlicProjectFile(ProjectFileRecord file)
    {
        if (!IsFilterableProjectFile(file)) return false;
        if (file.FileType.Equals("KLIC", StringComparison.OrdinalIgnoreCase)) return true;
        var name = string.IsNullOrWhiteSpace(file.DisplayName)
            ? System.IO.Path.GetFileName(file.LocalPath)
            : file.DisplayName;
        return name.Contains("KLIC", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsKlicReportMapSubstep(int stepNumber, string substepNumber) =>
        stepNumber == 3 && string.Equals(substepNumber, "3.2", StringComparison.OrdinalIgnoreCase);

    private bool IsSelectedStepThreeKlicSubstep()
    {
        if (_selectedStep?.Number != 3) return false;
        var substepNumber = _selectedSubstep?.Number;
        return string.Equals(substepNumber, "3.2", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(substepNumber, "3.3", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUsefulKlicCableDescription(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.Equals("onbekend", StringComparison.OrdinalIgnoreCase)) return false;
        if (value.Contains("extra attribuutregel", StringComparison.OrdinalIgnoreCase)) return false;
        if (ContainsKlicManagerInfo(value)) return false;
        if (value.StartsWith("Netbeheerder", StringComparison.OrdinalIgnoreCase)) return false;
        if (value.StartsWith("Status", StringComparison.OrdinalIgnoreCase)) return false;
        if (value.StartsWith("KLIC-ref", StringComparison.OrdinalIgnoreCase)) return false;
        if (value.StartsWith("Ref", StringComparison.OrdinalIgnoreCase)) return false;
        if (value.Length < 3) return false;

        return value.Contains("kabel", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("leiding", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("mantelbuis", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("pijpleiding", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("riool", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("gas", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("water", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("elektriciteit", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("PVC", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("PE", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("HDPE", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("staal", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("bar", StringComparison.OrdinalIgnoreCase) ||
               value.Contains(" volt", StringComparison.OrdinalIgnoreCase) ||
               Regex.IsMatch(value, @"\b\d+(\,\d+|\.\d+)?\s?V\b", RegexOptions.IgnoreCase) ||
               Regex.IsMatch(value, @"\b\d+(\,\d+|\.\d+)?\s?mm\b", RegexOptions.IgnoreCase) ||
               value.Contains("DN", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("\u00D8", StringComparison.Ordinal);
    }

    private sealed record KlicContactRow(string Code, string NetworkOperator, string Theme, string Contact, string Phone, string Email, string FaultPhone, string Source);

    private sealed record KlicEvZone(string Code, double Distance, double Offset, double X, double Y, double ProximityMeters, string ThemeLabel, string Theme, string Color, string LayerName, string GeometryType, string NetworkContent, string NetworkOperator, string NetworkContact, string Measure, string DataSummary);

    private sealed record KlicEvZoneRelation(double Station, double Offset, double X, double Y, double Proximity, bool Intersects, bool ContainsTrace);

    private sealed record KlicPlanCrossing(string Code, double Distance, double Offset, double X, double Y, string ThemeLabel, string Theme, string Color, string NetworkContent, string NetworkOperator, string NetworkContact, string DataSummary, string CrossingContent);

    private sealed record KlicPlanLine(string Color, IReadOnlyList<KlicPlanPoint> Points);

    private sealed record KlicPlanPoint(double Station, double Offset);

    private sealed record KlicSituationLine(string Color, string Label, IReadOnlyList<RdPoint> Points);

    private static double KlicThemeDepth(string? theme) => theme switch
    {
        "laagspanning" => 0.8,
        "middenspanning" => 1.0,
        "hoogspanning" => 1.2,
        "gasLageDruk" => 0.9,
        "gasHogeDruk" => 1.1,
        "water" => 1.3,
        "datatransport" => 0.7,
        "rioolVrijverval" => 1.8,
        "rioolOnderOverOfOnderdruk" => 1.6,
        "warmte" => 1.1,
        _ => 1.4
    };

    private static string NormalizeKlicTechnicalText(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        value = Regex.Replace(value, @"\bOlie\s+Gas\s+Chemicalien\s+Pijpleiding\b", "Gasleiding", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\bTelecommunicatiekabel\b", "Telecomkabel", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\bElektriciteitskabel\b", "Elektriciteitskabel", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\bpolyethylene\b", "PE", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\bpolyvinylchloride\b", "PVC", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\bnaturalGas\b", "aardgas", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\boperatingVoltage\b", "spanning", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\bnominalVoltage\b", "spanning", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\bpipeDiameter\b", "diameter", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\s*,\s*", ", ");
        return Regex.Replace(value, @"\s+", " ").Trim();
    }

    private static string ReadKlicDetailOrFallback(GeoJsonFeature feature, IReadOnlyDictionary<string, object>? details, string key, string fallback, int maxLength = 80)
    {
        var value = GetDetailProperty(details, key);
        if (!string.IsNullOrWhiteSpace(value)) return TruncateText(value, maxLength);
        value = GetFeatureProperty(feature, key);
        return string.IsNullOrWhiteSpace(value) ? fallback : TruncateText(value, maxLength);
    }

    private static string RemoveKlicManagerFragments(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";

        foreach (var marker in new[] { "Beheerobjectomschrijving", "Beheerder", "Netbeheerder", "E-mail", "Email", "Telefoon", "LEI-PERS" })
        {
            var index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index > 0)
            {
                value = value[..index];
            }
        }

        var parts = Regex.Split(value, @"\s*(?:;|,)\s*")
            .Where(part => !ContainsKlicManagerInfo(part))
            .Select(part => part.Trim())
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToList();

        return parts.Count == 0 ? value.Trim() : string.Join(", ", parts);
    }

    private void RenderKlicDocumentsPanel()
    {
        if (_selectedProject is null) return;

        _projectFiles = _projects.GetProjectFiles(_selectedProject.Id);
        var docs = BuildProjectDocumentEntries(_projectFiles).ToList();

        KlicDocumentsPanel.Children.Add(new TextBlock
        {
            Text = "KLIC / projectbestanden",
            Foreground = Brush("#071422"),
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 6)
        });

        if (docs.Count == 0)
        {
            KlicDocumentsPanel.Children.Add(new TextBlock
            {
                Text = "Geen documenten gevonden in de gekoppelde bestanden.",
                Foreground = Brush("#8FA6B2"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        foreach (var doc in docs.Take(80))
        {
            var button = new Button
            {
                Tag = doc,
                Height = 48,
                Background = Brush("#F8FAFB"),
                BorderBrush = Brush("#DEE6EA"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(7, 4, 7, 4),
                Margin = new Thickness(0, 0, 0, 5),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = doc.Name, Foreground = Brush("#071422"), FontSize = 10.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                        new TextBlock { Text = $"{doc.Type} · {doc.SizeKb:N0} KB", Foreground = Brush("#8FA6B2"), FontSize = 10 }
                    }
                }
            };
            button.Click += DocumentEntry_OnClick;
            KlicDocumentsPanel.Children.Add(button);
        }
    }

    private static string ResolveKlicNetworkContact(GeoJsonFeature feature, IReadOnlyDictionary<string, object>? details)
    {
        var explicitValue = ReadKlicDetailOrFallback(feature, details, "netbeheerderContact", "", 240);
        if (!string.IsNullOrWhiteSpace(explicitValue)) return explicitValue;

        var value = FindKlicDetailValue(feature, details,
            ["contact"],
            ["telefoon"],
            ["phone"],
            ["email"],
            ["mail"]);
        value = CleanKlicReportValue(value);
        return string.IsNullOrWhiteSpace(value) ? "-" : TruncateText(value, 240);
    }

    private static string ResolveKlicNetworkOperator(GeoJsonFeature feature, IReadOnlyDictionary<string, object>? details)
    {
        var explicitValue = ReadKlicDetailOrFallback(feature, details, "netbeheerderName", "", 240);
        explicitValue = CleanKlicNetworkOperator(explicitValue);
        if (!string.IsNullOrWhiteSpace(explicitValue)) return explicitValue;

        var value = FindKlicDetailValue(feature, details,
            ["netbeheerder", "naam"],
            ["beheerder", "naam"],
            ["netbeheerder"],
            ["eigenaar"],
            ["owner"],
            ["operator"],
            ["organisation"],
            ["organisatie"]);
        value = CleanKlicNetworkOperator(value);
        return string.IsNullOrWhiteSpace(value) ? "-" : TruncateText(value, 240);
    }

    private static IReadOnlyList<string> SelectKlicCrossingContentParts(string rawContent, string theme, string themeLabel)
    {
        rawContent = Regex.Replace(rawContent ?? "", @"\s+", " ").Trim();
        if (string.IsNullOrWhiteSpace(rawContent)) return [];

        var parts = Regex.Split(rawContent, @"\s*(?:\||;|\r?\n)\s*")
            .Select(part => Regex.Replace(part.Trim(), @"\s+", " "))
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (parts.Count <= 2 && rawContent.Length <= 220)
        {
            return parts.Count == 0 ? [rawContent] : parts;
        }

        var keywords = BuildKlicThemeKeywords(theme, themeLabel);
        var selected = parts
            .Where(part => keywords.Any(keyword => part.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .Take(3)
            .ToList();
        if (selected.Count == 0)
        {
            selected = parts.Take(2).ToList();
        }

        return selected;
    }

    private void SendKlicCrossingLabelsToMap()
    {
        if (_selectedProject is null || !_mapLibreLoaded || StepThreeMapView.CoreWebView2 is null) return;
        var labels = GetCurrentKlicPlanCrossings()
            .Select(crossing =>
            {
                var wgs = RdToWgs84(crossing.X, crossing.Y);
                return new
                {
                    code = crossing.Code,
                    lon = wgs[0],
                    lat = wgs[1],
                    color = crossing.Color
                };
            })
            .Where(label => double.IsFinite(label.lon) && double.IsFinite(label.lat))
            .ToList();

        SendMapMessage(JsonSerializer.Serialize(new { type = "klicCrossingLabels", labels }, JsonOptions));
    }
}
