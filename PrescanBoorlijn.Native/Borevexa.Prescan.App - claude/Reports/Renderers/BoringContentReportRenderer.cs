using System.Globalization;
using System.Text.Json;
using Borevexa.Prescan.App.Reports.Blocks;

namespace Borevexa.Prescan.App.Reports.Renderers;

public sealed class BoringContentReportRenderer : IReportSubstepRenderer
{
    public string SubstepNumber => "1.2";

    public ReportRenderDocument Render(JsonElement data)
    {
        var items = ReportJson.Array(data, "items").ToList();
        var blocks = new List<ReportRenderBlock>
        {
            new ReportRenderKeyValuesBlock([new("Aantal producten", items.Count.ToString(CultureInfo.InvariantCulture))])
        };

        if (items.Count == 0)
        {
            blocks.Add(new ReportRenderNoteBlock("Er zijn nog geen producten of mantelbuizen vastgelegd."));
            return new ReportRenderDocument(blocks);
        }

        var cards = new List<ReportRenderCard>();
        foreach (var item in items)
        {
            var type = ReportJson.Int(item, "type") == 0 ? "Mantelbuis" : "Direct product";
            var title = ReportJson.Text(item, "label", type);
            var dn = ReportJson.Int(item, "dn");
            var diameter = ReportJson.Double(item, "outsideDiameter");
            var color = ReportJson.Text(item, "color", "#64748B");
            var lines = new List<ReportRenderLine>
            {
                new($"{type} - \u00D8{diameter:N0} mm - kleur {color}", color)
            };

            foreach (var content in ReportJson.Array(item, "contents"))
            {
                var contentColor = ReportJson.Text(content, "color", "#64748B");
                lines.Add(new ReportRenderLine(
                    $"{ReportJson.Text(content, "label", "Product")} - \u00D8{ReportJson.Double(content, "outsideDiameter"):N0} mm - kleur {contentColor}",
                    contentColor));
            }

            cards.Add(new ReportRenderCard(dn > 0 ? $"{title} DN{dn}" : title, color, lines));
        }

        blocks.Add(new ReportRenderCardsBlock(cards));
        return new ReportRenderDocument(blocks);
    }
}
