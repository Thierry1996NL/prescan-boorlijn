using System.Globalization;
using Borevexa.App.Models;

namespace Borevexa.App.Services;

public sealed class GisDxfParserService
{
    private readonly GisCoordinateService _coordinates;

    public GisDxfParserService(GisCoordinateService coordinates)
    {
        _coordinates = coordinates;
    }

    public List<GeoJsonFeature> ReadDxfFeatures(string text, string fileType, string sourceName, string color)
    {
        var features = new List<GeoJsonFeature>();
        var pairs = ReadDxfPairs(text);
        var theme = DesignThemeFromFileType(fileType);
        var properties = new Dictionary<string, object>
        {
            ["sourceName"] = sourceName,
            ["objectType"] = "DXF",
            ["color"] = color
        };

        for (var i = 0; i < pairs.Count; i++)
        {
            if (!pairs[i].Code.Equals("0", StringComparison.OrdinalIgnoreCase)) continue;
            var entity = pairs[i].Value.Trim().ToUpperInvariant();
            if (entity == "LINE")
            {
                var end = FindNextDxfEntity(pairs, i + 1);
                if (TryReadDxfLine(pairs, i + 1, end, out var line))
                {
                    features.Add(CreateFeature(new GeoJsonGeometry("LineString", line), fileType, theme, properties));
                }

                i = Math.Max(i, end - 1);
            }
            else if (entity == "LWPOLYLINE")
            {
                var end = FindNextDxfEntity(pairs, i + 1);
                var positions = ReadDxfPolylineVertices(pairs, i + 1, end, out var closed);
                if (positions.Count >= 2)
                {
                    if (closed && !SamePosition(positions[0], positions[^1]))
                    {
                        positions.Add(positions[0]);
                    }

                    features.Add(CreateFeature(new GeoJsonGeometry("LineString", positions), fileType, theme, properties));
                }

                i = Math.Max(i, end - 1);
            }
            else if (entity == "POLYLINE")
            {
                var positions = new List<double[]>();
                var closed = false;
                for (var j = i + 1; j < pairs.Count; j++)
                {
                    if (pairs[j].Code == "70" && int.TryParse(pairs[j].Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var flags))
                    {
                        closed = (flags & 1) == 1;
                    }

                    if (pairs[j].Code != "0") continue;
                    var childEntity = pairs[j].Value.Trim().ToUpperInvariant();
                    if (childEntity == "SEQEND")
                    {
                        i = j;
                        break;
                    }

                    if (childEntity != "VERTEX") continue;

                    var end = FindNextDxfEntity(pairs, j + 1);
                    if (TryReadDxfVertex(pairs, j + 1, end, out var vertex))
                    {
                        positions.Add(vertex);
                    }

                    j = Math.Max(j, end - 1);
                }

                if (positions.Count >= 2)
                {
                    if (closed && !SamePosition(positions[0], positions[^1]))
                    {
                        positions.Add(positions[0]);
                    }

                    features.Add(CreateFeature(new GeoJsonGeometry("LineString", positions), fileType, theme, properties));
                }
            }
        }

        return features;
    }

    public bool TryParseDxfDouble(string text, out double value) =>
        double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static List<DxfPair> ReadDxfPairs(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var pairs = new List<DxfPair>();
        for (var i = 0; i + 1 < lines.Length; i += 2)
        {
            var code = lines[i].Trim();
            var value = lines[i + 1].Trim();
            if (!string.IsNullOrWhiteSpace(code))
            {
                pairs.Add(new DxfPair(code, value));
            }
        }

        return pairs;
    }

    private static int FindNextDxfEntity(IReadOnlyList<DxfPair> pairs, int start)
    {
        for (var i = start; i < pairs.Count; i++)
        {
            if (pairs[i].Code == "0") return i;
        }

        return pairs.Count;
    }

    private bool TryReadDxfLine(IReadOnlyList<DxfPair> pairs, int start, int end, out List<double[]> line)
    {
        line = [];
        double? x1 = null, y1 = null, x2 = null, y2 = null;
        for (var i = start; i < end; i++)
        {
            if (!TryParseDxfDouble(pairs[i].Value, out var value)) continue;
            switch (pairs[i].Code)
            {
                case "10": x1 = value; break;
                case "20": y1 = value; break;
                case "11": x2 = value; break;
                case "21": y2 = value; break;
            }
        }

        if (x1 is null || y1 is null || x2 is null || y2 is null) return false;
        line.Add(DxfToPosition(x1.Value, y1.Value));
        line.Add(DxfToPosition(x2.Value, y2.Value));
        return true;
    }

    private List<double[]> ReadDxfPolylineVertices(IReadOnlyList<DxfPair> pairs, int start, int end, out bool closed)
    {
        closed = false;
        var positions = new List<double[]>();
        double? pendingX = null;
        for (var i = start; i < end; i++)
        {
            if (pairs[i].Code == "70" && int.TryParse(pairs[i].Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var flags))
            {
                closed = (flags & 1) == 1;
                continue;
            }

            if (!TryParseDxfDouble(pairs[i].Value, out var value)) continue;
            if (pairs[i].Code == "10")
            {
                pendingX = value;
            }
            else if (pairs[i].Code == "20" && pendingX is not null)
            {
                positions.Add(DxfToPosition(pendingX.Value, value));
                pendingX = null;
            }
        }

        return positions;
    }

    private bool TryReadDxfVertex(IReadOnlyList<DxfPair> pairs, int start, int end, out double[] vertex)
    {
        vertex = [];
        double? x = null, y = null;
        for (var i = start; i < end; i++)
        {
            if (!TryParseDxfDouble(pairs[i].Value, out var value)) continue;
            if (pairs[i].Code == "10") x = value;
            else if (pairs[i].Code == "20") y = value;
        }

        if (x is null || y is null) return false;
        vertex = DxfToPosition(x.Value, y.Value);
        return true;
    }

    private double[] DxfToPosition(double x, double y) =>
        _coordinates.LooksLikeRd(x, y) ? _coordinates.RdToWgs84(x, y) : [x, y];

    private static string DesignThemeFromFileType(string fileType) =>
        fileType.ToUpperInvariant() switch
        {
            "LS" => "laagspanning",
            "MS" => "middenspanning",
            "GAS" => "gasLageDruk",
            "WATER" => "water",
            "DATA" => "datatransport",
            _ => "overig"
        };

    private static bool SamePosition(double[] a, double[] b) =>
        a.Length >= 2 && b.Length >= 2 &&
        Math.Abs(a[0] - b[0]) < 0.0000001 &&
        Math.Abs(a[1] - b[1]) < 0.0000001;

    private static GeoJsonFeature CreateFeature(
        GeoJsonGeometry geometry,
        string fileType,
        string theme,
        IReadOnlyDictionary<string, object> extraProperties)
    {
        var properties = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["source"] = fileType,
            ["theme"] = theme,
            ["label"] = theme
        };

        foreach (var property in extraProperties)
        {
            properties[property.Key] = property.Value;
        }

        return new GeoJsonFeature(geometry, properties);
    }

    private sealed record DxfPair(string Code, string Value);
}
