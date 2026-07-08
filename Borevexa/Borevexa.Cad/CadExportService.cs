using Borevexa.Core.Models;

namespace Borevexa.Cad;

public sealed class CadExportService
{
    public CadExportPreview CreatePreview(PrescanProject project)
    {
        var layers = new[]
        {
            "BVX_BOORLIJN",
            "BVX_KLIC",
            "BVX_BGT",
            "BVX_PROFIEL",
            "BVX_LABELS"
        };

        return new CadExportPreview(
            $"DXF/DWG export klaarzetten voor {project.Name}",
            layers,
            "AutoCAD .NET plugin kan later deze lagen direct plaatsen en annoteren."
        );
    }
}

public sealed record CadExportPreview(string Title, IReadOnlyList<string> Layers, string Note);
