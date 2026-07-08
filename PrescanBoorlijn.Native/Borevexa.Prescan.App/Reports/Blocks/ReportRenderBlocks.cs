namespace Borevexa.Prescan.App.Reports.Blocks;

public sealed record ReportRenderDocument(IReadOnlyList<ReportRenderBlock> Blocks)
{
    public static ReportRenderDocument Empty { get; } = new([]);
}

public abstract record ReportRenderBlock;

public sealed record ReportRenderKeyValuesBlock(IReadOnlyList<ReportRenderRow> Rows) : ReportRenderBlock;

public sealed record ReportRenderTableBlock(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows) : ReportRenderBlock;

public sealed record ReportRenderHeadingBlock(string Text) : ReportRenderBlock;

public sealed record ReportRenderCardsBlock(IReadOnlyList<ReportRenderCard> Cards) : ReportRenderBlock;

public sealed record ReportRenderNoteBlock(string Text) : ReportRenderBlock;

public sealed record ReportRenderSoundingProfileBlock(
    string Title,
    string Subtitle,
    IReadOnlyList<ReportRenderSoundingInterval> Intervals,
    double? SurfaceNap,
    double? EndDepth) : ReportRenderBlock;

public sealed record ReportRenderRow(string Label, string Value);

public sealed record ReportRenderCard(string Title, string Color, IReadOnlyList<ReportRenderLine> Lines);

public sealed record ReportRenderLine(string Text, string Color);

public sealed record ReportRenderSoundingInterval(
    double TopDepth,
    double BottomDepth,
    string Code,
    string Label,
    string Lithology,
    string Color);
