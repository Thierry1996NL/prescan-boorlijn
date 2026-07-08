using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Borevexa.Prescan.App.Models;

namespace Borevexa.Prescan.App.Services;

public sealed class GisBgtKadasterParserService
{
    private const int MaxPopupProperties = 32;

    private readonly GisCoordinateService _coordinates;
    private readonly GisFeatureDetailStore _featureDetails;
    private readonly Func<GeoJsonFeature, string> _bgtSurfaceLabel;
    private readonly Func<GeoJsonFeature, string> _bgtSurfaceColor;
    private readonly Func<string, string> _normalizeBgtSurfaceKey;

    public GisBgtKadasterParserService(
        GisCoordinateService coordinates,
        GisFeatureDetailStore featureDetails,
        Func<GeoJsonFeature, string> bgtSurfaceLabel,
        Func<GeoJsonFeature, string> bgtSurfaceColor,
        Func<string, string> normalizeBgtSurfaceKey)
    {
        _coordinates = coordinates;
        _featureDetails = featureDetails;
        _bgtSurfaceLabel = bgtSurfaceLabel;
        _bgtSurfaceColor = bgtSurfaceColor;
        _normalizeBgtSurfaceKey = normalizeBgtSurfaceKey;
    }

    public bool IsBagOrKadasterSource(string fileType, string sourceName) =>
        fileType.Equals("BAG", StringComparison.OrdinalIgnoreCase) ||
        fileType.Equals("KADASTER", StringComparison.OrdinalIgnoreCase) ||
        sourceName.Contains("kadaster", StringComparison.OrdinalIgnoreCase) ||
        sourceName.Contains("kadastrale", StringComparison.OrdinalIgnoreCase) ||
        sourceName.Contains("perceel", StringComparison.OrdinalIgnoreCase);

    public List<GeoJsonFeature> ReadKadasterFeatures(string text, string fileType, string sourceName)
    {
        var features = new List<GeoJsonFeature>();
        try
        {
            var document = XDocument.Parse(text, LoadOptions.None);
            var rd = _coordinates.IsRdText(text);
            var members = document.Descendants()
                .Where(element => IsKadasterMemberElement(element.Name.LocalName))
                .Select(element => element.Elements().FirstOrDefault())
                .Where(element => element is not null)
                .Cast<XElement>()
                .ToList();

            if (members.Count == 0 && document.Root is not null)
            {
                members.AddRange(document.Root.Elements().Where(element => !IsGeometryElement(element.Name.LocalName)));
            }

            foreach (var element in members)
            {
                var objectType = KadasterObjectType(element, sourceName);
                var theme = KadasterTheme(objectType, sourceName);
                var properties = ExtractKadasterProperties(element, sourceName, objectType);
                foreach (var geometry in ReadBgtGeometries(element, rd))
                {
                    features.Add(CreateFeature(geometry, fileType, theme, properties));
                }
            }
        }
        catch
        {
            return [];
        }

        return features;
    }

    public List<GeoJsonFeature> ReadBgtFeatures(string text, string sourceName)
    {
        var features = new List<GeoJsonFeature>();
        try
        {
            var document = XDocument.Parse(text, LoadOptions.None);
            var rd = _coordinates.IsRdText(text);
            var objectType = BgtObjectTypeFromSourceName(sourceName);

            foreach (var member in document.Descendants().Where(element => element.Name.LocalName is "cityObjectMember" or "featureMember" or "member"))
            {
                var element = member.Elements().FirstOrDefault();
                if (element is null) continue;

                // BGT-leveringen bevatten mutatiehistorie: objecten met een
                // eindRegistratie zijn beëindigde (vervangen) versies. Die meenemen
                // legt historische vlakken óver de actuele heen, waardoor bijv. een
                // oud grasvlak een huidige sloot maskeert in de oppervlakteanalyse.
                if (element.Descendants().Any(child => child.Name.LocalName == "eindRegistratie"))
                {
                    continue;
                }

                var localName = element.Name.LocalName;
                var type = !string.IsNullOrWhiteSpace(localName) && !IsGeometryElement(localName) ? localName : objectType;
                var properties = ExtractBgtProperties(element, sourceName, type);
                foreach (var geometry in ReadBgtGeometries(element, rd))
                {
                    features.Add(CreateBgtFeature(geometry, type, properties));
                }
            }

            if (features.Count == 0)
            {
                var root = document.Root;
                if (root is not null)
                {
                    var properties = ExtractBgtProperties(root, sourceName, objectType);
                    foreach (var geometry in ReadBgtGeometries(root, rd))
                    {
                        features.Add(CreateBgtFeature(geometry, objectType, properties));
                    }
                }
            }
        }
        catch
        {
            return [];
        }

        return features;
    }

