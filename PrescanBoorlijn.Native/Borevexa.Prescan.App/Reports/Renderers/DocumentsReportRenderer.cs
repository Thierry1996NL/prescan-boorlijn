using System.Globalization;
using System.Text.Json;
using Borevexa.Prescan.App.Reports.Blocks;

namespace Borevexa.Prescan.App.Reports.Renderers;

public sealed class DocumentsReportRenderer : IReportSubstepRenderer
{
    public string SubstepNumber => "2.2";

    public ReportRenderDocument Render(JsonElement data)
    {
        var docs = ReportJson.Array(data, "documents").ToList();
        var rows = docs.Take(12)
            .Select(doc => (IReadOnlyList<string>)
            [
                ReportText.ShortCell(ReportJson.Text(doc, "name", "-"), 52),
                ReportText.NormalizeImportedFileType(ReportJson.Text(doc, "type", "-")),
                $"{ReportJson.Int(doc, "sizeKb"):N0} KB"
            ])
            .ToList();
        if (rows.Count == 0)
        {
            rows.Add(["Geen documenten gekoppeld", "-", "-"]);
        }

        return new ReportRenderDocument([
            new ReportRenderKeyValuesBlock([
                Row("Aantal documenten", docs.Count.ToString(CultureInfo.InvariantCulture)),
                Row("PDF/bijlage", docs.Count(doc => ReportJson.Text(doc, "name", "").EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)).ToString(CultureInfo.InvariantCulture)),
                Row("Bron", docs.Count == 0 ? "Nog geen documenten" : "Projectbestanden")
            ]),
            new ReportRenderTableBlock(["Document", "Type", "Grootte"], rows)
        ]);
    }

    private static ReportRenderRow Row(string label, string value) => new(label, value);
}
