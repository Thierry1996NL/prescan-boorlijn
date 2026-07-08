using System.IO;
using System.Text;
using System.Windows;
using Borevexa.Prescan.App.Models;
using Microsoft.Web.WebView2.Core;

namespace Borevexa.Prescan.App;

public partial class MainWindow
{
    private string? _lastWorkDrawingPreviewHtml;
    private string? _lastWorkDrawingPreviewSummary;
    private string? _lastWorkDrawingPreviewSignature;

    private async void WorkDrawingGenerate_OnClick(object sender, RoutedEventArgs e)
    {
        await RunUiBackgroundOperationAsync(
            "Werktekening-preview opbouwen...",
            async () =>
            {
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                return GenerateWorkDrawingPreview();
            });
    }

    private async void WorkDrawingExport_OnClick(object sender, RoutedEventArgs e)
    {
        await RunUiBackgroundOperationAsync(
            "Werktekening exporteren...",
            () => ExportWorkDrawingHtmlAsync(openAfterExport: true));
    }

    private string GenerateWorkDrawingPreview()
    {
        if (_selectedProject is null) return "Geen project actief.";
        EnsureProfilePoints();
        if (_profilePoints.Count < 2)
        {
            return "Geen boorlijn/dwarsprofiel beschikbaar. Genereer eerst het dwarsprofiel in stap 7.";
        }

        RenderWorkDrawingPreview();
        return $"Werktekening-preview gegenereerd\n\n{BuildWorkDrawingDebugSummary()}";
    }

