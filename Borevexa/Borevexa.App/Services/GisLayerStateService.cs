using System.IO;
using Borevexa.Core.Services;

namespace Borevexa.App.Services;

public sealed class GisLayerStateService
{
    private static readonly string[] StepThreeCleanMapOverlays =
    [
        "buildings",
        "addresses",
        "bgt",
        "bagImport",
        "ahn4Dtm",
        "ahn4Dsm",
        "broGeomorphology",
        "broSoilMap",
        "broGroundwaterGhg",
        "broGroundwaterGlg",
        "broGroundwaterGvg",
        "broGroundwaterGt",
        "broGroundwaterDocumentation",
        "klic",
        "klicBuffer",
        "designImport",
        "customImport",
        "profileTracePoints",
        "machines"
    ];

    public string BaseLayer { get; set; } = "pdok-brt";

    public Dictionary<string, bool> Overlays { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["parcels"] = false,
        ["buildings"] = false,
        ["addresses"] = false,
        ["baseMap"] = true,
        ["bgt"] = true,
        ["bagImport"] = true,
        ["ahn4Dtm"] = true,
        ["ahn4Dsm"] = false,
        ["broGeomorphology"] = true,
        ["broSoilMap"] = true,
        ["broGroundwaterGhg"] = true,
        ["broGroundwaterGlg"] = true,
        ["broGroundwaterGvg"] = true,
        ["broGroundwaterGt"] = true,
        ["broGroundwaterDocumentation"] = true,
        ["klic"] = true,
        ["klicBuffer"] = true,
        ["designImport"] = true,
        ["customImport"] = true,
        ["boreTrace"] = true,
        ["boreTraceNumbers"] = true,
        ["boreTraceLengths"] = true,
        ["boreTraceInfo"] = true,
        ["profileTracePoints"] = true,
        ["machines"] = true
    };

