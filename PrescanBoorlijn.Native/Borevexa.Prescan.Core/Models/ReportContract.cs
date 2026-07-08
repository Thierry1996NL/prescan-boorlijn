namespace Borevexa.Prescan.Core.Models;

public sealed record ReportContract(
    int StepNumber,
    string SubstepNumber,
    string Title,
    string Description,
    IReadOnlyList<string> RequiredSourceKeys,
    IReadOnlyList<string> OptionalSourceKeys,
    bool RequiresMapState = false,
    bool RequiresReportCapture = false,
    bool RequiresMapLock = false,
    string TemplateKey = "standard",
    string OutputKind = "text");

public sealed record ReportQualityIssue(
    string Code,
    int StepNumber,
    string? SubstepNumber,
    string Severity,
    string Title,
    string Action);

public enum ReportReadinessStatus
{
    NotStarted,
    Incomplete,
    NeedsReview,
    Ready
}

public sealed record ReportQualitySummary(
    int TotalIssues,
    int HighIssues,
    int MediumIssues,
    int LowIssues,
    bool IsReady,
    ReportReadinessStatus Status,
    string StatusLabel,
    string StatusColor,
    IReadOnlyList<ReportQualityIssue> Issues)
{
    public static ReportQualitySummary FromIssues(IReadOnlyList<ReportQualityIssue> issues)
    {
        var high = issues.Count(issue => issue.Severity.Equals("Hoog", StringComparison.OrdinalIgnoreCase));
        var medium = issues.Count(issue => issue.Severity.Equals("Middel", StringComparison.OrdinalIgnoreCase));
        var low = issues.Count(issue => issue.Severity.Equals("Laag", StringComparison.OrdinalIgnoreCase));
        var status = DetermineStatus(issues, high, medium, low);
        return new ReportQualitySummary(
            issues.Count,
            high,
            medium,
            low,
            status == ReportReadinessStatus.Ready,
            status,
            GetStatusLabel(status),
            GetStatusColor(status),
            issues);
    }

    private static ReportReadinessStatus DetermineStatus(IReadOnlyList<ReportQualityIssue> issues, int high, int medium, int low)
    {
        if (issues.Count == 0)
        {
            return ReportReadinessStatus.Ready;
        }

        if (issues.All(issue => issue.Code.StartsWith("substep-missing", StringComparison.OrdinalIgnoreCase)))
        {
            return ReportReadinessStatus.NotStarted;
        }

        if (high > 0)
        {
            return ReportReadinessStatus.Incomplete;
        }

        if (medium > 0 || low > 0)
        {
            return ReportReadinessStatus.NeedsReview;
        }

        return ReportReadinessStatus.Ready;
    }

    private static string GetStatusLabel(ReportReadinessStatus status) => status switch
    {
        ReportReadinessStatus.NotStarted => "Niet gestart",
        ReportReadinessStatus.Incomplete => "Onvolledig",
        ReportReadinessStatus.NeedsReview => "Controle nodig",
        _ => "Rapportklaar"
    };

    private static string GetStatusColor(ReportReadinessStatus status) => status switch
    {
        ReportReadinessStatus.NotStarted => "#64748B",
        ReportReadinessStatus.Incomplete => "#A33A2A",
        ReportReadinessStatus.NeedsReview => "#8A5A00",
        _ => "#1D6B45"
    };
}
