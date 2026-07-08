using System.Globalization;
using System.Text.Json;
using Borevexa.App.Reports.Blocks;

namespace Borevexa.App.Reports.Renderers;

public sealed class BgtSegmentsReportRenderer : IReportSubstepRenderer
{
    public string SubstepNumber => "4.1";

    public ReportRenderDocument Render(JsonElement data)
    {
        var segments = ReportJson.Array(data, "segments").ToList();
        var traceLength = ReportJson.Double(data, "traceLength");
        var measuredLength = segments.Sum(segment => ReportJson.Double(segment, "length"));
        return new ReportRenderDocument([
            new ReportRenderKeyValuesBlock([
                Row("Boorlijnlengte", traceLength > 0 ? $"{traceLength:N1} m" : "-"),
                Row("BGT-segmenten", segments.Count.ToString(CultureInfo.InvariantCulture)),
                Row("Gemeten oppervlaklengte", measuredLength > 0 ? $"{measuredLength:N1} m" : "-")
            ])
        ]);
    }

    private static ReportRenderRow Row(string label, string value) => new(label, value);
}