    public Dictionary<string, bool> ProjectLayers { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, bool> BgtSurfaces { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["asfalt"] = true,
        ["groenstrook"] = true,
        ["water"] = true,
        ["onverhard"] = true,
        ["bebouwing"] = true,
        ["spoor"] = true,
        ["overig"] = true
    };

    public Dictionary<string, bool> KlicThemes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void ApplyStepThreeCleanMapDefaults()
    {
        foreach (var key in StepThreeCleanMapOverlays)
        {
            Overlays[key] = false;
        }

        Overlays["baseMap"] = true;
        Overlays["boreTrace"] = true;
        Overlays["boreTraceNumbers"] = false;
        Overlays["boreTraceLengths"] = false;
        Overlays["boreTraceInfo"] = false;
    }

    public void SetOverlay(string key, bool visible) => Overlays[key] = visible;

    public bool ToggleOverlay(string key)
    {
        var visible = !Overlays.TryGetValue(key, out var current) || !current;
        Overlays[key] = visible;
        return visible;
    }

    public void SetNonImportOverlays(bool visible)
    {
        foreach (var key in Overlays.Keys.Where(key => !IsImportOverlayKey(key)).ToList())
        {
            Overlays[key] = visible;
        }
    }

    public void SetImportFilters(bool visible)
    {
        foreach (var key in Overlays.Keys.Where(IsImportOverlayKey).ToList())
        {
            Overlays[key] = visible;
        }

        SetAll(KlicThemes, visible);
        SetAll(BgtSurfaces, visible);
        SetAll(ProjectLayers, visible);
    }

    public void RestoreVisibleDefaults()
    {
        BaseLayer = "pdok-brt";

        foreach (var key in Overlays.Keys.ToList())
        {
            Overlays[key] = key is not "ahn4Dsm";
        }

        SetAll(ProjectLayers, true);
        SetAll(KlicThemes, true);
        SetAll(BgtSurfaces, true);
    }

    public void ApplySurfaceAnalysisMapDefaults()
    {
        BaseLayer = "pdok-brt";
        foreach (var key in Overlays.Keys.ToList())
        {
            Overlays[key] = false;
        }

        Overlays["baseMap"] = true;
        Overlays["parcels"] = true;
        Overlays["bgt"] = true;
        Overlays["bagImport"] = true;
        Overlays["klic"] = true;
        Overlays["klicBuffer"] = true;
        Overlays["boreTrace"] = true;
        Overlays["boreTraceInfo"] = false;
        Overlays["boreTraceNumbers"] = false;
        Overlays["boreTraceLengths"] = false;

        SetAll(ProjectLayers, true);
        SetAll(KlicThemes, true);
        SetAll(BgtSurfaces, true);
    }

    // Dwarsprofiel (stap 7.2) GIS-kaart: luchtfoto als ondergrond + KLIC zichtbaar, zodat
    // de vastgezette kaartcapture in het rapport direct de boorlijn tegen de echte
    // luchtfoto en kabels/leidingen toont, in plaats van de kale BRT-ondergrond zonder
    // KLIC die hier voorheen standaard stond.
    public void ApplyProfileMapDefaults()
    {
        BaseLayer = "pdok-aerial";
        foreach (var key in Overlays.Keys.ToList())
        {
            Overlays[key] = false;
        }

        Overlays["baseMap"] = true;
        Overlays["parcels"] = true;
        Overlays["klic"] = true;
        Overlays["klicBuffer"] = true;
        Overlays["boreTrace"] = true;
        Overlays["boreTraceInfo"] = false;
        Overlays["boreTraceNumbers"] = false;
        Overlays["boreTraceLengths"] = false;

        SetAll(ProjectLayers, true);
        SetAll(KlicThemes, true);
        SetAll(BgtSurfaces, true);
    }

    // Substep 4.3 (AHN4/maaiveld hoogte bepalen) is a focused view: only the boorlijn
    // and the AHN4-maaiveldhoogtekaart, nothing else. Safe to mutate the shared
    // dictionary here because step 4 now has per-substep scoped map state (see
    // GisMapWorkspaceRegistry) — this only ever gets saved under 4.3's own context.
    public void ApplyAhn4HeightMapDefaults()
    {
        BaseLayer = "pdok-brt";
        foreach (var key in Overlays.Keys.ToList())
        {
            Overlays[key] = false;
        }

        Overlays["baseMap"] = true;
        Overlays["ahn4Dtm"] = true;
        Overlays["boreTrace"] = true;
        Overlays["boreTraceInfo"] = false;
        Overlays["boreTraceNumbers"] = false;
        Overlays["boreTraceLengths"] = false;

        SetAll(ProjectLayers, false);
        SetAll(KlicThemes, false);
        SetAll(BgtSurfaces, false);
    }

    // Guards against stale scoped map_state rows saved for substep 7.2 before
    // ApplyProfileMapDefaults existed (or leaked in from another step): forces the BRO
    // groundwater/soil/geomorphology WMS overlays off even if a persisted JSON blob still
    // has them set to true. Those layers are near-opaque choropleth fills covering the
    // whole viewport (bro-grondwaterspiegeldiepte in particular renders as a solid dark
    // blue/navy fill) and were mistaken for a broken base-layer render.
    public void NormalizeProfileMapState()
    {
        Overlays["ahn4Dtm"] = false;
        Overlays["ahn4Dsm"] = false;
        Overlays["broGeomorphology"] = false;
        Overlays["broSoilMap"] = false;
        Overlays["broGroundwaterGhg"] = false;
        Overlays["broGroundwaterGlg"] = false;
        Overlays["broGroundwaterGvg"] = false;
        Overlays["broGroundwaterGt"] = false;
        Overlays["broGroundwaterDocumentation"] = false;
        Overlays["baseMap"] = true;
    }

    public void NormalizeSurfaceAnalysisMapState()
    {
        foreach (var key in new[]
                 {
                     "buildings", "addresses", "ahn4Dtm", "ahn4Dsm", "broGeomorphology", "broSoilMap",
                     "broGroundwaterGhg", "broGroundwaterGlg", "broGroundwaterGvg", "broGroundwaterGt",
                     "broGroundwaterDocumentation", "designImport", "customImport", "profileTracePoints", "machines"
                 })
        {
            Overlays[key] = false;
        }

        // The surface-analysis map is a "Boorlijn op BGT-achtergrond" view: the basemap
        // must always be shown. Force it on so a stored baseMap=false (e.g. left over from
        // an earlier map-state) can never blank the map.
        Overlays["baseMap"] = true;

        foreach (var key in new[] { "parcels", "bgt", "bagImport", "klic", "klicBuffer", "boreTrace" })
        {
            if (!Overlays.ContainsKey(key))
            {
                Overlays[key] = true;
            }
        }

        if (!Overlays.ContainsKey("boreTraceInfo")) Overlays["boreTraceInfo"] = false;
        if (!Overlays.ContainsKey("boreTraceNumbers")) Overlays["boreTraceNumbers"] = false;
        if (!Overlays.ContainsKey("boreTraceLengths")) Overlays["boreTraceLengths"] = false;
    }

    public bool IsProjectLayerVisible(string layerId) =>
        !ProjectLayers.TryGetValue(layerId, out var visible) || visible;

    public bool ToggleProjectLayer(string layerId)
    {
        var visible = !IsProjectLayerVisible(layerId);
        ProjectLayers[layerId] = visible;
        return visible;
    }

    public void SyncProjectLayerStates(
        IEnumerable<ProjectFileRecord> files,
        Func<ProjectFileRecord, bool> isFilterable,
        Func<ProjectFileRecord, string> layerIdResolver)
    {
        var activeIds = UniqueProjectFiles(files)
            .Where(isFilterable)
            .Select(layerIdResolver)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var staleId in ProjectLayers.Keys.Where(id => !activeIds.Contains(id)).ToList())
        {
            ProjectLayers.Remove(staleId);
        }

        foreach (var layerId in activeIds)
        {
            if (!ProjectLayers.ContainsKey(layerId))
            {
                ProjectLayers[layerId] = true;
            }
        }
    }

    public bool EnsureKlicTheme(string theme, bool visible = true)
    {
        if (KlicThemes.ContainsKey(theme)) return false;
        KlicThemes[theme] = visible;
        return true;
    }

    public bool ToggleKlicTheme(string theme)
    {
        var visible = !KlicThemes.TryGetValue(theme, out var current) || !current;
        KlicThemes[theme] = visible;
        return visible;
    }

    public bool IsBgtSurfaceVisible(string key) =>
        !BgtSurfaces.TryGetValue(key, out var visible) || visible;

    public bool ToggleBgtSurface(string key)
    {
        var visible = !BgtSurfaces.TryGetValue(key, out var current) || !current;
        BgtSurfaces[key] = visible;
        return visible;
    }

    public static bool IsImportOverlayKey(string key) =>
        key is "bgt" or "bagImport" or "klic" or "klicBuffer" or "designImport" or "customImport";

    private static void SetAll(Dictionary<string, bool> states, bool visible)
    {
        foreach (var key in states.Keys.ToList())
        {
            states[key] = visible;
        }
    }

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
