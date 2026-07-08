using Borevexa.Prescan.Core.Models;
using System.Text.Json;

namespace Borevexa.Prescan.Core.Services;

public sealed class ReportQualityService(ProjectRepository projects)
{
    private const string StepReportDataKey = "step_report_data";

    public ReportQualitySummary EvaluateProject(Guid projectId, IReadOnlyList<ReportContract> contracts)
    {
        var issues = new List<ReportQualityIssue>();
        var substepCache = new Dictionary<(int StepNumber, string SubstepNumber), JsonElement>();
        var stepReportDataCache = new Dictionary<int, string?>();
        foreach (var contract in contracts)
        {
            if (TryGetSubstep(projectId, contract, substepCache, stepReportDataCache, out var substep))
            {
                var status = Text(substep, "status", "");
                var ready = Bool(substep, "ready");
                if (!ready || IsIncompleteStatus(status))
                {
                    issues.Add(new ReportQualityIssue(
                        $"substep-not-ready-{contract.StepNumber}-{contract.SubstepNumber}",
                        contract.StepNumber,
                        contract.SubstepNumber,
                        contract.RequiresReportCapture ? "Hoog" : "Middel",
                        $"{contract.SubstepNumber} {contract.Title}: substap niet rapportklaar",
                        string.IsNullOrWhiteSpace(status)
                            ? "Open de substap, vul de gegevens aan en sla opnieuw op."
                            : $"Status: {status}. Vul de substap aan en sla opnieuw op."));
                }
            }
            else
            {
                issues.Add(new ReportQualityIssue(
                    $"substep-missing-{contract.StepNumber}-{contract.SubstepNumber}",
                    contract.StepNumber,
                    contract.SubstepNumber,
                    "Middel",
                    $"{contract.SubstepNumber} {contract.Title}: substapdata ontbreekt",
                    "Open de substap zodat de automatische rapportdata wordt opgebouwd en sla het project op."));
            }

            foreach (var sourceKey in contract.RequiredSourceKeys.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (HasSourceData(projectId, contract, sourceKey, substepCache, stepReportDataCache))
                {
                    continue;
                }

                issues.Add(new ReportQualityIssue(
                    $"missing-{contract.StepNumber}-{contract.SubstepNumber}-{sourceKey}",
                    contract.StepNumber,
                    contract.SubstepNumber,
                    SeverityFor(contract, sourceKey),
                    $"{contract.SubstepNumber} {contract.Title}: brondata ontbreekt",
                    $"Vul de substap en sla bron '{sourceKey}' op."));
            }

            if (contract.RequiresMapState && !HasSourceData(projectId, contract, ReportDataKeys.MapState, substepCache, stepReportDataCache))
            {
                issues.Add(new ReportQualityIssue(
                    $"map-state-missing-{contract.StepNumber}-{contract.SubstepNumber}",
                    contract.StepNumber,
                    contract.SubstepNumber,
                    "Middel",
                    $"{contract.SubstepNumber} {contract.Title}: kaartstatus ontbreekt",
                    "Open de kaartstap en sla de kaartpositie, schaal en zichtbare lagen op."));
            }

            if (contract.RequiresMapLock && !HasSourceData(projectId, contract, ReportDataKeys.ReportLock, substepCache, stepReportDataCache))
            {
                issues.Add(new ReportQualityIssue(
                    $"map-lock-missing-{contract.StepNumber}-{contract.SubstepNumber}",
                    contract.StepNumber,
                    contract.SubstepNumber,
                    "Middel",
                    $"{contract.SubstepNumber} {contract.Title}: rapportkaart niet vastgezet",
                    "Zet de kaart vast voor rapportage of laat de live rapportcapture opnieuw maken."));
            }
        }

        return ReportQualitySummary.FromIssues(issues
            .GroupBy(issue => issue.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList());
    }

    public ReportQualitySummary EvaluateStep(Guid projectId, int stepNumber) =>
        EvaluateProject(projectId, ReportContractCatalog.GetContracts(stepNumber));

    public ReportQualitySummary EvaluateSubstep(Guid projectId, int stepNumber, string substepNumber)
    {
        var contract = ReportContractCatalog.GetContract(stepNumber, substepNumber);
        return contract is null
            ? ReportQualitySummary.FromIssues([])
            : EvaluateProject(projectId, [contract]);
    }

    private bool HasSourceData(
        Guid projectId,
        ReportContract contract,
        string sourceKey,
        Dictionary<(int StepNumber, string SubstepNumber), JsonElement> substepCache,
        Dictionary<int, string?> stepReportDataCache)
    {
        if (sourceKey.Equals(ReportDataKeys.ProjectFiles, StringComparison.OrdinalIgnoreCase))
        {
            return projects.GetProjectFiles(projectId).Count > 0;
        }

        if (HasDerivedSourceData(projectId, contract, sourceKey, substepCache, stepReportDataCache))
        {
            return true;
        }

        var sourceStepNumber = ResolveSourceStepNumber(contract, sourceKey);
        var json = projects.GetStepData(projectId, sourceStepNumber, sourceKey);
        return !string.IsNullOrWhiteSpace(json);
    }

    private bool HasDerivedSourceData(
        Guid projectId,
        ReportContract contract,
        string sourceKey,
        Dictionary<(int StepNumber, string SubstepNumber), JsonElement> substepCache,
        Dictionary<int, string?> stepReportDataCache)
    {
        if (!TryGetSubstep(projectId, contract, substepCache, stepReportDataCache, out var substep))
        {
            return false;
        }

        var data = Property(substep, "data") ?? default;
        return sourceKey switch
        {
            ReportDataKeys.ProjectInfo => data.ValueKind == JsonValueKind.Object,
            ReportDataKeys.BoringConfig => data.ValueKind == JsonValueKind.Object,
            ReportDataKeys.MachineChoice => HasText(data, "selectedMachineId") || HasText(data, "selectedMachine"),
            ReportDataKeys.MapState => Bool(data, "mapStateAvailable") || Bool(data, "mapLocked") || HasSourceKey(substep, ReportDataKeys.MapState),
            ReportDataKeys.LiveMapPreview => !string.IsNullOrWhiteSpace(projects.GetStepData(projectId, contract.StepNumber, ReportDataKeys.LiveMapPreview)),
            ReportDataKeys.ReportLock => Bool(data, "mapLocked") || !string.IsNullOrWhiteSpace(projects.GetStepData(projectId, contract.StepNumber, ReportDataKeys.ReportLock)),
            ReportDataKeys.KlicCrossings => HasArray(data, "crossings") || HasArray(data, "klicCrossings"),
            ReportDataKeys.KlicEvZones => Bool(data, "analysisAvailable") || HasArray(data, "evZones") || HasArray(data, "zones"),
            ReportDataKeys.BoreTraceGeoJson => HasArray(data, "points") || HasText(projects.GetStepData(projectId, contract.StepNumber, ReportDataKeys.BoreTraceGeoJson)),
            ReportDataKeys.SurfaceAnalysis => HasArray(data, "segments") || Bool(data, "generated"),
            ReportDataKeys.ParcelOwnerAnalysis => HasArray(data, "segments") || Int(data, "segmentCount") > 0,
            ReportDataKeys.EnvironmentSegments => HasArray(data, "segments"),
            ReportDataKeys.EnvironmentAnalysis => HasArray(data, "segments") || Int(data, "crossedParcelCount") > 0,
            ReportDataKeys.Profile3D => HasArray(data, "points") || Int(data, "profilePointCount") > 0,
            ReportDataKeys.MachinePlacements => HasArray(data, "placements"),
            ReportDataKeys.Soundings => !IsIncompleteStatus(Text(substep, "status", "")),
            ReportDataKeys.ReportSnapshot => !string.IsNullOrWhiteSpace(projects.GetStepData(projectId, WorkflowCatalog.ReportStepNumber, ReportDataKeys.ReportSnapshot)),
            _ => false
        };
    }

    private bool TryGetSubstep(
        Guid projectId,
        ReportContract contract,
        Dictionary<(int StepNumber, string SubstepNumber), JsonElement> substepCache,
        Dictionary<int, string?> stepReportDataCache,
        out JsonElement substep)
    {
        substep = default;
        var cacheKey = (contract.StepNumber, contract.SubstepNumber);
        if (substepCache.TryGetValue(cacheKey, out substep))
        {
            return true;
        }

        var json = GetStepReportData(projectId, contract.StepNumber, stepReportDataCache);
        if (string.IsNullOrWhiteSpace(json)) return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("substeps", out var substeps) || substeps.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var item in substeps.EnumerateArray())
            {
                if (Text(item, "number", "").Equals(contract.SubstepNumber, StringComparison.OrdinalIgnoreCase))
                {
                    substep = item.Clone();
                    substepCache[cacheKey] = substep;
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private string? GetStepReportData(Guid projectId, int stepNumber, Dictionary<int, string?> stepReportDataCache)
    {
        if (stepReportDataCache.TryGetValue(stepNumber, out var cached))
        {
            return cached;
        }

        var json = projects.GetStepData(projectId, stepNumber, StepReportDataKey);
        stepReportDataCache[stepNumber] = json;
        return json;
    }

    private static int ResolveSourceStepNumber(ReportContract contract, string sourceKey)
    {
        return sourceKey switch
        {
            ReportDataKeys.ProjectInfo => 1,
            ReportDataKeys.BoringConfig => 1,
            ReportDataKeys.MachineChoice => 1,
            ReportDataKeys.ReportSnapshot => WorkflowCatalog.ReportStepNumber,
            ReportDataKeys.ReportExport => WorkflowCatalog.ReportStepNumber,
            ReportDataKeys.ThreeDExport => WorkflowCatalog.ThreeDStepNumber,
            ReportDataKeys.WorkDrawingExport => WorkflowCatalog.WorkDrawingStepNumber,
            _ => contract.StepNumber
        };
    }

    private static string SeverityFor(ReportContract contract, string sourceKey)
    {
        if (contract.RequiresReportCapture || sourceKey.Equals(ReportDataKeys.LiveMapPreview, StringComparison.OrdinalIgnoreCase))
        {
            return "Hoog";
        }

        return contract.RequiresMapState || contract.RequiresMapLock ? "Middel" : "Middel";
    }

    private static bool IsIncompleteStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status)) return false;
        return status.Contains("ontbreekt", StringComparison.OrdinalIgnoreCase) ||
               status.Contains("nog", StringComparison.OrdinalIgnoreCase) ||
               status.Contains("geen", StringComparison.OrdinalIgnoreCase) ||
               status.Contains("niet", StringComparison.OrdinalIgnoreCase);
    }

