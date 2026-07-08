namespace Borevexa.Geo;

public sealed class GeoDataService
{
    public string GetBgtStatus() => "BGT service klaar voor PDOK OGC/WFS koppeling.";

    public string GetBroStatus() => "BRO service klaar voor CPT, BHR en GMW datasets.";

    public string GetAhnStatus() => "AHN service klaar voor maaiveldhoogte en NAP-profielen.";

    public RdCoordinate ToApproximateRd(double latitude, double longitude)
    {
        var dLat = 0.36 * (latitude - 52.15517440);
        var dLon = 0.36 * (longitude - 5.38720621);
        var x = 155000
                + 190094.945 * dLon
                - 11832.228 * dLat * dLon
                - 114.221 * dLat * dLat * dLon
                - 32.391 * Math.Pow(dLon, 3)
                - 0.705 * dLat
                - 2.34 * Math.Pow(dLat, 3) * dLon
                - 0.608 * dLat * Math.Pow(dLon, 3)
                - 0.008 * Math.Pow(dLon, 5);
        var y = 463000
                + 309056.544 * dLat
                + 3638.893 * dLon * dLon
                + 73.077 * dLat * dLat
                - 157.984 * dLat * dLon * dLon
                + 59.788 * Math.Pow(dLat, 3)
                + 0.433 * dLon
                - 6.439 * dLat * dLat * dLon * dLon
                - 0.032 * dLat * Math.Pow(dLon, 4)
                + 0.092 * Math.Pow(dLon, 4)
                - 0.054 * dLat * Math.Pow(dLon, 4);
        return new RdCoordinate(Math.Round(x), Math.Round(y));
    }
}

public readonly record struct RdCoordinate(double X, double Y);
