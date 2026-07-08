using System.Text.Json;
using Borevexa.App.Reports.Blocks;

namespace Borevexa.App.Reports.Renderers;

public interface IReportSubstepRenderer
{
    string SubstepNumber { get; }

    ReportRenderDocument Render(JsonElement data);
}
