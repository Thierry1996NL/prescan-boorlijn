namespace Borevexa.Prescan.Core.Models;

public sealed class PrescanStep
{
    public int Number { get; init; }
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public StepState State { get; init; }
    public IReadOnlyList<PrescanSubstep> Substeps { get; init; } = Array.Empty<PrescanSubstep>();
}

public sealed class PrescanSubstep
{
    public int StepNumber { get; init; }
    public string Number { get; init; } = "";
    public string DisplayNumber { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string ReportSectionTitle { get; init; } = "";
    public string ReportCardTitle { get; init; } = "";
    public bool IsChapterIntroduction { get; init; }
}

public enum StepState
{
    Todo,
    Active,
    Done
}
