using System.Globalization;
using System.Text.Json;
using Borevexa.App.Reports.Blocks;

namespace Borevexa.App.Reports.Renderers;

public sealed class MachineChoiceReportRenderer : IReportSubstepRenderer
{
    public string SubstepNumber => "1.4";

    public ReportRenderDocument Render(JsonElement data)
    {
        if (ReportJson.Text(data, "status", "").Contains("Geen boringconfiguratie", StringComparison.OrdinalIgnoreCase))
        {
            return new ReportRenderDocument([
                new ReportRenderKeyValuesBlock([
                    Row("Machine", "Nog niet gekozen"),
                    Row("Techniek", ReportJson.Text(data, "drillingTechnique", "Nader te bepalen")),
                    Row("Status", "Geen boringconfiguratie vastgelegd")
                ]),
                new ReportRenderNoteBlock("Machinekeuze wordt pas definitief opgenomen nadat de actuele boringconfiguratie is vastgelegd.")
            ]);
        }

        var machine = ReportJson.Property(data, "selectedMachine") ?? default;
        var requiredBoringDiameter = ReportJson.Int(data, "requiredBoringDiameter");
        var rows = new List<ReportRenderRow>
        {
            Row("Vereiste boring", requiredBoringDiameter > 0 ? $"Ø{requiredBoringDiameter:N0} mm" : "-"),
            Row("Machine", MachineName(machine, ReportJson.Text(data, "selectedMachineId", "Nog niet gekozen"))),
            Row("Techniek", ReportJson.Text(data, "drillingTechnique", "-")),
            Row("Status", string.IsNullOrWhiteSpace(ReportJson.Text(data, "selectedMachineId", "")) ? "Nog kiezen" : "Gekozen")
        };

        var recommendationLabel = ReportJson.Text(data, "recommendationLabel", "");
        var recommendationReason = ReportJson.Text(data, "recommendationReason", "");
        if (!string.IsNullOrWhiteSpace(recommendationLabel))
        {
            rows.Add(Row("Advies", recommendationLabel));
        }

        if (!string.IsNullOrWhiteSpace(recommendationReason))
        {
            rows.Add(Row("Waarom", recommendationReason));
        }

        if (machine.ValueKind == JsonValueKind.Object)
        {
            rows.Add(Row("Motor", ReportJson.Text(machine, "engine", "-")));
            rows.Add(Row("Max. boring", $"Ø{ReportJson.Int(machine, "maxBoring"):N0} mm"));
            rows.Add(Row("Duw/trek", MachineForce(machine)));
            rows.Add(Row("Koppel", $"{ReportJson.Double(machine, "torqueNm"):N0} Nm"));
            rows.Add(Row("Stangenrek", $"{ReportJson.Double(machine, "rodsMeters"):N0} m"));
            var sourceNote = ReportJson.Text(machine, "sourceNote", "");
            if (!string.IsNullOrWhiteSpace(sourceNote))
            {
                rows.Add(Row("Bronnotitie", sourceNote));
            }
        }

        return new ReportRenderDocument([
            new ReportRenderKeyValuesBlock(rows),
            new ReportRenderNoteBlock("Controleer machinebereik, boringdiameter, duw/trekkracht en werkruimte voordat dit rapportdeel definitief wordt vrijgegeven.")
        ]);
    }

    private static string MachineName(JsonElement machine, string fallback)
    {
        var brand = ReportJson.Text(machine, "brand", "");
        var model = ReportJson.Text(machine, "model", "");
        var name = $"{brand} {model}".Trim();
        return string.IsNullOrWhiteSpace(name) ? fallback : name;
    }

    private static string MachineForce(JsonElement machine)
    {
        var thrust = ReportJson.Double(machine, "pushKn");
        var pullback = ReportJson.Double(machine, "pullbackKn", thrust);
        if (pullback <= 0)
        {
            pullback = thrust;
        }

        return Math.Abs(pullback - thrust) < 0.05
            ? $"{thrust:N1} kN"
            : $"duw {thrust:N1} kN / trek {pullback:N1} kN";
    }

