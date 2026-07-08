namespace Borevexa.Core.Models;

public static class StepReportCatalog
{
    public static IReadOnlyList<PrescanSubstep> GetSubsteps(int stepNumber) => stepNumber switch
    {
        0 =>
        [
            Substep(0, "0.1", "0.1", "Voorblad", "Projectidentiteit, revisie en rapportmetadata."),
            Substep(0, "0.2", "0.2", "Voorwoord", "Rapportcontext, uitgangspunten en verantwoording."),
            Substep(0, "0.3", "0.3", "Inhoudsopgave", "Automatische hoofdstuk- en bijlagenstructuur.")
        ],
        1 =>
        [
            ChapterIntroduction(1, "Projectinformatie", "Hoofdstukinleiding voor projectgegevens, boringconfiguratie en machinekeuze."),
            Substep(1, "1.1", "1.2", "Projectinformatie", "Projectnaam, opdrachtgever, locatie, status en basisparameters."),
            Substep(1, "1.2", "1.3", "Inhoud", "Productbundel, mantelbuizen, kabels, materiaal en boortechniek."),
            Substep(1, "1.3", "1.4", "Vulgraadberekening & dwarsdoorsnede", "Bundeldiameter, benodigde boring, vulgraad en doorsnede."),
            Substep(1, "1.4", "1.5", "Machine kiezen", "Machinekeuze, capaciteit en boortechniek.")
        ],
        2 =>
        [
            ChapterIntroduction(2, "Ontwerp, KLIC, BAG & BGT inladen", "Hoofdstukinleiding voor importbestanden, documenten en GIS-lagen."),
            Substep(2, "2.1", "2.2", "Bestanden", "Ontwerp-, KLIC-, BAG/Kadaster- en BGT-bronnen."),
            Substep(2, "2.2", "2.3", "Documenten", "KLIC-documenten en overige bijlagen."),
            Substep(2, "2.3", "2.4", "Lagen & kruisingen", "Gelezen GIS-lagen en KLIC-kruisingen langs de boorlijn.")
        ],
        3 =>
        [
            ChapterIntroduction(3, "Boorlijn", "Hoofdstukinleiding voor trace, KLIC-kruisingen en EV-zones."),
            Substep(3, "3.1", "3.2", "Boorlijn ingetekend", "Opgeslagen tracepunten, richting, lengte en rapportkaart."),
            Substep(3, "3.2", "3.3", "KLIC kruisingen", "KLIC-kruisingen en raakvlakken langs de boorlijn."),
            Substep(3, "3.3", "3.4", "EV zones", "Eisvoorzorgsmaatregelen uit KLIC nabij of op de boorlijn.")
        ],
        4 =>
        [
            ChapterIntroduction(4, "Oppervlakteanalyse", "Hoofdstukinleiding voor BGT-segmenten, oppervlakten en AHN4-maaiveldhoogte."),
            Substep(4, "4.1", "4.2", "BGT-segmenten", "BGT-segmenten en oppervlakken langs de boorlijn."),
            Substep(4, "4.2", "4.3", "BGT Analyse langs boorlijn", "Herstel-, vergunning- en uitvoeringssignalen per BGT-segment."),
            Substep(4, "4.3", "4.4", "AHN4/maaiveld hoogte bepalen", "GIS kaart met boorlijn en AHN4 maaiveldhoogteprofiel.")
        ],
        5 =>
        [
            ChapterIntroduction(5, "Omgevingsmanagement", "Hoofdstukinleiding voor percelen, eigenaren, ZRO en omgevingsinformatie."),
            Substep(5, "5.1", "5.2", "Perceelsegmenten", "Kadastrale segmenten langs de boorlijn."),
            Substep(5, "5.2", "5.3", "ZRO Analyse & informatie", "Zakelijke rechten, bronhouders, stakeholders en restpunten.")
        ],
        6 =>
        [
            ChapterIntroduction(6, "Ondergrondanalyse", "Hoofdstukinleiding voor BRO/DINOloket-bronnen en ondergrondkaarten."),
            Substep(6, "6.1", "6.2", "BRO DGM", "Losse BRO DGM kaartdataset met handmatig gekozen kaartpunt."),
            Substep(6, "6.2", "6.3", "REGIS II", "Losse BRO REGIS II kaartdataset met handmatig gekozen kaartpunt."),
            Substep(6, "6.3", "6.4", "Geomorfologie", "BRO Geomorfologie kaartdataset met handmatig gekozen kaartpunt."),
            Substep(6, "6.4", "6.5", "Bodemkaart", "BRO Bodemkaart kaartdataset met handmatig gekozen kaartpunt."),
            Substep(6, "6.5.1", "6.6.1", "Grondwaterspiegeldiepte GHG", "Gemiddelde hoogste grondwaterspiegeldiepte (GHG) als BRO WMS-kaartlaag."),
            Substep(6, "6.5.2", "6.6.2", "Grondwaterspiegeldiepte GLG", "Gemiddelde laagste grondwaterspiegeldiepte (GLG) als BRO WMS-kaartlaag."),
            Substep(6, "6.5.3", "6.6.3", "Grondwaterspiegeldiepte GVG", "Gemiddelde voorjaarsgrondwaterspiegeldiepte (GVG) als BRO WMS-kaartlaag."),
            Substep(6, "6.5.4", "6.6.4", "Grondwatertrappen", "Afgeleide grondwatertrappen (Gt) als BRO WMS-kaartlaag."),
            Substep(6, "6.5.5", "6.6.5", "Modeldocumentatie", "BRO modeldocumentatie grondwaterspiegeldiepte als WMS-kaartlaag.")
        ],
        7 =>
        [
            ChapterIntroduction(7, "Dwarsprofiel", "Hoofdstukinleiding voor profiel, hoogte, dieptepunten en kruisingen."),
            Substep(7, "7.1", "7.2", "Dwarsprofiel ingetekend", "Afstand, diepte, maaiveld, NAP en kruisingen in het dwarsprofiel.")
        ],
        8 =>
        [
            ChapterIntroduction(8, "Machine locatie", "Hoofdstukinleiding voor machinepositie, werkruimte en uitvoering."),
            Substep(8, "8.1", "8.2", "Machine ingetekend", "Machinepositie, werkvak, putten, bereik en uitvoeringsruimte.")
        ],
        9 =>
        [
            ChapterIntroduction(9, "Sonderingen", "Hoofdstukinleiding voor sonderingen en geotechnische aandachtspunten."),
            Substep(9, "9.1", "9.2", "Sonderingen ingetekend", "Sonderingen, bronverwijzingen en geotechnische aandachtspunten.")
        ],
        10 =>
        [
            ChapterIntroduction(10, "Eindrapport & Export", "Hoofdstukinleiding voor rapportcontrole, conclusie en export."),
            Substep(10, "10.1", "10.2", "Volledigheidscheck", "Beschikbare staprapportdata, bronnen en kaartbeelden."),
            Substep(10, "10.2", "10.3", "Eindconclusie", "Samenvatting uit alle opgeslagen substappen."),
            Substep(10, "10.3", "10.4", "Export", "Rapport- en CAD-exportstatus.")
        ],
        11 =>
        [
            ChapterIntroduction(11, "3D export", "Hoofdstukinleiding voor 3D-context, conflictcontrole en export."),
            Substep(11, "11.1", "11.2", "3D context", "Boorlijn, maaiveld en objecten in ruimte."),
            Substep(11, "11.2", "11.3", "Conflictcontrole", "Ruimtelijke controles en aandachtspunten."),
            Substep(11, "11.3", "11.4", "3D export", "Aparte 3D-output buiten het eindrapport.")
        ],
        12 =>
        [
            ChapterIntroduction(12, "Werktekening", "Hoofdstukinleiding voor werktekeningkaarten, profiel en exportdiagnose."),
            Substep(12, "12.1", "12.2", "Situatiekaart", "Werktekeningkaart en titelblok."),
            Substep(12, "12.2", "12.3", "Dwarsprofiel", "Profiel op tekenblad."),
            Substep(12, "12.3", "12.4", "Exportdiagnose", "Brondata, schaal en waarschuwingen.")
        ],
        _ =>
        [
            ChapterIntroduction(stepNumber, "Overzicht", "Hoofdstukinleiding voor dit rapportonderdeel."),
            Substep(stepNumber, $"{stepNumber}.1", $"{stepNumber}.2", "Overzicht", "Algemene stapdata.")
        ]
    };

    private static PrescanSubstep ChapterIntroduction(int stepNumber, string chapterTitle, string description) =>
        Substep(stepNumber, $"{stepNumber}.intro", $"{stepNumber}.1", "Inleiding", description, isChapterIntroduction: true, reportCardTitle: $"{chapterTitle} - inleiding");

    private static PrescanSubstep Substep(
        int stepNumber,
        string number,
        string displayNumber,
        string title,
        string description,
        bool isChapterIntroduction = false,
        string? reportCardTitle = null) => new()
    {
        StepNumber = stepNumber,
        Number = number,
        DisplayNumber = displayNumber,
        Title = title,
        Description = description,
        ReportSectionTitle = $"{displayNumber} {title}",
        ReportCardTitle = reportCardTitle ?? title,
        IsChapterIntroduction = isChapterIntroduction
    };
}
