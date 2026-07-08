using System.Globalization;
using System.Text.Json;
using Borevexa.Prescan.App.Reports.Blocks;

namespace Borevexa.Prescan.App.Reports.Renderers;

public sealed class BgtAnalysisReportRenderer : IReportSubstepRenderer
{
    public string SubstepNumber => "4.2";

    public ReportRenderDocument Render(JsonElement data)
    {
        var segments = ReportJson.Array(data, "segments").ToList();
        var traceLength = ReportJson.Double(data, "traceLength");
        var measuredLength = ReportJson.Double(data, "measuredLength");
        if (measuredLength <= 0)
        {
            measuredLength = segments.Sum(segment => ReportJson.Double(segment, "length"));
        }

        var dominantSurface = segments
            .GroupBy(segment => ReportJson.Text(segment, "label", "Onbekend"))
            .Select(group => new
            {
                Label = group.Key,
                Length = group.Sum(segment => ReportJson.Double(segment, "length"))
            })
            .OrderByDescending(group => group.Length)
            .FirstOrDefault();

        var rows = segments.Take(18)
            .Select(segment => (IReadOnlyList<string>)
            [
                ReportText.ShortCell(ReportJson.Text(segment, "label", "-"), 34),
                $"{ReportJson.Double(segment, "start"):N1} m",
                $"{ReportJson.Double(segment, "end"):N1} m",
                $"{ReportJson.Double(segment, "length"):N1} m"
            ])
            .ToList();

        if (rows.Count == 0)
        {
            rows.Add(["Geen BGT-oppervlakteprofiel", "-", "-", "-"]);
        }

        return new ReportRenderDocument([
            new ReportRenderKeyValuesBlock([
                Row("Boorlijnlengte", traceLength > 0 ? $"{traceLength:N1} m" : "-"),
                Row("Gemeten oppervlaklengte", measuredLength > 0 ? $"{measuredLength:N1} m" : "-"),
                Row("BGT-segmenten", segments.Count.ToString(CultureInfo.InvariantCulture)),
                Row("Dominant oppervlak", dominantSurface is null ? "-" : $"{dominantSurface.Label} ({dominantSurface.Length:N1} m)"),
                Row("Analyse uitgevoerd", ReportJson.Text(data, "generated", "Nee")),
                Row("Kaart vastgezet", ReportJson.Text(data, "mapLocked", "Nee"))
            ]),
            new ReportRenderHeadingBlock("Oppervlakteanalyse langs boorlijn"),
            new ReportRenderTableBlock(["Oppervlak", "Van", "Tot", "Lengte"], rows)
        ]);
    }

    private static ReportRenderRow Row(string label, string value) => new(label, value);
}
