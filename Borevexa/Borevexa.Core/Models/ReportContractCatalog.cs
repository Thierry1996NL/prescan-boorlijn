namespace Borevexa.Core.Models;

public static class ReportContractCatalog
{
    private static readonly IReadOnlyList<ReportContract> Contracts =
    [
        Contract(0, "0.1", [ReportDataKeys.ProjectInfo], [ReportDataKeys.ReportSnapshot], "cover"),
        Contract(0, "0.2", [ReportDataKeys.ReportSnapshot], [], "preface"),
        Contract(0, "0.3", [ReportDataKeys.ReportSnapshot], [], "contents"),

        Contract(1, "1.1", [ReportDataKeys.ProjectInfo], [], "project-info"),
        Contract(1, "1.2", [ReportDataKeys.BoringConfig], [], "boring-content"),
        Contract(1, "1.3", [ReportDataKeys.BoringConfig], [], "cross-section", outputKind: "calculation"),
        Contract(1, "1.4", [ReportDataKeys.MachineChoice], [ReportDataKeys.BoringConfig], "machine-choice"),

        Contract(2, "2.1", [ReportDataKeys.ProjectFiles], [ReportDataKeys.MapState], "files"),
        Contract(2, "2.2", [ReportDataKeys.ProjectFiles], [], "documents"),
        Contract(2, "2.3", [ReportDataKeys.ProjectFiles, ReportDataKeys.KlicCrossings], [ReportDataKeys.MapState], "layers-crossings"),

        Contract(3, "3.1", [ReportDataKeys.MapState, ReportDataKeys.LiveMapPreview], [ReportDataKeys.BoreTraceGeoJson, ReportDataKeys.ReportLock], "map-control", requiresMapState: true, requiresReportCapture: true, outputKind: "map"),
        Contract(3, "3.2", [ReportDataKeys.KlicCrossings], [ReportDataKeys.BoreTraceGeoJson, ReportDataKeys.MapState], "klic-crossings"),
        Contract(3, "3.3", [ReportDataKeys.KlicEvZones], [ReportDataKeys.BoreTraceGeoJson, ReportDataKeys.MapState], "klic-ev-zones"),

        Contract(4, "4.1", [ReportDataKeys.SurfaceAnalysis], [ReportDataKeys.MapState], "bgt-segments"),
        Contract(4, "4.3", [ReportDataKeys.SurfaceAnalysis, ReportDataKeys.BoreTraceGeoJson], [ReportDataKeys.MapState, ReportDataKeys.ReportLock], "ahn4-surface-profile", outputKind: "profile"),

        Contract(5, "5.1", [ReportDataKeys.ParcelOwnerAnalysis], [ReportDataKeys.EnvironmentSegments], "parcel-segments"),
        Contract(5, "5.2", [ReportDataKeys.ParcelOwnerAnalysis], [ReportDataKeys.EnvironmentAnalysis], "zro-analysis"),

        Contract(6, "6.1", [ReportDataKeys.ProjectFiles], [ReportDataKeys.MapState, ReportDataKeys.BroAhn], "subsurface-sources"),
        Contract(6, "6.2", [ReportDataKeys.ProjectFiles], [ReportDataKeys.MapState, ReportDataKeys.BroAhn], "subsurface-sources"),
        Contract(6, "6.3", [ReportDataKeys.ProjectFiles], [ReportDataKeys.MapState, ReportDataKeys.BroAhn], "subsurface-sources"),
        Contract(6, "6.4", [ReportDataKeys.ProjectFiles], [ReportDataKeys.MapState, ReportDataKeys.BroAhn], "subsurface-sources"),
        Contract(6, "6.5.1", [ReportDataKeys.ProjectFiles], [ReportDataKeys.MapState, ReportDataKeys.BroAhn], "subsurface-sources"),
        Contract(6, "6.5.2", [ReportDataKeys.ProjectFiles], [ReportDataKeys.MapState, ReportDataKeys.BroAhn], "subsurface-sources"),
        Contract(6, "6.5.3", [ReportDataKeys.ProjectFiles], [ReportDataKeys.MapState, ReportDataKeys.BroAhn], "subsurface-sources"),
        Contract(6, "6.5.4", [ReportDataKeys.ProjectFiles], [ReportDataKeys.MapState, ReportDataKeys.BroAhn], "subsurface-sources"),
        Contract(6, "6.5.5", [ReportDataKeys.ProjectFiles], [ReportDataKeys.MapState, ReportDataKeys.BroAhn], "subsurface-sources"),

        Contract(7, "7.1", [ReportDataKeys.Profile3D], [ReportDataKeys.ProfileVisualSettings, ReportDataKeys.ReportLock], "profile", outputKind: "profile"),

        Contract(8, "8.1", [ReportDataKeys.MachinePlacements], [ReportDataKeys.MachineChoice, ReportDataKeys.ReportLock], "machine-location", outputKind: "map"),

        Contract(9, "9.1", [ReportDataKeys.Soundings], [ReportDataKeys.ProjectFiles], "soundings"),

        Contract(10, "10.1", [ReportDataKeys.ReportSnapshot], [], "completeness"),
        Contract(10, "10.2", [ReportDataKeys.ReportSnapshot], [], "final-conclusion"),
        Contract(10, "10.3", [ReportDataKeys.ReportSnapshot], [ReportDataKeys.ReportExport], "export"),

        Contract(11, "11.1", [ReportDataKeys.Profile3D], [ReportDataKeys.ProfileVisualSettings], "3d-context", outputKind: "model"),
        Contract(11, "11.2", [ReportDataKeys.Profile3D], [], "3d-conflicts", outputKind: "model"),
        Contract(11, "11.3", [ReportDataKeys.ThreeDExport], [ReportDataKeys.Profile3D], "3d-export"),

        Contract(12, "12.1", [ReportDataKeys.WorkDrawingExport], [], "drawing-map"),
        Contract(12, "12.2", [ReportDataKeys.WorkDrawingExport], [ReportDataKeys.Profile3D], "drawing-profile"),
        Contract(12, "12.3", [ReportDataKeys.WorkDrawingExport], [], "drawing-diagnostics")
    ];

