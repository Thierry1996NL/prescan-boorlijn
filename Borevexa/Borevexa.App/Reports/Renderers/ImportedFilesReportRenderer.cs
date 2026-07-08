using System.Globalization;
using System.Text.Json;
using Borevexa.App.Reports.Blocks;

namespace Borevexa.App.Reports.Renderers;

public sealed class ImportedFilesReportRenderer : IReportSubstepRenderer
{
    public string SubstepNumber => "2.1";

    public ReportRenderDocument Render(JsonElement data)
    {
        var files = ReportJson.Array(data, "files").ToList();
        var uniqueFiles = files
            .GroupBy(file => $"{ReportJson.Text(file, "fileType", "")}|{ReportJson.Text(file, "displayName", "")}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(file => ReportText.ImportedFileTypeOrder(ReportJson.Text(file, "fileType", "")))
            .ThenBy(file => ReportJson.Text(file, "displayName", ""))
            .ToList();

        var rows = uniqueFiles.Take(12)
            .Select(file =>
            {
                var fileType = ReportJson.Text(file, "fileType", "-");
                return (IReadOnlyList<string>)
                [
                    ReportText.NormalizeImportedFileType(fileType),
                    ReportText.ShortCell(ReportJson.Text(file, "displayName", "-"), 42),
                    ReportText.FormatBytes(ReportJson.Double(file, "sizeBytes")),
                    ReportText.DescribeImportedFileType(fileType)
                ];
            })
            .ToList();
        if (rows.Count == 0)
        {
            rows.Add(["Geen bestanden", "-", "-", "Importeer eerst ontwerp-, KLIC-, BAG/Kadaster- of BGT-bronnen."]);
        }

        return new ReportRenderDocument([
            new ReportRenderKeyValuesBlock([
                Row("Aantal importbestanden", uniqueFiles.Count.ToString(CultureInfo.InvariantCulture)),
                Row("Ontwerpbronnen", uniqueFiles.Count(file => ReportText.IsDesignImportType(ReportJson.Text(file, "fileType", ""))).ToString(CultureInfo.InvariantCulture)),
                Row("Omgevingsbronnen", uniqueFiles.Count(file => ReportText.IsEnvironmentImportType(ReportJson.Text(file, "fileType", ""))).ToString(CultureInfo.InvariantCulture)),
                Row("KLIC leveringen", uniqueFiles.Count(file => ReportJson.Text(file, "fileType", "").Contains("KLIC", StringComparison.OrdinalIgnoreCase)).ToString(CultureInfo.InvariantCulture))
            ]),
            new ReportRenderHeadingBlock("Bronbestanden"),
            new ReportRenderTableBlock(["Type", "Bestand", "Grootte", "Gebruik in prescan"], rows),
            new ReportRenderNoteBlock("Dit overzicht gebruikt alleen gekoppelde projectbestanden. Lokale opslagpaden worden bewust niet in het rapport getoond.")
        ]);
    }

    private static ReportRenderRow Row(string label, string value) => new(label, value);
}