    private async void RenderWorkDrawingPreview()
    {
        if (_selectedProject is null || StepElevenWorkDrawingGrid.Visibility != Visibility.Visible) return;

        try
        {
            EnsureProfilePoints();
            if (_profilePoints.Count < 2)
            {
                WorkDrawingPreviewStatusText.Text = "Geen boorlijn/profiel beschikbaar. Genereer eerst het dwarsprofiel in stap 7.";
                return;
            }

            var signature = BuildWorkDrawingPreviewSignature();
            if (string.Equals(_lastWorkDrawingPreviewSignature, signature, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(_lastWorkDrawingPreviewSummary))
            {
                WorkDrawingPreviewStatusText.Text = _lastWorkDrawingPreviewSummary.Replace("\n", " - ");
                return;
            }

            WorkDrawingPreviewStatusText.Text = "Werktekening-preview opbouwen...";
            if (WorkDrawingPreviewView.CoreWebView2 is null)
            {
                var userDataFolder = Path.Combine(GetWebView2UserDataFolder(), "WorkDrawingPreview");
                Directory.CreateDirectory(userDataFolder);
                var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await WorkDrawingPreviewView.EnsureCoreWebView2Async(environment);
            }

            var webView = WorkDrawingPreviewView.CoreWebView2;
            if (webView is null)
            {
                WorkDrawingPreviewStatusText.Text = "Preview kon niet worden opgebouwd: WebView2 is niet beschikbaar.";
                return;
            }

            webView.Settings.AreDevToolsEnabled = false;
            webView.Settings.IsStatusBarEnabled = false;
            var html = BuildWorkDrawingHtml(includeToolbar: false);
            if (!string.Equals(_lastWorkDrawingPreviewHtml, html, StringComparison.Ordinal))
            {
                webView.NavigateToString(html);
                _lastWorkDrawingPreviewHtml = html;
                _lastWorkDrawingPreviewSummary = null;
            }
            _lastWorkDrawingPreviewSummary ??= BuildWorkDrawingDebugSummary();
            _lastWorkDrawingPreviewSignature = signature;
            WorkDrawingPreviewStatusText.Text = _lastWorkDrawingPreviewSummary.Replace("\n", " - ");
        }
        catch (Exception exception)
        {
            WorkDrawingPreviewStatusText.Text = $"Preview kon niet worden opgebouwd: {exception.Message}";
            OutputText.Text = $"Werktekening-preview fout\n\n{exception}";
        }
    }

    private string BuildWorkDrawingPreviewSignature()
    {
        return string.Join(";",
            _selectedProject?.Id.ToString("N") ?? "geen-project",
            _workDrawingScale,
            _profilePoints.Count,
            _profilePoints.Count == 0 ? "" : _profilePoints[^1].Distance.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            _projectFiles.Count,
            _currentMachinePlacementsJson?.GetHashCode(StringComparison.Ordinal).ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "");
    }

    private string BuildWorkDrawingDebugSummary()
    {
        if (_profilePoints.Count < 2) return "Geen boorlijn/profiel beschikbaar.";

        var drawing = CreateWorkDrawingGeometry();
        _projectFiles = _selectedProject is null ? _projectFiles : _projects.GetProjectFiles(_selectedProject.Id);
        var layers = BuildProjectMapLayers(_projectFiles);
        var sourceLines = layers
            .Where(layer => IsBgtLayer(layer) || IsBagOrKadasterLayer(layer) || IsKlicLayer(layer))
            .SelectMany(EnumerateWorkDrawingLines)
            .Where(item => item.Points.Count > 0)
            .ToList();
        var visibleLines = sourceLines.Where(item => IntersectsWorkDrawingView(drawing, item.Points)).ToList();
        var bagSourceCount = sourceLines.Count(item => IsBagOrKadasterLayer(item.Layer));
        var bagVisibleCount = visibleLines.Count(item => IsBagOrKadasterLayer(item.Layer));
        var bgtSourceCount = sourceLines.Count(item => IsBgtLayer(item.Layer));
        var bgtVisibleCount = visibleLines.Count(item => IsBgtLayer(item.Layer));
        var klicCrossings = GetVisibleKlicProfileCrossings(drawing.TotalDistance);
        var bagNearest = NearestWorkDrawingOffset(drawing, sourceLines.Where(item => IsBagOrKadasterLayer(item.Layer)));
        var bgtNearest = NearestWorkDrawingOffset(drawing, sourceLines.Where(item => IsBgtLayer(item.Layer)));
        var warning = WorkDrawingPlanWarning(bagSourceCount, bagVisibleCount, bgtSourceCount, bgtVisibleCount, bagNearest, bgtNearest, drawing.CorridorMeters);

        var builder = new StringBuilder();
        builder.AppendLine($"Schaal 1:{_workDrawingScale} · profielpunten {_profilePoints.Count} · contextband +/- {drawing.CorridorMeters:N1} m");
        builder.AppendLine($"BAG/Kadaster lijnen: {bagVisibleCount}/{bagSourceCount} binnen beeld · BGT lijnen: {bgtVisibleCount}/{bgtSourceCount} binnen beeld");
        var visibleKlicThemes = visibleLines
            .Where(item => IsKlicLayer(item.Layer))
            .GroupBy(item => item.Theme)
            .Select(group => $"{KlicThemeLabel(group.Key)} {group.Count()}")
            .ToList();
        builder.AppendLine($"KLIC kruisingen in profiel: {klicCrossings.Count} · bovenaanzicht {string.Join(", ", visibleKlicThemes.DefaultIfEmpty("geen"))}");
        builder.AppendLine($"RD boorlijn {FormatWorkDrawingBounds(_profilePoints.Select(point => new RdPoint(point.X, point.Y)))}");
        builder.AppendLine($"RD BAG/Kadaster {FormatWorkDrawingBounds(sourceLines.Where(item => IsBagOrKadasterLayer(item.Layer)).SelectMany(item => item.Points))}");
        builder.AppendLine($"RD BGT {FormatWorkDrawingBounds(sourceLines.Where(item => IsBgtLayer(item.Layer)).SelectMany(item => item.Points))}");
        if (!string.IsNullOrWhiteSpace(warning)) builder.AppendLine($"Waarschuwing: {warning}");
        return builder.ToString().Trim();
    }

    private WorkDrawingGeometry CreateWorkDrawingGeometry()
    {
        const double width = 1495;
        const double planHeight = 430;
        const double profileHeight = 330;
        const double plotLeft = 72;
        const double plotRight = 72;
        const double planTop = 42;
        const double planBottom = 56;
        const double profileTop = 42;
        const double profileGraphBottom = 226;

        var start = _profilePoints[0];
        var end = _profilePoints[^1];
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 0.001)
        {
            dx = 1;
            dy = 0;
            length = 1;
        }

        var totalDistance = Math.Max(1, _profilePoints[^1].Distance);
        var corridor = Math.Clamp(totalDistance * 0.25, 10, 18);
        var xBuffer = Math.Clamp(totalDistance * 0.08, 6, 18);
        return new WorkDrawingGeometry(
            width,
            planHeight,
            profileHeight,
            plotLeft,
            plotRight,
            planTop,
            planBottom,
            profileTop,
            profileGraphBottom,
            start.X,
            start.Y,
            dx / length,
            dy / length,
            Math.Max(0, -xBuffer),
            totalDistance + xBuffer,
            totalDistance,
            corridor);
    }

    private static (double Along, double Perp) RotateToWorkDrawingAxis(WorkDrawingGeometry drawing, RdPoint point)
    {
        var dx = point.X - drawing.AxisStartX;
        var dy = point.Y - drawing.AxisStartY;
        return (dx * drawing.Cos + dy * drawing.Sin, -dx * drawing.Sin + dy * drawing.Cos);
    }