    private static ReportRenderRow Row(string label, string value) => new(label, value);
}

public sealed class ParcelSegmentsReportRenderer : IReportSubstepRenderer
{
    public string SubstepNumber => "5.1";

    public ReportRenderDocument Render(JsonElement data)
    {
        var segments = ReportJson.Array(data, "segments").ToList();
        var rows = segments.Take(18)
            .Select(segment => (IReadOnlyList<string>)
            [
                $"{ReportJson.Double(segment, "start"):N1} m",
                $"{ReportJson.Double(segment, "end"):N1} m",
                $"{ReportJson.Double(segment, "length"):N1} m",
                ParcelLabel(segment),
                ReportText.ShortCell(ReportJson.Text(segment, "bgtHolderName", "-"), 32),
                ReportJson.Text(segment, "zroStatus", "Controle nodig")
            ])
            .ToList();

        if (rows.Count == 0)
        {
            rows.Add(["-", "-", "-", "Geen perceelsegmenten", "-", "Controle nodig"]);
        }

        return new ReportRenderDocument([
            new ReportRenderKeyValuesBlock([
                Row("Perceelsegmenten", segments.Count.ToString(CultureInfo.InvariantCulture)),
                Row("Gekruiste percelen", ReportJson.Int(data, "crossedParcelCount").ToString(CultureInfo.InvariantCulture)),
                Row("Boorlijnlengte", ReportJson.Double(data, "traceLength") > 0 ? $"{ReportJson.Double(data, "traceLength"):N1} m" : "-")
            ]),
            new ReportRenderHeadingBlock("Percelen langs boorlijn"),
            new ReportRenderTableBlock(["Van", "Tot", "Lengte", "Perceel", "Bronhouder", "ZRO"], rows)
        ]);
    }

    private static string ParcelLabel(JsonElement segment)
    {
        var municipality = ReportJson.Text(segment, "cadastralMunicipality", "");
        var section = ReportJson.Text(segment, "section", "");
        var parcel = ReportJson.Text(segment, "parcelNumber", "");
        var label = $"{municipality} {section} {parcel}".Trim();
        return string.IsNullOrWhiteSpace(label) ? ReportJson.Text(segment, "cadastralObjectId", "-") : label;
    }

    private static ReportRenderRow Row(string label, string value) => new(label, value);
}

public sealed class ZroAnalysisReportRenderer : IReportSubstepRenderer
{
    public string SubstepNumber => "5.2";

    public ReportRenderDocument Render(JsonElement data)
    {
        var segments = ReportJson.Array(data, "segments").ToList();
        var rows = segments.Take(18)
            .Select(segment => (IReadOnlyList<string>)
            [
                ReportText.ShortCell(ParcelLabel(segment), 30),
                ReportJson.Text(segment, "zroStatus", "Controle nodig"),
                ReportJson.Text(segment, "level", ReportJson.Text(segment, "risk", "Middel")),
                ReportText.ShortCell(ReportJson.Text(segment, "action", "Handmatig controleren"), 44)
            ])
            .ToList();

        if (rows.Count == 0)
        {
            rows.Add(["Geen percelen", "Controle nodig", "Hoog", "Voer perceel- en ZRO-analyse uit."]);
        }

        var manualChecks = segments.Count(segment => ReportJson.Text(segment, "zroStatus", "")
            .Contains("handmatig", StringComparison.OrdinalIgnoreCase));

        return new ReportRenderDocument([
            new ReportRenderKeyValuesBlock([
                Row("ZRO controles", segments.Count.ToString(CultureInfo.InvariantCulture)),
                Row("Handmatige controles", manualChecks.ToString(CultureInfo.InvariantCulture)),
                Row("Status", segments.Count == 0 ? "Nog uitvoeren" : "Controlelijst beschikbaar")
            ]),
            new ReportRenderHeadingBlock("ZRO en omgevingsacties"),
            new ReportRenderTableBlock(["Perceel", "ZRO status", "Risico", "Actie"], rows)
        ]);
    }

