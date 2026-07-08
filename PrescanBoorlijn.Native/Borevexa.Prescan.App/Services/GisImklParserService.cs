using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Borevexa.Prescan.App.Models;

namespace Borevexa.Prescan.App.Services;

public sealed class GisImklParserService
{
    private const int MaxPopupProperties = 32;

    private readonly GisCoordinateService _coordinates;
    private readonly GisFeatureDetailStore _featureDetails;
    private readonly Func<string, string> _themeColor;

    public GisImklParserService(
        GisCoordinateService coordinates,
        GisFeatureDetailStore featureDetails,
        Func<string, string> themeColor)
    {
        _coordinates = coordinates;
        _featureDetails = featureDetails;
        _themeColor = themeColor;
    }

    public List<GeoJsonFeature> ReadImklFeatures(string text, string fileType, string sourceName)
    {
        try
        {
            var document = XDocument.Parse(text, LoadOptions.None);
            var networkElements = document.Descendants()
                .Where(element => element.Name.LocalName.Equals("Utiliteitsnet", StringComparison.OrdinalIgnoreCase))
                .Select(element => new
                {
                    Id = element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "id")?.Value,
                    Element = element
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                .ToDictionary(item => item.Id!, item => item.Element, StringComparer.OrdinalIgnoreCase);
            var networkObjectDetails = BuildImklNetworkObjectDetails(document);
            var networkManagers = BuildImklNetworkManagers(document);

            var rd = _coordinates.IsRdText(text);
            var features = new List<GeoJsonFeature>();
            foreach (var link in document.Descendants().Where(element => element.Name.LocalName == "UtilityLink"))
            {
                var utilityLinkId = link.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "id")?.Value ?? "";
                var networkHref = link.Descendants().FirstOrDefault(child => child.Name.LocalName == "inNetwork")
                    ?.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "href")?.Value?.TrimStart('#');
                var networkElement = !string.IsNullOrWhiteSpace(networkHref) && networkElements.TryGetValue(networkHref, out var foundNetwork)
                    ? foundNetwork
                    : null;
                var themeHref = networkElement?.Descendants().FirstOrDefault(child => child.Name.LocalName == "thema")
                    ?.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "href")?.Value;
                var theme = !string.IsNullOrWhiteSpace(themeHref)
                    ? NormalizeKlicTheme(themeHref)
                    : "overig";
                var properties = ExtractImklProperties(link, networkElement);
                properties["utilityLinkId"] = utilityLinkId;
                properties["networkId"] = networkHref ?? "";
                properties["objectType"] = "leiding";
                properties["sourceName"] = sourceName;
                if (!string.IsNullOrWhiteSpace(networkHref) && networkObjectDetails.TryGetValue(networkHref, out var networkContent))
                {
                    properties["networkContent"] = networkContent;
                }

                var managerHref = networkElement?.Descendants().FirstOrDefault(child => child.Name.LocalName == "netbeheerder")
                    ?.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "href")?.Value?.TrimStart('#');
                if (!string.IsNullOrWhiteSpace(managerHref) && networkManagers.TryGetValue(managerHref, out var manager))
                {
                    properties["netbeheerderName"] = manager.Name;
                    if (!string.IsNullOrWhiteSpace(manager.Contact))
                    {
                        properties["netbeheerderContact"] = manager.Contact;
                    }
                }

