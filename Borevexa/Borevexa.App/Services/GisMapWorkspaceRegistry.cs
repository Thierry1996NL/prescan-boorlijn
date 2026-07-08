using Borevexa.Core.Models;
using Borevexa.Core.Services;

namespace Borevexa.App.Services;

public enum GisMapWorkspacePurpose
{
    Generic,
    Boreline,
    SurfaceAnalysis,
    Environment,
    Subsurface,
    Profile,
    Machine,
    Soundings,
    ThreeD
}

public sealed record GisMapWorkspaceDefinition(
    int StepNumber,
    GisMapWorkspacePurpose Purpose,
    bool RequiresScopedState,
    bool SupportsReportLock,
    bool SupportsLiveCapture,
    bool SendsTraceBeforeCapture,
    bool SendsSurfaceAnalysisBeforeCapture,
    bool HasMultiVariantReportCapture);

public sealed record GisMapWorkspaceRuntime(
    GisMapWorkspaceDefinition Definition,
    string? ContextKey,
    string? NormalizedContextKey,
    string? ActiveReportVariantKey,
    string? ScopedReportVariantKey,
    bool HasScopedContext,
    bool SuppressLegacyFallback);

public sealed class GisMapWorkspaceRegistry
{
    private readonly int _threeDStepNumber;
    private readonly int _reportStepNumber;
    private readonly int _profileStepNumber;
    private readonly Dictionary<int, GisMapWorkspaceDefinition> _definitions;

    public GisMapWorkspaceRegistry(int threeDStepNumber, int reportStepNumber, int profileStepNumber)
    {
        _threeDStepNumber = threeDStepNumber;
        _reportStepNumber = reportStepNumber;
        _profileStepNumber = profileStepNumber;

        _definitions = new Dictionary<int, GisMapWorkspaceDefinition>
        {
            // Stap 3 levert meerdere rapportbeelden op (OSM-cover, BAG, luchtfoto, KLIC-
            // varianten); alle andere stappen vergrendelen één enkel kaartbeeld.
            [3] = Create(3, GisMapWorkspacePurpose.Boreline, sendsTrace: true, multiVariantReportCapture: true),
            [4] = Create(4, GisMapWorkspacePurpose.SurfaceAnalysis, requiresScopedState: false, sendsTrace: true, sendsSurfaceAnalysis: true),
            [5] = Create(5, GisMapWorkspacePurpose.Environment),
            [6] = Create(6, GisMapWorkspacePurpose.Subsurface, sendsTrace: true),
            [_profileStepNumber] = Create(_profileStepNumber, GisMapWorkspacePurpose.Profile, requiresScopedState: false, sendsTrace: true),
            [8] = Create(8, GisMapWorkspacePurpose.Machine),
            [9] = Create(9, GisMapWorkspacePurpose.Soundings),
            [_threeDStepNumber] = Create(_threeDStepNumber, GisMapWorkspacePurpose.ThreeD)
        };
    }

    public bool IsMapWorkspaceStep(int stepNumber) =>
        stepNumber >= 3 && stepNumber <= _threeDStepNumber && stepNumber != _reportStepNumber;

    public GisMapWorkspaceRuntime CreateRuntime(int stepNumber, PrescanSubstep? selectedSubstep, string? activeReportVariantKey = null)
    {
        var definition = GetDefinition(stepNumber);
        var contextKey = BuildContextKey(stepNumber, selectedSubstep, definition);
        var normalizedContext = MapStateService.NormalizeContextKey(contextKey);
        var normalizedVariant = ReportPreviewService.NormalizeLiveMapVariantKey(activeReportVariantKey);
        var scopedVariant = BuildScopedReportVariantKey(normalizedContext, normalizedVariant);
        var hasScopedContext = !string.IsNullOrWhiteSpace(normalizedContext);

        return new GisMapWorkspaceRuntime(
            definition,
            contextKey,
            normalizedContext,
            normalizedVariant,
            scopedVariant,
            hasScopedContext,
            definition.RequiresScopedState && hasScopedContext);
    }

    private GisMapWorkspaceDefinition GetDefinition(int stepNumber)
    {
        if (_definitions.TryGetValue(stepNumber, out var definition)) return definition;

        return IsMapWorkspaceStep(stepNumber)
            ? Create(stepNumber, GisMapWorkspacePurpose.Generic)
            : Create(stepNumber, GisMapWorkspacePurpose.Generic, requiresScopedState: false, supportsReportLock: false, supportsLiveCapture: false);
    }

    private static GisMapWorkspaceDefinition Create(
        int stepNumber,
        GisMapWorkspacePurpose purpose,
        bool requiresScopedState = true,
        bool supportsReportLock = true,
        bool supportsLiveCapture = true,
        bool sendsTrace = false,
        bool sendsSurfaceAnalysis = false,
        bool multiVariantReportCapture = false) =>
        new(
            stepNumber,
            purpose,
            requiresScopedState,
            supportsReportLock,
            supportsLiveCapture,
            sendsTrace,
            sendsSurfaceAnalysis,
            multiVariantReportCapture);

    private static string? BuildContextKey(int stepNumber, PrescanSubstep? selectedSubstep, GisMapWorkspaceDefinition definition)
    {
        if (!definition.RequiresScopedState || selectedSubstep is null) return null;

        var substepNumber = string.IsNullOrWhiteSpace(selectedSubstep.Number)
            ? selectedSubstep.DisplayNumber
            : selectedSubstep.Number;

        return string.IsNullOrWhiteSpace(substepNumber)
            ? null
            : $"step-{stepNumber}-substep-{substepNumber}";
    }

    private static string? BuildScopedReportVariantKey(string? normalizedContext, string? normalizedVariant)
    {
        if (string.IsNullOrWhiteSpace(normalizedContext)) return normalizedVariant;
        return string.IsNullOrWhiteSpace(normalizedVariant)
            ? normalizedContext
            : $"{normalizedContext}-{normalizedVariant}";
    }
}
