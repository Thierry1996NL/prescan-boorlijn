namespace Borevexa.App.Models;

public sealed record ProjectMapLayer(
    string Id,
    string Type,
    string Name,
    string Color,
    GeoJsonFeatureCollection FeatureCollection);

public sealed record GeoJsonFeatureCollection(IReadOnlyList<GeoJsonFeature> Features)
{
    public string Type => "FeatureCollection";
}

public sealed record GeoJsonFeature(
    GeoJsonGeometry Geometry,
    IReadOnlyDictionary<string, object> Properties)
{
    public string Type => "Feature";
}

public sealed record GeoJsonGeometry(string Type, object Coordinates);
