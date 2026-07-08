using System.Text.RegularExpressions;
using Borevexa.App.Models;

namespace Borevexa.App;

public partial class MainWindow
{
    private IReadOnlyList<ParcelOwnerSegment> GetParcelOwnerSegments(double profileTotal)
    {
        var traceRows = GetTraceRowsForProfile();
        if (traceRows.Count < 2 || profileTotal <= 0) return [];

        _projectFiles = _selectedProject is null ? _projectFiles : _projects.GetProjectFiles(_selectedProject.Id);
        var mapLayers = BuildProjectMapLayers(_projectFiles);
        return AnalyzeParcelOwnerSegments(
            traceRows,
            profileTotal,
            BuildCadastralParcelPolygons(mapLayers),
            BuildBgtHolderPolygons(mapLayers));
    }

    private static IReadOnlyList<ParcelOwnerSegment> AnalyzeParcelOwnerSegments(
        IReadOnlyList<TracePointRow> traceRows,
        double profileTotal,
        IReadOnlyList<CadastralParcelPolygon> parcelPolygons,
        IReadOnlyList<BgtHolderPolygon> holderPolygons)
    {
        return new EnvironmentGisAnalysisService().AnalyzeParcelOwnerSegments(
            traceRows,
            profileTotal,
            parcelPolygons,
            holderPolygons);
    }

    private ParcelOwnerAnalysis BuildParcelOwnerAnalysis(bool refresh = false)
    {
        if (_selectedProject is null)
        {
            return new ParcelOwnerAnalysis([], 0, [], [], []);
        }

        if (!refresh &&
            _lastEnvironmentAnalysisProjectId == _selectedProject.Id &&
            _lastEnvironmentAnalysis is not null)
        {
            return _lastEnvironmentAnalysis;
        }

        _projectFiles = _projects.GetProjectFiles(_selectedProject.Id);
        var traceRows = GetTraceRowsForProfile();
        var traceDistances = BuildTraceDistances(traceRows);
        var traceLength = traceDistances.Count >= 2 ? traceDistances[^1] : Math.Max(1, _selectedProject.BoreLengthMeters);
        var mapLayers = BuildProjectMapLayers(_projectFiles);
        var parcelPolygons = BuildCadastralParcelPolygons(mapLayers);
        var holderPolygons = BuildBgtHolderPolygons(mapLayers);
        var segments = AnalyzeParcelOwnerSegments(traceRows, Math.Max(1, traceLength), parcelPolygons, holderPolygons);
        var analysis = new ParcelOwnerAnalysis(traceRows, traceLength, segments, parcelPolygons, holderPolygons);
        _lastEnvironmentAnalysisProjectId = _selectedProject.Id;
        _lastEnvironmentAnalysis = analysis;
        return analysis;
    }

    private void ClearEnvironmentAnalysisCache()
    {
        _lastEnvironmentAnalysisProjectId = null;
        _lastEnvironmentAnalysis = null;
        _selectedEnvironmentSegmentKey = "";
    }

    private IReadOnlyList<CadastralParcelPolygon> BuildCadastralParcelPolygons(IEnumerable<ProjectMapLayer> mapLayers)
    {
        return mapLayers
            .Where(IsBagOrKadasterLayer)
            .SelectMany(layer => layer.FeatureCollection.Features)
            .Where(feature => feature.Geometry.Type.Equals("Polygon", StringComparison.OrdinalIgnoreCase))
            .Where(IsCadastralParcelFeature)
            .Select(feature =>
            {
                var rings = ExtractNestedCoordinateLists(feature.Geometry.Coordinates)
                    .Select(ring => ring.Select(ToRdPoint).Where(point => point.X > 0 && point.Y > 0).ToList())
                    .Where(ring => ring.Count >= 4)
                    .ToList();
                if (rings.Count == 0) return null;

                var outerRing = rings[0];
                var holes = rings.Skip(1).ToList();
                var area = PolygonArea(outerRing) - holes.Sum(PolygonArea);
                if (area <= 0.5 || area > 5000000) return null;

                return new CadastralParcelPolygon(
                    outerRing,
                    holes,
                    GetFeatureString(feature, "Kadastrale gemeente", "-"),
                    GetFeatureString(feature, "Sectie", "-"),
                    GetFeatureString(feature, "Perceelnummer", "-"),
                    FirstFeatureString(feature, "Identificatie", "objectId", "kadaster.identificatie"),
                    Math.Abs(area));
            })
            .Where(polygon => polygon is not null)
            .Select(polygon => polygon!)
            .ToList();
    }

