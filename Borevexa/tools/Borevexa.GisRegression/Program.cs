using System.IO.Compression;
using Borevexa.App.Models;
using Borevexa.App.Services;

var importDirectory = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.OrdinalIgnoreCase))
    ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "OneDrive - Inpark",
        "Documenten",
        "Borevexa Rootmap",
        "Importbestanden");

var strict = args.Any(arg => arg.Equals("--strict", StringComparison.OrdinalIgnoreCase));
var runner = new GisRegressionRunner(importDirectory, strict);
var result = runner.Run();
Console.WriteLine(result.Report);
return result.Success ? 0 : 1;

internal sealed class GisRegressionRunner
{
    private readonly string _importDirectory;
    private readonly bool _strict;
    private readonly GisCoordinateService _coordinates = new();
    private readonly GisFeatureDetailStore _details = new();
    private readonly GisFeatureParserService _genericParser;
    private readonly GisDxfParserService _dxfParser;
    private readonly GisBgtKadasterParserService _bgtKadasterParser;
    private readonly GisImklParserService _imklParser;

    public GisRegressionRunner(string importDirectory, bool strict)
    {
        _importDirectory = importDirectory;
        _strict = strict;
        _genericParser = new GisFeatureParserService(_coordinates);
        _dxfParser = new GisDxfParserService(_coordinates);
        _bgtKadasterParser = new GisBgtKadasterParserService(
            _coordinates,
            _details,
            BgtSurfaceLabel,
            BgtSurfaceColor,
            NormalizeBgtSurfaceKey);
        _imklParser = new GisImklParserService(_coordinates, _details, KlicThemeColor);
    }

    public RegressionResult Run()
    {
        var report = new StringWriter();
        report.WriteLine("Borevexa GIS regressie");
        report.WriteLine($"Importmap: {_importDirectory}");
        report.WriteLine($"Strict: {(_strict ? "ja" : "nee")}");
        report.WriteLine();

        if (!Directory.Exists(_importDirectory))
        {
            report.WriteLine("FOUT: importmap bestaat niet.");
            return new RegressionResult(false, report.ToString());
        }

        var cases = new[]
        {
            // Geijkt 07-07-2026: de BGT-parser filtert sindsdien beëindigde
            // (historische) objectversies weg via eindRegistratie; alleen actuele
            // objecten tellen mee. Gemeten op de testlevering: 3242 features
            // (1970 Polygon, 1215 Point, 57 LineString).
            new RegressionCase("BGT download.zip", "BGT", 3000, new Dictionary<string, int>
            {
                ["Polygon"] = 1800,
                ["Point"] = 1100,
                ["LineString"] = 50
            }),
            new RegressionCase("Kadaster,BAG Invoegen.zip", "BAG", 1600, new Dictionary<string, int>
            {
                ["LineString"] = 1000,
                ["Polygon"] = 400,
                ["Point"] = 40
            }),
            new RegressionCase("KLIC-melding (ZIP,GML).zip", "KLIC", 1500, new Dictionary<string, int>
            {
                ["LineString"] = 800,
                ["Point"] = 700
            },
            RequiredThemes: ["datatransport", "water", "laagspanning", "gasLageDruk", "middenspanning"]),
            new RegressionCase("Middenspanning (MS).dxf", "MS", 8, new Dictionary<string, int>
            {
                ["LineString"] = 8
            },
            RequiredThemes: ["middenspanning"])
        };

        var success = true;
        foreach (var test in cases)
        {
            var path = Path.Combine(_importDirectory, test.FileName);
            if (!File.Exists(path))
            {
                var message = $"ONTBREKEND: {test.FileName}";
                report.WriteLine(message);
                if (_strict) success = false;
                continue;
            }

            var features = ReadFeatures(path, test.FileType).ToList();
            var summary = Summarize(features);
            report.WriteLine($"{test.FileName}");
            report.WriteLine($"  features: {features.Count}");
            report.WriteLine($"  geometrie: {string.Join(", ", summary.ByGeometry.Select(pair => $"{pair.Key}={pair.Value}"))}");
            report.WriteLine($"  thema top: {string.Join(", ", summary.ByTheme.OrderByDescending(pair => pair.Value).Take(8).Select(pair => $"{pair.Key}={pair.Value}"))}");

            var caseOk = features.Count >= test.MinimumFeatures;
            foreach (var minimum in test.MinimumGeometryCounts)
            {
                caseOk &= summary.ByGeometry.TryGetValue(minimum.Key, out var count) && count >= minimum.Value;
            }

            foreach (var theme in test.RequiredThemes)
            {
                caseOk &= summary.ByTheme.ContainsKey(theme);
            }

            report.WriteLine(caseOk ? "  OK" : "  FOUT");
            report.WriteLine();
            success &= caseOk;
        }

        return new RegressionResult(success, report.ToString());
    }