    private static double WorkDrawingX(WorkDrawingGeometry drawing, double distance)
    {
        var domain = Math.Max(1, drawing.MaxDistance - drawing.MinDistance);
        return drawing.PlotLeft + (distance - drawing.MinDistance) / domain * (drawing.Width - drawing.PlotLeft - drawing.PlotRight);
    }

    private static double WorkDrawingPlanY(WorkDrawingGeometry drawing, double perp)
    {
        return drawing.PlanTop + (drawing.CorridorMeters - perp) / (drawing.CorridorMeters * 2) * (drawing.PlanHeight - drawing.PlanTop - drawing.PlanBottom);
    }

    private static bool WorkDrawingPointInView(WorkDrawingGeometry drawing, RdPoint point)
    {
        var rotated = RotateToWorkDrawingAxis(drawing, point);
        return rotated.Along >= drawing.MinDistance &&
               rotated.Along <= drawing.MaxDistance &&
               rotated.Perp >= -drawing.CorridorMeters &&
               rotated.Perp <= drawing.CorridorMeters;
    }

    private static string WorkDrawingPlanWarning(int bagSourceCount, int bagVisibleCount, int bgtSourceCount, int bgtVisibleCount, double? bagNearest, double? bgtNearest, double corridor)
    {
        if (bagSourceCount == 0 && bgtSourceCount == 0) return "Geen BAG/Kadaster/BGT geometrie geparsed. Controleer importbestanden of parser.";
        if (bagSourceCount > 0 && bagVisibleCount == 0) return $"BAG/Kadaster is geparsed maar valt buiten de contextband. Dichtstbijzijnde offset: {bagNearest:N1} m.";
        if (bgtSourceCount > 0 && bgtVisibleCount == 0) return $"BGT is geparsed maar valt buiten de contextband. Dichtstbijzijnde offset: {bgtNearest:N1} m.";
        if (bagVisibleCount > 0 && bagNearest > corridor * 0.75) return "BAG/Kadaster ligt ver van de boorlijn. Controleer of de boorlijn op dezelfde RD-locatie ligt.";
        return "";
    }

    private static string FormatWorkDrawingBounds(IEnumerable<RdPoint> points)
    {
        var list = points.Where(point => point.X > 0 && point.Y > 0).Take(20000).ToList();
        if (list.Count == 0) return "geen";
        return $"X {list.Min(point => point.X):N0}-{list.Max(point => point.X):N0}, Y {list.Min(point => point.Y):N0}-{list.Max(point => point.Y):N0}";
    }

    private static double? NearestWorkDrawingOffset(WorkDrawingGeometry drawing, IEnumerable<WorkDrawingLine> lines)
    {
        var offsets = lines
            .SelectMany(line => line.Points)
            .Select(point => RotateToWorkDrawingAxis(drawing, point))
            .Where(point => point.Along >= drawing.MinDistance && point.Along <= drawing.MaxDistance)
            .Select(point => Math.Abs(point.Perp))
            .ToList();
        return offsets.Count == 0 ? null : offsets.Min();
    }

    private static int WorkDrawingPlanDrawOrder(WorkDrawingLine line)
    {
        if (IsBgtLayer(line.Layer)) return 0;
        if (IsBagOrKadasterLayer(line.Layer)) return 1;
        if (IsKlicLayer(line.Layer)) return 2;
        return 3;
    }

    private static bool IntersectsWorkDrawingView(WorkDrawingGeometry drawing, IReadOnlyList<RdPoint> points)
    {
        var rotated = points.Select(point => RotateToWorkDrawingAxis(drawing, point)).ToList();
        return rotated.Max(point => point.Along) >= drawing.MinDistance &&
               rotated.Min(point => point.Along) <= drawing.MaxDistance &&
               rotated.Max(point => point.Perp) >= -drawing.CorridorMeters &&
               rotated.Min(point => point.Perp) <= drawing.CorridorMeters;
    }

    private static double DistanceToWorkDrawingTrace(WorkDrawingGeometry drawing, IReadOnlyList<RdPoint> points)
    {
        return points
            .Select(point => RotateToWorkDrawingAxis(drawing, point))
            .Where(point => point.Along >= drawing.MinDistance && point.Along <= drawing.MaxDistance)
            .Select(point => Math.Abs(point.Perp))
            .DefaultIfEmpty(double.MaxValue)
            .Min();
    }

    private sealed record WorkDrawingGeometry(double Width, double PlanHeight, double ProfileHeight, double PlotLeft, double PlotRight, double PlanTop, double PlanBottom, double ProfileTop, double ProfileGraphBottom, double AxisStartX, double AxisStartY, double Cos, double Sin, double MinDistance, double MaxDistance, double TotalDistance, double CorridorMeters);
    private sealed record WorkDrawingLine(ProjectMapLayer Layer, IReadOnlyList<RdPoint> Points, string Theme, string Color, string Label);
}
