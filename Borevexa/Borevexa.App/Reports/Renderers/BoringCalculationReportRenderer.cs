using System.Text.Json;
using Borevexa.App.Reports.Blocks;

namespace Borevexa.App.Reports.Renderers;

public sealed class BoringCalculationReportRenderer : IReportSubstepRenderer
{
    public string SubstepNumber => "1.3";

    public ReportRenderDocument Render(JsonElement data)
    {
        var boring = ReportJson.Property(data, "boring") ?? default;
        var boringDiameter = ReportJson.Int(boring, "boringDiameter");
        if (boringDiameter <= 0)
        {
            return new ReportRenderDocument([
                new ReportRenderKeyValuesBlock([
                    Row("Status", "Geen boringconfiguratie vastgelegd")
                ]),
                new ReportRenderNoteBlock("Leg de productbundel, mantelbuizen/kabels en benodigde boring vast voordat dit rapportdeel definitief wordt vrijgegeven.")
            ]);
        }

        var fillFactor = ReportJson.Double(data, "fillFactor", 0.64);
        var boringFactor = ReportJson.Double(data, "boringFactor", 1.5);
        var bundleDiameter = ReportJson.Double(boring, "bundleDiameter");
        var items = ReadCalculationItems(boring);
        var totalArea = items.Sum(item => Math.PI * Math.Pow(item.EffectiveDiameter / 2d, 2));
        var calculatedBundleDiameter = totalArea > 0 && fillFactor > 0
            ? 2d * Math.Sqrt(totalArea / (Math.PI * fillFactor))
            : bundleDiameter;
        var calculatedBoringDiameter = calculatedBundleDiameter * boringFactor;

        return new ReportRenderDocument([
            new ReportRenderHeadingBlock("Resultaat"),
            new ReportRenderKeyValuesBlock([
                Row("Productbundel", $"\u00D8{bundleDiameter:N0} mm"),
                Row("Vereiste boring", $"\u00D8{boringDiameter} mm"),
                Row("Vulfactor", $"{fillFactor:N2}"),
                Row("Normfactor", $"{boringFactor:N2}"),
                Row("Machinekeuze", ReportJson.Text(data, "selectedMachineId", "Nog niet gekozen"))
            ]),
            new ReportRenderHeadingBlock("Berekening"),
            new ReportRenderTableBlock(
                ["Onderdeel", "Berekening", "Uitkomst"],
                [
                    [
                        "Productdoorsnede",
                        ProductSummary(items),
                        totalArea > 0 ? $"A totaal = {totalArea:N0} mm2" : "-"
                    ],
                    [
                        "Productbundel",
                        $"D = 2 x sqrt(A / (pi x {fillFactor:N2}))",
                        $"\u00D8{calculatedBundleDiameter:N1} mm, afgerond {FormatDiameter(bundleDiameter)}"
                    ],
                    [
                        "Vereiste boring",
                        $"{FormatDiameter(bundleDiameter)} x {boringFactor:N2}",
                        $"\u00D8{calculatedBoringDiameter:N1} mm, naar boven afgerond {FormatDiameter(boringDiameter)}"
                    ]
                ]),
            new ReportRenderNoteBlock("De productbundel wordt berekend uit de gezamenlijke productdoorsnede en de ingestelde vulfactor. De vereiste boring wordt daarna bepaald met de normfactor en naar boven afgerond op stappen van 25 mm, met minimaal \u00D875 mm.")
        ]);
    }

    private static ReportRenderRow Row(string label, string value) => new(label, value);

    private static IReadOnlyList<CalculationItem> ReadCalculationItems(JsonElement boring)
    {
        var items = new List<CalculationItem>();
        foreach (var processed in ReportJson.Array(boring, "processed"))
        {
            var item = ReportJson.Property(processed, "item") ?? default;
            var label = ReportJson.Text(item, "label", "Product");
            var effectiveDiameter = ReportJson.Double(processed, "effectiveOutsideDiameter");
            if (effectiveDiameter <= 0)
            {
                effectiveDiameter = ReportJson.Double(item, "outsideDiameter");
            }

            if (effectiveDiameter > 0)
            {
                items.Add(new CalculationItem(label, effectiveDiameter));
            }
        }

        return items;
    }

    private static string ProductSummary(IReadOnlyList<CalculationItem> items)
    {
        if (items.Count == 0) return "-";

        return string.Join("; ", items.Select(item => $"{item.Label} {FormatDiameter(item.EffectiveDiameter)}"));
    }

    private static string FormatDiameter(double diameter) => $"\u00D8{diameter:N0} mm";

    private sealed record CalculationItem(string Label, double EffectiveDiameter);
}