    public static IReadOnlyList<ReportContract> GetAll() => Contracts;

    public static IReadOnlyList<ReportContract> GetContracts(int stepNumber) =>
        Contracts.Where(contract => contract.StepNumber == stepNumber).ToArray();

    public static ReportContract? GetContract(int stepNumber, string substepNumber) =>
        Contracts.FirstOrDefault(contract =>
            contract.StepNumber == stepNumber &&
            contract.SubstepNumber.Equals(substepNumber, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<string> GetStepSourceKeys(int stepNumber)
    {
        var keys = GetContracts(stepNumber)
            .SelectMany(contract => contract.RequiredSourceKeys.Concat(contract.OptionalSourceKeys))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return keys.Length == 0 ? [ReportDataKeys.StepSave] : keys;
    }

    private static ReportContract Contract(
        int stepNumber,
        string substepNumber,
        IReadOnlyList<string> requiredSourceKeys,
        IReadOnlyList<string> optionalSourceKeys,
        string templateKey,
        bool requiresMapState = false,
        bool requiresReportCapture = false,
        bool requiresMapLock = false,
        string outputKind = "text")
    {
        var substep = StepReportCatalog.GetSubsteps(stepNumber)
            .FirstOrDefault(item => item.Number.Equals(substepNumber, StringComparison.OrdinalIgnoreCase));
        var title = substep?.Title ?? substepNumber;
        var description = substep?.Description ?? "";
        return new ReportContract(
            stepNumber,
            substepNumber,
            title,
            description,
            requiredSourceKeys,
            optionalSourceKeys,
            requiresMapState,
            requiresReportCapture,
            requiresMapLock,
            templateKey,
            outputKind);
    }
}
