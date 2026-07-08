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

// Ondergrondanalyse (stap 6): BRO/DINOloket-profielen, DGM/REGIS/GeoTop-modellen,
// WMS-overlays, sonderingen en de bijbehorende panelen en rapportpagina's.
// Fase 3-opsplitsing (07-07-2026) van MainWindow.xaml.cs; gedrag ongewijzigd.

public partial class MainWindow
{
    private sealed record BroCptSearchBounds(double MinLat, double MinLon, double MaxLat, double MaxLon);

    private static string BroImportedProfilesDataKey(string modelType) =>
        $"{BroImportedProfilesDataKeyPrefix}{NormalizeBroModelType(modelType).ToLowerInvariant()}";

    private static string BroModelIntervalColor(string label)
    {
        var palette = new[] { "#FDE047", "#FDBA74", "#86EFAC", "#7DD3FC", "#C4B5FD", "#F9A8D4", "#A7F3D0", "#CBD5E1" };
        var hash = Math.Abs((label ?? "").ToLowerInvariant().GetHashCode());
        return palette[hash % palette.Length];
    }

    private static string BroModelLoadAction(string modelType) =>
        NormalizeBroModelType(modelType) switch
        {
            BroRegisModelType => "BRO REGIS II laden",
            BroGeomorphologyModelType => "BRO Geomorfologie kaartlaag tonen",
            BroSoilMapModelType => "BRO Bodemkaart kaartlaag tonen",
            BroGroundwaterGhgModelType => "BRO GHG kaartlaag tonen",
            BroGroundwaterGlgModelType => "BRO GLG kaartlaag tonen",
            BroGroundwaterGvgModelType => "BRO GVG kaartlaag tonen",
            BroGroundwaterGtModelType => "BRO Grondwatertrappen kaartlaag tonen",
            BroGroundwaterDocumentationModelType => "BRO Modeldocumentatie kaartlaag tonen",
            _ => "BRO DGM laden"
        };

    private static string BroProfileFill(BroProfileInterval interval)
    {
        if (IsReportHexColor(interval.Color)) return interval.Color;
        var text = $"{interval.Label} {interval.Lithology} {interval.Code}".ToLowerInvariant();
        if (text.Contains("klei") || text.Contains("clay")) return "#15803D";
        if (text.Contains("zand") || text.Contains("sand")) return "#FDE047";
        if (text.Contains("veen") || text.Contains("peat")) return "#A0523F";
        if (text.Contains("grind") || text.Contains("gravel")) return "#94A3B8";
        if (text.Contains("leem") || text.Contains("loam")) return "#C4B5FD";
        return "#CBD5E1";
    }

    private sealed record BroProfileInterval(double TopDepth, double BottomDepth, string Code, string Label, string Lithology, string Color);

    private sealed record BroSoundingLoadResult(IReadOnlyList<BroSoundingPoint> Soundings, int RemoteCount, int LocalCount, string Status);

    private sealed record BroSoundingPoint(string Id, string Code, string Name, double X, double Y, double Lon, double Lat, double Distance, double Offset, double? SurfaceNap, double? EndDepth, string SoilSummary, string Source, string Date, string Status, string ModelType, string ModelName, IReadOnlyList<BroProfileInterval> ProfileIntervals);

    private static IReadOnlyList<(string Color, string Label)> BroWmsCompactLegendRows(string modelType) =>
        NormalizeBroModelType(modelType) switch
        {
            BroGeomorphologyModelType =>
            [
                ("#F012A6", "Dijk / kunstmatige vorm"),
                ("#CFFAFE", "Water"),
                ("#F6C76F", "Dekzand-, duin- of strandwalvorm"),
                ("#78C679", "Rivier-, beekdal- of laagtevorm"),
                ("#D7DDE2", "Bebouwing / overige kaartcontext")
            ],
            BroSoilMapModelType =>
            [
                ("#F87171", "Zandgronden"),
                ("#2563EB", "Klei-, veen- of natte gronden"),
                ("#22C55E", "Beekdal- en groenere bodemeenheden"),
                ("#92400E", "Bodemkundig aandachtsgebied"),
                ("#F8FAFC", "Niet gekarteerd / overige kaartcontext")
            ],
            BroGroundwaterGhgModelType or BroGroundwaterGlgModelType or BroGroundwaterGvgModelType =>
            [
                ("#440154", "0-20 cm onder maaiveld"),
                ("#3B528B", "20-40 cm onder maaiveld"),
                ("#21918C", "40-80 cm onder maaiveld"),
                ("#5EC962", "80-120 cm onder maaiveld"),
                ("#FDE725", "> 120 cm onder maaiveld")
            ],
            BroGroundwaterGtModelType =>
            [
                ("#2C7BB6", "Natte grondwatertrap"),
                ("#ABD9E9", "Matig nat"),
                ("#FFFFBF", "Gemiddeld"),
                ("#FDAE61", "Droog"),
                ("#D7191C", "Zeer droog")
            ],
            BroGroundwaterDocumentationModelType =>
            [
                ("#2563EB", "Modelgebied / documentatievlak"),
                ("#94A3B8", "Beperkte of geen modeldekking"),
                ("#E2E8F0", "Overige kaartcontext")
            ],
            _ => []
        };

    private static string BroWmsFeatureDetails(string modelType, JsonElement properties)
    {
        var normalized = NormalizeBroModelType(modelType);
        if (normalized == BroSoilMapModelType)
        {
            return JoinNonEmpty(
                BroWmsPropertyWithLabel(properties, "Bodemcode", "soilcode", "code"),
                BroWmsPropertyWithLabel(properties, "Grondsoort", "first_soilname", "soil_group"),
                BroWmsPropertyWithLabel(properties, "Helling", "soilslope", "slope"));
        }

        if (normalized == BroGeomorphologyModelType)
        {
            return JoinNonEmpty(
                BroWmsPropertyWithLabel(properties, "Genese", "genese_description"),
                BroWmsPropertyWithLabel(properties, "Relief", "relief_subklasse"),
                BroWmsPropertyWithLabel(properties, "Code", "landform_subgroup_code", "code"));
        }

        return BroWmsPropertyWithLabel(properties, "Bronwaarde", "value_list", "value", "class", "klasse", "gridcode");
    }

    private sealed record BroWmsFeatureInfoRequest(string ServiceUrl, string Layers, string Styles);

    private static BroWmsFeatureInfoRequest? BroWmsFeatureInfoRequestForModel(string modelType) =>
        NormalizeBroModelType(modelType) switch
        {
            BroGeomorphologyModelType => new(
                "https://service.pdok.nl/bzk/bro-geomorfologischekaart/wms/v2_0",
                "geomorphological_area,area_of_geomorphological_interest",
                "geomorphological_area,geomorphological_interest"),
            BroSoilMapModelType => new(
                "https://service.pdok.nl/bzk/bro-bodemkaart/wms/v1_0",
                "soilarea,areaofpedologicalinterest",
                "soilslope,pedologicalinterest"),
            BroGroundwaterGhgModelType => new(
                "https://service.pdok.nl/bzk/bro-grondwaterspiegeldiepte/wms/v2_0",
                "bro-grondwaterspiegeldieptemetingen-GHG",
                ""),
            BroGroundwaterGlgModelType => new(
                "https://service.pdok.nl/bzk/bro-grondwaterspiegeldiepte/wms/v2_0",
                "bro-grondwaterspiegeldieptemetingen-GLG",
                ""),
            BroGroundwaterGvgModelType => new(
                "https://service.pdok.nl/bzk/bro-grondwaterspiegeldiepte/wms/v2_0",
                "bro-grondwaterspiegeldieptemetingen-GVG",
                ""),
            BroGroundwaterGtModelType => new(
                "https://service.pdok.nl/bzk/bro-grondwaterspiegeldiepte/wms/v2_0",
                "bro-grondwaterspiegeldieptemetingen-GT",
                ""),
            BroGroundwaterDocumentationModelType => new(
                "https://service.pdok.nl/bzk/bro-grondwaterspiegeldiepte/wms/v2_0",
                "bro-grondwaterspiegeldiepte-modeldocumentatie",
                ""),
            _ => null
        };

    private static string BroWmsFeatureLabel(string modelType, JsonElement properties)
    {
        var normalized = NormalizeBroModelType(modelType);
        if (normalized == BroSoilMapModelType)
        {
            return FirstNonEmpty(
                BroWmsPropertyText(properties, "normal_soilprofile_name", "soil_name", "soilname"),
                BroWmsPropertyText(properties, "first_soilname", "soil_group"),
                BroWmsPropertyText(properties, "soilcode", "code"));
        }

        if (normalized == BroGeomorphologyModelType)
        {
            return FirstNonEmpty(
                BroWmsPropertyText(properties, "landform_subgroup_description", "landform_description"),
                BroWmsPropertyText(properties, "genese_description"),
                BroWmsPropertyText(properties, "relief_subklasse", "description"));
        }

        var value = FirstNonEmpty(
            BroWmsPropertyText(properties, "value_list"),
            BroWmsPropertyText(properties, "value", "class", "klasse", "gridcode"));

        return normalized switch
        {
            BroGroundwaterGhgModelType => string.IsNullOrWhiteSpace(value) ? "GHG-klasse" : $"GHG circa {value} cm-mv",
            BroGroundwaterGlgModelType => string.IsNullOrWhiteSpace(value) ? "GLG-klasse" : $"GLG circa {value} cm-mv",
            BroGroundwaterGvgModelType => string.IsNullOrWhiteSpace(value) ? "GVG-klasse" : $"GVG circa {value} cm-mv",
            BroGroundwaterGtModelType => string.IsNullOrWhiteSpace(value) ? "Grondwatertrap" : $"Grondwatertrap {value}",
            BroGroundwaterDocumentationModelType => string.IsNullOrWhiteSpace(value) ? "Modeldocumentatieklasse" : $"Modeldocumentatieklasse {value}",
            _ => value
        };
    }

    private static string BroWmsLayerDescription(string modelType) =>
        NormalizeBroModelType(modelType) switch
        {
            BroGeomorphologyModelType => "geomorphological_area + area_of_geomorphological_interest",
            BroSoilMapModelType => "soilarea + areaofpedologicalinterest",
            BroGroundwaterGhgModelType => "bro-grondwaterspiegeldieptemetingen-GHG",
            BroGroundwaterGlgModelType => "bro-grondwaterspiegeldieptemetingen-GLG",
            BroGroundwaterGvgModelType => "bro-grondwaterspiegeldieptemetingen-GVG",
            BroGroundwaterGtModelType => "bro-grondwaterspiegeldieptemetingen-GT",
            BroGroundwaterDocumentationModelType => "bro-grondwaterspiegeldiepte-modeldocumentatie",
            _ => "-"
        };

    private static IReadOnlyList<(string Title, string Url)> BroWmsLegendSources(string modelType) =>
        NormalizeBroModelType(modelType) switch
        {
            BroGeomorphologyModelType =>
            [
                ("Legenda BRO Geomorfologie 2025-01",
                    "https://service.pdok.nl/bzk/bro-geomorfologischekaart/wms/v2_0?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetLegendGraphic&LAYER=geomorphological_area&STYLE=geomorphological_area&FORMAT=image/png"),
                ("Gebieden van geomorfologisch belang",
                    "https://service.pdok.nl/bzk/bro-geomorfologischekaart/wms/v2_0?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetLegendGraphic&LAYER=area_of_geomorphological_interest&STYLE=geomorphological_interest&FORMAT=image/png")
            ],
            BroSoilMapModelType =>
            [
                ("Legenda BRO Bodemkaart 2025-01",
                    "https://service.pdok.nl/bzk/bro-bodemkaart/wms/v1_0?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetLegendGraphic&LAYER=soilarea&STYLE=soilslope&FORMAT=image/png"),
                ("Vlakken van bodemkundig belang",
                    "https://service.pdok.nl/bzk/bro-bodemkaart/wms/v1_0?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetLegendGraphic&LAYER=areaofpedologicalinterest&STYLE=pedologicalinterest&FORMAT=image/png")
            ],
            BroGroundwaterGhgModelType =>
            [
                ("Legenda GHG - gemiddeld kleinste diepte",
                    "https://service.pdok.nl/bzk/bro-grondwaterspiegeldiepte/wms/v2_0?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetLegendGraphic&LAYER=bro-grondwaterspiegeldieptemetingen-GHG&FORMAT=image/png")
            ],
            BroGroundwaterGlgModelType =>
            [
                ("Legenda GLG - gemiddeld grootste diepte",
                    "https://service.pdok.nl/bzk/bro-grondwaterspiegeldiepte/wms/v2_0?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetLegendGraphic&LAYER=bro-grondwaterspiegeldieptemetingen-GLG&FORMAT=image/png")
            ],
            BroGroundwaterGvgModelType =>
            [
                ("Legenda GVG - gemiddelde diepte in voorjaar",
                    "https://service.pdok.nl/bzk/bro-grondwaterspiegeldiepte/wms/v2_0?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetLegendGraphic&LAYER=bro-grondwaterspiegeldieptemetingen-GVG&FORMAT=image/png")
            ],
            BroGroundwaterGtModelType =>
            [
                ("Legenda grondwatertrappen",
                    "https://service.pdok.nl/bzk/bro-grondwaterspiegeldiepte/wms/v2_0?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetLegendGraphic&LAYER=bro-grondwaterspiegeldieptemetingen-GT&FORMAT=image/png")
            ],
            BroGroundwaterDocumentationModelType =>
            [
                ("Legenda modeldocumentatie",
                    "https://service.pdok.nl/bzk/bro-grondwaterspiegeldiepte/wms/v2_0?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetLegendGraphic&LAYER=bro-grondwaterspiegeldiepte-modeldocumentatie&FORMAT=image/png")
            ],
            _ => []
        };

    private static string BroWmsOverlayKey(string modelType) =>
        NormalizeBroModelType(modelType) switch
        {
            BroGeomorphologyModelType => "broGeomorphology",
            BroSoilMapModelType => "broSoilMap",
            BroGroundwaterGhgModelType => "broGroundwaterGhg",
            BroGroundwaterGlgModelType => "broGroundwaterGlg",
            BroGroundwaterGvgModelType => "broGroundwaterGvg",
            BroGroundwaterGtModelType => "broGroundwaterGt",
            BroGroundwaterDocumentationModelType => "broGroundwaterDocumentation",
            _ => ""
        };

    private static IReadOnlyList<string> BroWmsOverlayKeys() =>
    [
        "broGeomorphology",
        "broSoilMap",
        "broGroundwaterGhg",
        "broGroundwaterGlg",
        "broGroundwaterGvg",
        "broGroundwaterGt",
        "broGroundwaterDocumentation"
    ];

    private static string BroWmsPropertyText(JsonElement properties, params string[] names)
    {
        foreach (var name in names)
        {
            if (JsonProperty(properties, name) is not { } property) continue;
            var text = JsonText(property, "");
            if (!string.IsNullOrWhiteSpace(text) && text != "-") return text.Trim();
        }

        return "";
    }

    private static string BroWmsPropertyWithLabel(JsonElement properties, string label, params string[] names)
    {
        var value = BroWmsPropertyText(properties, names);
        return string.IsNullOrWhiteSpace(value) ? "" : $"{label}: {value}";
    }

    private sealed record BroWmsTraceFinding(double Station, string Label, string Details);

    private static BroCptSearchBounds? BuildBroCptSearchBounds(IReadOnlyList<TracePointRow> traceRows, double bufferMeters)
    {
        var points = traceRows
            .Select(row => new RdPoint(row.X, row.Y))
            .Where(IsValidRdPoint)
            .ToList();
        if (points.Count == 0) return null;

        var minX = points.Min(point => point.X) - bufferMeters;
        var maxX = points.Max(point => point.X) + bufferMeters;
        var minY = points.Min(point => point.Y) - bufferMeters;
        var maxY = points.Max(point => point.Y) + bufferMeters;
        var corners = new[]
        {
            RdToWgs84(minX, minY),
            RdToWgs84(minX, maxY),
            RdToWgs84(maxX, minY),
            RdToWgs84(maxX, maxY)
        };

        return new BroCptSearchBounds(
            Math.Round(corners.Min(item => item[1]), 8),
            Math.Round(corners.Min(item => item[0]), 8),
            Math.Round(corners.Max(item => item[1]), 8),
            Math.Round(corners.Max(item => item[0]), 8));
    }