    private IReadOnlyList<BgtHolderPolygon> BuildBgtHolderPolygons(IEnumerable<ProjectMapLayer> mapLayers)
    {
        return mapLayers
            .Where(IsBgtLayer)
            .SelectMany(layer => layer.FeatureCollection.Features)
            .Where(feature => feature.Geometry.Type.Equals("Polygon", StringComparison.OrdinalIgnoreCase))
            .Select(feature =>
            {
                var bronhouder = NormalizeBronhouderCode(FirstFeatureString(feature, "Bronhouder", "bronhouder"));
                if (string.IsNullOrWhiteSpace(bronhouder)) return null;

                var rings = ExtractNestedCoordinateLists(feature.Geometry.Coordinates)
                    .Select(ring => ring.Select(ToRdPoint).Where(point => point.X > 0 && point.Y > 0).ToList())
                    .Where(ring => ring.Count >= 4)
                    .ToList();
                if (rings.Count == 0) return null;

                var outerRing = rings[0];
                var holes = rings.Skip(1).ToList();
                var area = PolygonArea(outerRing) - holes.Sum(PolygonArea);
                if (area <= 0.5 || area > 5000000) return null;

                return new BgtHolderPolygon(outerRing, holes, bronhouder, Math.Abs(area));
            })
            .Where(polygon => polygon is not null)
            .Select(polygon => polygon!)
            .ToList();
    }

    private static bool IsCadastralParcelFeature(GeoJsonFeature feature)
    {
        if (!feature.Geometry.Type.Equals("Polygon", StringComparison.OrdinalIgnoreCase) &&
            !feature.Geometry.Type.Equals("MultiPolygon", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var sourceName = GetFeatureString(feature, "sourceName", "");
        var objectType = GetFeatureString(feature, "objectType", "");
        var hasParcelSource = sourceName.Contains("kadastralekaart_perceel", StringComparison.OrdinalIgnoreCase) ||
                              (sourceName.Contains("perceel", StringComparison.OrdinalIgnoreCase) &&
                               !sourceName.Contains("grens", StringComparison.OrdinalIgnoreCase) &&
                               !sourceName.Contains("pand", StringComparison.OrdinalIgnoreCase));
        var hasParcelType = objectType.Contains("perceel", StringComparison.OrdinalIgnoreCase) &&
                            !objectType.Contains("grens", StringComparison.OrdinalIgnoreCase) &&
                            !objectType.Contains("pand", StringComparison.OrdinalIgnoreCase);
        var hasId = !string.IsNullOrWhiteSpace(FirstFeatureString(feature, "Identificatie", "objectId", "kadaster.identificatie"));
        var hasParcelFields =
            !string.IsNullOrWhiteSpace(FirstFeatureString(feature, "Perceelnummer", "kadaster.perceelnummer")) ||
            !string.IsNullOrWhiteSpace(FirstFeatureString(feature, "Sectie", "kadaster.sectie")) ||
            !string.IsNullOrWhiteSpace(FirstFeatureString(feature, "Kadastrale gemeente", "kadaster.kadastraleGemeente"));

        return hasId && (hasParcelSource || hasParcelType) && hasParcelFields;
    }

    private static string FirstFeatureString(GeoJsonFeature feature, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = GetFeatureString(feature, key, "");
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        return "";
    }

    private static string NormalizeBronhouderCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        var match = Regex.Match(code.Trim(), @"[GWPL]\d{4}", RegexOptions.IgnoreCase);
        return match.Success ? match.Value.ToUpperInvariant() : Regex.Replace(code.Trim(), @"\s+", " ");
    }

}