    private static string ParcelLabel(JsonElement segment)
    {
        var section = ReportJson.Text(segment, "section", "");
        var parcel = ReportJson.Text(segment, "parcelNumber", "");
        var label = $"{section} {parcel}".Trim();
        return string.IsNullOrWhiteSpace(label) ? ReportJson.Text(segment, "cadastralObjectId", "-") : label;
    }

    private static ReportRenderRow Row(string label, string value) => new(label, value);
}

public sealed class SubsurfaceSourcesReportRenderer : IReportSubstepRenderer
{
    public SubsurfaceSourcesReportRenderer(string substepNumber = "6.1")
    {
        SubstepNumber = substepNumber;
    }

    public string SubstepNumber { get; }

    public ReportRenderDocument Render(JsonElement data)
    {
        var soundings = ReportJson.Array(data, "soundings").ToList();
        var selected = ReportJson.Property(data, "selectedSounding") ?? default;
        var selectedSoundings = ReportJson.Array(data, "selectedSoundings").ToList();
        if (selectedSoundings.Count == 0 && selected.ValueKind == JsonValueKind.Object)
        {
            selectedSoundings.Add(selected);
        }
        var importedProfiles = ReportJson.Array(data, "importedProfiles").ToList();

        if (ReportJson.Bool(data, "isWmsMapLayer"))
        {
            return new ReportRenderDocument([
                new ReportRenderKeyValuesBlock([
                    Row("Actief model", ReportJson.Text(data, "activeModel", "-")),
                    Row("Bron", "BRO/DINOloket via PDOK WMS"),
                    Row("Boorlijnreferentie", ReportJson.Double(data, "traceLength") > 0 ? $"{ReportJson.Double(data, "traceLength"):N1} m beschikbaar" : "Geen boorlijnreferentie"),
                    Row("Kaartstatus", ReportJson.Text(data, "mapStateAvailable", "Nee")),
                    Row("Status", ReportJson.Text(data, "status", "Kaartlaag beschikbaar"))
                ]),
                new ReportRenderNoteBlock(ReportJson.Text(data, "note", "Deze BRO/DINOloket kaartlaag wordt als transparante WMS-laag over de GIS-kaart gelegd. Zet de kaart vast in de substap om de actuele kaartuitsnede in het rapport te gebruiken."))
            ]);
        }

        var configuredMaxSelected = ReportJson.Int(data, "maxSelectedSoundings");
        var maxSelected = Math.Max(1, configuredMaxSelected == 0 ? 2 : configuredMaxSelected);

        var blocks = new List<ReportRenderBlock>
        {
            new ReportRenderKeyValuesBlock([
                Row("Actief model", ReportJson.Text(data, "activeModel", "-")),
                Row("Kaartpunten", ReportJson.Int(data, "soundingCount").ToString(CultureInfo.InvariantCulture)),
                Row("Rapportbron", importedProfiles.Count > 0 ? $"{importedProfiles.Count} geimporteerde PDF-profiel(en)" : SelectedLabel(selectedSoundings, maxSelected)),
                Row("Boorlijnreferentie", ReportJson.Double(data, "traceLength") > 0 ? $"{ReportJson.Double(data, "traceLength"):N1} m beschikbaar" : "Geen boorlijnreferentie"),
                Row("Kaartstatus", ReportJson.Text(data, "mapStateAvailable", "Nee")),
                Row("Status", ReportJson.Text(data, "status", "Controle nodig"))
            ])
        };

        if (importedProfiles.Count > 0)
        {
            var importedIndex = 1;
            var configuredMaxImported = ReportJson.Int(data, "maxImportedProfiles");
            foreach (var profile in importedProfiles.Take(Math.Max(1, configuredMaxImported == 0 ? maxSelected : configuredMaxImported)))
            {
                if (profile.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                blocks.Add(new ReportRenderHeadingBlock($"Geimporteerd BRO/DINOloket PDF-profiel {importedIndex}"));
                blocks.Add(new ReportRenderKeyValuesBlock([
                    Row("Identificatie", ReportJson.Text(profile, "identification", ReportJson.Text(profile, "code", "-"))),
                    Row("Dataset/model", ReportJson.Text(profile, "modelName", "-")),
                    Row("RD X", FormatNullable(profile, "x", "")),
                    Row("RD Y", FormatNullable(profile, "y", "")),
                    Row("Maaiveld", FormatNullable(profile, "surfaceNap", "m NAP")),
                    Row("Diepte t.o.v. maaiveld", $"{FormatNullable(profile, "depthTop", "m")} - {FormatNullable(profile, "depthBottom", "m")}"),
                    Row("Bronbestand", ReportJson.Text(profile, "sourceFile", "-")),
                    Row("Bron", "Officieel BRO/DINOloket boormonsterprofiel PDF")
                ]));

                var summary = ReportJson.Text(profile, "extractedSummary", "");
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    blocks.Add(new ReportRenderHeadingBlock("Uitgelezen profieltekst"));
                    blocks.Add(new ReportRenderNoteBlock(ReportText.ShortCell(summary, 900)));
                }

                importedIndex++;
            }

            blocks.Add(new ReportRenderNoteBlock("Voor DGM en REGIS II zijn de door de gebruiker geimporteerde officiele BRO/DINOloket PDF-profielen leidend. De GIS-kaart toont de boorlijn en bronlocatie alleen ter orientatie."));
            return new ReportRenderDocument(blocks);
        }

        if (soundings.Count == 0)
        {
            blocks.Add(new ReportRenderNoteBlock("Deze ondergronddataset is nog niet geladen of bevat nog geen kaartpunten voor dit projectgebied. Laad de dataset in de substap en kies de relevante bronpunten voordat dit onderdeel definitief wordt vrijgegeven."));
            return new ReportRenderDocument(blocks);
        }

        if (selectedSoundings.Count == 0)
        {
            blocks.Add(new ReportRenderNoteBlock($"Er zijn nog geen {ReportJson.Text(data, "activeModel", "BRO/DINOloket")}-bronpunten geselecteerd voor de rapportage. Selecteer maximaal {maxSelected} DINO-punt(en) in de GIS-kaart en sla de selectie op."));
            return new ReportRenderDocument(blocks);
        }

        var selectedIndex = 1;
        foreach (var selectedItem in selectedSoundings.Take(maxSelected))
        {
            if (selectedItem.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var intervals = ReportJson.Array(selectedItem, "profileIntervals").Take(16)
                .Select(interval => (IReadOnlyList<string>)
                [
                    $"{ReportJson.Double(interval, "topDepth"):N2} m",
                    $"{ReportJson.Double(interval, "bottomDepth"):N2} m",
                    ReportJson.Text(interval, "code", "-"),
                    ReportText.ShortCell(ReportJson.Text(interval, "label", "-"), 34),
                    ReportText.ShortCell(ReportJson.Text(interval, "lithology", "-"), 42)
                ])
                .ToList();

            blocks.Add(new ReportRenderHeadingBlock($"Geselecteerd BRO/DINOloket kaartpunt {selectedIndex}"));
            blocks.Add(new ReportRenderKeyValuesBlock([
                Row("Identificatie", ReportJson.Text(selectedItem, "code", "-")),
                Row("Dataset/model", ReportJson.Text(selectedItem, "modelName", "-")),
                Row("RD X", FormatNullable(selectedItem, "x", "")),
                Row("RD Y", FormatNullable(selectedItem, "y", "")),
                Row("Boorlijnreferentie", $"afstand {ReportJson.Double(selectedItem, "distance"):N1} m, offset {ReportJson.Double(selectedItem, "offset"):N1} m"),
                Row("Maaiveld", FormatNullable(selectedItem, "surfaceNap", "m NAP")),
                Row("Diepte t.o.v. maaiveld", $"0,00 - {FormatNullable(selectedItem, "endDepth", "m")}"),
                Row("Samenvatting", ReportText.ShortCell(ReportJson.Text(selectedItem, "profileSummary", ReportJson.Text(selectedItem, "soilSummary", "-")), 96)),
                Row("Status bron", ReportJson.Text(selectedItem, "status", "-"))
            ]));

            var profileIntervals = BuildProfileIntervals(selectedItem);
            if (profileIntervals.Count > 0)
            {
                blocks.Add(new ReportRenderSoundingProfileBlock(
                    $"Boormonsterprofiel en interpretatie {ReportJson.Text(selectedItem, "modelName", "-")}",
                    $"Identificatie {ReportJson.Text(selectedItem, "code", "-")} | RD {FormatNullable(selectedItem, "x", "")}, {FormatNullable(selectedItem, "y", "")} | maaiveld {FormatNullable(selectedItem, "surfaceNap", "m NAP")} | diepte 0,00 - {FormatNullable(selectedItem, "endDepth", "m")}",
                    profileIntervals,
                    OptionalDouble(selectedItem, "surfaceNap"),
                    OptionalDouble(selectedItem, "endDepth")));
            }

            if (intervals.Count > 0)
            {
                blocks.Add(new ReportRenderHeadingBlock($"Datasetintervallen kaartpunt {selectedIndex}"));
                blocks.Add(new ReportRenderTableBlock(["Van", "Tot", "Code", "Eenheid", "Lithologie"], intervals));
            }

            selectedIndex++;
        }

        blocks.Add(new ReportRenderNoteBlock($"Alleen de handmatig geselecteerde {ReportJson.Text(data, "activeModel", "BRO/DINOloket")}-bronpunten worden in de rapportage opgenomen. De volledige DINOloket-puntenlaag blijft beschikbaar in de app, maar wordt niet als lange database-overzichtstabel afgedrukt."));
        return new ReportRenderDocument(blocks);
    }

    private static string SelectedLabel(IReadOnlyList<JsonElement> selectedSoundings, int maxSelected)
    {
        if (selectedSoundings.Count == 0) return "Geen selectie";
        return string.Join(", ", selectedSoundings.Take(maxSelected).Select(SelectedLabel));
    }

    private static string SelectedLabel(JsonElement selected)
    {
        if (selected.ValueKind != JsonValueKind.Object) return "Geen selectie";
        var code = ReportJson.Text(selected, "code", "");
        var name = ReportJson.Text(selected, "name", "");
        return string.IsNullOrWhiteSpace(name) || string.Equals(name, code, StringComparison.OrdinalIgnoreCase)
            ? code
            : $"{code} - {name}";
    }

    private static string FormatNullable(JsonElement element, string property, string suffix)
    {
        var value = ReportJson.Double(element, property, double.NaN);
        if (!double.IsFinite(value))
        {
            return "-";
        }

        return string.IsNullOrWhiteSpace(suffix) ? $"{value:N2}" : $"{value:N2} {suffix}";
    }

    private static double? OptionalDouble(JsonElement element, string property)
    {
        var value = ReportJson.Double(element, property, double.NaN);
        return double.IsFinite(value) ? value : null;
    }

    private static IReadOnlyList<ReportRenderSoundingInterval> BuildProfileIntervals(JsonElement selected)
    {
        return ReportJson.Array(selected, "profileIntervals")
            .Select(interval => new ReportRenderSoundingInterval(
                ReportJson.Double(interval, "topDepth", double.NaN),
                ReportJson.Double(interval, "bottomDepth", double.NaN),
                ReportJson.Text(interval, "code", "-"),
                ReportJson.Text(interval, "label", "-"),
                ReportJson.Text(interval, "lithology", "-"),
                ReportJson.Text(interval, "color", "#CBD5E1")))
            .Where(interval => double.IsFinite(interval.TopDepth)
                && double.IsFinite(interval.BottomDepth)
                && interval.BottomDepth > interval.TopDepth)
            .Take(24)
            .ToList();
    }

    private static ReportRenderRow Row(string label, string value) => new(label, value);
}

public sealed class ProfileReportRenderer : IReportSubstepRenderer
{
    public string SubstepNumber => "7.1";

