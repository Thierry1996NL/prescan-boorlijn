using System.Text.Json;
using System.Text.RegularExpressions;
using Borevexa.App.Models;

namespace Borevexa.App.Services;

public sealed class GisFeatureParserService
{
    private readonly GisCoordinateService _coordinates;

    public GisFeatureParserService(GisCoordinateService coordinates)
    {
        _coordinates = coordinates;
    }

    public List<GeoJsonFeature> ReadGeoJsonFeatures(string text, string fileType, string sourceName)
    {
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        var features = new List<GeoJsonFeature>();

        if (root.TryGetProperty("type", out var type) && type.GetString() == "FeatureCollection" &&
            root.TryGetProperty("features", out var sourceFeatures))
        {
            foreach (var feature in sourceFeatures.EnumerateArray())
            {
                if (!feature.TryGetProperty("geometry", out var geometry)) continue;
                var converted = ConvertGeoJsonGeometry(geometry);
                if (converted is not null)
                {
                    features.Add(new GeoJsonFeature(converted, new Dictionary<string, object>
                    {
                        ["source"] = fileType
                    }));
                }
            }
        }

        return features;
    }

    public GeoJsonGeometry? ConvertGeoJsonGeometry(JsonElement geometry)
    {
        if (!geometry.TryGetProperty("type", out var typeElement) ||
            !geometry.TryGetProperty("coordinates", out var coordinatesElement))
        {
            return null;
        }

        var type = typeElement.GetString();
        return type switch
        {
            "Point" => new GeoJsonGeometry("Point", _coordinates.ConvertPosition(coordinatesElement)),
            "LineString" => new GeoJsonGeometry("LineString", _coordinates.ConvertPositionArray(coordinatesElement)),
            "Polygon" => new GeoJsonGeometry("Polygon", _coordinates.ConvertNestedPositionArray(coordinatesElement)),
            "MultiLineString" => new GeoJsonGeometry("MultiLineString", _coordinates.ConvertNestedPositionArray(coordinatesElement)),
            "MultiPolygon" => new GeoJsonGeometry("MultiPolygon", _coordinates.ConvertMultiNestedPositionArray(coordinatesElement)),
            _ => null
        };
    }

    public List<GeoJsonFeature> ReadGenericGmlFeatures(string text, string fileType)
    {
        var features = new List<GeoJsonFeature>();
        var rd = _coordinates.IsRdText(text);

        foreach (Match match in Regex.Matches(text, @"<(?<tag>[^>]*posList[^>]*)>(?<coords>[\s\d.,+-]+)</[^>]*posList>", RegexOptions.IgnoreCase))
        {
            var positions = _coordinates.ParseCoordinateText(
                match.Groups["coords"].Value,
                rd,
                _coordinates.GetSrsDimension(match.Groups["tag"].Value));
            if (positions.Count >= 2)
            {
                features.Add(CreateFeature(new GeoJsonGeometry("LineString", positions), fileType, "overig"));
            }
        }

        if (features.Count == 0)
        {
            foreach (Match match in Regex.Matches(text, @"<(?<tag>[^>]*pos[^>]*)>(?<coords>[\s\d.,+-]+)</[^>]*pos>", RegexOptions.IgnoreCase))
            {
                var positions = _coordinates.ParseCoordinateText(
                    match.Groups["coords"].Value,
                    rd,
                    _coordinates.GetSrsDimension(match.Groups["tag"].Value));
                if (positions.Count == 1)
                {
                    features.Add(CreateFeature(new GeoJsonGeometry("Point", positions[0]), fileType, "overig"));
                }
            }
        }

        return features;
    }

    private static GeoJsonFeature CreateFeature(GeoJsonGeometry geometry, string fileType, string theme)
    {
        var properties = new Dictionary<string, object>
        {
            ["source"] = fileType,
            ["theme"] = theme,
            ["label"] = theme
        };
        return new GeoJsonFeature(geometry, properties);
    }
}
