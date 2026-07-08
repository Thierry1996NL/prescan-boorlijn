using Borevexa.App.Models;
using Borevexa.Core.Models;
using Borevexa.Core.Services;

namespace Borevexa.App;

public partial class MainWindow
{
    private static string ProjectConclusion(PrescanProject project, BoringResult boring) =>
        $"Projectgegevens zijn vastgelegd voor {project.Name}. De berekende minimale boring is Ø{boring.BoringDiameter:N0} mm bij een productbundel van Ø{boring.BundleDiameter:N0} mm.";

    private static IEnumerable<(string Item, string Priority, string Action)> ProjectRestPoints(PrescanProject project, BoringResult boring)
    {
        if (string.IsNullOrWhiteSpace(project.Client) || project.Client.Equals("Lokale opdrachtgever", StringComparison.OrdinalIgnoreCase))
        {
            yield return ("Opdrachtgever controleren", "Laag", "Projectinformatie aanvullen");
        }

        if (project.BoreLengthMeters <= 0)
        {
            yield return ("Boorlengte ontbreekt", "Hoog", "Projectinformatie corrigeren");
        }

        if (boring.Processed.Count == 0)
        {
            yield return ("Boringconfiguratie bevat geen onderdelen", "Middel", "Mantelbuizen/kabels toevoegen");
        }
    }

    private static string ImportConclusion(IReadOnlyList<ProjectFileRecord> projectFiles, IReadOnlyList<ProjectDocumentEntry> docs, IReadOnlyList<ProjectMapLayer> layers) =>
        $"Er zijn {projectFiles.Count} projectbestand(en), {docs.Count} document(en) en {layers.Count} GIS-laag/lagen beschikbaar voor de prescan.";

    private static IEnumerable<(string Item, string Priority, string Action)> ImportRestPoints(IReadOnlyList<ProjectFileRecord> projectFiles, IReadOnlyList<ProjectMapLayer> layers)
    {
        if (!projectFiles.Any(file => file.FileType.Contains("KLIC", StringComparison.OrdinalIgnoreCase)))
        {
            yield return ("KLIC-bestand ontbreekt", "Hoog", "KLIC GML/ZIP importeren");
        }

        if (!projectFiles.Any(file => file.FileType.Contains("BGT", StringComparison.OrdinalIgnoreCase)))
        {
            yield return ("BGT-import ontbreekt", "Middel", "BGT ZIP/GML importeren");
        }

        if (!projectFiles.Any(file => file.FileType.Contains("BAG", StringComparison.OrdinalIgnoreCase) || file.FileType.Contains("Kadaster", StringComparison.OrdinalIgnoreCase)))
        {
            yield return ("Kadaster/BAG-import ontbreekt", "Middel", "Kadaster/BAG ZIP importeren");
        }

        if (layers.Count == 0)
        {
            yield return ("Geen GIS-lagen uit import gelezen", "Hoog", "Importbestanden controleren");
        }
    }

    private string DesignConclusion(IReadOnlyList<ProjectMapLayer> layers) =>
        $"De ontwerp- en importkaart bevat {layers.Count} laag/lagen. Zichtbare overlays: {JoinVisible(_mapOverlayStates)}.";

    private IEnumerable<(string Item, string Priority, string Action)> DesignRestPoints(IReadOnlyList<ProjectMapLayer> layers)
    {
        if (layers.Count == 0)
        {
            yield return ("Geen kaartlagen beschikbaar", "Hoog", "Importeer ontwerp/KLIC/BGT/Kadaster-data");
        }

        if (!_mapOverlayStates.Any(pair => pair.Value))
        {
            yield return ("Alle kaartoverlays staan uit", "Laag", "Relevante kaartlagen inschakelen");
        }
    }

    private static string TraceConclusion(IReadOnlyList<TracePointRow> traceRows, double traceLength) =>
        traceRows.Count >= 2
            ? $"De boorlijn is vastgelegd met {traceRows.Count} punt(en) en een lengte van {traceLength:N1} m."
            : "Er is nog geen volledige boorlijn beschikbaar.";

    private static IEnumerable<(string Item, string Priority, string Action)> TraceRestPoints(IReadOnlyList<TracePointRow> traceRows, double traceLength)
    {
        if (traceRows.Count < 2)
        {
            yield return ("Boorlijn heeft minder dan twee punten", "Hoog", "Boorlijn tekenen en opslaan");
        }

        if (traceLength <= 0)
        {
            yield return ("Boorlijnlengte is 0 m", "Hoog", "Boorlijn controleren");
        }
    }

    private static string SurfaceConclusion(IReadOnlyList<BgtSurfaceSegment> surfaceSegments, double traceLength) =>
        surfaceSegments.Count == 0
            ? "Er is nog geen BGT-oppervlakteprofiel beschikbaar."
            : $"De oppervlakteanalyse verdeelt {traceLength:N1} m boorlijn over {surfaceSegments.Count} BGT-segment(en).";