    public ReportRenderDocument Render(JsonElement data)
    {
        var points = ReportJson.Array(data, "points").ToList();
        var rows = points.Take(20)
            .Select(point => (IReadOnlyList<string>)
            [
                ReportJson.Int(point, "index").ToString(CultureInfo.InvariantCulture),
                ReportJson.Text(point, "role", "Punt"),
                $"{ReportJson.Double(point, "distance"):N1} m",
                $"{ReportJson.Double(point, "depth"):N2} m",
                $"{ReportJson.Double(point, "nap"):N2} m"
            ])
            .ToList();

        if (rows.Count == 0)
        {
            rows.Add(["-", "Geen profielpunten", "-", "-", "-"]);
        }

        return new ReportRenderDocument([
            new ReportRenderKeyValuesBlock([
                Row("Profielpunten", points.Count.ToString(CultureInfo.InvariantCulture)),
                Row("Status", points.Count >= 2 ? "Dwarsprofiel beschikbaar" : "Profiel ontbreekt")
            ]),
            new ReportRenderHeadingBlock("Dwarsprofielpunten"),
            new ReportRenderTableBlock(["#", "Rol", "Afstand", "Diepte", "NAP"], rows)
        ]);
    }

    private static ReportRenderRow Row(string label, string value) => new(label, value);
}

public sealed class MachineLocationReportRenderer : IReportSubstepRenderer
{
    public string SubstepNumber => "8.1";

