namespace Borevexa.Prescan.Core.Models;

public sealed class StepWorkspace
{
    public int StepNumber { get; init; }
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string MapTitle { get; init; } = "";
    public string MapSubtitle { get; init; } = "";
    public IReadOnlyList<WorkspaceCard> Cards { get; init; } = Array.Empty<WorkspaceCard>();
    public IReadOnlyList<string> Actions { get; init; } = Array.Empty<string>();
}

public sealed class WorkspaceCard
{
    public string Label { get; init; } = "";
    public string Title { get; init; } = "";
    public string Body { get; init; } = "";
}