    private IEnumerable<GeoJsonFeature> ReadFeatures(string path, string fileType)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".zip")
        {
            using var archive = ZipFile.OpenRead(path);
            foreach (var entry in archive.Entries.Where(IsReadableArchiveEntry))
            {
                using var stream = entry.Open();
                using var reader = new StreamReader(stream);
                foreach (var feature in ReadTextFeatures(reader.ReadToEnd(), fileType, entry.FullName))
                {
                    yield return feature;
                }
            }

            yield break;
        }

        var text = File.ReadAllText(path);
        foreach (var feature in ReadTextFeatures(text, fileType, Path.GetFileName(path)))
        {
            yield return feature;
        }
    }

    private IEnumerable<GeoJsonFeature> ReadTextFeatures(string text, string fileType, string sourceName)
    {
        var extension = Path.GetExtension(sourceName).ToLowerInvariant();
        if (extension == ".dxf")
        {
            return _dxfParser.ReadDxfFeatures(text, fileType, sourceName, DesignColor(fileType));
        }

        if (extension is ".geojson" or ".json" && text.TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            return _genericParser.ReadGeoJsonFeatures(text, fileType, sourceName);
        }

        if (fileType.Equals("KLIC", StringComparison.OrdinalIgnoreCase) ||
            sourceName.Contains("imkl", StringComparison.OrdinalIgnoreCase) ||
            sourceName.Contains("klic", StringComparison.OrdinalIgnoreCase))
        {
            var imkl = _imklParser.ReadImklFeatures(text, fileType, sourceName);
            if (imkl.Count > 0) return imkl;
        }

        if (fileType.Equals("BGT", StringComparison.OrdinalIgnoreCase) ||
            sourceName.Contains("bgt", StringComparison.OrdinalIgnoreCase))
        {
            var bgt = _bgtKadasterParser.ReadBgtFeatures(text, sourceName);
            if (bgt.Count > 0) return bgt;
        }

        if (_bgtKadasterParser.IsBagOrKadasterSource(fileType, sourceName))
        {
            var kadaster = _bgtKadasterParser.ReadKadasterFeatures(text, fileType, sourceName);
            if (kadaster.Count > 0) return kadaster;
        }

        return _genericParser.ReadGenericGmlFeatures(text, fileType);
    }

    private static bool IsReadableArchiveEntry(ZipArchiveEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Name) || entry.Length == 0) return false;
        var extension = Path.GetExtension(entry.Name).ToLowerInvariant();
        return extension is ".gml" or ".xml" or ".geojson" or ".json" or ".dxf" or ".kml";
    }

    private static FeatureSummary Summarize(IEnumerable<GeoJsonFeature> features)
    {
        var byGeometry = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var byTheme = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var feature in features)
        {
            Add(byGeometry, feature.Geometry.Type);
            if (feature.Properties.TryGetValue("theme", out var themeValue))
            {
                var theme = themeValue?.ToString();
                if (!string.IsNullOrWhiteSpace(theme)) Add(byTheme, theme);
            }
        }

        return new FeatureSummary(byGeometry, byTheme);
    }

    private static void Add(Dictionary<string, int> counts, string key) =>
        counts[key] = counts.TryGetValue(key, out var count) ? count + 1 : 1;

    private static string BgtSurfaceLabel(GeoJsonFeature feature)
    {
        foreach (var key in new[] { "surface", "surfaceType", "bgtSurface", "objectType", "class", "theme" })
        {
            if (feature.Properties.TryGetValue(key, out var value) && value is not null)
            {
                return NormalizeBgtSurfaceKey(value.ToString() ?? "");
            }
        }

        return "overig";
    }

    private static string BgtSurfaceColor(GeoJsonFeature feature) =>
        BgtSurfaceLabel(feature) switch
        {
            "water" => "#7DD3FC",
            "asfalt" => "#CBD5E1",
            "spoor" => "#9CA3AF",
            "groenstrook" => "#86EFAC",
            "onverhard" => "#FDE68A",
            "bebouwing" => "#D1D5DB",
            _ => "#E2E8F0"
        };

    private static string NormalizeBgtSurfaceKey(string label)
    {
        var value = label.ToLowerInvariant();
        if (value.Contains("asfalt") || value.Contains("weg") || value.Contains("verhard")) return "asfalt";
        if (value.Contains("groen") || value.Contains("vegetatie") || value.Contains("plant")) return "groenstrook";
        if (value.Contains("water")) return "water";
        if (value.Contains("onverhard") || value.Contains("zand") || value.Contains("gravel")) return "onverhard";
        if (value.Contains("bebouwing") || value.Contains("pand") || value.Contains("gebouw") || value.Contains("building")) return "bebouwing";
        if (value.Contains("spoor")) return "spoor";
        return "overig";
    }

    private static string KlicThemeColor(string theme) =>
        theme.ToLowerInvariant() switch
        {
            "laagspanning" => "#EAB308",
            "middenspanning" => "#F97316",
            "gaslagedruk" => "#FACC15",
            "water" => "#38BDF8",
            "rioolvrijverval" => "#A855F7",
            "datatransport" => "#22C55E",
            _ => "#64748B"
        };

    private static string DesignColor(string fileType) =>
        fileType.ToUpperInvariant() switch
        {
            "MS" => "#F97316",
            "LS" => "#EAB308",
            "GAS" => "#FACC15",
            "WATER" => "#38BDF8",
            "DATA" => "#22C55E",
            _ => "#0EA5E9"
        };

    private sealed record FeatureSummary(
        IReadOnlyDictionary<string, int> ByGeometry,
        IReadOnlyDictionary<string, int> ByTheme);
}

internal sealed record RegressionCase(
    string FileName,
    string FileType,
    int MinimumFeatures,
    IReadOnlyDictionary<string, int> MinimumGeometryCounts,
    IReadOnlyList<string>? RequiredThemes = null)
{
    public IReadOnlyList<string> RequiredThemes { get; } = RequiredThemes ?? [];
}

internal sealed record RegressionResult(bool Success, string Report);