    public ReportRenderDocument Render(JsonElement data)
    {
        var machine = ReportJson.Property(data, "selectedMachine") ?? default;
        var placements = ReportJson.Array(data, "placements").ToList();
        var rows = placements.Take(12)
            .Select(placement => (IReadOnlyList<string>)
            [
                ReportJson.Text(placement, "label", "Object"),
                $"{ReportJson.Double(placement, "length"):N1} m",
                $"{ReportJson.Double(placement, "width"):N1} m"
            ])
            .ToList();

        if (rows.Count == 0)
        {
            rows.Add(["Geen werkvakobjecten", "-", "-"]);
        }

        return new ReportRenderDocument([
            new ReportRenderKeyValuesBlock([
                Row("Machine", MachineName(machine, ReportJson.Text(data, "selectedMachineId", "Nog niet gekozen"))),
                Row("Plaatsingen", placements.Count.ToString(CultureInfo.InvariantCulture)),
                Row("Techniek", ReportJson.Text(data, "technique", "-"))
            ]),
            new ReportRenderHeadingBlock("Machine en werkvak"),
            new ReportRenderTableBlock(["Object", "Lengte", "Breedte"], rows)
        ]);
    }

    private static string MachineName(JsonElement machine, string fallback)
    {
        var brand = ReportJson.Text(machine, "brand", "");
        var model = ReportJson.Text(machine, "model", "");
        var name = $"{brand} {model}".Trim();
        return string.IsNullOrWhiteSpace(name) ? fallback : name;
    }