    private static IReadOnlyList<(RdPoint Point, double Distance, double Offset)> BuildBroDatasetCandidates(IReadOnlyList<TracePointRow> traceRows)
    {
        if (traceRows.Count == 0) return [];

        var minX = traceRows.Min(point => point.X);
        var maxX = traceRows.Max(point => point.X);
        var minY = traceRows.Min(point => point.Y);
        var maxY = traceRows.Max(point => point.Y);
        if (!double.IsFinite(minX) || !double.IsFinite(maxX) || !double.IsFinite(minY) || !double.IsFinite(maxY)) return [];

        var centerX = (minX + maxX) / 2d;
        var centerY = (minY + maxY) / 2d;
        var width = Math.Max(250d, maxX - minX);
        var height = Math.Max(250d, maxY - minY);
        var radius = Math.Clamp(Math.Max(width, height) * 6d, 1500d, 5000d);
        var gridSteps = new[] { -1d, -0.5d, 0d, 0.5d, 1d };
        var traceDistances = traceRows.Count >= 2 ? BuildTraceDistances(traceRows) : [];
        var result = new List<(RdPoint Point, double Distance, double Offset)>();

        foreach (var yStep in gridSteps)
        {
            foreach (var xStep in gridSteps)
            {
                var point = new RdPoint(centerX + xStep * radius, centerY + yStep * radius);
                var reference = traceRows.Count >= 2 && traceDistances.Count >= 2
                    ? ProjectPointOnTraceSigned(point, traceRows, traceDistances)
                    : new KlicPlanPoint(0, 0);
                result.Add((point, Math.Round(reference.Station, 2), Math.Round(reference.Offset, 2)));
            }
        }

        return result
            .Where(item => IsValidRdPoint(item.Point))
            .GroupBy(item => $"{Math.Round(item.Point.X, 0)}:{Math.Round(item.Point.Y, 0)}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => Math.Sqrt(Math.Pow(item.Point.X - centerX, 2) + Math.Pow(item.Point.Y - centerY, 2)))
            .Take(25)
            .ToList();
    }

    private BroSoundingPoint BuildBroDatasetPoint(RdPoint rd, double distance, double offset, string modelType, int index, Exception? sourceError = null)
    {
        modelType = NormalizeBroModelType(modelType);
        var wgs = RdToWgs84(rd.X, rd.Y);
        var code = $"{DinoModelShortLabel(modelType)}-{index}";
        var label = DinoModelLabel(modelType);
        var sourceText = sourceError is null
            ? $"{label} kaartdatasetpunt. Selecteer dit punt handmatig als bron voor de rapportage."
            : $"{label} kaartdatasetpunt. Detailprofiel kon niet worden gelezen ({sourceError.Message}).";

        return new BroSoundingPoint(
            $"{modelType}-{Math.Round(rd.X, 0):0}-{Math.Round(rd.Y, 0):0}-{index}",
            code,
            $"{label} kaartpunt {index}",
            Math.Round(rd.X, 3),
            Math.Round(rd.Y, 3),
            Math.Round(wgs[0], 8),
            Math.Round(wgs[1], 8),
            Math.Round(distance, 2),
            Math.Round(offset, 2),
            null,
            null,
            sourceText,
            "DINOloket kaartdataset",
            DateTime.Today.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
            "Kaartdatasetpunt beschikbaar",
            modelType,
            label,
            []);
    }

    private static FrameworkElement BuildBroImportedProfileDetailPanel(BroImportedProfileRecord profile)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = profile.Identification,
            Foreground = Brush("#315B7E"),
            FontWeight = FontWeights.Bold,
            FontSize = 11.5,
            Margin = new Thickness(0, 0, 0, 4)
        });

        panel.Children.Add(new TextBlock
        {
            Text =
                $"Model: {profile.ModelName}\n" +
                $"RD X/Y: {FormatBroNullable(profile.X, 0)}, {FormatBroNullable(profile.Y, 0)}\n" +
                $"Maaiveld: {FormatBroNullable(profile.SurfaceNap, 2, " m NAP")}\n" +
                $"Diepte t.o.v. maaiveld: {FormatBroNullable(profile.DepthTop, 2, " m")} - {FormatBroNullable(profile.DepthBottom, 2, " m")}\n" +
                $"PDF: {profile.FileName}",
            Foreground = Brush("#1F3B55"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap
        });

        var profileImagePath = ShouldRegenerateBroProfileImage(profile.ProfileImagePath) && File.Exists(profile.LocalPath)
            ? RenderBroImportedProfileImage(profile.LocalPath, profile.ModelType, profile.Identification)
            : profile.ProfileImagePath;
        if (!string.IsNullOrWhiteSpace(profileImagePath) && File.Exists(profileImagePath))
        {
            panel.Children.Add(CreateBroImportedProfileSidebarImage(profileImagePath));
        }

        if (!string.IsNullOrWhiteSpace(profile.ExtractedSummary))
        {
            panel.Children.Add(new TextBlock
            {
                Text = "\n" + CompactBroSidebarText(profile.ExtractedSummary, 360),
                Foreground = Brush("#587080"),
                FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap
            });
        }

        return panel;
    }

    private object BuildBroImportedProfilePayload(BroImportedProfileRecord profile) => new
    {
        id = profile.Id,
        identification = profile.Identification,
        code = profile.Identification,
        modelType = profile.ModelType,
        modelName = profile.ModelName,
        x = profile.X,
        y = profile.Y,
        surfaceNap = profile.SurfaceNap,
        depthTop = profile.DepthTop,
        depthBottom = profile.DepthBottom,
        sourceFile = profile.FileName,
        localPath = profile.LocalPath,
        sourcePath = profile.SourcePath,
        profileImagePath = profile.ProfileImagePath,
        importedAt = profile.ImportedAt,
        extractedSummary = profile.ExtractedSummary
    };

    private static string BuildBroModelSummary(IReadOnlyList<BroProfileInterval> intervals, string modelType)
    {
        if (intervals.Count == 0) return $"{DinoModelLabel(modelType)} modelprofiel beschikbaar, zonder leesbare intervalmetadata.";
        return string.Join("; ", intervals.Take(6).Select(item =>
            $"{item.TopDepth:0.##}-{item.BottomDepth:0.##} m {item.Label}"));
    }

    private static string BuildBroPdfExtractSummary(string text)
    {
        var lines = text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => Regex.Replace(line, @"\s+", " ").Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !line.Equals("Diepte t.o.v maaiveld in meters", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(28)
            .ToList();

        return lines.Count == 0 ? "" : string.Join("\n", lines);
    }

    private FrameworkElement BuildBroSoundingDetailPanel(BroSoundingPoint selected)
    {
        var panel = new StackPanel();
        var intervalLabel = NormalizeBroModelType(selected.ModelType) switch
        {
            BroRegisModelType => "REGIS-lagen",
            BroGeomorphologyModelType => "Geomorfologie-eenheden",
            BroSoilMapModelType => "Bodemklassen",
            BroGroundwaterGhgModelType
                or BroGroundwaterGlgModelType
                or BroGroundwaterGvgModelType
                or BroGroundwaterGtModelType
                or BroGroundwaterDocumentationModelType => "Grondwaterspiegeldiepte",
            _ => "DGM-lagen"
        };
        panel.Children.Add(new TextBlock
        {
            Text = "Boormonsterprofiel en interpretatie",
            Foreground = Brush("#315B7E"),
            FontWeight = FontWeights.Bold,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 4)
        });

        panel.Children.Add(new TextBlock
        {
            Text =
                $"Identificatie: {selected.Code}\n" +
                $"Model: {selected.ModelName}\n" +
                $"RD X/Y: {selected.X:0.###}, {selected.Y:0.###}\n" +
                $"Maaiveld: {FormatBroOptionalMeters(selected.SurfaceNap, "m NAP")}\n" +
                $"Diepte t.o.v. maaiveld: 0,00 - {FormatBroOptionalMeters(selected.EndDepth)}\n" +
                $"Boorlijnreferentie: station {FormatBroMeters(selected.Distance)}, offset {FormatBroMeters(selected.Offset)}\n" +
                $"{intervalLabel}: {selected.ProfileIntervals.Count}\n" +
                $"Bron: {selected.Source}",
            Foreground = Brush("#1F3B55"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap
        });

        if (selected.ProfileIntervals.Count > 0)
        {
            panel.Children.Add(CreateBroSoundingProfilePreview(selected));
        }

        if (selected.ProfileIntervals.Count == 0 && !string.IsNullOrWhiteSpace(selected.SoilSummary))
        {
            panel.Children.Add(new TextBlock
            {
                Text = "\n" + CompactBroSidebarText(selected.SoilSummary, 220),
                Foreground = Brush("#587080"),
                FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });
        }

        return panel;
    }

    private object BuildBroSoundingPayload(BroSoundingPoint sounding) => new
    {
        id = sounding.Id,
        code = sounding.Code,
        name = sounding.Name,
        x = sounding.X,
        y = sounding.Y,
        lon = sounding.Lon,
        lat = sounding.Lat,
        distance = sounding.Distance,
        offset = sounding.Offset,
        surfaceNap = sounding.SurfaceNap,
        endDepth = sounding.EndDepth,
        soilSummary = sounding.SoilSummary,
        source = sounding.Source,
        date = sounding.Date,
        status = sounding.Status,
        modelType = sounding.ModelType,
        modelName = sounding.ModelName,
        profileIntervals = sounding.ProfileIntervals.Select(interval => new
        {
            topDepth = interval.TopDepth,
            bottomDepth = interval.BottomDepth,
            code = interval.Code,
            label = interval.Label,
            lithology = interval.Lithology,
            color = interval.Color
        }).ToList(),
        profileSummary = sounding.ProfileIntervals.Count == 0
            ? sounding.SoilSummary
            : string.Join("; ", sounding.ProfileIntervals.Take(5).Select(interval => $"{interval.TopDepth:0.##}-{interval.BottomDepth:0.##} m {interval.Label}"))
    };

    private BroSoundingPoint? BuildBroSoundingPointFromImportedProfile(
        BroImportedProfileRecord profile,
        IReadOnlyList<TracePointRow> traceRows,
        IReadOnlyList<double> traceDistances)
    {
        if (profile.X is not double x || profile.Y is not double y) return null;
        if (!double.IsFinite(x) || !double.IsFinite(y)) return null;

        var wgs = RdToWgs84(x, y);
        var reference = traceRows.Count >= 2
            ? ProjectPointOnTraceSigned(new RdPoint(x, y), traceRows, traceDistances)
            : new KlicPlanPoint(0, 0);

        return new BroSoundingPoint(
            profile.Identification,
            profile.Identification,
            profile.Identification,
            x,
            y,
            wgs[0],
            wgs[1],
            Math.Round(reference.Station, 2),
            Math.Round(reference.Offset, 2),
            profile.SurfaceNap,
            profile.DepthBottom,
            profile.ExtractedSummary,
            $"BRO/DINOloket PDF-import: {profile.FileName}",
            profile.ImportedAt.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
            "Geimporteerd uit PDF",
            profile.ModelType,
            profile.ModelName,
            []);
    }

    private IReadOnlyList<BroSoundingPoint> BuildBroSoundings(IReadOnlyList<TracePointRow> traceRows)
    {
        if (_selectedProject is null) return [];
        _projectFiles = _projects.GetProjectFiles(_selectedProject.Id);
        var layers = BuildProjectMapLayers(_projectFiles);
        var distances = traceRows.Count >= 2 ? BuildTraceDistances(traceRows) : [];
        var result = new List<BroSoundingPoint>();
        var index = 1;

        foreach (var layer in layers.Where(IsBroSoundingLayer))
        {
            foreach (var feature in layer.FeatureCollection.Features)
            {
                if (!IsBroSoundingFeature(feature, layer)) continue;
                if (!TryGetCoordinate(feature.Geometry.Coordinates, out var coordinate)) continue;

                var rd = ToRdPoint(coordinate);
                if (!IsValidRdPoint(rd)) continue;

                var wgs = RdToWgs84(rd.X, rd.Y);
                var projected = traceRows.Count >= 2 && distances.Count >= 2
                    ? ProjectPointOnTraceSigned(rd, traceRows, distances)
                    : new KlicPlanPoint(0, 0);

                var id = FirstNonEmptyText(
                    FeatureText(feature, "broId", "bro_id", "id", "identificatie", "registrationObjectId", "sonderingId", "sourceId", "localId", "fid"),
                    $"{layer.Id}:{rd.X:0.###}:{rd.Y:0.###}");
                var code = FirstNonEmptyText(
                    FeatureText(feature, "code", "label", "naam", "name", "objectId", "identificatie"),
                    $"BRO {index}");
                var name = FirstNonEmptyText(
                    FeatureText(feature, "naam", "name", "omschrijving", "description"),
                    code);

                result.Add(new BroSoundingPoint(
                    id,
                    code,
                    name,
                    Math.Round(rd.X, 3),
                    Math.Round(rd.Y, 3),
                    Math.Round(wgs[0], 8),
                    Math.Round(wgs[1], 8),
                    Math.Round(projected.Station, 2),
                    Math.Round(projected.Offset, 2),
                    FeatureDoubleNullable(feature, "maaiveldNap", "surfaceNap", "surface_nap", "groundLevelNap", "nap", "groundLevel", "z"),
                    FeatureDoubleNullable(feature, "endDepth", "einddiepte", "diepte", "depth", "onderzoekDiepte", "maxDepth"),
                    FirstNonEmptyText(
                        FeatureText(feature, "soilSummary", "lithology", "bodemopbouw", "sonderingsdata", "description", "omschrijving", "summary"),
                        "Geen sonderingssamenvatting in bronbestand."),
                    FirstNonEmptyText(FeatureText(feature, "sourceName", "source", "bron"), layer.Name),
                    FeatureText(feature, "date", "datum", "onderzoekDatum"),
                    FeatureText(feature, "status", "quality", "kwaliteitsregime"),
                    "LOCAL",
                    "Projectbestand",
                    []));
                index++;
            }
        }

        return result
            .GroupBy(sounding => sounding.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(sounding => sounding.Distance)
            .ThenBy(sounding => Math.Abs(sounding.Offset))
            .Take(250)
            .ToList();
    }

    private static string BuildBroWmsFeatureInfoUrl(BroWmsFeatureInfoRequest request, RdPoint point)
    {
        static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        const double radius = 10d;
        var bbox = string.Join(",",
        [
            F(point.X - radius),
            F(point.Y - radius),
            F(point.X + radius),
            F(point.Y + radius)
        ]);

        return $"{request.ServiceUrl}?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetFeatureInfo" +
            $"&LAYERS={request.Layers}&QUERY_LAYERS={request.Layers}&STYLES={request.Styles}" +
            $"&SRS=EPSG:28992&BBOX={bbox}&WIDTH=101&HEIGHT=101&X=50&Y=50&INFO_FORMAT=application/json";
    }

    private static Border BuildBroWmsLegendPanel(string modelType)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Legenda",
            Foreground = Brush("#315B7E"),
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 6)
        });

        foreach (var (title, url) in BroWmsLegendSources(modelType))
        {
            panel.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = Brush("#334155"),
                FontWeight = FontWeights.SemiBold,
                FontSize = 10.5,
                Margin = new Thickness(0, 7, 0, 4)
            });

            panel.Children.Add(new Border
            {
                BorderBrush = Brush("#D7E8FA"),
                BorderThickness = new Thickness(1),
                Background = Brushes.White,
                Padding = new Thickness(6),
                Child = CreateRemoteLegendImage(url)
            });
        }

        return new Border
        {
            BorderBrush = Brush("#D7E8FA"),
            BorderThickness = new Thickness(1),
            Background = Brush("#F8FBFF"),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 8, 0, 0),
            Child = new ScrollViewer
            {
                MaxHeight = 360,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = panel
            }
        };
    }

    private static string BuildBroWmsTraceFindingCacheKey(string modelType, IReadOnlyList<TracePointRow> traceRows, double totalLength)
    {
        var traceKey = string.Join("|", traceRows.Select(row => $"{Math.Round(row.X):0}:{Math.Round(row.Y):0}"));
        return $"{NormalizeBroModelType(modelType)}:{Math.Round(totalLength):0}:{traceKey}";
    }

    private static string BuildBroWmsTraceFindingIntroduction(JsonElement data, string modelType)
    {
        var findings = ReadBroWmsTraceFindings(data);
        if (findings.Count == 0)
        {
            return "De app kon nog geen eenduidige WMS-featureinfo langs de boorlijn uitlezen; gebruik in dat geval de opgeslagen GIS-kaart en legenda als visuele controlebron.";
        }

        var grouped = findings
            .GroupBy(finding => finding.Label, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var stations = group
                    .Select(finding => finding.Station)
                    .Where(double.IsFinite)
                    .DistinctBy(station => Math.Round(station / 5d))
                    .Take(4)
                    .Select(station => $"{station:N0} m");
                var details = group.Select(finding => finding.Details).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
                var stationText = string.Join(", ", stations);
                var suffix = string.IsNullOrWhiteSpace(details) ? "" : $" ({details})";
                return string.IsNullOrWhiteSpace(stationText)
                    ? $"{group.Key}{suffix}"
                    : $"{group.Key}{suffix} rond station {stationText}";
            })
            .Take(4)
            .ToList();

        var subject = NormalizeBroModelType(modelType) switch
        {
            BroGeomorphologyModelType => "geomorfologische eenheden",
            BroSoilMapModelType => "bodemkundige bovengronden",
            BroGroundwaterGhgModelType => "GHG-kaartwaarden",
            BroGroundwaterGlgModelType => "GLG-kaartwaarden",
            BroGroundwaterGvgModelType => "GVG-kaartwaarden",
            BroGroundwaterGtModelType => "grondwatertrappen",
            BroGroundwaterDocumentationModelType => "modeldocumentatieklassen",
            _ => "kaartklassen"
        };

        return $"Aangetroffen {subject} langs de boorlijn: {string.Join("; ", grouped)}.";
    }

    private static IReadOnlyList<BroWmsTraceFinding> BuildBroWmsTraceFindings(
        IReadOnlyList<TracePointRow> traceRows,
        double traceLength,
        string modelType)
    {
        modelType = NormalizeBroModelType(modelType);
        if (!IsBroWmsMapLayer(modelType) || traceLength <= 0 || traceRows.Count < 2)
        {
            return [];
        }

        var rdRows = NormalizeTraceRowsToRd(traceRows)
            .Where(row => IsValidRdPoint(new RdPoint(row.X, row.Y)))
            .ToList();
        if (rdRows.Count < 2) return [];

        var distances = BuildTraceDistances(rdRows);
        var totalLength = distances.Count > 0 ? distances[^1] : traceLength;
        if (totalLength <= 0) return [];

        var cacheKey = BuildBroWmsTraceFindingCacheKey(modelType, rdRows, totalLength);
        lock (BroWmsTraceFindingCache)
        {
            if (BroWmsTraceFindingCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }
        }

        var findings = new List<BroWmsTraceFinding>();
        foreach (var station in BuildBroWmsTraceSampleStations(totalLength))
        {
            var tracePoint = InterpolateTracePoint(rdRows, distances, station);
            var point = new RdPoint(tracePoint.X, tracePoint.Y);
            if (!IsValidRdPoint(point)) continue;

            findings.AddRange(QueryBroWmsFeatureInfoAtPoint(modelType, point, station));
        }

        var compact = findings
            .Where(item => !string.IsNullOrWhiteSpace(item.Label))
            .GroupBy(item => $"{item.Label}|{Math.Round(item.Station / 5d)}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(14)
            .ToList();

        lock (BroWmsTraceFindingCache)
        {
            BroWmsTraceFindingCache[cacheKey] = compact;
        }

        return compact;
    }

    private static IEnumerable<double> BuildBroWmsTraceSampleStations(double totalLength)
    {
        if (totalLength <= 0) yield break;

        var fractions = new[] { 0d, 0.2d, 0.4d, 0.6d, 0.8d, 1d };
        foreach (var fraction in fractions)
        {
            yield return Math.Clamp(totalLength * fraction, 0, totalLength);
        }
    }

    private IReadOnlyList<BroSoundingPoint> BuildFallbackBroDatasetPoints(IReadOnlyList<TracePointRow> traceRows, string modelType)
    {
        var index = 1;
        return BuildBroDatasetCandidates(traceRows)
            .Select(candidate => BuildBroDatasetPoint(candidate.Point, candidate.Distance, candidate.Offset, modelType, index++))
            .ToList();
    }

    private object BuildUndergroundReportDataPayload(IReadOnlyList<TracePointRow> traceRows, double traceLength, string modelType)
    {
        modelType = NormalizeBroModelType(modelType);
        IReadOnlyList<BroImportedProfileRecord> importedProfiles = SupportsImportedBroProfiles(modelType)
            ? ReadBroImportedProfiles(modelType)
            : [];
        if (importedProfiles.Count > 0)
        {
            MergeImportedBroProfilesIntoSoundings(modelType, importedProfiles, selectImported: false);
        }

        var soundings = GetBroSoundings(modelType);
        var selected = GetSelectedBroSoundingIds(modelType)
            .Select(id => soundings.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            .Where(item => item is not null)
            .Cast<BroSoundingPoint>()
            .ToList();
        var primarySelected = selected.FirstOrDefault();
        var isBroWmsMapLayer = IsBroWmsMapLayer(modelType);
        IReadOnlyList<BroWmsTraceFinding> wmsTraceFindings = isBroWmsMapLayer
            ? BuildBroWmsTraceFindings(traceRows, traceLength, modelType)
            : [];

        return new
        {
            status = isBroWmsMapLayer
                ? $"{DinoModelLabel(modelType)} kaartlaag beschikbaar"
                : importedProfiles.Count > 0 ? $"{DinoModelLabel(modelType)} PDF-profiel(en) geimporteerd"
                : soundings.Count == 0 ? $"{DinoModelLabel(modelType)} nog niet geladen" : $"{DinoModelLabel(modelType)} beschikbaar",
            activeModel = DinoModelLabel(modelType),
            activeModelType = modelType,
            isWmsMapLayer = isBroWmsMapLayer,
            wmsOverlayKey = isBroWmsMapLayer ? BroWmsOverlayKey(modelType) : "",
            wmsLayerDescription = isBroWmsMapLayer ? BroWmsLayerDescription(modelType) : "",
            wmsLegendSources = BroWmsLegendSources(modelType)
                .Select(source => new { source.Title, source.Url })
                .ToList(),
            wmsLegendRows = BroWmsCompactLegendRows(modelType)
                .Select(row => new { color = row.Color, label = row.Label })
                .ToList(),
            wmsTraceFindings = wmsTraceFindings
                .Select(item => new
                {
                    station = Math.Round(item.Station, 1),
                    item.Label,
                    item.Details
                })
                .ToList(),
            traceLength,
            soundingCount = soundings.Count,
            selectedSoundingId = primarySelected?.Id ?? "",
            selectedSoundingIds = selected.Select(item => item.Id).ToList(),
            selectedSounding = primarySelected is null ? null : BuildBroSoundingPayload(primarySelected),
            selectedSoundings = selected.Select(BuildBroSoundingPayload).ToList(),
            selectedSoundingCount = selected.Count,
            maxSelectedSoundings = GetMaxBroSelectedSoundings(modelType),
            importedProfileCount = importedProfiles.Count,
            maxImportedProfiles = SupportsImportedBroProfiles(modelType) ? MaxBroImportedProfilesPerModel : 0,
            importedProfiles = importedProfiles.Select(BuildBroImportedProfilePayload).ToList(),
            soundings = soundings.Select(BuildBroSoundingPayload).ToList(),
            projectFiles = _projectFiles
                .Where(IsBroSoundingFile)
                .Select(file => new { file.FileType, file.DisplayName, file.SizeBytes }),
            loadStatus = GetBroLoadStatus(modelType),
            mapStateAvailable = _selectedProject is not null && !string.IsNullOrWhiteSpace(GetCurrentMapStateJson(6)),
            note = isBroWmsMapLayer
                ? $"De {DinoModelLabel(modelType)} kaartlaag wordt als losse DINOloket/PDOK WMS-kaartlaag over de GIS-kaart gelegd. De boorlijn blijft zichtbaar als referentie."
                : importedProfiles.Count > 0
                ? $"Er zijn {importedProfiles.Count} officiele BRO/DINOloket PDF-profiel(en) geimporteerd. Deze PDF-gegevens zijn leidend voor het rapport; de kaartpunten blijven alleen als bronlocatie/referentie zichtbaar."
                : SupportsImportedBroProfiles(modelType)
                ? $"Importeer maximaal {MaxBroImportedProfilesPerModel} officiele BRO/DINOloket boormonsterprofiel-PDF's. De app leest identificatie, RD-coordinaten, maaiveld en diepte uit en neemt dit op in de rapportage."
                : $"Klik zelf op een {DinoModelShortLabel(modelType)}-punt in de GIS-kaart om deze losse BRO/DINOloket datasetbron voor de rapportage te selecteren."
        };
    }

    private static DrawingBitmap CombineBroProfilePages(IReadOnlyList<DrawingBitmap> pages)
    {
        var validPages = pages.Where(page => page.Width > 1 && page.Height > 1).ToList();
        if (validPages.Count == 0) return new DrawingBitmap(1, 1, DrawingPixelFormat.Format32bppArgb);
        if (validPages.Count == 1) return new DrawingBitmap(validPages[0]);

        const int padding = 22;
        const int gap = 18;
        var width = validPages.Sum(page => page.Width) + gap * (validPages.Count - 1) + padding * 2;
        var height = validPages.Max(page => page.Height) + padding * 2;
        var combined = new DrawingBitmap(width, height, DrawingPixelFormat.Format32bppArgb);
        using var graphics = System.Drawing.Graphics.FromImage(combined);
        graphics.Clear(System.Drawing.Color.White);

        var x = padding;
        foreach (var page in validPages)
        {
            var y = padding + Math.Max(0, (height - padding * 2 - page.Height) / 2);
            graphics.DrawImage(page, x, y, page.Width, page.Height);
            x += page.Width + gap;
        }

        return combined;
    }

    private static string CompactBroSidebarText(string value, int maxLength)
    {
        value = Regex.Replace(value ?? "", @"\s+", " ").Trim();
        if (value.Length <= maxLength) return value;
        return value[..Math.Max(0, maxLength - 1)].TrimEnd() + "...";
    }

    private static UIElement CreateBroImportedProfileInformationBlock(JsonElement profile)
    {
        var depthTop = JsonDoubleNullable(profile, "depthTop");
        var depthBottom = JsonDoubleNullable(profile, "depthBottom");
        return CreateReportKeyValues(
            ("Identificatie", JsonText(profile, "identification", JsonText(profile, "code", "-"))),
            ("Model", JsonText(profile, "modelName", "-")),
            ("RD X/Y", $"{FormatReportNumber(JsonDouble(profile, "x", double.NaN), 0)}, {FormatReportNumber(JsonDouble(profile, "y", double.NaN), 0)}"),
            ("Maaiveld", FormatReportNumber(JsonDouble(profile, "surfaceNap", double.NaN), 2, " m NAP")),
            ("Diepte t.o.v. maaiveld", $"{FormatReportNullable(depthTop, 2, " m")} - {FormatReportNullable(depthBottom, 2, " m")}"),
            ("Bronbestand", JsonText(profile, "sourceFile", "-")),
            ("Bron", "Officieel BRO/DINOloket boormonsterprofiel PDF"));
    }

    private static UIElement CreateBroImportedProfileSidebarImage(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.EndInit();
            bitmap.Freeze();

            return new Border
            {
                BorderBrush = Brush("#D7E8FA"),
                BorderThickness = new Thickness(1),
                Background = Brushes.White,
                Padding = new Thickness(4),
                Margin = new Thickness(0, 8, 0, 2),
                Child = new Image
                {
                    Source = bitmap,
                    Height = 190,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            };
        }
        catch
        {
            return new TextBlock
            {
                Text = "Profielbeeld kon niet geladen worden.",
                Foreground = Brush("#8FA6B2"),
                FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            };
        }
    }

    private static Border CreateBroPointReportLegendBlock(string modelType)
    {
        var color = NormalizeBroModelType(modelType) == BroRegisModelType ? "#0EA5E9" : "#D97706";
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Legenda kaartpunten",
            Foreground = Brush("#333333"),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });
        panel.Children.Add(CreateLegendRow(color, $"{DinoModelShortLabel(modelType)} kaartpunt"));
        panel.Children.Add(CreateLegendRow("#16A34A", "Geselecteerd kaartpunt"));
        panel.Children.Add(CreateLegendRow("#E11D48", "Boorlijn / trace"));

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("#E5EDF3"),
            BorderThickness = new Thickness(0, 1, 0, 1),
            Padding = new Thickness(0, 8, 0, 8),
            Margin = new Thickness(0, 2, 0, 7),
            Child = panel
        };
    }

    private Border CreateBroProfileLocationMapCard(
        JsonElement profile,
        string modelType,
        string liveMapPath,
        int stepNumber,
        string? variantKey,
        string title,
        string subtitle,
        double imageWidth,
        double imageHeight)
    {
        var liveSubtitle = $"Opgeslagen GIS-kaart uit deze substap. Dit kaartbeeld volgt exact de gekozen kaartuitsnede in de app; gebruik 'Opslaan voor rapportage' om de uitsnede te vernieuwen.";
        if (!string.IsNullOrWhiteSpace(liveMapPath) && File.Exists(liveMapPath))
        {
            return CreateLiveMapReportImageCard(
                title,
                liveSubtitle,
                liveMapPath,
                stepNumber,
                variantKey,
                imageWidth,
                imageHeight);
        }

        if (_selectedStep?.Number == stepNumber)
        {
            QueueLiveMapReportCapture(stepNumber);
        }

        return CreateLiveMapReportImageCard(title, liveSubtitle, liveMapPath, stepNumber, variantKey, imageWidth, imageHeight);
    }

    private static Border CreateBroSoundingProfilePreview(BroSoundingPoint selected)
    {
        var intervals = selected.ProfileIntervals
            .Where(interval => double.IsFinite(interval.TopDepth)
                && double.IsFinite(interval.BottomDepth)
                && interval.BottomDepth > interval.TopDepth)
            .OrderBy(interval => interval.TopDepth)
            .Take(60)
            .ToList();

        if (intervals.Count == 0)
        {
            return new Border
            {
                Background = Brush("#F8FBFF"),
                BorderBrush = Brush("#D7E8FA"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 8, 0, 0),
                Child = new TextBlock
                {
                    Text = "Nog geen profieltekening beschikbaar. Klik opnieuw op het bronpunt om het profiel op te halen.",
                    Foreground = Brush("#587080"),
                    FontSize = 10.5,
                    TextWrapping = TextWrapping.Wrap
                }
            };
        }

        var endDepth = selected.EndDepth is double depth && double.IsFinite(depth) && depth > 0
            ? Math.Min(depth, Math.Max(20d, intervals.Max(interval => interval.BottomDepth)))
            : Math.Max(0.5d, intervals.Max(interval => interval.BottomDepth));

        const double width = 332;
        const double height = 270;
        const double plotTop = 34;
        const double plotHeight = 178;
        const double axisLeft = 34;
        const double geoLeft = 75;
        const double lithLeft = 148;
        const double barWidth = 48;
        const double legendLeft = 212;

        var canvas = new Canvas
        {
            Width = width,
            Height = height,
            Background = Brushes.White,
            ClipToBounds = true
        };

        AddCanvasText(canvas, "Geologische eenheid", geoLeft - 15, 10, "#334155", 8.5, FontWeights.Bold);
        AddCanvasText(canvas, "Lithologie", lithLeft + 2, 10, "#334155", 8.5, FontWeights.Bold);
        AddCanvasLine(canvas, axisLeft, plotTop, axisLeft, plotTop + plotHeight, "#64748B", 1, null);

        for (var i = 0; i <= 4; i++)
        {
            var depthValue = endDepth * i / 4d;
            var y = SoundingProfileDepthY(depthValue, plotTop, plotHeight, endDepth);
            AddCanvasLine(canvas, axisLeft - 3, y, lithLeft + barWidth + 14, y, "#E5E7EB", 0.7, i == 0 ? null : [3, 3]);
            AddCanvasText(canvas, $"{depthValue:N0}", 8, y - 6, "#64748B", 7.8, FontWeights.Normal);
        }

        foreach (var interval in intervals)
        {
            var y1 = SoundingProfileDepthY(interval.TopDepth, plotTop, plotHeight, endDepth);
            var y2 = SoundingProfileDepthY(interval.BottomDepth, plotTop, plotHeight, endDepth);
            var h = Math.Max(2.5, y2 - y1);
            var color = BroProfileFill(interval);
            AddCanvasRect(canvas, geoLeft, y1, barWidth, h, color, "#334155", 0.35);
            AddCanvasRect(canvas, lithLeft, y1, barWidth, h, color, "#334155", 0.35);
        }

        AddCanvasRect(canvas, legendLeft, plotTop, 106, 124, "#FBFCFD", "#CBD5E1", 1);
        AddCanvasText(canvas, "Legenda", legendLeft + 8, plotTop + 8, "#334155", 8.8, FontWeights.Bold);

        var legendItems = intervals
            .GroupBy(BroProfileFill, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(5)
            .ToList();

        for (var i = 0; i < legendItems.Count; i++)
        {
            var item = legendItems[i];
            var y = plotTop + 28 + i * 17;
            AddCanvasRect(canvas, legendLeft + 8, y, 9, 9, BroProfileFill(item), "#94A3B8", 0.5);
            var label = string.IsNullOrWhiteSpace(item.Lithology) || item.Lithology == "-"
                ? item.Label
                : item.Lithology;
            AddCanvasText(canvas, CompactSoundingLegend(label, 15), legendLeft + 22, y - 3, "#334155", 7.6, FontWeights.Normal);
        }

        AddCanvasText(canvas, $"Maaiveld {FormatBroOptionalMeters(selected.SurfaceNap, "m NAP")}", legendLeft, plotTop + 142, "#475569", 8.2, FontWeights.SemiBold);
        AddCanvasText(canvas, $"Einddiepte {endDepth:N2} m", legendLeft, plotTop + 158, "#475569", 8.2, FontWeights.SemiBold);
        AddCanvasText(canvas, "Profiel op geselecteerd DINOloket-bronpunt", 52, height - 28, "#587080", 8.5, FontWeights.Normal);

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("#D7E8FA"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6),
            Margin = new Thickness(0, 8, 0, 0),
            Child = canvas
        };
    }

    private static UIElement CreateBroSourceInformationBlock(JsonElement source)
    {
        var profileCount = JsonProperty(source, "profileIntervals") is { ValueKind: JsonValueKind.Array } intervals
            ? intervals.GetArrayLength()
            : 0;

        return CreateReportKeyValues(
            ("Identificatie", JsonText(source, "code", "-")),
            ("Model", JsonText(source, "modelName", "BRO DGM v2.2.1")),
            ("RD X/Y", $"{FormatReportNumber(JsonDouble(source, "x", double.NaN), 0)}, {FormatReportNumber(JsonDouble(source, "y", double.NaN), 0)}"),
            ("Maaiveld", FormatReportNumber(JsonDouble(source, "surfaceNap", double.NaN), 2, " m NAP")),
            ("Diepte t.o.v. maaiveld", $"0,00 - {FormatReportNumber(JsonDouble(source, "endDepth", double.NaN), 2, " m")}"),
            ("Boorlijnreferentie", $"station {FormatReportNumber(JsonDouble(source, "distance", double.NaN), 1, " m")}, offset {FormatReportNumber(JsonDouble(source, "offset", double.NaN), 1, " m")}"),
            ("DGM-lagen", profileCount.ToString(CultureInfo.InvariantCulture)),
            ("Bron", JsonText(source, "source", "BRO DGM v2.2.1 booronderzoeklaag DINOloket; profiel via DINOloket modelviewer")),
            ("Samenvatting", ShortReportCell(JsonText(source, "profileSummary", JsonText(source, "soilSummary", "-")), 110)));
    }

    private static ReportRenderSoundingProfileBlock CreateBroSourceProfileBlock(JsonElement source)
    {
        var code = JsonText(source, "code", "-");
        var model = JsonText(source, "modelName", "BRO DGM v2.2.1");
        return new ReportRenderSoundingProfileBlock(
            $"Boormonsterprofiel en interpretatie {model}",
            $"Identificatie {code} | RD {FormatReportNumber(JsonDouble(source, "x", double.NaN), 0)}, {FormatReportNumber(JsonDouble(source, "y", double.NaN), 0)} | maaiveld {FormatReportNumber(JsonDouble(source, "surfaceNap", double.NaN), 2, " m NAP")} | diepte 0,00 - {FormatReportNumber(JsonDouble(source, "endDepth", double.NaN), 2, " m")}",
            ReadBroSourceProfileIntervals(source),
            JsonDoubleNullable(source, "surfaceNap"),
            JsonDoubleNullable(source, "endDepth"));
    }

    private static Border CreateBroWmsApiReportBlock(string modelType, double traceLength, string mapPath, IReadOnlyList<BroWmsTraceFinding> traceFindings)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Kaartdata",
            Foreground = Brush("#333333"),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });
        panel.Children.Add(CreateReportKeyValues(
            ("Dataset", DinoModelLabel(modelType)),
            ("Bron", "BRO/DINOloket via PDOK WMS"),
            ("WMS-laag", BroWmsLayerDescription(modelType)),
            ("Overlay", BroWmsOverlayKey(modelType)),
            ("Boorlijn", traceLength > 0 ? $"{traceLength:N1} m als referentie" : "Geen boorlijnreferentie"),
            ("Kaartuitsnede", string.IsNullOrWhiteSpace(mapPath) ? "Nog niet opgeslagen" : "Opgeslagen in deze substap"),
            ("Aangetroffen kaartklassen", FormatBroWmsTraceFindingsForReport(traceFindings)),
            ("Legenda", "Compacte rapportlegenda op basis van de actieve kaartlaag")));

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("#E5EDF3"),
            BorderThickness = new Thickness(0, 1, 0, 1),
            Padding = new Thickness(0, 8, 0, 8),
            Margin = new Thickness(0, 2, 0, 7),
            Child = panel
        };
    }

    private static Border CreateBroWmsCompactLegendReportBlock(string modelType)
    {
        var rows = BroWmsCompactLegendRows(modelType);
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Legenda kaartlaag",
            Foreground = Brush("#333333"),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });

        if (UseOfficialInlineBroWmsLegend(modelType) && BroWmsLegendSources(modelType).Count > 0)
        {
            foreach (var (title, url) in BroWmsLegendSources(modelType))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = title,
                    Foreground = Brush("#333333"),
                    FontSize = 9,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 3)
                });
                panel.Children.Add(CreateReportRemoteLegendImage(url));
            }
        }
        else if (rows.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Geen compacte legenda beschikbaar voor deze kaartlaag.",
                Foreground = Brush("#8FA6B2"),
                FontSize = 9.5,
                TextWrapping = TextWrapping.Wrap
            });
        }
        else
        {
            var grid = new UniformGrid
            {
                Columns = rows.Count > 4 ? 2 : 1,
                Margin = new Thickness(0, 0, 0, 4)
            };
            foreach (var (color, label) in rows)
            {
                grid.Children.Add(CreateLegendRow(color, label));
            }

            panel.Children.Add(grid);
        }

        panel.Children.Add(new TextBlock
        {
            Text = UseOfficialInlineBroWmsLegend(modelType)
                ? "Legenda volgens de actieve BRO/PDOK kaartlaag."
                : "De legenda toont de relevante kaartklassen voor de rapportuitsnede, zodat kaart en legenda samen op A4 passen.",
            Foreground = Brush("#64748B"),
            FontSize = 8.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("#E5EDF3"),
            BorderThickness = new Thickness(0, 1, 0, 1),
            Padding = new Thickness(0, 8, 0, 8),
            Margin = new Thickness(0, 0, 0, 7),
            Child = panel
        };
    }

    private static Border CreateBroWmsReportLegendBlock(string modelType)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Legenda kaartlaag",
            Foreground = Brush("#333333"),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var sources = BroWmsLegendSources(modelType);
        if (sources.Count == 0)
        {
            panel.Children.Add(CreateReportStepPreviewText("Geen legenda beschikbaar voor deze kaartlaag."));
        }
        else
        {
            foreach (var (title, url) in sources)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = title,
                    Foreground = Brush("#333333"),
                    FontSize = 9.5,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 5, 0, 3)
                });
                panel.Children.Add(CreateReportRemoteLegendImage(url));
            }
        }

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("#E5EDF3"),
            BorderThickness = new Thickness(0, 1, 0, 1),
            Padding = new Thickness(0, 8, 0, 8),
            Margin = new Thickness(0, 8, 0, 6),
            Child = panel
        };
    }

    private IReadOnlyList<UIElement> CreateBroWmsReportLegendPages(int stepNumber, PrescanSubstep substep, string modelType, string legendTitle, string url)
    {
        if (!TryLoadReportLegendBitmap(url, out var bitmap))
        {
            var missingPanel = new StackPanel();
            missingPanel.Children.Add(CreateReportSubheading("Volledige legenda kaartlaag"));
            missingPanel.Children.Add(CreateReportStepPreviewText($"Legenda kon niet worden geladen: {legendTitle}."));
            missingPanel.Children.Add(CreateReportNote("Controleer de internetverbinding of open de kaartlaag opnieuw in de app-zijbalk."));
            var missingTitle = $"{DisplayReportSectionTitle(substep)} - legenda";
            return [CreateReportLandscapePage(stepNumber, missingTitle, CreateReportSection(stepNumber, missingTitle, missingPanel))];
        }

        var groups = CreateReportLegendTileGroups(bitmap);
        var pages = new List<UIElement>();
        var partCount = groups.Count;
        for (var index = 0; index < groups.Count; index++)
        {
            var panel = new StackPanel();
            panel.Children.Add(CreateReportSubheading("Volledige legenda kaartlaag"));
            panel.Children.Add(new TextBlock
            {
                Text = partCount > 1 ? $"{legendTitle} - deel {index + 1} van {partCount}" : legendTitle,
                Foreground = Brush("#333333"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            panel.Children.Add(CreateReportLegendTileWrap(bitmap, groups[index]));
            panel.Children.Add(CreateReportNote($"Bron: {DinoModelLabel(modelType)} via PDOK WMS GetLegendGraphic. Lange of brede legenda's worden automatisch opgeknipt zodat de volledige legenda zichtbaar blijft."));

            var pageTitle = partCount > 1
                ? $"{DisplayReportSectionTitle(substep)} - legenda {index + 1}"
                : $"{DisplayReportSectionTitle(substep)} - legenda";
            pages.Add(CreateReportLandscapePage(stepNumber, pageTitle, CreateReportSection(stepNumber, pageTitle, panel)));
        }

        return pages;
    }

    private IReadOnlyList<UIElement> CreateInlineBroProfileSourceReportPages(int stepNumber, PrescanSubstep substep, JsonElement root)
    {
        var modelType = SubsurfaceModelTypeForSubstep(substep.Number) ?? BroDgmModelType;
        var modelShortLabel = DinoModelShortLabel(modelType);
        if (!TryGetSubstepElement(root, substep.Number, out var substepElement))
        {
            var missingPanel = new StackPanel();
            missingPanel.Children.Add(CreateReportNote($"Sla {DisplayReportSectionTitle(substep)} op om geimporteerde {modelShortLabel} PDF-profielen in de rapportage op te nemen."));
            var missingTitle = $"{DisplayReportSectionTitle(substep)} - bronpunten";
            return [CreateReportPage(stepNumber, missingTitle, CreateReportSection(stepNumber, missingTitle, missingPanel))];
        }

        var data = JsonProperty(substepElement, "data") ?? default;
        var importedProfiles = ReadImportedBroProfileElements(data).Take(MaxBroImportedProfilesPerModel).ToList();
        var selectedSources = ReadSelectedBroSourceElements(data).Take(GetMaxBroSelectedSoundings(modelType)).ToList();
        var variantKey = GetSubsurfaceMapReportVariantKey(modelType);
        var mapPath = GetLiveMapReportPreviewImagePath(stepNumber, variantKey);

        if (importedProfiles.Count == 0 && selectedSources.Count == 0)
        {
            var emptyPanel = new StackPanel();
            emptyPanel.Children.Add(CreateLiveMapReportImageCard(
                $"GIS kaart met boorlijn en {modelShortLabel}-bronpunten",
                $"Kaartuitsnede van {DisplayReportSectionTitle(substep)}. Importeer maximaal twee officiele DINOloket/BRO PDF-profielen om de broninformatie in de rapportage te vullen.",
                mapPath,
                stepNumber,
                variantKey,
                724,
                260));
            emptyPanel.Children.Add(CreateReportNote($"Er zijn nog geen {modelShortLabel} PDF-profielen geimporteerd. Download het boormonsterprofiel uit DINOloket/BRO en importeer de PDF in deze substap."));
            var emptyTitle = $"{DisplayReportSectionTitle(substep)} - bronpunten";
            return [CreateReportPage(stepNumber, emptyTitle, CreateReportSection(stepNumber, emptyTitle, emptyPanel))];
        }

        var pages = new List<UIElement>();
        if (importedProfiles.Count > 0)
        {
            for (var index = 0; index < importedProfiles.Count; index++)
            {
                var profile = importedProfiles[index];
                var code = JsonText(profile, "identification", $"{modelShortLabel} profiel {index + 1}");
                var panel = new StackPanel();

                panel.Children.Add(CreateBroProfileLocationMapCard(
                    profile,
                    modelType,
                    mapPath,
                    stepNumber,
                    variantKey,
                    $"GIS kaart met boorlijn en {modelShortLabel}-bronpunt {code}",
                    $"Kaartuitsnede met de boorlijn als referentie en de geimporteerde {modelShortLabel}-bronlocatie op RD-coordinaten.",
                    724,
                    245));

                panel.Children.Add(CreateReportSubheading("Boormonsterprofiel en legenda"));
                var profileImagePath = GetOrCreateBroProfileImagePath(profile, modelType);
                panel.Children.Add(CreateLocalReportImageCard(
                    "Officieel boormonsterprofiel uit DINOloket/BRO",
                    "Visuele profielweergave uit de geimporteerde PDF, inclusief legenda.",
                    profileImagePath,
                    724,
                    425));

                panel.Children.Add(CreateReportSubheading("Officieel BRO/DINOloket PDF-profiel"));
                panel.Children.Add(CreateBroImportedProfileInformationBlock(profile));

                var summary = JsonText(profile, "extractedSummary", "");
                if (string.IsNullOrWhiteSpace(profileImagePath) && !string.IsNullOrWhiteSpace(summary))
                {
                    panel.Children.Add(CreateReportSubheading("Uitgelezen profieltekst"));
                    panel.Children.Add(CreateReportNote(ShortReportCell(summary, 1100)));
                }

                var title = $"{DisplayReportSectionTitle(substep)} - PDF-profiel {index + 1} {code}";
                pages.Add(CreateReportPage(stepNumber, title, CreateReportSection(stepNumber, title, panel)));
            }

            return pages;
        }

        for (var index = 0; index < selectedSources.Count; index++)
        {
            var source = selectedSources[index];
            var code = JsonText(source, "code", $"{modelShortLabel} bronpunt {index + 1}");
            var panel = new StackPanel();

            panel.Children.Add(CreateBroProfileLocationMapCard(
                source,
                modelType,
                mapPath,
                stepNumber,
                variantKey,
                $"GIS kaart met boorlijn en {modelShortLabel}-bronpunt {code}",
                $"Kaartuitsnede met de boorlijn als referentie en het geselecteerde {modelShortLabel}-bronpunt op de echte DINOloket-locatie.",
                724,
                245));

            panel.Children.Add(CreateReportSubheading("Boormonsterprofiel en interpretatie"));
            panel.Children.Add(CreateReportSoundingProfile(CreateBroSourceProfileBlock(source)));

            panel.Children.Add(CreateReportSubheading("Broninformatie"));
            panel.Children.Add(CreateBroSourceInformationBlock(source));

            var title = $"{DisplayReportSectionTitle(substep)} - bronpunt {index + 1} {code}";
            pages.Add(CreateReportPage(stepNumber, title, CreateReportSection(stepNumber, title, panel)));
        }

        return pages;
    }

    private IReadOnlyList<UIElement> CreateInlineSubsurfaceLegendReportPages(int stepNumber, PrescanSubstep substep)
    {
        var modelType = SubsurfaceModelTypeForSubstep(substep.Number) ?? BroDgmModelType;
        if (IsBroWmsMapLayer(modelType)) return [];

        return [];
    }

    private UIElement CreateInlineSubsurfaceMapReportPage(int stepNumber, PrescanSubstep substep, JsonElement data)
    {
        var modelType = SubsurfaceModelTypeForSubstep(substep.Number) ?? BroDgmModelType;
        var variantKey = GetSubsurfaceMapReportVariantKey(modelType);
        var mapPath = GetLiveMapReportPreviewImagePath(stepNumber, variantKey);
        var isWmsLayer = IsBroWmsMapLayer(modelType);
        var traceLength = JsonDouble(data, "traceLength", _selectedProject?.BoreLengthMeters ?? 0);
        var soundingCount = JsonInt(data, "soundingCount", GetBroSoundings(modelType).Count);
        var importedCount = ReadImportedBroProfileElements(data).Count;
        var selectedCount = Math.Max(importedCount, JsonInt(data, "selectedSoundingCount", GetSelectedBroSoundingIds(modelType).Count));
        var traceFindings = ReadBroWmsTraceFindings(data);

        var panel = new StackPanel();
        panel.Children.Add(CreateReportSubheading("GIS kaart"));
        panel.Children.Add(CreateLiveMapReportImageCard(
            $"{DinoModelLabel(modelType)} met boorlijn",
            isWmsLayer
                ? "Vastgezette GIS-kaartuitsnede met boorlijn en actieve BRO/PDOK WMS-kaartlaag."
                : "Vastgezette GIS-kaartuitsnede met boorlijn en losse BRO/DINOloket kaartpunten.",
            mapPath,
            stepNumber,
            variantKey,
            724,
            isWmsLayer ? 245 : 310));

        if (!isWmsLayer)
        {
            panel.Children.Add(CreateReportSubheading("Geselecteerde bronpunten"));
            panel.Children.Add(CreateSelectedBroSourceReportTable(data));
        }

        if (isWmsLayer)
        {
            panel.Children.Add(CreateBroWmsApiReportBlock(modelType, traceLength, mapPath, traceFindings));
            panel.Children.Add(CreateBroWmsCompactLegendReportBlock(modelType));
        }
        else
        {
            panel.Children.Add(CreateReportKeyValues(
                ("Model", DinoModelLabel(modelType)),
                ("Bron", "BRO/DINOloket datasetpunten"),
                ("Weergave", "Losse kaartpunten met handmatige selectie"),
                ("Boorlijnreferentie", traceLength > 0 ? $"{traceLength:N1} m beschikbaar" : "Geen boorlijnreferentie"),
                ("Kaartstatus", string.IsNullOrWhiteSpace(mapPath) ? "Nog opslaan in deze substap" : "Opgeslagen voor rapportage"),
                ("Bronpunten op kaart", soundingCount.ToString(CultureInfo.InvariantCulture)),
                ("Geselecteerd", selectedCount.ToString(CultureInfo.InvariantCulture))));
            panel.Children.Add(CreateBroPointReportLegendBlock(modelType));
        }

        panel.Children.Add(CreateReportNote(string.IsNullOrWhiteSpace(mapPath)
            ? $"Open {DisplayReportSectionTitle(substep)}, zet de juiste kaartlaag aan en gebruik 'Opslaan in rapport' om dit kaartbeeld definitief in de eindrapportage op te nemen."
            : "Deze kaartbijlage gebruikt uitsluitend de opgeslagen kaartuitsnede van deze ondergrond-substap. Er wordt geen kaartbeeld van een andere processtap als fallback gebruikt."));

        var sectionTitle = $"{DisplayReportSectionTitle(substep)} - GIS kaart";
        return CreateReportPage(stepNumber, sectionTitle, CreateReportSection(stepNumber, sectionTitle, panel));
    }

    private static Border CreateSelectedBroSourceReportTable(JsonElement data)
    {
        var importedProfiles = ReadImportedBroProfileElements(data);
        if (importedProfiles.Count > 0)
        {
            var importedTable = CreateReportTable(["PDF-profiel", "RD X", "RD Y", "Maaiveld", "Einddiepte", "Bronbestand"]);
            foreach (var profile in importedProfiles)
            {
                AddReportTableRow(importedTable, [
                    JsonText(profile, "identification", JsonText(profile, "code", "-")),
                    FormatReportNumber(JsonDouble(profile, "x", double.NaN), 0),
                    FormatReportNumber(JsonDouble(profile, "y", double.NaN), 0),
                    FormatReportNumber(JsonDouble(profile, "surfaceNap", double.NaN), 2, " m NAP"),
                    FormatReportNumber(JsonDouble(profile, "depthBottom", double.NaN), 2, " m"),
                    JsonText(profile, "sourceFile", "-")
                ]);
            }

            return importedTable;
        }

        var selectedSoundings = JsonProperty(data, "selectedSoundings") is { ValueKind: JsonValueKind.Array } selectedArray
            ? selectedArray.EnumerateArray().ToList()
            : [];

        var table = CreateReportTable(["Bronpunt", "RD X", "RD Y", "Maaiveld", "Einddiepte", "Offset"]);
        foreach (var selected in selectedSoundings)
        {
            AddReportTableRow(table, [
                JsonText(selected, "code", "-"),
                FormatReportNumber(JsonDouble(selected, "x", double.NaN), 0),
                FormatReportNumber(JsonDouble(selected, "y", double.NaN), 0),
                FormatReportNumber(JsonDouble(selected, "surfaceNap", double.NaN), 2, " m NAP"),
                FormatReportNumber(JsonDouble(selected, "endDepth", double.NaN), 2, " m"),
                FormatReportNumber(JsonDouble(selected, "offset", double.NaN), 1, " m")
            ]);
        }

        if (selectedSoundings.Count == 0)
        {
            AddReportTableRow(table, ["-", "-", "-", "-", "-", "Geen opgeslagen bronpunten"]);
        }

        return table;
    }

    private static DrawingBitmap CropBroProfileBitmap(DrawingBitmap bitmap, string modelType, string identification)
    {
        var bounds = FindNonWhiteBounds(bitmap);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return new DrawingBitmap(bitmap);
        }

        var margin = 26;
        var crop = new DrawingRectangle(
            Math.Max(0, bounds.Left - margin),
            Math.Max(0, bounds.Top - margin),
            Math.Min(bitmap.Width - Math.Max(0, bounds.Left - margin), bounds.Width + margin * 2),
            Math.Min(bitmap.Height - Math.Max(0, bounds.Top - margin), bounds.Height + margin * 2));

        return bitmap.Clone(crop, DrawingPixelFormat.Format32bppArgb);
    }

    private static string DetectBroProfileModelType(string modelName, string fallbackModelType)
    {
        if (modelName.Contains("REGIS", StringComparison.OrdinalIgnoreCase)) return BroRegisModelType;
        if (modelName.Contains("DGM", StringComparison.OrdinalIgnoreCase)) return BroDgmModelType;
        return NormalizeBroModelType(fallbackModelType);
    }

    private static void DrawCenteredBroLocationMarker(string path, string code, string modelLabel)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

        var tempPath = path + ".tmp.png";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        using (var bitmap = new DrawingBitmap(path))
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        using (var outerBrush = new System.Drawing.SolidBrush(System.Drawing.ColorTranslator.FromHtml("#0F766E")))
        using (var whiteBrush = new System.Drawing.SolidBrush(System.Drawing.Color.White))
        using (var orangeBrush = new System.Drawing.SolidBrush(System.Drawing.ColorTranslator.FromHtml("#F59E0B")))
        using (var outerWhitePen = new System.Drawing.Pen(System.Drawing.Color.White, 4f))
        using (var tealPen = new System.Drawing.Pen(System.Drawing.ColorTranslator.FromHtml("#0F766E"), 3f))
        using (var darkOrangePen = new System.Drawing.Pen(System.Drawing.ColorTranslator.FromHtml("#7C2D12"), 2f))
        using (var labelBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(244, 248, 250, 252)))
        using (var labelPen = new System.Drawing.Pen(System.Drawing.ColorTranslator.FromHtml("#CBD5E1"), 1.5f))
        using (var titleBrush = new System.Drawing.SolidBrush(System.Drawing.ColorTranslator.FromHtml("#0F172A")))
        using (var subtitleBrush = new System.Drawing.SolidBrush(System.Drawing.ColorTranslator.FromHtml("#475569")))
        using (var titleFont = new System.Drawing.Font("Segoe UI", 18f, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel))
        using (var subtitleFont = new System.Drawing.Font("Segoe UI", 13f, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel))
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var centerX = bitmap.Width / 2f;
            var centerY = bitmap.Height / 2f;
            graphics.FillEllipse(outerBrush, centerX - 18, centerY - 18, 36, 36);
            graphics.DrawEllipse(outerWhitePen, centerX - 18, centerY - 18, 36, 36);
            graphics.FillEllipse(whiteBrush, centerX - 12, centerY - 12, 24, 24);
            graphics.DrawEllipse(tealPen, centerX - 12, centerY - 12, 24, 24);
            graphics.FillEllipse(orangeBrush, centerX - 7, centerY - 7, 14, 14);
            graphics.DrawEllipse(darkOrangePen, centerX - 7, centerY - 7, 14, 14);

            var labelWidth = 214f;
            var labelHeight = 52f;
            var labelLeft = Math.Min(bitmap.Width - labelWidth - 12, centerX + 22);
            if (labelLeft < centerX + 22) labelLeft = Math.Max(12, centerX - labelWidth - 22);
            var labelTop = Math.Clamp(centerY - 30, 12, bitmap.Height - labelHeight - 12);
            var labelRect = new System.Drawing.RectangleF(labelLeft, labelTop, labelWidth, labelHeight);
            graphics.FillRectangle(labelBrush, labelRect);
            graphics.DrawRectangle(labelPen, labelRect.X, labelRect.Y, labelRect.Width, labelRect.Height);
            graphics.DrawString(code, titleFont, titleBrush, labelRect.X + 12, labelRect.Y + 7);
            graphics.DrawString($"{modelLabel} bronlocatie", subtitleFont, subtitleBrush, labelRect.X + 12, labelRect.Y + 31);

            bitmap.Save(tempPath, DrawingImageFormat.Png);
        }

        File.Copy(tempPath, path, overwrite: true);
        File.Delete(tempPath);
    }

    private async System.Threading.Tasks.Task EnsureBroModelProfileForSelectionAsync(string modelType, string soundingId)
    {
        modelType = NormalizeBroModelType(modelType);
        if (!IsBroVirtualColumnModel(modelType) || string.IsNullOrWhiteSpace(soundingId)) return;

        var soundings = GetBroSoundings(modelType).ToList();
        var index = soundings.FindIndex(item => item.Id.Equals(soundingId, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return;

        var selected = soundings[index];
        if (selected.ProfileIntervals.Count > 0 && selected.SurfaceNap is not null) return;

        SetBroLoadStatus(modelType, $"{selected.Code}: modelprofiel wordt opgehaald op echte bronpuntlocatie...");
        RefreshUndergroundAnalysisSidebarPanel();
        BeginBackgroundTask($"{selected.Code} profiel ophalen...");
        try
        {
            var profile = await FetchBroModelProfileAsync(
                new RdPoint(selected.X, selected.Y),
                selected.Distance,
                selected.Offset,
                modelType,
                index + 1);

            if (profile is null)
            {
                SetBroLoadStatus(modelType, $"{selected.Code}: geen modelprofiel ontvangen voor deze bronpuntlocatie.");
                return;
            }

            soundings[index] = profile with
            {
                Id = selected.Id,
                Code = selected.Code,
                Name = selected.Name,
                X = selected.X,
                Y = selected.Y,
                Lon = selected.Lon,
                Lat = selected.Lat,
                Distance = selected.Distance,
                Offset = selected.Offset,
                Source = $"{selected.Source}; profiel via DINOloket modelviewer",
                Status = "Bronpunt geselecteerd, modelprofiel beschikbaar"
            };

            SetBroSoundings(modelType, soundings);
            SetBroLoadStatus(modelType, $"{selected.Code}: modelprofiel opgehaald en gekoppeld aan de selectie.");
        }
        catch (Exception exception)
        {
            SetBroLoadStatus(modelType, $"{selected.Code}: modelprofiel ophalen mislukt ({exception.Message}).");
            AppendMapDiagnostic($"BRO-modelprofiel ophalen mislukt: {exception.Message}");
        }
        finally
        {
            EndBackgroundTask();
            RefreshUndergroundAnalysisSidebarPanel();
            SendBroSoundingsToMap();
            SaveStepReportDataForStep(6);
            RefreshWorkflowReportStatus(6);
            RefreshInlineReportPreviewIfVisible();
        }
    }

    private async System.Threading.Tasks.Task<IReadOnlyList<BroSoundingPoint>> FetchBroCptSoundingsAsync(IReadOnlyList<TracePointRow> traceRows)
    {
        foreach (var bufferMeters in new[] { BroCptSearchBufferMeters, 6000d, 10000d })
        {
            var bounds = BuildBroCptSearchBounds(traceRows, bufferMeters);
            if (bounds is null) return [];

            var payload = JsonSerializer.Serialize(new
            {
                area = new
                {
                    boundingBox = new
                    {
                        lowerCorner = new { lat = bounds.MinLat, lon = bounds.MinLon },
                        upperCorner = new { lat = bounds.MaxLat, lon = bounds.MaxLon }
                    }
                }
            }, JsonOptions);

            using var request = new HttpRequestMessage(HttpMethod.Post, BroCptCharacteristicsEndpoint)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));

            using var response = await BroCptHttpClient.SendAsync(request);
            var xml = await response.Content.ReadAsStringAsync();
            response.EnsureSuccessStatusCode();

            var result = ParseBroCptCharacteristicsXml(xml, traceRows);
            if (result.Count > 0 || bufferMeters >= 10000d) return result;
        }

        return [];
    }

    private async System.Threading.Tasks.Task<BroSoundingPoint?> FetchBroModelProfileAsync(RdPoint rd, double distance, double offset, string modelType, int index)
    {
        var modelName = DinoModelName(modelType);
        var payload = JsonSerializer.Serialize(new
        {
            language = "nl",
            modelType,
            model = modelName,
            depthReference = "MV",
            version = DinoModelVersion(modelType),
            resolution = "100",
            xcoordinate = rd.X,
            ycoordinate = rd.Y
        }, JsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, DinoVirtualColumnsEndpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await DinoModelHttpClient.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        if (JsonInt(root, "status", 0) != 1) return null;

        var intervals = ParseBroModelIntervals(root, modelType);
        var wgs = RdToWgs84(rd.X, rd.Y);
        var surface = JsonDoubleNullable(root, "surfacelevelHeight");
        var bottomDepth = JsonDoubleNullable(root, "bottomDepth");
        var code = $"{DinoModelShortLabel(modelType)}-{index}";
        var name = $"{DinoModelLabel(modelType)} profiel {index}";

        return new BroSoundingPoint(
            $"{modelType}-{Math.Round(rd.X, 2):0.##}-{Math.Round(rd.Y, 2):0.##}-{index}",
            code,
            name,
            Math.Round(rd.X, 3),
            Math.Round(rd.Y, 3),
            Math.Round(wgs[0], 8),
            Math.Round(wgs[1], 8),
            Math.Round(distance, 2),
            Math.Round(offset, 2),
            surface,
            bottomDepth,
            BuildBroModelSummary(intervals, modelType),
            "DINOloket modelviewer",
            DateTime.Today.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
            "Modelprofiel beschikbaar",
            modelType,
            DinoModelLabel(modelType),
            intervals);
    }

    private async System.Threading.Tasks.Task<IReadOnlyList<BroSoundingPoint>> FetchBroModelSoundingsAsync(IReadOnlyList<TracePointRow> traceRows, string modelType)
    {
        modelType = NormalizeBroModelType(modelType);
        if (traceRows.Count < 2) return [];

        if (DinoModelPointLayerId(modelType) is not null)
        {
            return await FetchDinoModelPointLayerSoundingsAsync(traceRows, modelType);
        }

        return [];
    }

    private string? FindBroModelTypeBySoundingId(string id)
    {
        foreach (var pair in _broModelSoundings)
        {
            if (pair.Value.Any(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            {
                return pair.Key;
            }
        }

        return null;
    }

    private static string FormatBroMeters(double value) => $"{value:N1} m";

    private static string FormatBroNullable(double? value, int decimals, string suffix = "")
    {
        if (value is not double number || !double.IsFinite(number)) return "-";
        var format = decimals <= 0 ? "N0" : $"N{decimals}";
        return string.IsNullOrWhiteSpace(suffix)
            ? number.ToString(format, CultureInfo.CurrentCulture)
            : $"{number.ToString(format, CultureInfo.CurrentCulture)}{suffix}";
    }

    private static string FormatBroOptionalMeters(double? value, string suffix = "m") =>
        value is double number && double.IsFinite(number) ? $"{number:N2} {suffix}" : "-";

    private static string FormatBroWmsTraceFindingsForReport(IReadOnlyList<BroWmsTraceFinding> traceFindings)
    {
        if (traceFindings.Count == 0) return "Nog niet uitgelezen";

        return string.Join("; ", traceFindings
            .GroupBy(finding => finding.Label, StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .Select(group =>
            {
                var stations = group
                    .Select(finding => finding.Station)
                    .Where(double.IsFinite)
                    .DistinctBy(station => Math.Round(station / 5d))
                    .Take(3)
                    .Select(station => $"{station:N0} m");
                return $"{group.Key} ({string.Join(", ", stations)})";
            }));
    }

    private string GetActiveUndergroundModelType()
    {
        if (_selectedStep?.Number != 6) return NormalizeBroModelType(_selectedBroModelType);
        return (_selectedSubstep?.Number ?? "6.1") switch
        {
            "6.2" => BroRegisModelType,
            "6.3" => BroGeomorphologyModelType,
            "6.4" => BroSoilMapModelType,
            "6.5" or "6.5.1" => BroGroundwaterGhgModelType,
            "6.5.2" => BroGroundwaterGlgModelType,
            "6.5.3" => BroGroundwaterGvgModelType,
            "6.5.4" => BroGroundwaterGtModelType,
            "6.5.5" => BroGroundwaterDocumentationModelType,
            _ => BroDgmModelType
        };
    }

    private string GetBroLoadStatus(string? modelType = null)
    {
        var normalized = NormalizeBroModelType(modelType ?? GetActiveUndergroundModelType());
        return _broModelLoadStatuses.TryGetValue(normalized, out var status) ? status : "Nog niet geladen.";
    }

    private string GetBroProfileImportDirectory(string modelType)
    {
        if (_selectedProject is null) return "";
        var directory = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Borevexa",
            "BroProfiles",
            _selectedProject.Id.ToString("N"),
            NormalizeBroModelType(modelType).ToLowerInvariant());
        Directory.CreateDirectory(directory);
        return directory;
    }

    private IReadOnlyList<BroSoundingPoint> GetBroSoundings(string? modelType = null)
    {
        if (_selectedProject is null) return [];
        var normalized = NormalizeBroModelType(modelType ?? GetActiveUndergroundModelType());
        return _broModelSoundings.TryGetValue(normalized, out var soundings) ? soundings : [];
    }

    private IReadOnlyList<BroSoundingPoint> GetBroSoundingsForUndergroundMap(string activeModelType)
    {
        activeModelType = NormalizeBroModelType(activeModelType);
        if (IsBroPointDatasetModel(activeModelType))
        {
            return GetBroSoundings(activeModelType)
                .OrderBy(item => Math.Abs(item.Offset))
                .ThenBy(item => item.Distance)
                .ThenBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return BroUndergroundModelTypes
            .SelectMany(GetBroSoundings)
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.ModelType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private int GetMaxBroSelectedSoundings(string? modelType = null)
    {
        var normalized = NormalizeBroModelType(modelType ?? GetActiveUndergroundModelType());
        return normalized.Equals(BroDgmModelType, StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals(BroRegisModelType, StringComparison.OrdinalIgnoreCase)
            ? MaxBroDgmReportSoundings
            : 1;
    }

    private static string GetOrCreateBroProfileImagePath(JsonElement profile, string modelType)
    {
        var imagePath = JsonText(profile, "profileImagePath", "");
        if (!ShouldRegenerateBroProfileImage(imagePath)) return imagePath;

        var pdfPath = JsonText(profile, "localPath", "");
        if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath)) return imagePath;

        var identification = JsonText(profile, "identification", JsonText(profile, "code", ""));
        return RenderBroImportedProfileImage(pdfPath, modelType, identification);
    }

    private string GetOrCreateBroProfileLocationMapPath(JsonElement profile, string modelType, string liveMapPath)
    {
        if (_selectedProject is null) return liveMapPath;

        if (!TryReadBroProfileRdPoint(profile, out var sourcePoint) || !IsValidRdPoint(sourcePoint))
        {
            return liveMapPath;
        }

        var normalizedModel = NormalizeBroModelType(modelType);
        var code = FirstNonEmptyText(
            JsonText(profile, "identification", ""),
            JsonText(profile, "code", ""),
            $"{DinoModelShortLabel(normalizedModel)} bronpunt");
        var directory = System.IO.Path.Combine(GetBroProfileImportDirectory(normalizedModel), "maps");
        Directory.CreateDirectory(directory);

        var fileName = $"{ToSafeFileName(code)}-{normalizedModel.ToLowerInvariant()}-{Math.Round(sourcePoint.X):0}-{Math.Round(sourcePoint.Y):0}-locatiekaart-centered-v6.png";
        var path = System.IO.Path.Combine(directory, fileName);
        var traceRows = NormalizeTraceRowsToRd(GetTraceRowsForProfile())
            .Where(row => IsValidRdPoint(new RdPoint(row.X, row.Y)))
            .ToList();

        try
        {
            RenderBroProfileLocationMapImage(path, code, normalizedModel, sourcePoint, traceRows);
            return path;
        }
        catch
        {
            return File.Exists(path) ? path : liveMapPath;
        }
    }

    private string? GetSelectedBroSoundingId(string? modelType = null)
    {
        return GetSelectedBroSoundingIds(modelType).FirstOrDefault();
    }

    private IReadOnlyList<string> GetSelectedBroSoundingIds(string? modelType = null)
    {
        var normalized = NormalizeBroModelType(modelType ?? GetActiveUndergroundModelType());
        if (_selectedBroModelSoundingIdLists.TryGetValue(normalized, out var ids) && ids.Count > 0)
        {
            return ids.Take(GetMaxBroSelectedSoundings(normalized)).ToList();
        }

        if (_selectedBroModelSoundingIds.TryGetValue(normalized, out var legacyId) && !string.IsNullOrWhiteSpace(legacyId))
        {
            return [legacyId];
        }

        return [];
    }

    private static string GetSubsurfaceMapReportVariantKey(string modelType)
    {
        var overlayKey = BroWmsOverlayKey(modelType);
        return !string.IsNullOrWhiteSpace(overlayKey)
            ? overlayKey
            : NormalizeBroModelType(modelType).ToLowerInvariant();
    }

    private async void HandleBroSoundingSelected(string message)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            var id = JsonText(document.RootElement, "id", "");
            if (string.IsNullOrWhiteSpace(id)) return;

            var modelType = NormalizeBroModelType(JsonText(document.RootElement, "modelType", GetActiveUndergroundModelType()));
            if (!GetBroSoundings(modelType).Any(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            {
                modelType = FindBroModelTypeBySoundingId(id) ?? GetActiveUndergroundModelType();
            }

            _selectedBroModelType = modelType;
            var selectedIds = ToggleSelectedBroSoundingId(modelType, id);
            var sounding = GetBroSoundings(modelType).FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            RefreshUndergroundAnalysisSidebarPanel();
            SendBroSoundingsToMap();
            if (selectedIds.Any(selectedId => selectedId.Equals(id, StringComparison.OrdinalIgnoreCase)))
            {
                await EnsureBroModelProfileForSelectionAsync(modelType, id);
                sounding = GetBroSoundings(modelType).FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            }

            SaveStepReportDataForStep(6);
            RefreshWorkflowReportStatus(6);
            RefreshInlineReportPreviewIfVisible();
            var selectionText = $"{selectedIds.Count}/{GetMaxBroSelectedSoundings(modelType)}";
            OutputText.Text = sounding is null
                ? $"{DinoModelLabel(modelType)} profielselectie\n\nDe kaartselectie is bijgewerkt ({selectionText})."
                : $"{DinoModelLabel(modelType)} profielselectie\n\n{sounding.Code} staat op zijn echte bronlocatie en is bijgewerkt voor de rapportage. Selectie: {selectionText} punt(en). Afstand tot boorlijn {FormatBroMeters(Math.Abs(sounding.Offset))}.";
        }
        catch (Exception exception)
        {
            AppendMapDiagnostic($"BRO-selectie verwerken mislukt: {exception.Message}");
        }
    }

    private BroImportedProfileRecord ImportBroProfilePdf(string sourcePath, string fallbackModelType)
    {
        if (_selectedProject is null) throw new InvalidOperationException("Geen project actief.");
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("BRO PDF niet gevonden.", sourcePath);

        var parsed = ParseBroProfilePdf(sourcePath, fallbackModelType);
        var modelType = NormalizeBroModelType(parsed.ModelType);
        var id = Guid.NewGuid();
        var safeName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{id:N}_{ToSafeFileName(System.IO.Path.GetFileNameWithoutExtension(sourcePath))}.pdf";
        var localPath = System.IO.Path.Combine(GetBroProfileImportDirectory(modelType), safeName);
        File.Copy(sourcePath, localPath, overwrite: true);
        var profileImagePath = RenderBroImportedProfileImage(localPath, modelType, parsed.Identification);

        return parsed with
        {
            Id = id,
            SourcePath = sourcePath,
            LocalPath = localPath,
            ProfileImagePath = profileImagePath,
            FileName = System.IO.Path.GetFileName(sourcePath),
            ImportedAt = DateTimeOffset.Now
        };
    }

    private void ImportBroProfilePdfsForActiveModel()
    {
        if (_selectedProject is null || _selectedStep?.Number != 6) return;
        var activeModelType = GetActiveUndergroundModelType();
        if (!SupportsImportedBroProfiles(activeModelType))
        {
            OutputText.Text = "BRO PDF import\n\nPDF-profielimport is ingericht voor 6.1 BRO DGM en 6.2 REGIS II.";
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = $"Importeer {DinoModelLabel(activeModelType)} boormonsterprofiel PDF",
            Filter = "BRO/DINOloket PDF (*.pdf)|*.pdf",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true) return;

        var importedByModel = new Dictionary<string, List<BroImportedProfileRecord>>(StringComparer.OrdinalIgnoreCase);
        var failures = new List<string>();
        foreach (var path in dialog.FileNames)
        {
            try
            {
                var imported = ImportBroProfilePdf(path, activeModelType);
                if (!SupportsImportedBroProfiles(imported.ModelType))
                {
                    failures.Add($"{System.IO.Path.GetFileName(path)}: geen DGM/REGIS profiel herkend.");
                    continue;
                }

                if (!importedByModel.TryGetValue(imported.ModelType, out var list))
                {
                    list = ReadBroImportedProfiles(imported.ModelType).ToList();
                    importedByModel[imported.ModelType] = list;
                }

                list.RemoveAll(existing =>
                    existing.Identification.Equals(imported.Identification, StringComparison.OrdinalIgnoreCase) ||
                    existing.FileName.Equals(imported.FileName, StringComparison.OrdinalIgnoreCase));
                list.Add(imported);
            }
            catch (Exception exception)
            {
                failures.Add($"{System.IO.Path.GetFileName(path)}: {exception.Message}");
            }
        }

        foreach (var (modelType, profiles) in importedByModel)
        {
            SaveBroImportedProfiles(modelType, profiles);
            SetBroLoadStatus(modelType, $"{profiles.Count} {DinoModelShortLabel(modelType)} PDF-profiel(en) geimporteerd.");
        }

        RefreshUndergroundAnalysisSidebarPanel();
        SendBroSoundingsToMap();
        SaveStepReportDataForStep(6);
        RefreshWorkflowReportStatus(6);
        QueueLiveMapReportCapture(6);
        RefreshInlineReportPreviewIfVisible();

        var importedCount = importedByModel.Values.Sum(list => Math.Min(list.Count, MaxBroImportedProfilesPerModel));
        OutputText.Text = failures.Count == 0
            ? $"BRO PDF import voltooid\n\n{importedCount} PDF-profiel(en) zijn opgeslagen en worden meegenomen in de rapportage."
            : $"BRO PDF import deels voltooid\n\nGeimporteerd: {importedCount}\n\nMeldingen:\n{string.Join("\n", failures)}";
    }

    private static bool IsBroPointDatasetModel(string modelType) =>
        DinoModelPointLayerId(modelType) is not null;

    private static bool IsBroSoundingFeature(GeoJsonFeature feature, ProjectMapLayer layer)
    {
        if (!feature.Geometry.Type.Equals("Point", StringComparison.OrdinalIgnoreCase)) return false;
        var haystack = $"{layer.Type} {layer.Name} {string.Join(" ", feature.Properties.Select(kv => $"{kv.Key} {kv.Value}"))}";
        return ContainsAny(haystack, "BRO", "DINO", "sonder", "CPT", "GEF", "grondonderzoek", "geotechn", "maaiveld", "einddiepte");
    }

    private static bool IsBroSoundingFile(ProjectFileRecord file) =>
        ContainsAny($"{file.FileType} {file.DisplayName} {file.LocalPath}", "BRO", "DINO", "sonder", "CPT", "GEF", "grondonderzoek", "geotechn");

    private static bool IsBroSoundingLayer(ProjectMapLayer layer) =>
        ContainsAny($"{layer.Type} {layer.Name}", "BRO", "DINO", "sonder", "CPT", "GEF", "grondonderzoek", "geotechn");

    private static bool IsBroVirtualColumnModel(string modelType)
    {
        var normalized = NormalizeBroModelType(modelType);
        return normalized == BroDgmModelType || normalized == BroRegisModelType;
    }

    private static bool IsBroWmsMapLayer(string modelType)
    {
        var normalized = NormalizeBroModelType(modelType);
        return normalized is BroGeomorphologyModelType
            or BroSoilMapModelType
            or BroGroundwaterGhgModelType
            or BroGroundwaterGlgModelType
            or BroGroundwaterGvgModelType
            or BroGroundwaterGtModelType
            or BroGroundwaterDocumentationModelType;
    }

    private static bool IsSubsurfaceMapReportSubstep(int stepNumber, string substepNumber) =>
        stepNumber == 6 && SubsurfaceModelTypeForSubstep(substepNumber) is not null;

    private async System.Threading.Tasks.Task LoadBroModelForStepSixAsync(string loadModelType, bool fit, bool initiatedByUser)
    {
        loadModelType = NormalizeBroModelType(loadModelType);
        if (_selectedProject is null || _selectedStep?.Number != 6) return;
        if (IsBroWmsMapLayer(loadModelType)) return;
        if (!_broLoadingModelTypes.Add(loadModelType)) return;

        SetBroLoadStatus(loadModelType, $"{DinoModelLabel(loadModelType)} wordt geraadpleegd...");
        RefreshUndergroundAnalysisSidebarPanel();
        OutputText.Text = initiatedByUser
            ? $"{DinoModelLabel(loadModelType)} laden\n\nIk haal de echte DINOloket/BRO-booronderzoekpunten in het projectgebied op. De boorlijn blijft alleen zichtbaar als referentie."
            : $"{DinoModelLabel(loadModelType)} automatisch laden\n\nIk haal de echte DGM-bronpunten uit DINOloket op; er worden geen modelpunten op de boorlijn gegenereerd.";

        BeginBackgroundTask($"{DinoModelLabel(loadModelType)} laden...");
        try
        {
            var traceRows = GetTraceRowsForProfile();
            if (traceRows.Count < 2)
            {
                SetBroLoadStatus(loadModelType, "Geen opgeslagen boorlijn gevonden. Sla eerst stap 3.1 op en open daarna 6.1 opnieuw.");
                RefreshUndergroundAnalysisSidebarPanel();
                OutputText.Text = $"{DinoModelLabel(loadModelType)} niet geladen\n\nEr is nog geen opgeslagen boorlijn beschikbaar voor het zoekgebied.";
                return;
            }

            var loadResult = await LoadBroSoundingsAsync(traceRows, loadModelType);
            SetBroSoundings(loadModelType, loadResult.Soundings);
            SetBroLoadStatus(loadModelType, loadResult.Status);
            var soundings = GetBroSoundings(loadModelType);
            var validSelectedIds = GetSelectedBroSoundingIds(loadModelType)
                .Where(id => soundings.Any(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            SetSelectedBroSoundingIds(loadModelType, validSelectedIds);

            RefreshUndergroundAnalysisSidebarPanel();
            SendProjectLayersToMap();
            SendBroSoundingsToMap(fit);
            SaveStepReportDataForStep(6);
            RefreshWorkflowReportStatus(6);
            RefreshInlineReportPreviewIfVisible();
            OutputText.Text = soundings.Count == 0
                ? $"Geen {DinoModelShortLabel(loadModelType)}-bronpunten gevonden\n\n{GetBroLoadStatus(loadModelType)}"
                : $"{DinoModelLabel(loadModelType)} geladen\n\n{soundings.Count} echte DINOloket-bronpunten staan op hun eigen locatie op de kaart. Klik zelf op het gewenste bolletje voor de rapportage.\n{GetBroLoadStatus(loadModelType)}";
        }
        catch (Exception exception)
        {
            SetBroLoadStatus(loadModelType, $"Laden mislukt: {exception.Message}");
            RefreshUndergroundAnalysisSidebarPanel();
            OutputText.Text = $"{DinoModelLabel(loadModelType)} laden mislukt\n\n{exception.Message}";
        }
        finally
        {
            _broLoadingModelTypes.Remove(loadModelType);
            EndBackgroundTask();
        }
    }

    private async System.Threading.Tasks.Task<BroSoundingLoadResult> LoadBroSoundingsAsync(IReadOnlyList<TracePointRow> traceRows, string modelType)
    {
        modelType = NormalizeBroModelType(modelType);
        IReadOnlyList<BroSoundingPoint> local = modelType.Equals("CPT", StringComparison.OrdinalIgnoreCase)
            ? BuildBroSoundings(traceRows)
            : [];
        var remote = Array.Empty<BroSoundingPoint>() as IReadOnlyList<BroSoundingPoint>;
        var status = "";

        try
        {
            if (modelType.Equals("CPT", StringComparison.OrdinalIgnoreCase))
            {
                remote = await FetchBroCptSoundingsAsync(traceRows);
                status = remote.Count > 0
                    ? $"BRO CPT-service: {remote.Count} sondering(en) gevonden; lokale bestanden: {local.Count}."
                    : $"BRO CPT-service: geen sonderingen gevonden in het zoekgebied; lokale bestanden: {local.Count}.";
            }
            else
            {
                remote = await FetchBroModelSoundingsAsync(traceRows, modelType);
                status = remote.Count > 0
                    ? $"DINOloket {DinoModelLabel(modelType)}: {remote.Count} echte booronderzoekpunt(en) geladen uit de bronlaag; punten staan op hun eigen RD-locatie."
                    : $"DINOloket {DinoModelLabel(modelType)}: geen booronderzoekpunten gevonden in het ruime projectgebied.";
            }
        }
        catch (Exception exception)
        {
            status = modelType.Equals("CPT", StringComparison.OrdinalIgnoreCase)
                ? $"{DinoModelLabel(modelType)} laden mislukt ({exception.Message}); lokale bestanden: {local.Count}."
                : $"{DinoModelLabel(modelType)} laden mislukt ({exception.Message}).";
            AppendMapDiagnostic($"BRO/DINO laden mislukt: {exception.Message}");
        }

        var combined = remote
            .Concat(local)
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.ModelType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .Take(MaxDinoModelPointResults)
            .ToList();

        if (combined.Count == 0)
        {
            status += " Geen punten om op de kaart te tonen.";
        }

        return new BroSoundingLoadResult(combined, remote.Count, local.Count, status);
    }

    private void MergeImportedBroProfilesIntoSoundings(string modelType, IReadOnlyList<BroImportedProfileRecord>? profiles = null, bool selectImported = false)
    {
        var normalized = NormalizeBroModelType(modelType);
        if (!SupportsImportedBroProfiles(normalized)) return;

        profiles ??= ReadBroImportedProfiles(normalized);
        if (profiles.Count == 0) return;

        var current = GetBroSoundings(normalized).ToList();
        var traceRows = GetTraceRowsForProfile();
        var traceDistances = traceRows.Count >= 2 ? BuildTraceDistances(traceRows) : [];
        foreach (var profile in profiles)
        {
            if (current.Any(item => item.Id.Equals(profile.Identification, StringComparison.OrdinalIgnoreCase) ||
                                    item.Code.Equals(profile.Identification, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var sounding = BuildBroSoundingPointFromImportedProfile(profile, traceRows, traceDistances);
            if (sounding is not null)
            {
                current.Add(sounding);
            }
        }

        SetBroSoundings(normalized, current
            .OrderBy(item => Math.Abs(item.Offset))
            .ThenBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .ToList());

        if (selectImported)
        {
            SetSelectedBroSoundingIds(normalized, profiles.Select(profile => profile.Identification));
        }
    }

    private static string NormalizeBroModelType(string value)
    {
        value ??= "";
        var token = value.Trim().Replace("-", "_").Replace(" ", "_");
        if (value.Equals("REGIS", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("RGS", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("REGIS", StringComparison.OrdinalIgnoreCase)) return BroRegisModelType;
        if (value.Equals("GMF", StringComparison.OrdinalIgnoreCase) || value.Contains("GEOMORF", StringComparison.OrdinalIgnoreCase)) return BroGeomorphologyModelType;
        if (value.Equals("BDM", StringComparison.OrdinalIgnoreCase) || value.Contains("BODEM", StringComparison.OrdinalIgnoreCase)) return BroSoilMapModelType;
        if (token.Equals(BroGroundwaterGlgModelType, StringComparison.OrdinalIgnoreCase) || value.Contains("GLG", StringComparison.OrdinalIgnoreCase)) return BroGroundwaterGlgModelType;
        if (token.Equals(BroGroundwaterGvgModelType, StringComparison.OrdinalIgnoreCase) || value.Contains("GVG", StringComparison.OrdinalIgnoreCase)) return BroGroundwaterGvgModelType;
        if (token.Equals(BroGroundwaterGtModelType, StringComparison.OrdinalIgnoreCase) ||
            value.Contains("GRONDWATERTRAP", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("GRONDWATERTRAPPEN", StringComparison.OrdinalIgnoreCase)) return BroGroundwaterGtModelType;
        if (token.Equals(BroGroundwaterDocumentationModelType, StringComparison.OrdinalIgnoreCase) ||
            value.Contains("MODELDOCUMENTATIE", StringComparison.OrdinalIgnoreCase)) return BroGroundwaterDocumentationModelType;
        if (token.Equals(BroGroundwaterGhgModelType, StringComparison.OrdinalIgnoreCase) ||
            value.Equals("GWD", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("GHG", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("GRONDWATER", StringComparison.OrdinalIgnoreCase)) return BroGroundwaterGhgModelType;
        if (value.Equals("CPT", StringComparison.OrdinalIgnoreCase)) return "CPT";
        return BroDgmModelType;
    }

    private IReadOnlyList<BroSoundingPoint> ParseBroCptCharacteristicsXml(string xml, IReadOnlyList<TracePointRow> traceRows)
    {
        if (string.IsNullOrWhiteSpace(xml)) return [];
        var document = XDocument.Parse(xml);
        var distances = traceRows.Count >= 2 ? BuildTraceDistances(traceRows) : [];
        var result = new List<BroSoundingPoint>();
        var index = 1;

        foreach (var cpt in document.Descendants().Where(element => element.Name.LocalName.Equals("CPT_C", StringComparison.OrdinalIgnoreCase)))
        {
            var broId = FirstNonEmptyText(DescendantValue(cpt, "broId"), $"BRO-CPT-{index:000}");
            var hasRd = TryReadGmlPosition(cpt, "deliveredLocation", out var rdX, out var rdY);
            var hasWgs = TryReadGmlPosition(cpt, "standardizedLocation", out var lat, out var lon);

            RdPoint rd;
            if (hasRd)
            {
                rd = new RdPoint(rdX, rdY);
            }
            else if (hasWgs)
            {
                rd = Wgs84ToRd(lon, lat);
            }
            else
            {
                continue;
            }

            if (!IsValidRdPoint(rd)) continue;

            if (!hasWgs)
            {
                var wgs = RdToWgs84(rd.X, rd.Y);
                lon = wgs[0];
                lat = wgs[1];
            }

            var projected = traceRows.Count >= 2 && distances.Count >= 2
                ? ProjectPointOnTraceSigned(rd, traceRows, distances)
                : new KlicPlanPoint(0, 0);

            var finalDepth = DescendantDoubleNullable(cpt, "finalDepth");
            var predrilledDepth = DescendantDoubleNullable(cpt, "predrilledDepth");
            var standard = DescendantValue(cpt, "cptStandard");
            var qualityClass = DescendantValue(cpt, "qualityClass");
            var qualityRegime = DescendantValue(cpt, "qualityRegime");
            var startTime = DescendantValue(cpt, "startTime");
            var reportDate = DescendantValue(cpt, "date");
            var stopCriterion = DescendantValue(cpt, "stopCriterion");
            var review = DescendantValue(cpt, "underReview");
            var summaryParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(standard)) summaryParts.Add($"Standaard: {standard}");
            if (!string.IsNullOrWhiteSpace(qualityClass)) summaryParts.Add($"Kwaliteit: {qualityClass}");
            if (predrilledDepth is not null) summaryParts.Add($"Voorgeboord: {predrilledDepth:0.##} m");
            if (finalDepth is not null) summaryParts.Add($"Einddiepte: {finalDepth:0.##} m");
            if (!string.IsNullOrWhiteSpace(stopCriterion)) summaryParts.Add($"Stopcriterium: {stopCriterion}");

            result.Add(new BroSoundingPoint(
                broId,
                broId,
                $"BRO CPT {broId}",
                Math.Round(rd.X, 3),
                Math.Round(rd.Y, 3),
                Math.Round(lon, 8),
                Math.Round(lat, 8),
                Math.Round(projected.Station, 2),
                Math.Round(projected.Offset, 2),
                null,
                finalDepth,
                summaryParts.Count == 0 ? "BRO CPT-karakteristieken beschikbaar." : string.Join("; ", summaryParts),
                "BRO CPT-service",
                FirstNonEmptyText(startTime, reportDate),
                FirstNonEmptyText(qualityRegime, review),
                "CPT",
                "BRO CPT",
                []));
            index++;
        }

        return result
            .OrderBy(item => Math.Abs(item.Offset))
            .ThenBy(item => item.Distance)
            .Take(250)
            .ToList();
    }

    private static (double Top, double Bottom) ParseBroDepthRange(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return (0, 0);
        var match = Regex.Match(value, @"(?<top>-?\d+(?:[\.,]\d+)?)\s*m\s*-\s*(?<bottom>-?\d+(?:[\.,]\d+)?)\s*m", RegexOptions.IgnoreCase);
        if (!match.Success) return (0, 0);

        double Parse(string group) =>
            double.TryParse(match.Groups[group].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                ? number
                : 0;

        return (Parse("top"), Parse("bottom"));
    }

    private static IReadOnlyList<BroProfileInterval> ParseBroModelIntervals(JsonElement root, string modelType)
    {
        var columnType = modelType.Equals("RGS", StringComparison.OrdinalIgnoreCase)
            ? "HYDROGEOLOGY"
            : "LITHOSTRATIGRAPHY";
        var columns = JsonArray(root, "columns").ToList();
        var selectedColumn = columns.FirstOrDefault(column =>
            JsonText(column, "columnType", "").Equals(columnType, StringComparison.OrdinalIgnoreCase));
        if (selectedColumn.ValueKind == JsonValueKind.Undefined && columns.Count > 0)
        {
            selectedColumn = columns[0];
        }

        if (selectedColumn.ValueKind == JsonValueKind.Undefined) return [];

        var result = new List<BroProfileInterval>();
        foreach (var metadata in JsonArray(selectedColumn, "profileMetadata"))
        {
            var infos = JsonArray(metadata, "layerInfos").ToList();
            string Info(params string[] codes)
            {
                foreach (var code in codes)
                {
                    var match = infos.FirstOrDefault(item =>
                        JsonText(item, "code", "").Equals(code, StringComparison.OrdinalIgnoreCase));
                    if (match.ValueKind != JsonValueKind.Undefined)
                    {
                        var value = JsonText(match, "value", "");
                        if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
                    }
                }

                return "";
            }

            var depthText = Info("DEPTH");
            var depth = ParseBroDepthRange(depthText);
            var label = FirstNonEmptyText(
                Info(columnType),
                Info("LITHOSTRATIGRAPHY"),
                Info("HYDROGEOLOGY"),
                Info("LINK_NAME"),
                "Onbekende laag");
            var lithology = FirstNonEmptyText(Info("LITHOLOGY"), Info("KD_VALUE"));
            var shortCode = FirstNonEmptyText(Info("CODE"), label);

            result.Add(new BroProfileInterval(
                depth.Top,
                depth.Bottom,
                shortCode,
                label,
                lithology,
                BroModelIntervalColor(label)));
        }

        return result
            .Where(item => item.BottomDepth > item.TopDepth)
            .OrderBy(item => item.TopDepth)
            .Take(80)
            .ToList();
    }

    private static double? ParseBroPdfDouble(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var cleaned = Regex.Replace(value, @"[^0-9,.\-]", "").Replace(',', '.');
        return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }

    private BroImportedProfileRecord ParseBroProfilePdf(string path, string fallbackModelType)
    {
        var text = ExtractPdfText(path);
        var modelName = ReadBroPdfValue(text, @"Boormonsterprofiel\s+en\s+interpretatie\s+(?<value>BRO[^\r\n]+)");
        var modelType = DetectBroProfileModelType(modelName, fallbackModelType);
        var identification = ReadBroPdfValue(text, @"Identificatie:\s*(?<value>[A-Z0-9_-]+)");

        double? x = null;
        double? y = null;
        var coordinateMatch = Regex.Match(text, @"Co.?rdinaten:\s*(?<x>[-0-9.,]+)\s*,\s*(?<y>[-0-9.,]+)\s*\(RD\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (coordinateMatch.Success)
        {
            x = ParseBroPdfDouble(coordinateMatch.Groups["x"].Value);
            y = ParseBroPdfDouble(coordinateMatch.Groups["y"].Value);
        }

        var surfaceNap = ParseBroPdfDouble(ReadBroPdfValue(text, @"Maaiveld:\s*(?<value>[-0-9.,]+)\s*m"));
        double? depthTop = null;
        double? depthBottom = null;
        var depthMatch = Regex.Match(text, @"Diepte\s+t\.?o\.?v\.?\s*maaiveld:\s*(?<from>[-0-9.,]+)\s*m\s*-\s*(?<to>[-0-9.,]+)\s*m", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (depthMatch.Success)
        {
            depthTop = ParseBroPdfDouble(depthMatch.Groups["from"].Value);
            depthBottom = ParseBroPdfDouble(depthMatch.Groups["to"].Value);
        }

        return new BroImportedProfileRecord
        {
            Id = Guid.Empty,
            ModelType = modelType,
            ModelName = string.IsNullOrWhiteSpace(modelName) ? DinoModelLabel(modelType) : modelName.Trim(),
            Identification = string.IsNullOrWhiteSpace(identification) ? ToSafeFileName(System.IO.Path.GetFileNameWithoutExtension(path)) : identification.Trim(),
            X = x,
            Y = y,
            SurfaceNap = surfaceNap,
            DepthTop = depthTop,
            DepthBottom = depthBottom,
            ExtractedSummary = BuildBroPdfExtractSummary(text),
            SourcePath = path,
            LocalPath = path,
            ProfileImagePath = "",
            FileName = System.IO.Path.GetFileName(path),
            ImportedAt = DateTimeOffset.Now
        };
    }

    private static IReadOnlyList<BroWmsTraceFinding> QueryBroWmsFeatureInfoAtPoint(string modelType, RdPoint point, double station)
    {
        var request = BroWmsFeatureInfoRequestForModel(modelType);
        if (request is null) return [];

        try
        {
            var url = BuildBroWmsFeatureInfoUrl(request, point);
            var json = ReportTileHttpClient.GetStringAsync(url).GetAwaiter().GetResult();
            using var document = JsonDocument.Parse(json);
            if (JsonProperty(document.RootElement, "features") is not { ValueKind: JsonValueKind.Array } features)
            {
                return [];
            }

            var result = new List<BroWmsTraceFinding>();
            foreach (var feature in features.EnumerateArray())
            {
                var properties = JsonProperty(feature, "properties") ?? feature;
                var label = BroWmsFeatureLabel(modelType, properties);
                if (string.IsNullOrWhiteSpace(label)) continue;

                result.Add(new BroWmsTraceFinding(
                    station,
                    ShortBroWmsText(label, 120),
                    ShortBroWmsText(BroWmsFeatureDetails(modelType, properties), 110)));
            }

            return result
                .GroupBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private void QueueAutoLoadDgmForCurrentSubstep()
    {
        if (_selectedProject is null ||
            _selectedStep?.Number != 6 ||
            !string.Equals(_selectedSubstep?.Number, "6.1", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (GetBroSoundings(BroDgmModelType).Count > 0) return;
        if (_broLoadingModelTypes.Contains(BroDgmModelType)) return;

        var key = $"{_selectedProject.Id:N}:{BroDgmModelType}";
        if (!_broAutoLoadKeys.Add(key)) return;

        var traceRows = GetTraceRowsForProfile();
        if (traceRows.Count < 2)
        {
            SetBroLoadStatus(BroDgmModelType, "DGM kan nog niet automatisch laden: sla eerst de boorlijn op in stap 3.1.");
            RefreshUndergroundAnalysisSidebarPanel();
            return;
        }

        _ = LoadBroModelForStepSixAsync(BroDgmModelType, fit: true, initiatedByUser: false);
    }

    private IReadOnlyList<BroImportedProfileRecord> ReadBroImportedProfiles(string? modelType = null)
    {
        if (_selectedProject is null) return [];
        var normalized = NormalizeBroModelType(modelType ?? GetActiveUndergroundModelType());
        var json = _projects.GetStepData(_selectedProject.Id, 6, BroImportedProfilesDataKey(normalized));
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<BroImportedProfileRecord>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string ReadBroPdfValue(string text, string pattern)
    {
        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? Regex.Replace(match.Groups["value"].Value, @"\s+", " ").Trim() : "";
    }

    private static IReadOnlyList<ReportRenderSoundingInterval> ReadBroSourceProfileIntervals(JsonElement source)
    {
        if (JsonProperty(source, "profileIntervals") is not { ValueKind: JsonValueKind.Array } intervals)
        {
            return [];
        }

        return intervals.EnumerateArray()
            .Select(interval => new ReportRenderSoundingInterval(
                JsonDouble(interval, "topDepth", double.NaN),
                JsonDouble(interval, "bottomDepth", double.NaN),
                JsonText(interval, "code", "-"),
                JsonText(interval, "label", "-"),
                JsonText(interval, "lithology", "-"),
                JsonText(interval, "color", "#CBD5E1")))
            .Where(interval => double.IsFinite(interval.TopDepth)
                && double.IsFinite(interval.BottomDepth)
                && interval.BottomDepth > interval.TopDepth)
            .Take(24)
            .ToList();
    }

    private static IReadOnlyList<BroWmsTraceFinding> ReadBroWmsTraceFindings(JsonElement data)
    {
        if (JsonProperty(data, "wmsTraceFindings") is not { ValueKind: JsonValueKind.Array } findingsArray)
        {
            return [];
        }

        return findingsArray
            .EnumerateArray()
            .Select(item => new BroWmsTraceFinding(
                JsonDouble(item, "station", double.NaN),
                JsonText(item, "label", ""),
                JsonText(item, "details", "")))
            .Where(item => double.IsFinite(item.Station) && !string.IsNullOrWhiteSpace(item.Label))
            .ToList();
    }

    private static IReadOnlyList<JsonElement> ReadImportedBroProfileElements(JsonElement data)
    {
        if (JsonProperty(data, "importedProfiles") is { ValueKind: JsonValueKind.Array } importedArray)
        {
            return importedArray.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Select(item => item.Clone())
                .ToList();
        }

        return [];
    }

    private static IReadOnlyList<JsonElement> ReadSelectedBroSourceElements(JsonElement data)
    {
        if (JsonProperty(data, "selectedSoundings") is { ValueKind: JsonValueKind.Array } selectedArray)
        {
            return selectedArray.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Select(item => item.Clone())
                .ToList();
        }

        if (JsonProperty(data, "selectedSounding") is { ValueKind: JsonValueKind.Object } selected)
        {
            return [selected.Clone()];
        }

        return [];
    }

    private void RefreshUndergroundAnalysisSidebarPanel()
    {
        StepThreeImportsPanel.Children.Clear();
        RenderUndergroundAnalysisSidebarPanel();
    }

    private static string RenderBroImportedProfileImage(string pdfPath, string modelType, string identification)
    {
        if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath)) return "";

        lock (BroProfileRenderCache)
        {
            if (BroProfileRenderCache.TryGetValue(pdfPath, out var cached) &&
                (cached.Length == 0 || File.Exists(cached)))
            {
                return cached;
            }
        }

        var result = RenderBroImportedProfileImageCore(pdfPath, modelType, identification);
        lock (BroProfileRenderCache)
        {
            BroProfileRenderCache[pdfPath] = result;
        }

        return result;
    }

    private static string RenderBroImportedProfileImageCore(string pdfPath, string modelType, string identification)
    {
        var stopwatch = Stopwatch.StartNew();
        Exception? failure = null;
        try
        {
            var directory = System.IO.Path.GetDirectoryName(pdfPath);
            if (string.IsNullOrWhiteSpace(directory)) return "";

            var outputName = $"{System.IO.Path.GetFileNameWithoutExtension(pdfPath)}-profiel.png";
            var outputPath = System.IO.Path.Combine(directory, outputName);
            using var document = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(2.25));
            var pageCount = document.GetPageCount();
            if (pageCount == 0) return "";

            var renderedPages = new List<DrawingBitmap>();
            try
            {
                // DINOloket BRO profile PDFs use page 1 for the profile and page 2 for the legend.
                var pageLimit = Math.Min(2, pageCount);
                for (var pageIndex = 0; pageIndex < pageLimit; pageIndex++)
                {
                    using var page = document.GetPageReader(pageIndex);
                    var width = page.GetPageWidth();
                    var height = page.GetPageHeight();
                    var bytes = page.GetImage(new NaiveTransparencyRemover(255, 255, 255));
                    using var bitmap = CreateBitmapFromBgra(bytes, width, height);
                    renderedPages.Add(CropBroProfileBitmap(bitmap, modelType, identification));
                }

                using var combined = CombineBroProfilePages(renderedPages);
                combined.Save(outputPath, DrawingImageFormat.Png);
            }
            finally
            {
                foreach (var page in renderedPages)
                {
                    page.Dispose();
                }
            }

            return outputPath;
        }
        catch (Exception exception)
        {
            failure = exception;
            return "";
        }
        finally
        {
            stopwatch.Stop();
            LogPerformanceTimingIfSlow($"BRO profielbeeld renderen {modelType} {identification}", stopwatch.Elapsed, 150);
            if (failure is not null)
            {
                LogPerformanceTiming($"BRO profielbeeld renderen mislukt {modelType} {identification}", stopwatch.Elapsed, failure);
            }
        }
    }

    private static void RenderBroProfileLocationMapImage(
        string path,
        string code,
        string modelType,
        RdPoint sourcePoint,
        IReadOnlyList<TracePointRow> traceRows)
    {
        const int width = 1100;
        const int height = 372;
        var modelLabel = DinoModelShortLabel(modelType);
        var tracePoints = traceRows
            .Select(row => new RdPoint(row.X, row.Y))
            .Where(IsValidRdPoint)
            .ToList();
        if (!IsValidRdPoint(sourcePoint)) return;

        var focusPoints = new List<RdPoint> { sourcePoint };
        if (tracePoints.Count >= 2)
        {
            var nearestTracePoint = ClosestPointOnRdTrace(sourcePoint, tracePoints);
            focusPoints.Add(nearestTracePoint);
            var sourceToTrace = Distance(sourcePoint, nearestTracePoint);
            var localRadius = Math.Clamp(sourceToTrace + 130d, 120d, 320d);
            focusPoints.AddRange(tracePoints.Where(point => Distance(point, nearestTracePoint) <= localRadius));
        }

        var maxDx = Math.Max(1d, focusPoints.Max(point => Math.Abs(point.X - sourcePoint.X)));
        var maxDy = Math.Max(1d, focusPoints.Max(point => Math.Abs(point.Y - sourcePoint.Y)));
        var aspect = width / (double)height;
        var halfWidth = Math.Max(90d, maxDx + 55d);
        var halfHeight = Math.Max(58d, maxDy + 42d);
        if (halfWidth / halfHeight < aspect)
        {
            halfWidth = halfHeight * aspect;
        }
        else
        {
            halfHeight = halfWidth / aspect;
        }

        var minX = sourcePoint.X - halfWidth;
        var maxX = sourcePoint.X + halfWidth;
        var minY = sourcePoint.Y - halfHeight;
        var maxY = sourcePoint.Y + halfHeight;
        var centerRd = sourcePoint;
        var centerWgs = RdToWgs84(centerRd.X, centerRd.Y);
        var zoom = ReportTileZoomForBounds(centerWgs[1], maxX - minX, maxY - minY, width, height);
        var centerPixel = LonLatToWebMercatorPixel(centerWgs[0], centerWgs[1], zoom);
        var originX = centerPixel.X - width / 2d;
        var originY = centerPixel.Y - height / 2d;

        Point Project(RdPoint point)
        {
            var wgs = RdToWgs84(point.X, point.Y);
            var pixel = LonLatToWebMercatorPixel(wgs[0], wgs[1], zoom);
            return new Point(pixel.X - originX, pixel.Y - originY);
        }

        var canvas = new Canvas
        {
            Width = width,
            Height = height,
            Background = Brushes.White,
            ClipToBounds = true
        };

        AddBaseMapTilesForCamera(canvas, "osm", originX, originY, width, height, zoom, showKadasterOverlay: false);

        if (tracePoints.Count >= 2)
        {
            var traceCoordinates = tracePoints
                .Select(Project)
                .SelectMany(point => new[] { point.X, point.Y })
                .ToArray();
            AddCanvasPolyline(canvas, "#FFFFFF", 8, traceCoordinates);
            AddCanvasPolyline(canvas, "#E11D48", 4.4, traceCoordinates);

            var start = Project(tracePoints[0]);
            var end = Project(tracePoints[^1]);
            AddCanvasCircle(canvas, start.X, start.Y, 7.5, "#16A34A", "#FFFFFF", 2);
            AddCanvasText(canvas, "Start", start.X + 10, start.Y - 18, "#166534", 14, FontWeights.Bold);
            AddCanvasCircle(canvas, end.X, end.Y, 7.5, "#DC2626", "#FFFFFF", 2);
            AddCanvasText(canvas, "Einde", end.X + 10, end.Y - 18, "#991B1B", 14, FontWeights.Bold);
        }

        var pixelsPerMeter = 1d / WebMercatorMetersPerPixel(centerWgs[1], zoom);
        AddReportScaleBar(canvas, pixelsPerMeter, 22, height - 54, 140);
        AddCanvasText(canvas, "Boorlijn op OpenStreetMap", 22, height - 22, "#334155", 13, FontWeights.SemiBold);
        AddCanvasText(canvas, $"RD X {sourcePoint.X:N0} / Y {sourcePoint.Y:N0}", width - 206, height - 22, "#64748B", 12, FontWeights.SemiBold);

        canvas.Measure(new Size(width, height));
        canvas.Arrange(new Rect(0, 0, width, height));
        canvas.UpdateLayout();

        var bitmap = new RenderTargetBitmap(width, height, 144, 144, PixelFormats.Pbgra32);
        bitmap.Render(canvas);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            encoder.Save(stream);
        }

        DrawCenteredBroLocationMarker(path, code, modelLabel);
    }

    private void RenderUndergroundAnalysisSidebarPanel()
    {
        var modelType = GetActiveUndergroundModelType();
        _selectedBroModelType = modelType;
        var importedProfiles = SupportsImportedBroProfiles(modelType)
            ? ReadBroImportedProfiles(modelType)
            : Array.Empty<BroImportedProfileRecord>();
        if (importedProfiles.Count > 0)
        {
            MergeImportedBroProfilesIntoSoundings(modelType, importedProfiles, selectImported: false);
        }

        _broSoundings = GetBroSoundings(modelType);
        var selectedIds = GetSelectedBroSoundingIds(modelType);
        _selectedBroSoundingId = selectedIds.FirstOrDefault();
        _broSoundingLoadStatus = GetBroLoadStatus(modelType);
        var soundings = GetBroSoundings(modelType);
        var selectedItems = selectedIds
            .Select(id => soundings.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            .Where(item => item is not null)
            .Cast<BroSoundingPoint>()
            .ToList();
        var maxSelection = GetMaxBroSelectedSoundings(modelType);
        var title = DinoModelShortLabel(modelType);
        var label = DinoModelLabel(modelType);
        var loadAction = BroModelLoadAction(modelType);
        var isBroWmsMapLayer = IsBroWmsMapLayer(modelType);
        var wmsOverlayKey = BroWmsOverlayKey(modelType);
        var wmsLayerVisible = !string.IsNullOrWhiteSpace(wmsOverlayKey) &&
                              (!_mapOverlayStates.TryGetValue(wmsOverlayKey, out var wmsVisible) || wmsVisible);

        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("#D7E8FA"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 10)
        };

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brush("#315B7E"),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        panel.Children.Add(new TextBlock
        {
            Text = isBroWmsMapLayer
                ? $"{label} wordt als DINOloket/PDOK WMS-kaartlaag over de GIS-kaart getoond. De boorlijn blijft zichtbaar als referentie."
                : SupportsImportedBroProfiles(modelType)
                ? $"Importeer officiele BRO/DINOloket boormonsterprofiel-PDF's voor {label}. De kaartpunten zijn alleen bronlocatie/referentie; de PDF-gegevens zijn leidend voor de rapportage."
                : $"Echte {label}-booronderzoekpunten uit DINOloket. De boorlijn is alleen referentie; de bolletjes blijven op hun originele bronlocatie staan.",
            Foreground = Brush("#587080"),
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var buttons = new UniformGrid
        {
            Columns = 1,
            Rows = isBroWmsMapLayer ? 3 : SupportsImportedBroProfiles(modelType) ? 6 : 4
        };
        if (SupportsImportedBroProfiles(modelType))
        {
            AddBgtRibbonButton(buttons, $"{title} PDF importeren", "BRO profiel PDF importeren", true);
        }

        AddBgtRibbonButton(buttons, isBroWmsMapLayer ? "Kaartlaag tonen" : $"{title} punten laden", loadAction, !SupportsImportedBroProfiles(modelType));
        AddBgtRibbonButton(buttons, "Zoom naar boorlijn", "Zoom naar BRO-bronnen", false);
        if (!isBroWmsMapLayer)
        {
            AddBgtRibbonButton(buttons, "Selectie wissen", "BRO selectie wissen", false);
        }

        AddBgtRibbonButton(buttons, "Opslaan in rapport", "BRO selectie rapport", false);
        if (SupportsImportedBroProfiles(modelType))
        {
            AddBgtRibbonButton(buttons, "PDF-profielen wissen", "BRO profiel PDF wissen", false);
        }
        panel.Children.Add(buttons);

        panel.Children.Add(new TextBlock
        {
            Text = isBroWmsMapLayer
                ? $"Kaartlaag: {(wmsLayerVisible ? "zichtbaar" : "uit")}\nBron: PDOK WMS {label}"
                : SupportsImportedBroProfiles(modelType)
                ? $"PDF-profielen: {importedProfiles.Count}/{MaxBroImportedProfilesPerModel}\nKaartpunten: {soundings.Count}\nKaartselectie: {(selectedItems.Count == 0 ? "geen" : string.Join(", ", selectedItems.Select(item => item.Code)))}"
                : $"Punten op kaart: {soundings.Count}\nGeselecteerd: {(selectedItems.Count == 0 ? "geen" : $"{selectedItems.Count}/{maxSelection} - {string.Join(", ", selectedItems.Select(item => item.Code))}")}",
            Foreground = Brush("#334155"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 8)
        });

        panel.Children.Add(new TextBlock
        {
            Text = GetBroLoadStatus(modelType),
            Foreground = Brush("#7F99AC"),
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });

        if (!isBroWmsMapLayer)
        {
            panel.Children.Add(new TextBlock
            {
                Text = SupportsImportedBroProfiles(modelType) ? "Geimporteerde BRO-profielen" : "Opgeslagen bronpunten",
                Foreground = Brush("#315B7E"),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 4, 0, 6)
            });

            if (SupportsImportedBroProfiles(modelType) && importedProfiles.Count > 0)
            {
                var details = new StackPanel();
                for (var i = 0; i < importedProfiles.Count; i++)
                {
                    details.Children.Add(BuildBroImportedProfileDetailPanel(importedProfiles[i]));
                    if (i < importedProfiles.Count - 1)
                    {
                        details.Children.Add(new Border
                        {
                            Height = 1,
                            Background = Brush("#D7E8FA"),
                            Margin = new Thickness(0, 8, 0, 8)
                        });
                    }
                }

                panel.Children.Add(new Border
                {
                    Background = Brush("#F8FBFF"),
                    BorderBrush = Brush("#CFE0F2"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8),
                    Margin = new Thickness(0, 0, 0, 8),
                    Child = new ScrollViewer
                    {
                        MaxHeight = 300,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        Content = details
                    }
                });
            }
            else if (!SupportsImportedBroProfiles(modelType) && selectedItems.Count > 0)
            {
                var details = new StackPanel();
                for (var i = 0; i < selectedItems.Count; i++)
                {
                    details.Children.Add(BuildBroSoundingDetailPanel(selectedItems[i]));
                    if (i < selectedItems.Count - 1)
                    {
                        details.Children.Add(new Border
                        {
                            Height = 1,
                            Background = Brush("#D7E8FA"),
                            Margin = new Thickness(0, 8, 0, 8)
                        });
                    }
                }

                panel.Children.Add(new Border
                {
                    Background = Brush("#F8FBFF"),
                    BorderBrush = Brush("#CFE0F2"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8),
                    Margin = new Thickness(0, 0, 0, 8),
                    Child = new ScrollViewer
                    {
                        MaxHeight = 360,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        Content = details
                    }
                });
            }
            else
            {
                panel.Children.Add(new Border
                {
                    Background = Brush("#F8FBFF"),
                    BorderBrush = Brush("#CFE0F2"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8),
                    Margin = new Thickness(0, 0, 0, 8),
                    Child = new TextBlock
                    {
                        Text = SupportsImportedBroProfiles(modelType)
                            ? $"Importeer maximaal {MaxBroImportedProfilesPerModel} officiele {label} boormonsterprofiel-PDF's. De app leest de kerngegevens uit en neemt deze automatisch op in de rapportage."
                            : soundings.Count == 0
                            ? $"Laad eerst de {title}-punten. Daarna kun je maximaal {maxSelection} bronpunt(en) op de kaart aanklikken."
                            : $"Klik op een {title}-bolletje in de kaart om het direct op te slaan voor de rapportage. Je kunt maximaal {maxSelection} bronpunt(en) bewaren.",
                        Foreground = Brush("#587080"),
                        FontSize = 10.5,
                        TextWrapping = TextWrapping.Wrap
                    }
                });
            }

            panel.Children.Add(new TextBlock
            {
                Text = SupportsImportedBroProfiles(modelType)
                    ? "De volledige DINOloket-puntenlaag blijft optioneel beschikbaar op de kaart. Voor het rapport worden alleen de geimporteerde PDF-profielen gebruikt."
                    : soundings.Count == 0
                    ? $"Geen {title}-punten geladen. Gebruik '{title} laden' om losse DINOloket-datasetpunten op de kaart te tonen."
                    : $"De volledige {title}-puntenlaag blijft alleen op de kaart zichtbaar. In deze zijbalk en in de rapportage worden alleen de opgeslagen bronpunten getoond.",
                Foreground = Brush("#7F99AC"),
                FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });

            card.Child = panel;
            StepThreeImportsPanel.Children.Add(card);
            return;
        }

        panel.Children.Add(new TextBlock
        {
            Text = "Gebruik 'Filters' om deze BRO-kaartlaag aan of uit te zetten. De laag wordt samen met de boorlijn in de rapportkaart vastgelegd wanneer de kaart wordt opgeslagen.",
            Foreground = Brush("#7F99AC"),
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 6)
        });

        panel.Children.Add(BuildBroWmsLegendPanel(modelType));
        card.Child = panel;
        StepThreeImportsPanel.Children.Add(card);
    }

    private void SaveBroImportedProfiles(string modelType, IReadOnlyList<BroImportedProfileRecord> profiles)
    {
        if (_selectedProject is null) return;
        var normalized = NormalizeBroModelType(modelType);
        var previous = ReadBroImportedProfiles(normalized);
        var trimmed = profiles
            .Where(profile => profile.ModelType.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(profile => profile.ImportedAt)
            .Take(MaxBroImportedProfilesPerModel)
            .ToList();
        SaveSelectedProjectStepData(6, BroImportedProfilesDataKey(normalized), JsonSerializer.Serialize(trimmed, JsonOptions));
        if (trimmed.Count == 0 && previous.Count > 0)
        {
            var previousIds = previous.Select(profile => profile.Identification).ToHashSet(StringComparer.OrdinalIgnoreCase);
            SetBroSoundings(normalized, GetBroSoundings(normalized)
                .Where(sounding => !previousIds.Contains(sounding.Id) && !previousIds.Contains(sounding.Code))
                .ToList());
        }

        MergeImportedBroProfilesIntoSoundings(normalized, trimmed, selectImported: true);
    }

    private void SendBroSoundingsToMap(bool fit = false)
    {
        if (!_mapLibreLoaded || StepThreeMapView.CoreWebView2 is null)
        {
            QueueMapSync();
            return;
        }

        var modelType = GetActiveUndergroundModelType();
        var soundings = _selectedStep?.Number == 6
            ? GetBroSoundingsForUndergroundMap(modelType)
            : GetBroSoundings(modelType);
        var selectedModelType = NormalizeBroModelType(_selectedBroModelType);
        var activeSelectedIds = GetSelectedBroSoundingIds(selectedModelType);
        var selectedId = activeSelectedIds.FirstOrDefault() ?? GetSelectedBroSoundingIds(modelType).FirstOrDefault() ?? "";
        var selectedIds = BroUndergroundModelTypes
            .SelectMany(type => GetSelectedBroSoundingIds(type))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var payload = JsonSerializer.Serialize(new
        {
            type = "broSoundings",
            visible = _selectedStep?.Number == 6,
            fit,
            modelType,
            selectedId,
            selectedIds,
            soundings = soundings.Select(BuildBroSoundingPayload).ToList()
        }, JsonOptions);

        _gisMap.TryPostJson(
            StepThreeMapView.CoreWebView2,
            payload,
            exception => AppendMapDiagnostic($"BRO-sonderingen naar kaart sturen mislukt: {exception.Message}"));
    }

    private void SetBroLoadStatus(string modelType, string status)
    {
        var normalized = NormalizeBroModelType(modelType);
        _broModelLoadStatuses[normalized] = status;
        if (normalized.Equals(GetActiveUndergroundModelType(), StringComparison.OrdinalIgnoreCase))
        {
            _broSoundingLoadStatus = status;
        }
    }

    private void SetBroSoundings(string modelType, IReadOnlyList<BroSoundingPoint> soundings)
    {
        var normalized = NormalizeBroModelType(modelType);
        _broModelSoundings[normalized] = soundings;
        if (normalized.Equals(GetActiveUndergroundModelType(), StringComparison.OrdinalIgnoreCase))
        {
            _broSoundings = soundings;
        }
    }

    private void SetSelectedBroSoundingId(string modelType, string? id)
    {
        SetSelectedBroSoundingIds(modelType, string.IsNullOrWhiteSpace(id) ? [] : [id]);
    }

    private void SetSelectedBroSoundingIds(string modelType, IEnumerable<string> ids)
    {
        var normalized = NormalizeBroModelType(modelType);
        var selected = ids
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(GetMaxBroSelectedSoundings(normalized))
            .ToList();

        _selectedBroModelSoundingIdLists[normalized] = selected;
        _selectedBroModelSoundingIds[normalized] = selected.FirstOrDefault();
        if (normalized.Equals(GetActiveUndergroundModelType(), StringComparison.OrdinalIgnoreCase))
        {
            _selectedBroSoundingId = selected.FirstOrDefault();
        }
    }

    private static string ShortBroWmsText(string value, int maxLength)
    {
        var text = Regex.Replace(value.Trim(), @"\s+", " ");
        if (text.Length <= maxLength) return text;
        return text[..Math.Max(0, maxLength - 1)].TrimEnd() + "…";
    }

    private static bool ShouldRegenerateBroProfileImage(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath)) return true;

        try
        {
            using var image = DrawingBitmap.FromFile(imagePath);
            return image.Height < 420 || image.Width < 420;
        }
        catch
        {
            return true;
        }
    }

    private bool ShouldUseBroReportMapAspect() =>
        _selectedStep?.Number == 6 && SupportsImportedBroProfiles(GetActiveUndergroundModelType());

    private static string? SubsurfaceModelTypeForSubstep(string substepNumber) =>
        substepNumber switch
        {
            "6.1" => BroDgmModelType,
            "6.2" => BroRegisModelType,
            "6.3" => BroGeomorphologyModelType,
            "6.4" => BroSoilMapModelType,
            "6.5" or "6.5.1" => BroGroundwaterGhgModelType,
            "6.5.2" => BroGroundwaterGlgModelType,
            "6.5.3" => BroGroundwaterGvgModelType,
            "6.5.4" => BroGroundwaterGtModelType,
            "6.5.5" => BroGroundwaterDocumentationModelType,
            _ => null
        };

    private static bool SupportsImportedBroProfiles(string modelType)
    {
        var normalized = NormalizeBroModelType(modelType);
        return normalized.Equals(BroDgmModelType, StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals(BroRegisModelType, StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<string> ToggleSelectedBroSoundingId(string modelType, string id)
    {
        var normalized = NormalizeBroModelType(modelType);
        var selected = GetSelectedBroSoundingIds(normalized).ToList();
        var existingIndex = selected.FindIndex(item => item.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            selected.RemoveAt(existingIndex);
        }
        else
        {
            if (GetMaxBroSelectedSoundings(normalized) == 1)
            {
                selected.Clear();
            }

            selected.Add(id.Trim());
            while (selected.Count > GetMaxBroSelectedSoundings(normalized))
            {
                selected.RemoveAt(0);
            }
        }

        SetSelectedBroSoundingIds(normalized, selected);
        return GetSelectedBroSoundingIds(normalized);
    }

    private static bool TryGetBroModelTypeFromLoadAction(string action, out string modelType)
    {
        modelType = action switch
        {
            "BRO DGM laden" => BroDgmModelType,
            "BRO REGIS II laden" => BroRegisModelType,
            "BRO Geomorfologie laden" => BroGeomorphologyModelType,
            "BRO Geomorfologie kaartlaag tonen" => BroGeomorphologyModelType,
            "BRO Bodemkaart laden" => BroSoilMapModelType,
            "BRO Bodemkaart kaartlaag tonen" => BroSoilMapModelType,
            "BRO Grondwaterspiegeldiepte laden" => BroGroundwaterGhgModelType,
            "BRO GHG kaartlaag tonen" => BroGroundwaterGhgModelType,
            "BRO GLG kaartlaag tonen" => BroGroundwaterGlgModelType,
            "BRO GVG kaartlaag tonen" => BroGroundwaterGvgModelType,
            "BRO Grondwatertrappen kaartlaag tonen" => BroGroundwaterGtModelType,
            "BRO Modeldocumentatie kaartlaag tonen" => BroGroundwaterDocumentationModelType,
            _ => ""
        };

        return !string.IsNullOrWhiteSpace(modelType);
    }

    private static bool TryReadBroProfileRdPoint(JsonElement profile, out RdPoint point)
    {
        point = new RdPoint(0, 0);

        var x = FirstNullableDouble(
            JsonDoubleNullable(profile, "x"),
            JsonDoubleNullable(profile, "rdX"),
            JsonDoubleNullable(profile, "rd_x"),
            JsonDoubleNullable(profile, "rdCoordinateX"),
            JsonDoubleNullable(profile, "coordinateX"));
        var y = FirstNullableDouble(
            JsonDoubleNullable(profile, "y"),
            JsonDoubleNullable(profile, "rdY"),
            JsonDoubleNullable(profile, "rd_y"),
            JsonDoubleNullable(profile, "rdCoordinateY"),
            JsonDoubleNullable(profile, "coordinateY"));

        if (x is not double rdX || y is not double rdY)
        {
            if (!TryReadBroProfileRdPointFromText(JsonText(profile, "extractedSummary", ""), out rdX, out rdY) &&
                !TryReadBroProfileRdPointFromText(JsonText(profile, "soilSummary", ""), out rdX, out rdY) &&
                !TryReadBroProfileRdPointFromText(JsonText(profile, "summary", ""), out rdX, out rdY))
            {
                return false;
            }
        }

        point = new RdPoint(rdX, rdY);
        return IsValidRdPoint(point);
    }

    private static bool TryReadBroProfileRdPointFromText(string text, out double x, out double y)
    {
        x = 0;
        y = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var match = Regex.Match(
            text,
            @"RD\s*(?:X\s*/?\s*Y|X\s*,?\s*Y|X/Y)?\s*:?\s*(?<x>[0-9]{5,6}(?:[.,][0-9]+)?)\s*[,/; ]+\s*(?<y>[0-9]{5,6}(?:[.,][0-9]+)?)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            match = Regex.Match(
                text,
                @"Co.?rdinaten:\s*(?<x>[0-9]{5,6}(?:[.,][0-9]+)?)\s*,\s*(?<y>[0-9]{5,6}(?:[.,][0-9]+)?)\s*\(RD\)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        if (!match.Success) return false;

        x = ParseBroPdfDouble(match.Groups["x"].Value) ?? 0;
        y = ParseBroPdfDouble(match.Groups["y"].Value) ?? 0;
        return x > 0 && y > 0;
    }

    private static bool UseOfficialInlineBroWmsLegend(string modelType) =>
        NormalizeBroModelType(modelType) is BroGroundwaterGhgModelType
            or BroGroundwaterGlgModelType
            or BroGroundwaterGvgModelType
            or BroGroundwaterGtModelType
            or BroGroundwaterDocumentationModelType;
}
