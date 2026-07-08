namespace Borevexa.App.Services;

public sealed class GisFeatureDetailStore
{
    private readonly Dictionary<string, IReadOnlyDictionary<string, object>> _details = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, object> this[string id]
    {
        set => Store(id, value);
    }

    public void Store(string id, IReadOnlyDictionary<string, object> details)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        _details[id] = details;
    }

    public bool TryGetValue(string id, out IReadOnlyDictionary<string, object>? details) =>
        _details.TryGetValue(id, out details);

    public void Clear() => _details.Clear();
}
