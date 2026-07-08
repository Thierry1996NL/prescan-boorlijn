using System.Globalization;
using System.Text.Json;
using Borevexa.Prescan.App.Reports.Blocks;

namespace Borevexa.Prescan.App.Reports.Renderers;

public sealed class KlicEvZonesReportRenderer : IReportSubstepRenderer
{
    public string SubstepNumber => "3.3";

    public ReportRenderDocument Render(JsonElement data)
    {
        var zones = ReportJson.Array(data, "evZones").ToList();
        var rows = zones.Take(18)
            .Select(zone => (IReadOnlyList<string>)
            [
                ReportJson.Text(zone, "code", "-"),
                $"{ReportJson.Double(zone, "distance"):N1} m",
                $"{ReportJson.Double(zone, "proximityMeters"):N1} m",
                ReportText.ShortCell(ReportJson.Text(zone, "themeLabel", "-"), 22),
                ReportText.ShortCell(CleanNetworkOperator(ReportJson.Text(zone, "networkOperator", "-")), 30),
                ReportText.ShortCell(ReportJson.Text(zone, "measure", "-").Replace("\r", "").Replace("\n", "; "), 78)
            ])
            .ToList();

        var blocks = new List<ReportRenderBlock>
        {
            new ReportRenderKeyValuesBlock([
                Row("EV-zones", zones.Count.ToString(CultureInfo.InvariantCulture)),
                Row("Zoekafstand boorlijn", $"{ReportJson.Double(data, "searchBufferMeters", 5):N1} m"),
                Row("KLIC-lagen", ReportJson.Int(data, "klicLayerCount").ToString(CultureInfo.InvariantCulture)),
                Row("KLIC zichtbaar", ReportJson.Text(data, "klicVisible", "Nee"))
            ]),
            new ReportRenderHeadingBlock("Controle eisvoorzorgsmaatregelen")
        };

        if (rows.Count == 0)
        {
            blocks.Add(new ReportRenderNoteBlock(
                "Een EV-zone is een gebied of aanduiding uit de KLIC-levering waarvoor eisvoorzorgsmaatregelen gelden. " +
                "Bij werkzaamheden nabij zo'n zone moet de uitvoerende partij de KLIC-informatie, de voorwaarden van de netbeheerder en eventuele aanvullende werkinstructies controleren voordat de uitvoering start."));
            blocks.Add(new ReportRenderNoteBlock(
                "Voor dit project zijn in de gekoppelde KLIC-data geen EV-zones binnen de ingestelde zoekafstand rond de boorlijn gevonden. " +
                "De controle is daarmee niet van toepassing op basis van de huidige KLIC-levering. Controleer bij definitieve uitvoering altijd of de gebruikte KLIC-melding actueel is."));
        }
        else
        {
            blocks.Add(new ReportRenderTableBlock(["#", "Station", "Nabijheid", "Thema", "Netbeheerder", "Maatregel / info"], rows));
            blocks.Add(new ReportRenderNoteBlock(
                "EV-zones vragen om extra voorbereiding. Stem de aangetroffen eisvoorzorgsmaatregelen af met de netbeheerder en leg de gekozen werkwijze vast voordat de boring wordt uitgevoerd."));
        }

        return new ReportRenderDocument(blocks);
    }

    private static ReportRenderRow Row(string label, string value) => new(label, value);

    private static string CleanNetworkOperator(string value)
    {
        value = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value)) return "-";
        if (value.Equals("nl.imkl", StringComparison.OrdinalIgnoreCase)) return "-";
        if (value.Equals("imkl", StringComparison.OrdinalIgnoreCase)) return "-";
        if (value.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return "-";
        if (value.Contains("namespace", StringComparison.OrdinalIgnoreCase)) return "-";
        return value;
    }
}
