using System.Globalization;
using System.Text.Json;
using Borevexa.App.Reports.Blocks;

namespace Borevexa.App.Reports.Renderers;

public sealed class MapControlReportRenderer : IReportSubstepRenderer
{
    public string SubstepNumber => "3.1";

    public ReportRenderDocument Render(JsonElement data)
    {
        var blocks = new List<ReportRenderBlock>
        {
            new ReportRenderKeyValuesBlock([
                Row("Kaartprojectie", "RD New / EPSG:28992"),
                Row("PDOK kaartlaag", ReportJson.Text(data, "baseLayer", "Overgenomen uit live kaart")),
                Row("Rapportkaart", "Live kaartbeeld uit processtap 3.1"),
                Row("Projectlagen", "Niet opgenomen in deze kaartcontrole"),
                Row("Zoom", ReportJson.Double(data, "zoom", double.NaN) is var zoom && double.IsFinite(zoom) ? $"{zoom:N2}" : "Passend op uitsnede"),
                Row("Schaal", ReportJson.Int(data, "mapScale") > 0 ? $"1:{ReportJson.Int(data, "mapScale")}" : "Automatisch"),
                Row("Overlays", "Exact zoals zichtbaar in de live kaart")
            ])
        };

        var points = ReportJson.Array(data, "points").ToList();
        if (points.Count >= 2)
        {
            var start = points[0];
            var end = points[^1];
            blocks.Add(new ReportRenderHeadingBlock("Boorlijnpunten"));
            blocks.Add(new ReportRenderTableBlock(
                ["Punt", "X RD", "Y RD"],
                [
                    ["Startpunt", FormatRd(start, "X"), FormatRd(start, "Y")],
                    ["Eindpunt", FormatRd(end, "X"), FormatRd(end, "Y")]
                ]));
        }

        return new ReportRenderDocument(blocks);
    }

    private static ReportRenderRow Row(string label, string value) => new(label, value);

    private static string FormatRd(JsonElement point, string propertyName)
    {
        var value = ReportJson.Double(point, propertyName, double.NaN);
        return double.IsFinite(value)
            ? value.ToString("N2", CultureInfo.CurrentCulture)
            : "-";
    }
}
