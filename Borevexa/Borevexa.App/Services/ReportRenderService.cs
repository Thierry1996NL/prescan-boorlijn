using System.Text.Json;
using Borevexa.App.Reports.Blocks;
using Borevexa.App.Reports.Renderers;

namespace Borevexa.App.Services;

public sealed class ReportRenderService
{
    private readonly IReadOnlyDictionary<string, IReportSubstepRenderer> _renderers;

    public ReportRenderService()
        : this([
            new ProjectInformationReportRenderer(),
            new BoringContentReportRenderer(),
            new BoringCalculationReportRenderer(),
            new ImportedFilesReportRenderer(),
            new DocumentsReportRenderer(),
            new LayersAndCrossingsReportRenderer(),
            new MapControlReportRenderer(),
            new KlicCrossingsReportRenderer(),
            new KlicEvZonesReportRenderer(),
            new BgtSegmentsReportRenderer(),
            new MachineChoiceReportRenderer(),
            new ParcelSegmentsReportRenderer(),
            new ZroAnalysisReportRenderer(),
            new SubsurfaceSourcesReportRenderer("6.1"),
            new SubsurfaceSourcesReportRenderer("6.2"),
            new SubsurfaceSourcesReportRenderer("6.3"),
            new SubsurfaceSourcesReportRenderer("6.4"),
            new SubsurfaceSourcesReportRenderer("6.5.1"),
            new SubsurfaceSourcesReportRenderer("6.5.2"),
            new SubsurfaceSourcesReportRenderer("6.5.3"),
            new SubsurfaceSourcesReportRenderer("6.5.4"),
            new SubsurfaceSourcesReportRenderer("6.5.5"),
            new ProfileReportRenderer(),
            new MachineLocationReportRenderer(),
            new SoundingsReportRenderer(),
            new SummaryReportRenderer("0.1", "Voorblad", "Projectidentiteit en rapportmetadata worden automatisch uit projectinformatie en snapshotdata opgebouwd."),
            new SummaryReportRenderer("0.2", "Voorwoord", "Voorwoord en uitgangspunten worden automatisch uit de rapportgenerator gevuld."),
            new SummaryReportRenderer("0.3", "Inhoudsopgave", "De inhoudsopgave volgt automatisch uit de actieve stap- en substapstructuur."),
            new SummaryReportRenderer("10.1", "Volledigheidscheck", "Deze controle toont of de rapportcontracten per substap voldoende brondata hebben."),
            new FinalConclusionReportRenderer(),
            new SummaryReportRenderer("10.3", "Export", "Exportstatus voor rapport en aanvullende output."),
            new SummaryReportRenderer("11.1", "3D context", "3D context is een aparte output en wordt als rapportreferentie samengevat."),
            new SummaryReportRenderer("11.2", "Conflictcontrole", "Ruimtelijke conflicten worden samengevat zodra de 3D-controle is opgeslagen."),
            new SummaryReportRenderer("11.3", "3D export", "Status van de aparte 3D-export."),
            new SummaryReportRenderer("12.1", "Situatiekaart", "Werktekeningkaart en titelblok worden als aparte output beheerd."),
            new SummaryReportRenderer("12.2", "Dwarsprofiel", "Werktekeningprofiel wordt als aparte output beheerd."),
            new SummaryReportRenderer("12.3", "Exportdiagnose", "Diagnose van schaal, brondata en exporteerbaarheid van de werktekening.")
        ])
    {
    }

    public ReportRenderService(IEnumerable<IReportSubstepRenderer> renderers)
    {
        _renderers = renderers.ToDictionary(renderer => renderer.SubstepNumber, StringComparer.OrdinalIgnoreCase);
    }

    public bool TryRenderSubstep(string substepNumber, JsonElement data, out ReportRenderDocument document)
    {
        if (_renderers.TryGetValue(substepNumber, out var renderer))
        {
            document = renderer.Render(data);
            return document.Blocks.Count > 0;
        }

        document = ReportRenderDocument.Empty;
        return false;
    }
}
