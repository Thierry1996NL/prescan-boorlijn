using System.Text.Json;
using Borevexa.App.Reports.Blocks;

namespace Borevexa.App.Reports.Renderers;

public sealed class ProjectInformationReportRenderer : IReportSubstepRenderer
{
    public string SubstepNumber => "1.1";

    public ReportRenderDocument Render(JsonElement data) =>
        new([
            new ReportRenderKeyValuesBlock([
                Row("Projectnaam", ReportJson.Text(data, "name", "-")),
                Row("Datum", ReportJson.Text(data, "reportDate", "-")),
                Row("Projectnummer intern", ReportJson.Text(data, "internalProjectNumber", "-")),
                Row("Projectnummer extern", ReportJson.Text(data, "externalProjectNumber", "-")),
                Row("Opdrachtgever", ReportJson.Text(data, "client", "-")),
                Row("Locatie", ReportJson.Text(data, "location", "-")),
                Row("Status", ReportJson.Text(data, "status", "-")),
                Row("Boorlengte", $"{ReportJson.Double(data, "boreLengthMeters"):N1} m"),
                Row("Diameter", $"\u00D8{ReportJson.Int(data, "diameterMillimeters")} mm"),
                Row("Materiaal", ReportJson.Text(data, "material", "-"))
            ])
        ]);

    private static ReportRenderRow Row(string label, string value) => new(label, value);
}
