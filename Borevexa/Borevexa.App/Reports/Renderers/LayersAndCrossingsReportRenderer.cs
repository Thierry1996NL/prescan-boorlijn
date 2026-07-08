using System.Globalization;
using System.Text.Json;
using Borevexa.App.Reports.Blocks;

namespace Borevexa.App.Reports.Renderers;

public sealed class LayersAndCrossingsReportRenderer : IReportSubstepRenderer
{
    public string SubstepNumber => "2.3";

    public ReportRenderDocument Render(JsonElement data)
    {
        var layers = ReportJson.Array(data, "layers").ToList();
        var crossings = ReportJson.Array(data, "klicCrossings").ToList();
        var layerRows = layers.Take(12)
            .Select(layer => (IReadOnlyList<string>)
            [
                ReportText.ShortCell(ReportJson.Text(layer, "name", "-"), 42),
                ReportText.NormalizeImportedFileType(ReportJson.Text(layer, "type", "-")),
                ReportJson.Int(layer, "geometryCount").ToString(CultureInfo.InvariantCulture)
            ])
            .ToList();
        if (layerRows.Count == 0)
        {
            layerRows.Add(["Geen lagen gelezen", "-", "-"]);
        }

        var crossingRows = crossings.Take(8)
            .Select(crossing => (IReadOnlyList<string>)
            [
                ReportJson.Text(crossing, "code", "-"),
                $"{ReportJson.Double(crossing, "distance"):N2} m",
                ReportJson.Text(crossing, "themeLabel", "-"),
                ReportText.ShortCell(CleanNetworkOperator(ReportJson.Text(crossing, "networkOperator", "-")), 30)
            ])
            .ToList();
        if (crossingRows.Count == 0)
        {
            crossingRows.Add(["Geen kruisingen", "-", "-", "-"]);
        }

        return new ReportRenderDocument([
            new ReportRenderKeyValuesBlock([
                Row("GIS-lagen", layers.Count.ToString(CultureInfo.InvariantCulture)),
                Row("Geometrieen", layers.Sum(layer => ReportJson.Int(layer, "geometryCount")).ToString(CultureInfo.InvariantCulture)),
                Row("KLIC-kruisingen", crossings.Count.ToString(CultureInfo.InvariantCulture))
            ]),
            new ReportRenderHeadingBlock("Lagen"),
            new ReportRenderTableBlock(["Laag", "Type", "Objecten"], layerRows),
            new ReportRenderHeadingBlock("KLIC kruisingen"),
            new ReportRenderTableBlock(["#", "Afstand", "Thema", "Netbeheerder"], crossingRows)
        ]);
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
