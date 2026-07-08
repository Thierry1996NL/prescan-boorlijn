using System.Globalization;
using System.Text.Json;
using Borevexa.App.Reports.Blocks;

namespace Borevexa.App.Reports.Renderers;

public sealed class KlicCrossingsReportRenderer : IReportSubstepRenderer
{
    public string SubstepNumber => "3.2";

    public ReportRenderDocument Render(JsonElement data)
    {
        var crossings = ReportJson.Array(data, "crossings").ToList();
        var rows = crossings.Take(18)
            .Select(crossing =>
            {
                var cableText = ReportJson.Text(crossing, "crossingContent", string.Empty);
                if (string.IsNullOrWhiteSpace(cableText))
                {
                    cableText = ReportJson.Text(crossing, "networkContent", "-");
                }

                cableText = cableText.Replace("\r", string.Empty).Replace("\n", "; ");
                var networkOperator = CleanNetworkOperator(ReportJson.Text(crossing, "networkOperator", "-"));
                return (IReadOnlyList<string>)
                [
                    ReportJson.Text(crossing, "code", "-"),
                    $"{ReportJson.Double(crossing, "distance"):N1} m",
                    ReportText.ShortCell(ReportJson.Text(crossing, "themeLabel", "-"), 22),
                    $"{ReportText.ShortCell(cableText, 72)}\n{ReportText.ShortCell(networkOperator, 34)}",
                    $"X {ReportJson.Double(crossing, "x"):N1}\nY {ReportJson.Double(crossing, "y"):N1}"
                ];
            })
            .ToList();

        if (rows.Count == 0)
        {
            rows.Add(["-", "Geen kruisingen gevonden", "-", "-", "-"]);
        }

        return new ReportRenderDocument([
            new ReportRenderKeyValuesBlock([
                Row("KLIC-kruisingen", crossings.Count.ToString(CultureInfo.InvariantCulture)),
                Row("Thema's", CountDistinct(crossings, "themeLabel").ToString(CultureInfo.InvariantCulture)),
                Row("Netbeheerders", CountNetworkOperators(crossings).ToString(CultureInfo.InvariantCulture)),
                Row("KLIC-lagen", ReportJson.Int(data, "klicLayerCount").ToString(CultureInfo.InvariantCulture)),
                Row("Bufferzone", ReportJson.Text(data, "bufferEnabled", "Nee")),
                Row("KLIC zichtbaar", ReportJson.Text(data, "klicVisible", "Nee"))
            ]),
            new ReportRenderHeadingBlock("Kruisingen langs boorlijn"),
            new ReportRenderTableBlock(["#", "Afstand", "Thema", "Leiding / netbeheerder", "RD"], rows)
        ]);
    }

    private static int CountDistinct(IEnumerable<JsonElement> rows, string propertyName) =>
        rows.Select(row => ReportJson.Text(row, propertyName, string.Empty))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

    private static int CountNetworkOperators(IEnumerable<JsonElement> rows) =>
        rows.Select(row => CleanNetworkOperator(ReportJson.Text(row, "networkOperator", string.Empty)))
            .Where(value => !string.IsNullOrWhiteSpace(value) && value != "-")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

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