                foreach (var posList in link.Descendants().Where(element => element.Name.LocalName == "posList"))
                {
                    var dimensionText = posList.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "srsDimension")?.Value;
                    var dimension = int.TryParse(dimensionText, out var parsedDimension) && parsedDimension >= 2 ? parsedDimension : 2;
                    var positions = _coordinates.ParseCoordinateText(posList.Value, rd, dimension);
                    if (positions.Count >= 2)
                    {
                        features.Add(CreateFeature(new GeoJsonGeometry("LineString", positions), fileType, theme, properties));
                    }
                }
            }

            features.AddRange(ReadImklObjectFeatures(document, fileType, sourceName, rd, networkElements, networkObjectDetails, networkManagers));
            return features;
        }
        catch
        {
            return [];
        }
    }

    private static Dictionary<string, string> BuildImklNetworkObjectDetails(XDocument document)
    {
        var objectTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Elektriciteitskabel", "Waterleiding", "Telecommunicatiekabel", "Mantelbuis",
            "Rioolleiding", "OlieGasChemicalienPijpleiding", "ThermischePijpleiding",
            "Kabelbed", "KabelEnLeidingContainer", "Beschermingsbuis", "Buisleiding"
        };

        var detailsByNetwork = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in document.Descendants().Where(element => objectTypes.Contains(element.Name.LocalName)))
        {
            var networkHref = element.Descendants().FirstOrDefault(child => child.Name.LocalName == "inNetwork")
                ?.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "href")?.Value?.TrimStart('#');
            if (string.IsNullOrWhiteSpace(networkHref)) continue;

            var detail = BuildImklObjectSummary(element);
            if (string.IsNullOrWhiteSpace(detail)) continue;

            if (!detailsByNetwork.TryGetValue(networkHref, out var details))
            {
                details = [];
                detailsByNetwork[networkHref] = details;
            }

            if (!details.Contains(detail, StringComparer.OrdinalIgnoreCase))
            {
                details.Add(detail);
            }
        }

        return detailsByNetwork.ToDictionary(
            pair => pair.Key,
            pair => string.Join(" | ", pair.Value),
            StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, ImklNetworkManager> BuildImklNetworkManagers(XDocument document)
    {
        var managers = new Dictionary<string, ImklNetworkManager>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in document.Descendants().Where(element => element.Name.LocalName == "Beheerder"))
        {
            var id = element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "id")?.Value;
            if (string.IsNullOrWhiteSpace(id)) continue;

            var name = element.Descendants()
                .FirstOrDefault(child =>
                    child.Name.LocalName.Equals("naam", StringComparison.OrdinalIgnoreCase) ||
                    child.Name.LocalName.Contains("organisatieNaam", StringComparison.OrdinalIgnoreCase) ||
                    child.Name.LocalName.EndsWith("Name", StringComparison.OrdinalIgnoreCase))?.Value.Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                managers[id] = new ImklNetworkManager(Regex.Replace(name, @"\s+", " "), ExtractImklManagerContact(element));
            }
        }

        return managers;
    }

    private static string ExtractImklManagerContact(XElement manager)
    {
        var values = new List<string>();

        void Add(string label, string value)
        {
            value = Regex.Replace(value.Trim(), @"\s+", " ");
            if (string.IsNullOrWhiteSpace(value)) return;
            var entry = $"{label}: {TruncateText(value, 90)}";
            if (!values.Contains(entry, StringComparer.OrdinalIgnoreCase)) values.Add(entry);
        }

        var xmlText = manager.ToString(SaveOptions.DisableFormatting);
        foreach (Match match in Regex.Matches(xmlText, @"[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase))
        {
            Add("E-mail", match.Value);
        }

        foreach (var element in manager.Descendants())
        {
            var name = element.Name.LocalName;
            var value = element.Value;
            if (string.IsNullOrWhiteSpace(value)) continue;

            if (ContainsAny(name, "email", "eMail", "mail"))
            {
                Add("E-mail", value);
            }
            else if (ContainsAny(name, "telefoon", "telephone", "phone", "tel"))
            {
                Add("Telefoon", value);
            }
            else if (ContainsAny(name, "website", "web", "url", "uri"))
            {
                Add("Website", value);
            }
            else if (ContainsAny(name, "adres", "address", "straat", "postcode", "plaats", "city"))
            {
                Add("Adres", value);
            }
        }

        foreach (Match match in Regex.Matches(xmlText, @"(?:\+31|0031|0)\s?(?:\(?\d{1,3}\)?[\s\-]?)?\d{6,8}"))
        {
            Add("Telefoon", match.Value);
        }

        return values.Count == 0 ? "" : string.Join("; ", values.Take(4));
    }

    private static string BuildImklObjectSummary(XElement element)
    {
        static string CleanCode(string value)
        {
            value = value.Trim();
            if (string.IsNullOrWhiteSpace(value)) return "";
            value = value.Split('/', '#').LastOrDefault(part => !string.IsNullOrWhiteSpace(part)) ?? value;
            return value
                .Replace("PipeMaterialTypeIMKLValue", "", StringComparison.OrdinalIgnoreCase)
                .Replace("WaterTypeValue", "", StringComparison.OrdinalIgnoreCase)
                .Replace("SewerWaterTypeValue", "", StringComparison.OrdinalIgnoreCase)
                .Replace("OilGasChemicalsProductTypeValue", "", StringComparison.OrdinalIgnoreCase)
                .Trim('_', '-', '/', ' ');
        }

        string ValueOf(string localName)
        {
            var match = element.Descendants().FirstOrDefault(child => child.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));
            if (match is null) return "";
            var value = match.Value.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                value = match.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "href")?.Value ?? "";
            }

            return Regex.Replace(CleanCode(value), @"\s+", " ");
        }

        var parts = new List<string>();
        var label = ValueOf("label");
        if (!string.IsNullOrWhiteSpace(label)) parts.Add(label);

        var diameter = ValueOf("pipeDiameter");
        if (!string.IsNullOrWhiteSpace(diameter) && !diameter.Equals("0", StringComparison.OrdinalIgnoreCase)) parts.Add($"Ø{diameter}");

        var material = FirstNonEmpty(ValueOf("buismateriaalType"), ValueOf("telecommunicationsCableMaterialType"));
        if (!string.IsNullOrWhiteSpace(material)) parts.Add(material);

        var voltage = FirstNonEmpty(ValueOf("operatingVoltage"), ValueOf("nominalVoltage"));
        if (!string.IsNullOrWhiteSpace(voltage)) parts.Add($"{voltage} V");

        var pressure = ValueOf("pressure");
        if (!string.IsNullOrWhiteSpace(pressure) && !pressure.Equals("0", StringComparison.OrdinalIgnoreCase)) parts.Add($"{pressure} bar");

        var product = FirstNonEmpty(ValueOf("waterType"), ValueOf("sewerWaterType"), ValueOf("oilGasChemicalsProductType"));
        if (!string.IsNullOrWhiteSpace(product)) parts.Add(product);

        var count = ValueOf("aantalKabelsLeidingen");
        if (!string.IsNullOrWhiteSpace(count)) parts.Add($"{count} kabels/leidingen");

        var toelichting = ValueOf("toelichting");
        if (!string.IsNullOrWhiteSpace(toelichting)) parts.Add(TruncateText(toelichting, 80));

        var type = Regex.Replace(element.Name.LocalName, "(?<!^)([A-Z])", " $1").Trim();
        return parts.Count == 0 ? type : $"{type}: {string.Join(", ", parts.Distinct(StringComparer.OrdinalIgnoreCase))}";
    }

    private IEnumerable<GeoJsonFeature> ReadImklObjectFeatures(
        XDocument document,
        string fileType,
        string sourceName,
        bool rd,
        IReadOnlyDictionary<string, XElement> networkElements,
        IReadOnlyDictionary<string, string> networkObjectDetails,
        IReadOnlyDictionary<string, ImklNetworkManager> networkManagers)
    {
        var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Appurtenance", "Beschermingsbuis", "Diepteligging", "Kabelbed",
            "KabelEnLeidingContainer", "Mantelbuis", "Orientatiepunt", "Persing", "Boogzinker", "Boring",
            "Profielschets", "TechnischGebouw", "ThemaObject", "Voorzorgsmaatregel", "AanduidingEisVoorzorgsmaatregel"
        };

        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in document.Descendants())
        {
            var localName = element.Name.LocalName;
            if (localName is "UtilityLink" or "Utiliteitsnet") continue;
            if (IsGeometryElement(localName) || IsNoisyImklElement(localName)) continue;
            if (!supported.Contains(localName) && !ContainsAny(localName, "profiel", "boring", "bescherm", "mantel", "persing", "boogzinker", "eis", "voorzorg")) continue;

            var objectId = element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "id")?.Value ?? "";
            var key = string.IsNullOrWhiteSpace(objectId) ? element.GetHashCode().ToString(CultureInfo.InvariantCulture) : objectId;
            if (!yielded.Add(key)) continue;

            var geometries = ReadElementGeometries(element, rd).ToList();
            if (geometries.Count == 0) continue;

            var networkElement = FindReferencedNetwork(element, networkElements);
            var theme = InferThemeFromElement(element, networkElement);
            var properties = ExtractImklObjectProperties(element, sourceName);
            properties["objectId"] = objectId;
            properties["objectType"] = localName;
            properties["sourceName"] = sourceName;
            properties["specialObject"] = IsSpecialKlicObject(localName, element.ToString(SaveOptions.DisableFormatting));
            if (networkElement is not null)
            {
                var networkId = networkElement.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "id")?.Value ?? "";
                properties["networkId"] = networkId;
                if (!string.IsNullOrWhiteSpace(networkId) && networkObjectDetails.TryGetValue(networkId, out var networkContent))
                {
                    properties["networkContent"] = networkContent;
                }

                var managerHref = networkElement.Descendants().FirstOrDefault(child => child.Name.LocalName == "netbeheerder")
                    ?.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "href")?.Value?.TrimStart('#');
                if (!string.IsNullOrWhiteSpace(managerHref) && networkManagers.TryGetValue(managerHref, out var manager))
                {
                    properties["netbeheerderName"] = manager.Name;
                    if (!string.IsNullOrWhiteSpace(manager.Contact))
                    {
                        properties["netbeheerderContact"] = manager.Contact;
                    }
                }
            }

            foreach (var geometry in geometries)
            {
                yield return CreateFeature(geometry, fileType, theme, properties);
            }
        }
    }

    private IEnumerable<GeoJsonGeometry> ReadElementGeometries(XElement element, bool rd)
    {
        var emittedLine = false;
        foreach (var posList in element.Descendants().Where(child => child.Name.LocalName == "posList"))
        {
            var dimensionText = posList.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "srsDimension")?.Value;
            var dimension = int.TryParse(dimensionText, out var parsedDimension) && parsedDimension >= 2 ? parsedDimension : 2;
            var positions = _coordinates.ParseCoordinateText(posList.Value, rd, dimension);
            if (positions.Count >= 2)
            {
                emittedLine = true;
                yield return new GeoJsonGeometry("LineString", positions);
            }
        }

        if (emittedLine) yield break;

        foreach (var pos in element.Descendants().Where(child => child.Name.LocalName == "pos"))
        {
            var dimensionText = pos.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "srsDimension")?.Value;
            var dimension = int.TryParse(dimensionText, out var parsedDimension) && parsedDimension >= 2 ? parsedDimension : 2;
            var positions = _coordinates.ParseCoordinateText(pos.Value, rd, dimension);
            if (positions.Count == 1) yield return new GeoJsonGeometry("Point", positions[0]);
        }
    }

    private static Dictionary<string, object> ExtractImklObjectProperties(XElement element, string sourceName)
    {
        var properties = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["sourceName"] = sourceName
        };
        AddElementProperties(properties, element, "object");
        return properties;
    }

    private static XElement? FindReferencedNetwork(XElement element, IReadOnlyDictionary<string, XElement> networkElements)
    {
        foreach (var href in element.DescendantsAndSelf().Attributes().Where(attribute => attribute.Name.LocalName == "href").Select(attribute => attribute.Value.TrimStart('#')))
        {
            if (networkElements.TryGetValue(href, out var networkElement)) return networkElement;
        }

        return null;
    }

    private static bool IsSpecialKlicObject(string localName, string xml) =>
        ContainsAny(localName, "mantel", "persing", "boogzinker", "boring", "bescherm") ||
        ContainsAny(xml, "mantel", "persing", "boogzinker", "boring", "beschermingsbuis");

    private static string InferThemeFromElement(XElement element, XElement? networkElement)
    {
        var themeHref = element.Descendants().FirstOrDefault(child => child.Name.LocalName == "thema")
            ?.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "href")?.Value;
        themeHref ??= networkElement?.Descendants().FirstOrDefault(child => child.Name.LocalName == "thema")
            ?.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "href")?.Value;
        if (!string.IsNullOrWhiteSpace(themeHref)) return NormalizeKlicTheme(themeHref);

        var xml = element.ToString(SaveOptions.DisableFormatting);
        foreach (var theme in new[] { "laagspanning", "middenspanning", "hoogspanning", "gasLageDruk", "gasHogeDruk", "water", "datatransport", "rioolVrijverval", "rioolOnderOverOfOnderdruk", "warmte" })
        {
            if (xml.Contains(theme, StringComparison.OrdinalIgnoreCase)) return NormalizeKlicTheme(theme);
        }

        return "overig";
    }

    private static Dictionary<string, object> ExtractImklProperties(XElement utilityLink, XElement? network)
    {
        var properties = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        AddElementProperties(properties, utilityLink, "utility");
        if (network is not null)
        {
            AddElementProperties(properties, network, "network");
        }

        return properties;
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
            if (string.IsNullOrWhiteSpace(attribute.Value)) continue;
            AddProperty(properties, $"{prefix}.{localName}.{attributeName}", attribute.Value);
        }

        if (!element.HasElements)
        {
            var value = element.Value.Trim();
            if (!string.IsNullOrWhiteSpace(value))
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

    private GeoJsonFeature CreateFeature(GeoJsonGeometry geometry, string fileType, string theme, IReadOnlyDictionary<string, object>? extraProperties = null)
    {
        var detailId = Guid.NewGuid().ToString("N");
        var properties = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["source"] = fileType,
            ["theme"] = theme,
            ["color"] = _themeColor(theme),
            ["detailId"] = detailId
        };
        if (extraProperties is not null)
        {
            foreach (var pair in extraProperties)
            {
                properties[pair.Key] = pair.Value;
            }

            if (extraProperties.TryGetValue("utilityLinkId", out var utilityLinkId))
            {
                properties["utilityLinkId"] = utilityLinkId;
            }
            if (extraProperties.TryGetValue("networkId", out var networkId))
            {
                properties["networkId"] = networkId;
            }
            if (extraProperties.TryGetValue("objectType", out var objectType))
            {
                properties["objectType"] = objectType;
            }
            if (extraProperties.TryGetValue("objectId", out var objectId))
            {
                properties["objectId"] = objectId;
            }
            if (extraProperties.TryGetValue("specialObject", out var specialObject))
            {
                properties["specialObject"] = specialObject;
            }
            if (extraProperties.TryGetValue("color", out var color))
            {
                properties["color"] = color;
            }

            _featureDetails[detailId] = extraProperties;
        }
        return new GeoJsonFeature(geometry, properties);
    }

    private static bool IsGeometryElement(string localName) =>
        localName.Contains("geometry", StringComparison.OrdinalIgnoreCase) ||
        localName is "pos" or "posList" or "LineString" or "Point" or "Curve" or "Surface" or "Polygon" or "coordinates";

    private static bool IsNoisyImklElement(string localName) =>
        localName.Contains("boundedBy", StringComparison.OrdinalIgnoreCase) ||
        localName.Contains("Envelope", StringComparison.OrdinalIgnoreCase) ||
        localName.Contains("lowerCorner", StringComparison.OrdinalIgnoreCase) ||
        localName.Contains("upperCorner", StringComparison.OrdinalIgnoreCase) ||
        localName.Contains("validTime", StringComparison.OrdinalIgnoreCase) ||
        localName.Contains("beginLifespanVersion", StringComparison.OrdinalIgnoreCase) ||
        localName.Contains("endLifespanVersion", StringComparison.OrdinalIgnoreCase);

    private static bool IsNoisyImklAttribute(string localName) =>
        localName is "srsName" or "srsDimension" or "axisLabels" or "uomLabels";

    private static void AddProperty(IDictionary<string, object> properties, string key, string value)
    {
        if (properties.Count >= MaxPopupProperties) return;

        value = Regex.Replace(value.Trim(), @"\s+", " ");
        if (string.IsNullOrWhiteSpace(value)) return;

        if (value.Length > 240)
        {
            value = string.Concat(value.AsSpan(0, 237), "...");
        }

        var propertyKey = key;
        var suffix = 2;
        while (properties.ContainsKey(propertyKey))
        {
            propertyKey = $"{key}.{suffix++}";
        }

        properties[propertyKey] = value;
    }

    private static string NormalizeKlicTheme(string? hrefOrTheme)
    {
        if (string.IsNullOrWhiteSpace(hrefOrTheme)) return "overig";
        var theme = hrefOrTheme.Split('/', '#').LastOrDefault(part => !string.IsNullOrWhiteSpace(part)) ?? hrefOrTheme;
        return theme.Trim();
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private static string TruncateText(string text, int maxLength)
    {
        text = Regex.Replace(text.Trim(), @"\s+", " ");
        if (maxLength <= 3 || text.Length <= maxLength) return text;
        return string.Concat(text.AsSpan(0, maxLength - 3), "...");
    }

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private sealed record ImklNetworkManager(string Name, string Contact);
}