    private static JsonElement? Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var property)
            ? property
            : null;

    private static string Text(JsonElement element, string name, string fallback)
    {
        var property = Property(element, name);
        if (property is null) return fallback;
        return property.Value.ValueKind switch
        {
            JsonValueKind.String => property.Value.GetString() ?? fallback,
            JsonValueKind.Number => property.Value.GetRawText(),
            JsonValueKind.True => "Ja",
            JsonValueKind.False => "Nee",
            _ => fallback
        };
    }

    private static bool Bool(JsonElement element, string name)
    {
        var property = Property(element, name);
        return property is { ValueKind: JsonValueKind.True } ||
               property is { ValueKind: JsonValueKind.String } &&
               bool.TryParse(property.Value.GetString(), out var parsed) &&
               parsed;
    }

    private static int Int(JsonElement element, string name)
    {
        var property = Property(element, name);
        if (property is null) return 0;
        if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var value)) return value;
        if (property.Value.ValueKind == JsonValueKind.String && int.TryParse(property.Value.GetString(), out value)) return value;
        return 0;
    }

    private static bool HasArray(JsonElement element, string name)
    {
        var property = Property(element, name);
        return property is { ValueKind: JsonValueKind.Array } && property.Value.GetArrayLength() > 0;
    }

    private static bool HasText(JsonElement element, string name)
    {
        var property = Property(element, name);
        return property is { ValueKind: JsonValueKind.String } && !string.IsNullOrWhiteSpace(property.Value.GetString());
    }

    private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

    private static bool HasSourceKey(JsonElement substep, string sourceKey)
    {
        var sourceKeys = Property(substep, "sourceKeys");
        return sourceKeys is { ValueKind: JsonValueKind.Array } &&
               sourceKeys.Value.EnumerateArray()
                   .Any(item => item.ValueKind == JsonValueKind.String &&
                                sourceKey.Equals(item.GetString(), StringComparison.OrdinalIgnoreCase));
    }
}