    private static ReportRenderRow Row(string label, string value) => new(label, value);
}

public sealed class SoundingsReportRenderer : IReportSubstepRenderer
{
    public string SubstepNumber => "9.1";

    public ReportRenderDocument Render(JsonElement data) =>
        new([
            new ReportRenderKeyValuesBlock([
                Row("Status", ReportJson.Text(data, "status", "Nog te beoordelen")),
                Row("Onderdeel", "Sonderingen")
            ]),
            new ReportRenderNoteBlock(ReportJson.Text(data, "note", "Leg sonderingen of geotechnische bronpunten vast zodra deze beschikbaar zijn. Tot die tijd blijft dit onderdeel een aandachtspunt voor de definitieve beoordeling."))
        ]);

    private static ReportRenderRow Row(string label, string value) => new(label, value);
}

public sealed class FinalConclusionReportRenderer : IReportSubstepRenderer
{
    public string SubstepNumber => "10.2";

    public ReportRenderDocument Render(JsonElement data)
    {
        var status = ReportJson.Text(data, "status", "Rapportgenerator");
        var snapshotAvailable = ReportJson.Text(data, "snapshotAvailable", "Nee");

        return new ReportRenderDocument([
            new ReportRenderKeyValuesBlock([
                Row("Rapporttype", "Prescan rapportage haalbaarheidsonderzoek"),
                Row("Status", status),
                Row("Rapportsnapshot", snapshotAvailable.Equals("Ja", StringComparison.OrdinalIgnoreCase) ? "Actueel opgebouwd" : "Nog opnieuw genereren")
            ]),
            new ReportRenderHeadingBlock("Eindconclusie"),
            new ReportRenderNoteBlock("Deze concept-eindrapportage bundelt de opgeslagen projectgegevens, ontwerpbestanden, boorlijn, kaartbijlagen, KLIC-kruisingen, oppervlakteanalyse, omgevingsinformatie, ondergrondanalyse, dwarsprofiel, machinepositie en sonderingsinformatie. Controleer onderdelen met een aandachtspunt voordat het rapport definitief aan de klant wordt verstrekt."),
            new ReportRenderHeadingBlock("Aanbevolen vervolgstappen"),
            new ReportRenderTableBlock(["Onderdeel", "Actie"], [
                ["Brondata", "Controleer of alle gebruikte KLIC-, BGT-, BAG/Kadaster-, AHN4- en BRO/DINOloket-bronnen actueel zijn."],
                ["Ontwerp", "Controleer boorlijn, boringdiameter, productbundel, machinekeuze en dwarsprofiel op uitvoerbaarheid."],
                ["Omgeving", "Controleer ZRO/eigendom, vergunningen, bronhouders en lokale randvoorwaarden handmatig."],
                ["Oplevering", "Exporteer pas definitief wanneer de rapportcontrole geen kritische aandachtspunten meer toont."]
            ])
        ]);
    }

    private static ReportRenderRow Row(string label, string value) => new(label, value);
}

public sealed class SummaryReportRenderer(string substepNumber, string heading, string note) : IReportSubstepRenderer
{
    public string SubstepNumber { get; } = substepNumber;

    public ReportRenderDocument Render(JsonElement data)
    {
        var rows = new List<ReportRenderRow>();
        foreach (var property in EnumerateSimpleProperties(data).Take(8))
        {
            rows.Add(new ReportRenderRow(property.Name, property.Value));
        }

        if (rows.Count == 0)
        {
            rows.Add(new ReportRenderRow("Status", "Beschikbaar zodra deze substap is opgeslagen."));
        }

        return new ReportRenderDocument([
            new ReportRenderKeyValuesBlock(rows),
            new ReportRenderHeadingBlock(heading),
            new ReportRenderNoteBlock(note)
        ]);
    }

    private static IEnumerable<(string Name, string Value)> EnumerateSimpleProperties(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object) yield break;
        foreach (var property in data.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array) continue;
            yield return (property.Name, ReportJson.Text(property.Value, "-"));
        }
    }
}
