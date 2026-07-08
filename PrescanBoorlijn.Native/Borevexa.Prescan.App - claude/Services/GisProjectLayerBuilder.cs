using System.IO;
using Borevexa.Prescan.App.Models;
using Borevexa.Prescan.Core.Models;
using Borevexa.Prescan.Core.Services;

namespace Borevexa.Prescan.App.Services;

public sealed class GisProjectLayerBuilder
{
    private readonly GisProjectLayerCache<GeoJsonFeature> _cache = new();

    public List<ProjectMapLayer> Build(
        IEnumerable<ProjectFileRecord> files,
        Func<ProjectFileRecord, IReadOnlyList<GeoJsonFeature>> parser,
        Func<string, string> colorResolver)
    {
        var layers = new List<ProjectMapLayer>();
        foreach (var file in UniqueProjectFiles(files).Where(file => File.Exists(file.LocalPath)))
        {
            var features = _cache.GetOrAdd(file, parser);
            if (features.Count == 0) continue;

            layers.Add(new ProjectMapLayer(
                file.Id.ToString("N"),
                file.FileType,
                file.DisplayName,
                colorResolver(file.FileType),
                new GeoJsonFeatureCollection(features)));
        }

        return layers;
    }

    public void ClearCache() => _cache.Clear();

    private static IReadOnlyList<ProjectFileRecord> UniqueProjectFiles(IEnumerable<ProjectFileRecord> files)
    {
        return files
            .GroupBy(file => !string.IsNullOrWhiteSpace(file.LocalPath)
                ? Path.GetFullPath(file.LocalPath).ToLowerInvariant()
                : file.Id.ToString("N"))
            .Select(group => group.First())
            .ToList();
    }
}
