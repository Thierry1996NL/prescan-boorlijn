using System.IO;
using Borevexa.Prescan.Core.Services;

namespace Borevexa.Prescan.App.Services;

public sealed class GisProjectLayerCache<TFeature>
{
    private sealed record CachedLayer(string Signature, IReadOnlyList<TFeature> Features);

    private readonly Dictionary<Guid, CachedLayer> _cache = [];

    public IReadOnlyList<TFeature> GetOrAdd(ProjectFileRecord file, Func<ProjectFileRecord, IReadOnlyList<TFeature>> parser)
    {
        var signature = BuildSignature(file);
        if (!_cache.TryGetValue(file.Id, out var cached) || cached.Signature != signature)
        {
            cached = new CachedLayer(signature, parser(file));
            _cache[file.Id] = cached;
        }

        return cached.Features;
    }

    public void Clear() => _cache.Clear();

    private static string BuildSignature(ProjectFileRecord file)
    {
        var info = new FileInfo(file.LocalPath);
        return $"{file.LocalPath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
    }
}
