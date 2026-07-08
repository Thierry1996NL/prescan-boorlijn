using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Borevexa.Prescan.App.Services;

public sealed class GisCoordinateService
{
    public bool IsRdText(string text) =>
        text.Contains("28992", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("urn:ogc:def:crs:EPSG::28992", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("EPSG:28992", StringComparison.OrdinalIgnoreCase);

    public int GetSrsDimension(string tag)
    {
        var match = Regex.Match(tag, @"srsDimension\s*=\s*[""'](?<dimension>\d+)[""']", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups["dimension"].Value, out var dimension) && dimension >= 2 ? dimension : 2;
    }

    public List<double[]> ParseCoordinateText(string coordinateText, bool rd, int dimension)
    {
        var values = Regex.Matches(coordinateText, @"[-+]?\d+(?:[\.,]\d+)?")
            .Select(match => double.Parse(match.Value.Replace(',', '.'), CultureInfo.InvariantCulture))
            .ToArray();

        var positions = new List<double[]>();
        for (var i = 0; i + 1 < values.Length; i += dimension)
        {
            var x = values[i];
            var y = values[i + 1];
            positions.Add(rd || LooksLikeRd(x, y) ? RdToWgs84(x, y) : [x, y]);
        }

        return positions;
    }

    public object ConvertPosition(JsonElement element)
    {
        var x = element[0].GetDouble();
        var y = element[1].GetDouble();
        return LooksLikeRd(x, y) ? RdToWgs84(x, y) : new[] { x, y };
    }

    public List<object> ConvertPositionArray(JsonElement element)
    {
        var positions = new List<object>();
        foreach (var position in element.EnumerateArray())
        {
            positions.Add(ConvertPosition(position));
        }

        return positions;
    }

    public List<object> ConvertNestedPositionArray(JsonElement element)
    {
        var rings = new List<object>();
        foreach (var ring in element.EnumerateArray())
        {
            rings.Add(ConvertPositionArray(ring));
        }

        return rings;
    }

    public List<object> ConvertMultiNestedPositionArray(JsonElement element)
    {
        var polygons = new List<object>();
        foreach (var polygon in element.EnumerateArray())
        {
            polygons.Add(ConvertNestedPositionArray(polygon));
        }

        return polygons;
    }

    public bool LooksLikeRd(double x, double y) => x is > 0 and < 300000 && y is > 300000 and < 650000;

    public double[] RdToWgs84(double x, double y)
    {
        var dx = (x - 155000) / 100000;
        var dy = (y - 463000) / 100000;
        var lat = 52.15517440
            + (3235.65389 * dy
               - 32.58297 * Math.Pow(dx, 2)
               - 0.2475 * Math.Pow(dy, 2)
               - 0.84978 * Math.Pow(dx, 2) * dy
               - 0.0655 * Math.Pow(dy, 3)
               - 0.01709 * Math.Pow(dx, 2) * Math.Pow(dy, 2)
               - 0.00738 * dx
               + 0.0053 * Math.Pow(dx, 4)
               - 0.00039 * Math.Pow(dx, 2) * Math.Pow(dy, 3)
               + 0.00033 * Math.Pow(dx, 4) * dy
               - 0.00012 * dx * dy) / 3600;
        var lon = 5.38720621
            + (5260.52916 * dx
               + 105.94684 * dx * dy
               + 2.45656 * dx * Math.Pow(dy, 2)
               - 0.81885 * Math.Pow(dx, 3)
               + 0.05594 * dx * Math.Pow(dy, 3)
               - 0.05607 * Math.Pow(dx, 3) * dy
               + 0.01199 * dy
               - 0.00256 * Math.Pow(dx, 3) * Math.Pow(dy, 2)
               + 0.00128 * dx * Math.Pow(dy, 4)
               + 0.00022 * Math.Pow(dy, 2)
               - 0.00022 * Math.Pow(dx, 2)
               + 0.00026 * Math.Pow(dx, 5)) / 3600;
        return [lon, lat];
    }
}
