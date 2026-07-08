namespace Borevexa.Core.Models;

public static class WorkflowCatalog
{
    public const int EnvironmentStepNumber = 5;
    public const int ProfileStepNumber = 7;
    public const int MachineStepNumber = 8;
    public const int SoundingStepNumber = 9;
    public const int ReportStepNumber = 10;
    public const int ThreeDStepNumber = 11;
    public const int WorkDrawingStepNumber = 12;
    public const int LegacyProfileStepNumber = 8;
    public const int LegacyMachineStepNumber = 9;

    public static IReadOnlyDictionary<int, StepWorkspace> CreateWorkspaces() => new Dictionary<int, StepWorkspace>
    {
        [0] = Step(0, "Voorblad voorwoord & inhoudsopgave", "Controle van de rapportstart: voorblad, voorwoord, inhoudsopgave en rapportopbouw.", "Rapportstart", "Voorblad, voorwoord en inhoudsopgave.",
            ["Controleer rapportstart", "Genereer rapport"],
            Card("Voorblad", "Rapportidentiteit", "Projectnaam, opdrachtgever, locatie, status, boorlengte en generatiedatum voor het voorblad."),
            Card("Voorwoord", "Rapportcontext", "Korte toelichting op de prescan, uitgangspunten en automatische rapportopbouw."),
            Card("Inhoudsopgave", "Hoofdstukindeling", "Overzicht van de rapporthoofdstukken en bijlagen zoals deze in de eindrapportage terugkomen.")),
        [1] = Step(1, "Projectinformatie", "Projectgegevens, boringconfiguratie, diameter, materiaal en machinekeuze.", "Boring doorsnede", "Bundel, mantelbuizen en machinebereik.",
            ["Controleer lokale database", "Project opslaan", "Bereken boringconfiguratie", "Controleer materiaal"],
            Card("Input", "Projectgegevens", "Naam, opdrachtgever, locatie, boorlengte, diameter, materiaal en bijzonderheden."),
            Card("Boring", "Configurator", "Mantelbuis, kabels, vulgraad, boordiameter en machinekeuze.")),
        [2] = Step(2, "Ontwerp, KLIC, BAG & BGT inladen", "Ontwerp, custom lagen, KLIC, Kadaster/BAG en BGT bestanden beheren.", "Bestanden en lagen", "NEN-1775 kleuren, BAG/BGT en eigen lagen.",
            ["Kies KLIC bestand", "Importeer DXF/GML", "Valideer lagen"],
            Card("Upload", "Alle importbestanden", "Bestanden blijven lokaal beschikbaar in de projectbestandenmap en worden gekoppeld aan SQLite."),
            Card("Lagen", "Standaard en eigen lagen", "LS, MS, gas, water, data, KLIC, Kadaster/BAG, BGT en eigen laagvelden.")),
        [3] = Step(3, "Boorlijn", "Kaartcontrole, KLIC-kruisingen, EV-zones en opgeslagen boorlijn.", "Boorlijn werkvlak", "PDOK context, KLIC-kruisingen, EV-zones en tracepunten.",
            ["Start tekenmodus", "Boorlijn opslaan", "Controleer kruisingen", "Controleer EV-zones"],
            Card("Trace", "Boorlijn ingetekend", "Tracepunten, richting, lengte en rapportkaart."),
            Card("KLIC", "KLIC kruisingen", "Kruisingen langs de boorlijn met thema, afstand en aandachtspunt."),
            Card("EV", "EV zones", "Eisvoorzorgsmaatregelen uit KLIC nabij of op de boorlijn.")),
        [4] = Step(4, "Oppervlakteanalyse", "BGT-segmenten en analyse langs de boorlijn.", "BGT analyse", "Segmenten en oppervlaktes langs het trace.",
            ["Open PDOK BGT downloader", "Importeer BGT download", "Analyse uitvoeren", "Sla analyse op"],
            Card("BGT", "BGT-segmenten", "Segmenten uit BGT langs de boorlijn."),
            Card("Analyse", "BGT analyse langs boorlijn", "Herstel-, vergunning- en uitvoeringssignalen per segment."),
            Card("AHN4", "AHN4/maaiveld hoogte bepalen", "GIS kaart met boorlijn en AHN4 maaiveldhoogteprofiel.")),
        [5] = Step(5, "Omgevingsmanagement", "Perceelsegmenten, ZRO analyse en omgevingsinformatie.", "Omgevingsmanagement", "Percelen, bronhouders en rechten langs de boorlijn.",
            ["Analyse uitvoeren", "BAG/Kadaster aan/uit", "BGT aan/uit", "Sla analyse op"],
            Card("Percelen", "Perceelsegmenten", "Gemeente, sectie, perceelnummer en identificatie per boorlijnsegment."),
            Card("ZRO", "ZRO Analyse & informatie", "Zakelijke rechten, bronhouders, stakeholders en restpunten.")),
        [6] = Step(6, "Ondergrondanalyse", "Losse BRO/DINOloket kaartdatasets met handmatige bronselectie.", "BRO kaartdatasets", "DGM, REGIS II, geomorfologie, bodemkaart en grondwaterspiegeldiepte.",
            ["BRO DGM laden", "BRO REGIS II laden", "BRO Geomorfologie laden", "BRO Bodemkaart laden", "BRO GHG kaartlaag tonen", "BRO GLG kaartlaag tonen", "BRO GVG kaartlaag tonen", "BRO Grondwatertrappen kaartlaag tonen", "BRO Modeldocumentatie kaartlaag tonen", "Zoom naar boorlijn", "Selectie opslaan"],
            Card("DGM", "BRO DGM", "Geologische BRO DGM kaartpunten; kies handmatig het relevante bolletje voor de rapportage."),
            Card("REGIS", "REGIS II", "Hydrogeologische REGIS II kaartpunten; kies handmatig het relevante bolletje voor de rapportage."),
            Card("GEO", "Geomorfologie", "BRO Geomorfologie kaartdataset als losse bron rond het projectgebied."),
            Card("Bodem", "Bodemkaart", "BRO Bodemkaart kaartdataset als losse bron rond het projectgebied."),
            Card("GW", "Grondwaterspiegel", "BRO Grondwaterspiegeldiepte kaartdataset als losse bron rond het projectgebied: GHG, GLG, GVG, grondwatertrappen en modeldocumentatie.")),
        [7] = Step(7, "Dwarsprofiel", "Dwarsprofiel ingetekend met maaiveld, dieptepunten en kruisingen.", "Diepteprofiel", "Maaiveld, boorlijn en kabelkruisingen.",
            ["Genereer profiel", "Sla dieptepunten op", "Download GeoJSON"],
            Card("Profiel", "Dwarsprofiel ingetekend", "Punten over afstand met maaiveldhoogte, diepte, NAP en kruisingen.")),
        [8] = Step(8, "Machine locatie", "Machinepositie, werkvak en logistieke aandachtspunten.", "Werkvak", "Machine, put, buizenrek en bereik.",
            ["Plaats boormachine", "Plaats bentonietwagen", "Lijn machine uit op boorlijn", "Sla machines op"],
            Card("Machine", "Machine ingetekend", "Machinepositie, rotatie, werkruimte, putten en bereik ten opzichte van de boorlijn.")),
        [9] = Step(9, "Sonderingen", "Sonderingen ingetekend en gekoppeld aan het boortrace.", "Sonderingen", "Geotechnische referenties rond het trace.",
            ["Importeer sondering", "Koppel bodemprofiel", "Controleer boorrisico"],
            Card("Sonderingen", "Sonderingen ingetekend", "CPT/sonderingen en bodemprofielen als onderbouwing voor boring en risicobeoordeling.")),
        [10] = Step(10, "Eindrapport & Export", "Volledigheidscheck, rapportage, DXF/DWG en AutoCAD-ready output.", "Rapportage", "Prescan output en CAD-lagen.",
            ["Maak CAD export preview", "Genereer rapport", "Volledigheidscheck"],
            Card("Rapport", "Prescan samenvatting", "Projectdata, analyses, profiel, risico's en conclusies."),
            Card("CAD", "DXF/DWG export", "Boorlijn, labels, KLIC/BGT referentie en AutoCAD plugin voorbereiding.")),
        [11] = Step(11, "3D export", "Ruimtelijke context met boorlijn, KLIC, maaiveld en 3D objecten.", "3D context", "Boorlijn, maaiveld en objecten in ruimte.",
            ["Laad 3D viewer", "Toon KLIC in 3D", "Controleer conflicten"],
            Card("3D", "Ruimtelijke visualisatie", "Voor native kan dit via aparte 3D-module of embedded viewer."),
            Card("Conflict", "Verticale en horizontale ruimte", "Controle op kruisingen, dekking, werkruimte en objecthoogtes.")),
        [12] = Step(12, "Werktekening", "Print-preview op basis van boorlijn, dwarsprofiel, KLIC en BAG/BGT/Kadaster import.", "Werktekening", "Genereer een vaste A3 werktekening met diagnose van de kaartdata.",
            ["Genereer werktekening", "Exporteer werktekening"],
            Card("Preview", "A3 liggend", "Vaste tekenblad-compositie met situatiekaart, profiel, legenda, schaal en titelblok."),
            Card("Debug", "BAG/BGT/Kadaster", "De tekening meldt objectaantallen, RD-bounds en overlapwaarschuwingen."))
    };

    private static StepWorkspace Step(int number, string title, string subtitle, string mapTitle, string mapSubtitle, IReadOnlyList<string> actions, params WorkspaceCard[] cards) => new()
    {
        StepNumber = number,
        Title = title,
        Subtitle = subtitle,
        MapTitle = mapTitle,
        MapSubtitle = mapSubtitle,
        Actions = actions,
        Cards = cards
    };

    private static WorkspaceCard Card(string label, string title, string body) => new()
    {
        Label = label,
        Title = title,
        Body = body
    };
}
