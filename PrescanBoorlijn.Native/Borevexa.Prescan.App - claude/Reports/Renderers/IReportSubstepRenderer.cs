using System.Text.Json;
using Borevexa.Prescan.App.Reports.Blocks;

namespace Borevexa.Prescan.App.Reports.Renderers;

public interface IReportSubstepRenderer
{
    string SubstepNumber { get; }

    ReportRenderDocument Render(JsonElement data);
}