    private static IEnumerable<(string Item, string Priority, string Action)> SurfaceRestPoints(IReadOnlyList<BgtSurfaceSegment> surfaceSegments, IReadOnlyList<ProjectFileRecord> projectFiles)
    {
        if (!projectFiles.Any(file => file.FileType.Contains("BGT", StringComparison.OrdinalIgnoreCase)))
        {
            yield return ("BGT-bron ontbreekt voor oppervlakteanalyse", "Middel", "BGT ZIP/GML importeren");
        }

        if (surfaceSegments.Count == 0)
        {
            yield return ("Geen BGT-oppervlaktesegmenten gevonden", "Middel", "BGT filters en boorlijn controleren");
        }
    }

    private string UndergroundConclusion() =>
        _profilePoints.Count == 0
            ? "Er zijn nog geen profielpunten beschikbaar voor ondergrond/maaiveld."
            : $"Ondergrondinformatie is gekoppeld aan {_profilePoints.Count} profielpunt(en). Maaiveld varieert van {_profilePoints.Min(point => point.Surface):N2} tot {_profilePoints.Max(point => point.Surface):N2} m NAP.";

    private IEnumerable<(string Item, string Priority, string Action)> UndergroundRestPoints()
    {
        if (_profilePoints.Count == 0)
        {
            yield return ("Geen profielpunten voor ondergrondanalyse", "Middel", "Dwarsprofiel/profielpunten genereren");
        }

        if (!_mapOverlayStates.GetValueOrDefault("ahn4Dtm"))
        {
            yield return ("AHN4 maaiveldlaag staat uit", "Laag", "AHN4 DTM inschakelen indien nodig");
        }
    }

    private string ProfileConclusion() =>
        _profilePoints.Count >= 2
            ? $"Het dwarsprofiel bevat {_profilePoints.Count} punt(en), met een maximale boringdiepte van {_profilePoints.Max(point => point.Depth):N2} m."
            : "Het dwarsprofiel is nog niet volledig beschikbaar.";

    private IEnumerable<(string Item, string Priority, string Action)> ProfileRestPoints()
    {
        if (_profilePoints.Count < 2)
        {
            yield return ("Dwarsprofiel bevat onvoldoende punten", "Hoog", "Profielpunten genereren of controleren");
        }

        if (_profilePoints.Any(point => point.Depth <= 0))
        {
            yield return ("Een of meer profielpunten hebben geen positieve diepte", "Middel", "Diepteprofiel controleren");
        }
    }

    private static string MachineConclusion(IReadOnlyList<MachinePlacementRow> machines, string? selectedMachineId) =>
        machines.Count == 0
            ? "Er zijn nog geen machine-/werkvakobjecten geplaatst."
            : $"Er zijn {machines.Count} machine-/werkvakobject(en) opgenomen. Geselecteerde machine: {selectedMachineId ?? "nog niet gekozen"}.";

    private static IEnumerable<(string Item, string Priority, string Action)> MachineRestPoints(IReadOnlyList<MachinePlacementRow> machines, string? selectedMachineId)
    {
        if (string.IsNullOrWhiteSpace(selectedMachineId))
        {
            yield return ("Machine type nog niet gekozen", "Middel", "Machine selecteren");
        }

        if (machines.Count == 0)
        {
            yield return ("Werkvak/machineplaatsing ontbreekt", "Middel", "Machine en werkvak op kaart plaatsen");
        }
    }

    private string ThreeDConclusion(BoringResult boring) =>
        _profilePoints.Count >= 2
            ? $"3D/ontwerpcontrole gebruikt het profiel met {_profilePoints.Count} punt(en) en een boring Ø{boring.BoringDiameter:N0} mm."
            : "3D/ontwerpcontrole mist nog een volledig profiel.";

    private IEnumerable<(string Item, string Priority, string Action)> ThreeDRestPoints()
    {
        if (_profilePoints.Count < 2)
        {
            yield return ("3D-profiel mist profielpunten", "Hoog", "Dwarsprofiel genereren");
        }

        if (_selectedProject is not null &&
            (_projects.GetStepData(_selectedProject.Id, ProfileStepNumber, "diepteprofiel_3d") ??
             _projects.GetStepData(_selectedProject.Id, LegacyProfileStepNumber, "diepteprofiel_3d")) is null)
        {
            yield return ("3D-diepteprofiel is niet opgeslagen", "Middel", "3D ontwerp opslaan");
        }
    }

    private static string FinalReportConclusion(IReadOnlyList<ProjectFileRecord> files, IReadOnlyList<ProjectDocumentEntry> docs, ParcelOwnerAnalysis parcelAnalysis) =>
        $"Het conceptrapport bevat {files.Count} projectbestand(en), {docs.Count} document(en) en {parcelAnalysis.Segments.Count} perceel-/bronhoudersegment(en).";

    private IEnumerable<(string Item, string Priority, string Action)> FinalReportRestPoints(ParcelOwnerAnalysis parcelAnalysis)
    {
        foreach (var item in BuildEnvironmentResidualPoints(parcelAnalysis))
        {
            yield return item;
        }

        if (_selectedProject is not null && string.IsNullOrWhiteSpace(ReadStepReportSection(11)))
        {
            yield return ("Eindrapporttekst nog niet handmatig aangevuld", "Laag", "Rapportpreview stap 11 aanvullen indien gewenst");
        }
    }
}