    private IEnumerable<GeoJsonGeometry> ReadBgtGeometries(XElement element, bool rd)
    {
        var emittedPolygon = false;
        foreach (var polygon in element.Descendants().Where(child => child.Name.LocalName == "Polygon"))
        {
            var rings = new List<object>();
            var exterior = polygon.Descendants().FirstOrDefault(child => child.Name.LocalName == "exterior");
            var exteriorRing = exterior is null ? new List<double[]>() : ReadBgtRing(exterior, rd);
            if (exteriorRing.Count >= 4) rings.Add(exteriorRing);

            foreach (var interior in polygon.Descendants().Where(child => child.Name.LocalName == "interior"))
            {
                var ring = ReadBgtRing(interior, rd);
                if (ring.Count >= 4) rings.Add(ring);
            }

            if (rings.Count > 0)
            {
                emittedPolygon = true;
                yield return new GeoJsonGeometry("Polygon", rings);
            }
        }

        if (emittedPolygon) yield break;

        foreach (var lineString in element.Descendants().Where(child => child.Name.LocalName is "LineString" or "Curve"))
        {
            var positions = ReadBgtRing(lineString, rd, close: false);
            if (positions.Count >= 2)
            {
                yield return new GeoJsonGeometry("LineString", positions);
            }
        }

        foreach (var point in element.Descendants().Where(child => child.Name.LocalName == "Point"))
        {
            var pos = point.Descendants().FirstOrDefault(child => child.Name.LocalName == "pos");
            if (pos is null) continue;
            var dimensionText = pos.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "srsDimension")?.Value;
            var dimension = int.TryParse(dimensionText, out var parsedDimension) && parsedDimension >= 2 ? parsedDimension : 2;
            var positions = _coordinates.ParseCoordinateText(pos.Value, rd, dimension);
            if (positions.Count == 1) yield return new GeoJsonGeometry("Point", positions[0]);
        }
    }

    private List<double[]> ReadBgtRing(XElement element, bool rd, bool close = true)
    {
        var ring = new List<double[]>();
        foreach (var posList in element.Descendants().Where(child => child.Name.LocalName == "posList"))
        {
            var dimensionText = posList.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "srsDimension")?.Value;
            var dimension = int.TryParse(dimensionText, out var parsedDimension) && parsedDimension >= 2 ? parsedDimension : 2;
            foreach (var position in _coordinates.ParseCoordinateText(posList.Value, rd, dimension))
            {
                if (ring.Count == 0 || !SamePosition(ring[^1], position))
                {
                    ring.Add(position);
                }
            }
        }

        if (close && ring.Count >= 3 && !SamePosition(ring[0], ring[^1]))
        {
            ring.Add(ring[0]);
        }

        return ring;
    }

    private Dictionary<string, object> ExtractKadasterProperties(XElement element, string sourceName, string objectType)
    {
        var properties = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["sourceName"] = sourceName,
            ["objectType"] = objectType,
            ["theme"] = KadasterTheme(objectType, sourceName),
            ["color"] = "#0057D8"
        };

        var objectId = element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "id")?.Value;
        if (!string.IsNullOrWhiteSpace(objectId))
        {
            properties["objectId"] = objectId;
        }

        AddKadasterValue(properties, element, "identificatie", "Identificatie");
        AddKadasterValue(properties, element, "kadastraleGemeente", "Kadastrale gemeente");
        AddKadasterValue(properties, element, "sectie", "Sectie");
        AddKadasterValue(properties, element, "perceelnummer", "Perceelnummer");
        AddKadasterValue(properties, element, "identificatieBAGPND", "BAG pand ID");
        AddKadasterValue(properties, element, "tekst", "Label");
        AddElementProperties(properties, element, "kadaster");
        return properties;
    }

    private Dictionary<string, object> ExtractBgtProperties(XElement element, string sourceName, string objectType)
    {
        var properties = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["source"] = "BGT",
            ["sourceName"] = sourceName,
            ["objectType"] = objectType,
            ["bgtType"] = BgtObjectTypeFromSourceName(sourceName)
        };

        var objectId = element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "id")?.Value;
        if (!string.IsNullOrWhiteSpace(objectId)) properties["objectId"] = objectId;

        AddBgtValue(properties, element, "identificatie", "Identificatie");
        AddBgtValue(properties, element, "lokaalID", "Lokaal ID");
        AddBgtValue(properties, element, "namespace", "Namespace");
        AddBgtValue(properties, element, "relatieveHoogteligging", "Relatieve hoogteligging");
        AddBgtValue(properties, element, "bronhouder", "Bronhouder");
        AddBgtValue(properties, element, "bgt-status", "BGT status");
        AddBgtValue(properties, element, "function", "Functie");
        AddBgtValue(properties, element, "surfaceMaterial", "Fysiek voorkomen");
        AddBgtValue(properties, element, "class", "Klasse");
        AddBgtValue(properties, element, "plus-functieWegdeel", "Functie");
        AddBgtValue(properties, element, "plus-functieOndersteunendWegdeel", "Functie plus");
        AddBgtValue(properties, element, "functie", "Functie");
        AddBgtValue(properties, element, "bgt-fysiekVoorkomen", "Fysiek voorkomen");
        AddBgtValue(properties, element, "fysiekVoorkomen", "Fysiek voorkomen");
        AddBgtValue(properties, element, "plus-fysiekVoorkomen", "Fysiek voorkomen plus");
        AddBgtValue(properties, element, "plus-fysiekVoorkomenWegdeel", "Fysiek voorkomen plus");
        AddBgtValue(properties, element, "plus-fysiekVoorkomenOndersteunendWegdeel", "Fysiek voorkomen plus");
        AddBgtValue(properties, element, "typeOverbrugging", "Type overbrugging");
        AddBgtValue(properties, element, "naam", "Naam");
        AddBgtValue(properties, element, "tekst", "Tekst");
        AddBgtElementProperties(properties, element);
        return properties;
    }

    private GeoJsonFeature CreateBgtFeature(GeoJsonGeometry geometry, string objectType, IReadOnlyDictionary<string, object> extraProperties)
    {
        var detailId = Guid.NewGuid().ToString("N");
        var properties = new Dictionary<string, object>
        {
            ["source"] = "BGT",
            ["theme"] = objectType,
            ["color"] = BgtObjectColor(objectType),
            ["detailId"] = detailId,
            ["objectType"] = objectType
        };

        if (extraProperties.TryGetValue("objectId", out var objectId))
        {
            properties["objectId"] = objectId;
        }

        foreach (var key in new[]
                 {
                     "sourceName", "bgtType", "Functie", "Functie plus", "Fysiek voorkomen", "Fysiek voorkomen plus",
                     "Klasse", "Naam", "Tekst", "Identificatie", "Lokaal ID", "Relatieve hoogteligging",
                     "Bronhouder", "BGT status"
                 })
        {
            if (extraProperties.TryGetValue(key, out var value) && value is not null && !string.IsNullOrWhiteSpace(value.ToString()))
            {
                properties[key] = value;
            }
        }

        var feature = new GeoJsonFeature(geometry, properties);
        var surfaceLabel = _bgtSurfaceLabel(feature);
        var surfaceColor = _bgtSurfaceColor(feature);
        properties["surface"] = _normalizeBgtSurfaceKey(surfaceLabel);
        properties["surfaceLabel"] = surfaceLabel;
        properties["surfaceColor"] = surfaceColor;
        properties["color"] = surfaceColor;

        _featureDetails[detailId] = extraProperties;
        return new GeoJsonFeature(geometry, properties);
    }

    private static void AddKadasterValue(IDictionary<string, object> properties, XElement element, string localName, string label)
    {
        var value = element.Descendants()
            .Where(child => child.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))
            .Select(child => Regex.Replace((child.Value ?? string.Empty).Trim(), @"\s+", " "))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) && !LooksLikeCoordinateList(value));

        if (!string.IsNullOrWhiteSpace(value))
        {
            AddProperty(properties, label, value);
        }
    }

    private static void AddBgtValue(IDictionary<string, object> properties, XElement element, string localName, string label)
    {
        var value = element.Descendants()
            .Where(child => child.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))
            .Select(child => Regex.Replace((child.Value ?? string.Empty).Trim(), @"\s+", " "))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) && !IsBgtVoidValue(value));

        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (properties.TryGetValue(label, out var existing) &&
            existing is not null &&
            !string.IsNullOrWhiteSpace(existing.ToString()) &&
            !IsBgtVoidValue(existing.ToString()!))
        {
            AddProperty(properties, $"{label} extra", value);
            return;
        }

        properties[label] = value;
    }

    private static void AddBgtElementProperties(IDictionary<string, object> properties, XElement element)
    {
        foreach (var child in element.Descendants())
        {
            if (properties.Count >= MaxPopupProperties) return;

            var localName = child.Name.LocalName;
            if (!IsUsefulBgtElementName(localName) || child.Elements().Any()) continue;

            var value = Regex.Replace((child.Value ?? string.Empty).Trim(), @"\s+", " ");
            if (string.IsNullOrWhiteSpace(value) || IsBgtVoidValue(value) || LooksLikeCoordinateList(value)) continue;

            var label = BgtPropertyLabel(localName);
            if (properties.Keys.Any(key => key.Equals(label, StringComparison.OrdinalIgnoreCase) ||
                                           key.StartsWith($"{label}.", StringComparison.OrdinalIgnoreCase) ||
                                           key.StartsWith($"{label} extra", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            AddProperty(properties, label, value);
        }
    }

    private static void AddElementProperties(IDictionary<string, object> properties, XElement element, string prefix)
    {
        if (properties.Count >= MaxPopupProperties) return;

        var localName = element.Name.LocalName;
        if (IsGeometryElement(localName) || IsNoisyImklElement(localName)) return;

        foreach (var attribute in element.Attributes())
        {
            if (properties.Count >= MaxPopupProperties) return;
            if (attribute.IsNamespaceDeclaration) continue;
            var attributeName = attribute.Name.LocalName;
            if (IsNoisyImklAttribute(attributeName)) continue;
            AddProperty(properties, $"{prefix}.{localName}.{attributeName}", attribute.Value);
        }

        if (!element.Elements().Any())
        {
            var value = Regex.Replace((element.Value ?? string.Empty).Trim(), @"\s+", " ");
            if (!string.IsNullOrWhiteSpace(value) && !LooksLikeCoordinateList(value))
            {
                AddProperty(properties, $"{prefix}.{localName}", value);
            }

            return;
        }

        foreach (var child in element.Elements())
        {
            if (properties.Count >= MaxPopupProperties) return;
            AddElementProperties(properties, child, prefix);
        }
    }

    private static GeoJsonFeature CreateFeature(
        GeoJsonGeometry geometry,
        string fileType,
        string theme,
        IReadOnlyDictionary<string, object>? extraProperties = null)
    {
        var properties = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["source"] = fileType,
            ["theme"] = theme,
            ["label"] = theme
        };

        if (extraProperties is not null)
        {
            foreach (var property in extraProperties)
            {
                properties[property.Key] = property.Value;
            }
        }

        return new GeoJsonFeature(geometry, properties);
    }

    private static string BgtObjectTypeFromSourceName(string sourceName)
    {
        var name = Path.GetFileNameWithoutExtension(sourceName);
        if (name.StartsWith("bgt_", StringComparison.OrdinalIgnoreCase))
        {
            name = name[4..];
        }

        return string.IsNullOrWhiteSpace(name) ? "BGT object" : name.Replace('_', ' ');
    }

    private static string BgtObjectColor(string objectType)
    {
        var value = objectType.ToLowerInvariant();
        if (value.Contains("water")) return "#6BAED6";
        if (value.Contains("weg")) return "#D8DEE6";
        if (value.Contains("begroeid") || value.Contains("vegetatie")) return "#86C67A";
        if (value.Contains("onbegroeid")) return "#D9C9A3";
        if (value.Contains("pand") || value.Contains("building")) return "#B8B8B8";
        if (value.Contains("kunstwerk") || value.Contains("overbrug") || value.Contains("bouwwerk")) return "#9CA3AF";
        if (value.Contains("scheiding")) return "#111827";
        if (value.Contains("label")) return "#2563EB";
        return "#64748B";
    }

    private static string KadasterObjectType(XElement element, string sourceName)
    {
        var localName = element.Name.LocalName;
        if (!string.IsNullOrWhiteSpace(localName) && !IsGeometryElement(localName))
        {
            return localName;
        }

        var name = Path.GetFileNameWithoutExtension(sourceName).Replace('_', ' ');
        return string.IsNullOrWhiteSpace(name) ? "Kadaster object" : name;
    }

    private static string KadasterTheme(string objectType, string sourceName)
    {
        var value = $"{objectType} {sourceName}".ToLowerInvariant();
        if (value.Contains("perceel")) return "kadaster perceel";
        if (value.Contains("grens")) return "kadaster grens";
        if (value.Contains("pand") || value.Contains("building")) return "kadaster pand";
        if (value.Contains("label")) return "kadaster label";
        return "kadaster";
    }

    private static bool IsKadasterMemberElement(string localName) =>
        localName.EndsWith("Member", StringComparison.OrdinalIgnoreCase) ||
        localName.Equals("cityObjectMember", StringComparison.OrdinalIgnoreCase) ||
        localName.Equals("featureMember", StringComparison.OrdinalIgnoreCase) ||
        localName.Equals("member", StringComparison.OrdinalIgnoreCase);

    private static bool SamePosition(double[] a, double[] b) =>
        a.Length >= 2 && b.Length >= 2 &&
        Math.Abs(a[0] - b[0]) < 0.0000001 &&
        Math.Abs(a[1] - b[1]) < 0.0000001;

    private static bool IsBgtVoidValue(string value)
    {
        var normalized = value.Trim().Replace('_', ' ').Replace('-', ' ').ToLowerInvariant();
        return normalized is "waardeonbekend" or "waarde onbekend" or "geenwaarde" or "geen waarde" or "nvt" or "null" or "-"
            || normalized.Contains("waarde onbekend");
    }

    private static bool IsUsefulBgtElementName(string localName)
    {
        if (string.IsNullOrWhiteSpace(localName) || IsGeometryElement(localName) || IsNoisyImklElement(localName)) return false;
        if (localName.Contains("pos", StringComparison.OrdinalIgnoreCase) ||
            localName.Contains("bounded", StringComparison.OrdinalIgnoreCase) ||
            localName.Contains("coordinates", StringComparison.OrdinalIgnoreCase)) return false;

        return ContainsAny(localName,
            "identificatie", "lokaal", "namespace", "bronhouder", "status", "functie", "function",
            "fysiek", "surface", "material", "class", "type", "naam", "tekst", "hoogte",
            "plus", "creation", "registratie", "inOnderzoek", "relatieve");
    }

    private static string BgtPropertyLabel(string localName)
    {
        var label = Regex.Replace(localName, "(?<!^)([A-Z])", " $1")
            .Replace("bgt-", "BGT ")
            .Replace("imgeo:", "", StringComparison.OrdinalIgnoreCase)
            .Replace('-', ' ')
            .Replace('_', ' ')
            .Trim();
        return string.IsNullOrWhiteSpace(label) ? localName : char.ToUpperInvariant(label[0]) + label[1..];
    }

    private static bool LooksLikeCoordinateList(string value)
    {
        var parts = value.Split(new[] { ' ', '\t', '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4) return false;
        var numeric = parts.Count(part => double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out _));
        return numeric >= 4 && numeric >= parts.Length * 0.8;
    }

    private static bool IsGeometryElement(string localName) =>
        localName.Contains("geometry", StringComparison.OrdinalIgnoreCase) ||
        localName is "pos" or "posList" or "LineString" or "Point" or "Curve" or "Surface" or "Polygon" or "coordinates";

    private static bool IsNoisyImklElement(string localName) =>
        localName.Contains("boundedBy", StringComparison.OrdinalIgnoreCase) ||
        localName.Contains("Envelope", StringComparison.OrdinalIgnoreCase) ||
        localName.Equals("metaDataProperty", StringComparison.OrdinalIgnoreCase) ||
        localName.Equals("description", StringComparison.OrdinalIgnoreCase) ||
        localName.Equals("identifier", StringComparison.OrdinalIgnoreCase) ||
        localName.Equals("name", StringComparison.OrdinalIgnoreCase);

    private static bool IsNoisyImklAttribute(string localName) =>
        localName.Equals("id", StringComparison.OrdinalIgnoreCase) ||
        localName.Equals("href", StringComparison.OrdinalIgnoreCase);

    private static void AddProperty(IDictionary<string, object> properties, string key, string value)
    {
        if (properties.Count >= MaxPopupProperties) return;

        value = Regex.Replace(value.Trim(), @"\s+", " ");
        if (string.IsNullOrWhiteSpace(value) || LooksLikeCoordinateList(value)) return;
        if (!properties.ContainsKey(key))
        {
            properties[key] = value.Length > 180 ? string.Concat(value.AsSpan(0, 177), "...") : value;
        }
    }

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
}
