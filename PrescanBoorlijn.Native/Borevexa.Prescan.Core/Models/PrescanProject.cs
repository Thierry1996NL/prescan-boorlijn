namespace Borevexa.Prescan.Core.Models;

public sealed class PrescanProject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Client { get; set; } = "";
    public string Location { get; set; } = "";
    public string Status { get; set; } = "Actief";
    public double BoreLengthMeters { get; set; }
    public int DiameterMillimeters { get; set; }
    public string Material { get; set; } = "PE100";
    public string? BoringConfigJson { get; set; }
    public IReadOnlyList<PrescanStep> Steps { get; set; } = Array.Empty<PrescanStep>();
}

