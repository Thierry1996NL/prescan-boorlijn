using System.Globalization;
using System.Text.Json;
using Borevexa.App.Reports.Blocks;

namespace Borevexa.App.Reports.Renderers;

public sealed class AhnSurfaceProfileReportRenderer : IReportSubstepRenderer
{
    public string SubstepNumber => "4.3";

    public ReportRenderDocument Render(JsonElement data)
    {
        var points = ReportJson.Array(data, "points").ToList();
        var rows = points.Take(18)
            .Select(point => (IReadOnlyList<string>)
            [
                $"{ReportJson.Double(point, "distance"):N1} m",
                $"{ReportJson.Double(point, "surface"):N2} m NAP",
                $"{ReportJson.Double(point, "boreNap"):N2} m NAP"
            ])
            .ToList();

        if (rows.Count == 0)
        {
            rows.Add(["-", "Geen maaiveldpunten", "-"]);
        }

        var minSurface = ReportJson.Double(data, "minSurface", double.NaN);
        var maxSurface = ReportJson.Double(data, "maxSurface", double.NaN);

        return new ReportRenderDocument([
            new ReportRenderKeyValuesBlock([
                Row("Boorlijnlengte", ReportJson.Double(data, "traceLength") > 0 ? $"{ReportJson.Double(data, "traceLength"):N1} m" : "-"),
                Row("Profielpunten", ReportJson.Int(data, "profilePointCount").ToString(CultureInfo.InvariantCulture)),
                Row("Maaiveld minimaal", double.IsFinite(minSurface) ? $"{minSurface:N2} m NAP" : "-"),
                Row("Maaiveld maximaal", double.IsFinite(maxSurface) ? $"{maxSurface:N2} m NAP" : "-"),
                Row("Kaart vastgezet", ReportJson.Text(data, "mapLocked", "Nee"))
            ]),
            new ReportRenderHeadingBlock("AHN4 maaiveldpunten langs boorlijn"),
            new ReportRenderTableBlock(["Afstand", "Maaiveld", "Boorlijnhoogte"], rows),
            new ReportRenderNoteBlock("De kaartbijlage en het AHN4-profiel worden als aparte rapportpagina onder deze substap opgenomen.")
        ]);
    }

    private static ReportRenderRow Row(string label, string value) => new(label, value);
}
