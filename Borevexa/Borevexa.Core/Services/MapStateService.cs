using System.Text.Json;
using System.Text;

namespace Borevexa.Core.Services;

public sealed class MapStateService
{
    public const string DataKey = "map_state";

    private readonly ProjectRepository _projects;
    private readonly JsonSerializerOptions _jsonOptions;

    public MapStateService(ProjectRepository projects, JsonSerializerOptions? jsonOptions = null)
    {
        _projects = projects;
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    }

    public string? GetStepMapStateJson(Guid projectId, int stepNumber)
    {
        return _projects.GetStepData(projectId, stepNumber, DataKey);
    }

    public string? GetStepMapStateJson(Guid projectId, int stepNumber, string? contextKey, bool includeLegacyFallback = true)
    {
        var scoped = GetScopedStepMapStateJson(projectId, stepNumber, contextKey);
        if (!string.IsNullOrWhiteSpace(scoped)) return scoped;
        return includeLegacyFallback ? GetStepMapStateJson(projectId, stepNumber) : null;
    }

    public string? GetScopedStepMapStateJson(Guid projectId, int stepNumber, string? contextKey)
    {
        var dataKey = BuildContextDataKey(contextKey);
        return dataKey.Equals(DataKey, StringComparison.OrdinalIgnoreCase)
            ? null
            : _projects.GetStepData(projectId, stepNumber, dataKey);
    }

    public void SaveStepMapState(Guid projectId, int stepNumber, object state)
    {
        _projects.SaveStepData(projectId, stepNumber, DataKey, JsonSerializer.Serialize(state, _jsonOptions));
    }

    public void SaveStepMapState(Guid projectId, int stepNumber, object state, string? contextKey)
    {
        var json = JsonSerializer.Serialize(state, _jsonOptions);
        var scopedDataKey = BuildContextDataKey(contextKey);
        if (!scopedDataKey.Equals(DataKey, StringComparison.OrdinalIgnoreCase))
        {
            _projects.SaveStepData(projectId, stepNumber, scopedDataKey, json);
            return;
        }

        _projects.SaveStepData(projectId, stepNumber, DataKey, json);
    }

    public static string BuildContextDataKey(string? contextKey)
    {
        var normalized = NormalizeContextKey(contextKey);
        return string.IsNullOrWhiteSpace(normalized) ? DataKey : $"{DataKey}_{normalized}";
    }

    public static string NormalizeContextKey(string? contextKey)
    {
        if (string.IsNullOrWhiteSpace(contextKey)) return "";

        var builder = new StringBuilder(contextKey.Length);
        var previousWasSeparator = false;
        foreach (var character in contextKey.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator)
            {
                builder.Append('-');
                previousWasSeparator = true;
            }
        }

        var normalized = builder.ToString().Trim('-');
        return normalized.Length <= 96 ? normalized : normalized[..96].TrimEnd('-');
    }

    public static string ReadBaseLayer(JsonElement root, string fallback)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("baseLayer", out var baseLayer) &&
            baseLayer.ValueKind == JsonValueKind.String)
        {
            return baseLayer.GetString() ?? fallback;
        }

        return fallback;
    }

    public static JsonElement? ReadCamera(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("camera", out var camera) &&
            camera.ValueKind == JsonValueKind.Object)
        {
            return camera.Clone();
        }

        return null;
    }

    public static void ApplyBooleanDictionary(JsonElement root, string propertyName, IDictionary<string, bool> target)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(propertyName, out var source) ||
            source.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in source.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                target[property.Name] = property.Value.GetBoolean();
            }
        }
    }
}
