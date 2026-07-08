using System.Globalization;
using System.Text.Json;

namespace Borevexa.Prescan.App.Reports.Renderers;

internal static class ReportJson
{
    public static JsonElement? Property(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (element.TryGetProperty(name, out var property)) return property;

        foreach (var candidate in element.EnumerateObject())
        {
            if (candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return candidate.Value;
            }
        }

        return null;
    }

    public static IEnumerable<JsonElement> Array(JsonElement element, string name)
    {
        var property = Property(element, name);
        return property is { ValueKind: JsonValueKind.Array } ? property.Value.EnumerateArray() : [];
    }

    public static string Text(JsonElement element, string name, string fallback = "-")
    {
        var property = Property(element, name);
        return property is null ? fallback : Text(property.Value, fallback);
    }

    public static string Text(JsonElement element, string fallback)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? fallback,
            JsonValueKind.Number => element.ToString(),
            JsonValueKind.True => "Ja",
            JsonValueKind.False => "Nee",
            _ => fallback
        };
    }

    public static int Int(JsonElement element, string name)
    {
        var property = Property(element, name);
        if (property is null) return 0;
        if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var number)) return number;
        if (property.Value.ValueKind == JsonValueKind.String && int.TryParse(property.Value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) return number;
        return 0;
    }

    public static bool Bool(JsonElement element, string name, bool fallback = false)
    {
        var property = Property(element, name);
        if (property is null) return fallback;
        if (property.Value.ValueKind == JsonValueKind.True) return true;
        if (property.Value.ValueKind == JsonValueKind.False) return false;
        if (property.Value.ValueKind == JsonValueKind.String && bool.TryParse(property.Value.GetString(), out var value)) return value;
        return fallback;
    }

    public static double Double(JsonElement element, string name, double fallback = 0)
    {
        var property = Property(element, name);
        if (property is null) return fallback;
        if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDouble(out var number)) return number;
        if (property.Value.ValueKind == JsonValueKind.String && double.TryParse(property.Value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return number;
        return fallback;
    }
}
