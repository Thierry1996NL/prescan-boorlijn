using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using Borevexa.App.Models;

namespace Borevexa.App;

public partial class MainWindow
{
    private string ExportWorkDrawingHtml(bool openAfterExport)
    {
        if (_selectedProject is null) return "Geen project actief.";

        EnsureProfilePoints();
        if (_profilePoints.Count < 2)
        {
            return "Geen boorlijn/dwarsprofiel beschikbaar. Genereer eerst het dwarsprofiel in stap 7.";
        }

        SaveDepthProfile();
        var html = BuildWorkDrawingHtml(includeToolbar: true);
        var exportDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Borevexa", "Exports");
        Directory.CreateDirectory(exportDir);
        var safeName = Regex.Replace(_selectedProject.Name, @"[^A-Za-z0-9_\-]+", "-").Trim('-');
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "prescan";
        var htmlPath = Path.Combine(exportDir, $"werktekening-{safeName}-{DateTime.Now:yyyyMMdd-HHmmss}.html");
        File.WriteAllText(htmlPath, html, Encoding.UTF8);
        _projects.SaveStepData(_selectedProject.Id, WorkDrawingStepNumber, "werktekening_export", JsonSerializer.Serialize(new
        {
            exportedAt = DateTimeOffset.Now,
            path = htmlPath,
            format = "html-a4-landscape",
            profilePoints = _profilePoints.Count
        }, JsonOptions));

        if (openAfterExport)
        {
            try
            {
                Process.Start(new ProcessStartInfo(htmlPath) { UseShellExecute = true });
            }
            catch (Exception exception)
            {
                return $"Werktekening geexporteerd\n\n{htmlPath}\n\nOpenen lukte niet:\n{exception.Message}";
            }
        }

        return $"Werktekening geexporteerd\n\n{htmlPath}\n\nGebruik in de browser: Afdrukken -> Opslaan als PDF.";
    }

    private async Task<string> ExportWorkDrawingHtmlAsync(bool openAfterExport)
    {
        if (_selectedProject is null) return "Geen project actief.";

        EnsureProfilePoints();
        if (_profilePoints.Count < 2)
        {
            return "Geen boorlijn/dwarsprofiel beschikbaar. Genereer eerst het dwarsprofiel in stap 7.";
        }

        SaveDepthProfile();
        var html = BuildWorkDrawingHtml(includeToolbar: true);
        var project = _selectedProject;
        var profilePointCount = _profilePoints.Count;
        var result = await Task.Run(() =>
        {
            var exportDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Borevexa", "Exports");
            Directory.CreateDirectory(exportDir);
            var safeName = Regex.Replace(project.Name, @"[^A-Za-z0-9_\-]+", "-").Trim('-');
            if (string.IsNullOrWhiteSpace(safeName)) safeName = "prescan";
            var htmlPath = Path.Combine(exportDir, $"werktekening-{safeName}-{DateTime.Now:yyyyMMdd-HHmmss}.html");
            File.WriteAllText(htmlPath, html, Encoding.UTF8);
            _projects.SaveStepData(project.Id, WorkDrawingStepNumber, "werktekening_export", JsonSerializer.Serialize(new
            {
                exportedAt = DateTimeOffset.Now,
                path = htmlPath,
                format = "html-a4-landscape",
                profilePoints = profilePointCount
            }, JsonOptions));

            if (!openAfterExport)
            {
                return $"Werktekening geexporteerd\n\n{htmlPath}\n\nGebruik in de browser: Afdrukken -> Opslaan als PDF.";
            }

            try
            {
                Process.Start(new ProcessStartInfo(htmlPath) { UseShellExecute = true });
                return $"Werktekening geexporteerd\n\n{htmlPath}\n\nGebruik in de browser: Afdrukken -> Opslaan als PDF.";
            }
            catch (Exception exception)
            {
                return $"Werktekening geexporteerd\n\n{htmlPath}\n\nOpenen lukte niet:\n{exception.Message}";
            }
        });
        MarkReportUiDataChanged();
        return result;
    }

    private string BuildWorkDrawingHtml(bool includeToolbar)
    {
        var projectName = System.Net.WebUtility.HtmlEncode(_selectedProject?.Name ?? "Project");
        var location = System.Net.WebUtility.HtmlEncode(_selectedProject?.Location ?? "");
        var opdrachtgever = System.Net.WebUtility.HtmlEncode(_selectedProject?.Client ?? "");
        var totalLength = _profilePoints.Count > 0 ? _profilePoints[^1].Distance : 0;
        var minNap = _profilePoints.Count > 0 ? _profilePoints.Min(point => point.Nap) : 0;
        var boring = ComputeBoring();
        var drawing = CreateWorkDrawingGeometry();
        var planSvg = BuildWorkDrawingPlanSvg(drawing);
        var profileSvg = BuildWorkDrawingProfileSvg(drawing);
        var date = DateTime.Now.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        var time = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
        var scaleBar = _workDrawingScale <= 200 ? "20 m" : _workDrawingScale <= 500 ? "50 m" : "100 m";
        var html = new StringBuilder();
        html.AppendLine("<!doctype html>");
        html.AppendLine("<html lang=\"nl\"><head><meta charset=\"utf-8\"><title>Borevexa werktekening</title>");
        html.AppendLine("<style>@page{size:A3 landscape;margin:0}*{box-sizing:border-box}body{margin:0;background:#dfe7ec;font-family:Arial,Segoe UI,sans-serif;color:#071422}.toolbar{width:420mm;margin:10px auto}.print{background:#007a53;color:white;border:0;padding:10px 16px;font-weight:700;border-radius:2px}.sheet{width:420mm;height:297mm;margin:10px auto;background:white;position:relative;border:1px solid #94a3b8}.frame{position:absolute;left:10mm;top:10mm;right:10mm;bottom:10mm;border:1.4mm solid #111827}.inner{position:absolute;left:15mm;top:15mm;right:15mm;bottom:42mm;display:grid;grid-template-rows:1fr .88fr;gap:5mm}.view{border:0.45mm solid #111827;background:#fff;position:relative;overflow:hidden}.view-title{position:absolute;left:4mm;top:3mm;background:white;border:0.25mm solid #111827;padding:1.5mm 3mm;font-size:9pt;font-weight:700;z-index:2}.note{position:absolute;left:4mm;bottom:3mm;background:white;border:0.25mm solid #111827;padding:1.5mm 3mm;font-size:8pt}.titleblock{position:absolute;right:10mm;bottom:10mm;width:178mm;height:30mm;border-left:0.6mm solid #111827;border-top:0.6mm solid #111827;display:grid;grid-template-columns:34mm 54mm 28mm 28mm 34mm;grid-template-rows:10mm 10mm 10mm;background:white;font-size:7.5pt}.cell{border-right:0.35mm solid #111827;border-bottom:0.35mm solid #111827;padding:1.2mm 1.8mm;overflow:hidden}.label{display:block;color:#475569;font-size:6.2pt;text-transform:uppercase}.value{display:block;font-weight:700;margin-top:.8mm;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.brand{font-size:13pt;font-weight:800;color:#007a53}.revblock{position:absolute;left:10mm;bottom:10mm;width:142mm;height:30mm;border-top:0.6mm solid #111827;border-left:0.6mm solid #111827;display:grid;grid-template-columns:18mm 24mm 1fr 30mm;background:white;font-size:7pt}.revblock div{border-right:0.35mm solid #111827;border-bottom:0.35mm solid #111827;padding:1.2mm 1.6mm}.legend{position:absolute;right:18mm;top:18mm;background:white;border:0.25mm solid #111827;padding:2mm 3mm;font-size:8pt}.legend span{display:block;margin:1mm 0}.sw{display:inline-block;width:10mm;height:1.5mm;margin-right:2mm;vertical-align:middle}.north{position:absolute;right:8mm;top:8mm;width:16mm;height:22mm;text-align:center;font-weight:800}.north:before{content:\"\";display:block;margin:0 auto 1mm;width:0;height:0;border-left:4mm solid transparent;border-right:4mm solid transparent;border-bottom:14mm solid #111827}.scale{position:absolute;left:8mm;bottom:8mm;width:45mm;background:white;font-size:8pt}.scale-line{height:3mm;display:grid;grid-template-columns:repeat(4,1fr);border:0.25mm solid #111827}.scale-line span:nth-child(odd){background:#111827}.scale-label{display:flex;justify-content:space-between}@media print{body{background:white}.toolbar{display:none}.sheet{margin:0;border:0}}</style>");
        html.AppendLine("</head><body>");
        if (includeToolbar)
        {
            html.AppendLine("<div class=\"toolbar\"><button class=\"print\" onclick=\"window.print()\">Exporteer naar PDF</button></div>");
        }
        html.AppendLine("<section class=\"sheet\"><div class=\"frame\"></div><div class=\"inner\">");
        html.AppendLine($"<div class=\"view\"><div class=\"view-title\">Situatie boring - schaal 1:{_workDrawingScale}</div><div class=\"legend\"><span><i class=\"sw\" style=\"background:#e11d48\"></i>Boorlijn</span><span><i class=\"sw\" style=\"background:#0057d8\"></i>Kadaster/BAG</span><span><i class=\"sw\" style=\"background:#ffffff;border:1px solid #94a3b8\"></i>BGT</span><span><i class=\"sw\" style=\"background:linear-gradient(90deg,#7b00aa,#00ccff,#ffff00,#0000cc,#00cc00)\"></i>KLIC themakleuren</span></div><div class=\"north\">N</div><div class=\"scale\"><div class=\"scale-line\"><span></span><span></span><span></span><span></span></div><div class=\"scale-label\"><b>0</b><b>{scaleBar}</b></div></div>{planSvg}</div>");
        html.AppendLine($"<div class=\"view\"><div class=\"view-title\">Dwarsprofiel boorpad - schaal 1:{_workDrawingScale}</div>{profileSvg}<div class=\"note\">Laagste punt {minNap:N2} m NAP · lengte {totalLength:N1} m · boring Ø{boring.BoringDiameter} mm</div></div>");
        html.AppendLine("</div>");
        html.AppendLine($"<div class=\"revblock\"><div><b>Rev.</b></div><div><b>Datum</b></div><div><b>Omschrijving</b></div><div><b>Get.</b></div><div>0</div><div>{date}</div><div>Concept werktekening uit Borevexa Prescan</div><div>BVX</div><div></div><div></div><div></div><div></div></div>");
        html.AppendLine("<div class=\"titleblock\">");
        html.AppendLine($"<div class=\"cell\" style=\"grid-column:1 / span 2;grid-row:1 / span 2\"><span class=\"brand\">Borevexa</span><span class=\"label\">HDD werktekening</span><span class=\"value\">{projectName}</span></div>");
        html.AppendLine($"<div class=\"cell\"><span class=\"label\">Opdrachtgever</span><span class=\"value\">{opdrachtgever}</span></div><div class=\"cell\"><span class=\"label\">Locatie</span><span class=\"value\">{location}</span></div><div class=\"cell\"><span class=\"label\">Tekeningnr.</span><span class=\"value\">BVX-HDD-WT</span></div>");
        html.AppendLine($"<div class=\"cell\"><span class=\"label\">Schaal</span><span class=\"value\">1:{_workDrawingScale}</span></div><div class=\"cell\"><span class=\"label\">Formaat</span><span class=\"value\">A3 liggend</span></div><div class=\"cell\"><span class=\"label\">Datum</span><span class=\"value\">{date}</span></div><div class=\"cell\"><span class=\"label\">Tijd</span><span class=\"value\">{time}</span></div><div class=\"cell\"><span class=\"label\">Status</span><span class=\"value\">Concept</span></div>");
        html.AppendLine($"<div class=\"cell\"><span class=\"label\">Tracé</span><span class=\"value\">{totalLength:N1} m</span></div><div class=\"cell\"><span class=\"label\">Boring</span><span class=\"value\">Ø{boring.BoringDiameter} mm</span></div><div class=\"cell\"><span class=\"label\">Bundel</span><span class=\"value\">Ø{boring.BundleDiameter} mm</span></div><div class=\"cell\"><span class=\"label\">Revisie</span><span class=\"value\">0</span></div><div class=\"cell\"><span class=\"label\">Blad</span><span class=\"value\">1 / 1</span></div>");
        html.AppendLine("</div></section></body></html>");
        return html.ToString();
    }

    private string BuildWorkDrawingPlanSvg(WorkDrawingGeometry drawing)
    {
        if (_profilePoints.Count < 2) return "<div>Geen boorlijn beschikbaar.</div>";
        _projectFiles = _selectedProject is null ? _projectFiles : _projects.GetProjectFiles(_selectedProject.Id);
        var layers = BuildProjectMapLayers(_projectFiles);
        var sourceLines = layers
            .Where(layer => IsBgtLayer(layer) || IsBagOrKadasterLayer(layer) || IsKlicLayer(layer))
            .SelectMany(EnumerateWorkDrawingLines)
            .Where(item => item.Points.Count > 0)
            .ToList();
        var geometryLines = sourceLines
            .Where(item => IntersectsWorkDrawingView(drawing, item.Points))
            .OrderBy(item => IsBagOrKadasterLayer(item.Layer) ? 0 : 1)
            .ThenBy(item => DistanceToWorkDrawingTrace(drawing, item.Points))
            .Take(3200)
            .ToList();
        var bagSourceCount = sourceLines.Count(item => IsBagOrKadasterLayer(item.Layer));
        var bgtSourceCount = sourceLines.Count(item => IsBgtLayer(item.Layer));
        var klicSourceCount = sourceLines.Count(item => IsKlicLayer(item.Layer));
        var bagCount = geometryLines.Count(item => IsBagOrKadasterLayer(item.Layer));
        var bgtCount = geometryLines.Count(item => IsBgtLayer(item.Layer));
        var klicCount = geometryLines.Count(item => IsKlicLayer(item.Layer));
        var klicCrossings = GetVisibleKlicProfileCrossings(drawing.TotalDistance);
        var traceBounds = FormatWorkDrawingBounds(_profilePoints.Select(point => new RdPoint(point.X, point.Y)));
        var bagBounds = FormatWorkDrawingBounds(sourceLines.Where(item => IsBagOrKadasterLayer(item.Layer)).SelectMany(item => item.Points));
        var bgtBounds = FormatWorkDrawingBounds(sourceLines.Where(item => IsBgtLayer(item.Layer)).SelectMany(item => item.Points));
        var bagNearest = NearestWorkDrawingOffset(drawing, sourceLines.Where(item => IsBagOrKadasterLayer(item.Layer)));
        var bgtNearest = NearestWorkDrawingOffset(drawing, sourceLines.Where(item => IsBgtLayer(item.Layer)));
        static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
        static string Points(IEnumerable<(double X, double Y)> points) => string.Join(" ", points.Select(point => $"{N(point.X)},{N(point.Y)}"));
        static bool IsBgt(ProjectMapLayer layer) => IsBgtLayer(layer);
        static bool IsBag(ProjectMapLayer layer) => IsBagOrKadasterLayer(layer);
        static bool IsKlic(ProjectMapLayer layer) => IsKlicLayer(layer);
        (double X, double Y) ProjectGeometry(RdPoint point)
        {
            var rotated = RotateToWorkDrawingAxis(drawing, point);
            return (WorkDrawingX(drawing, rotated.Along), WorkDrawingPlanY(drawing, rotated.Perp));
        }
        (double X, double Y) ProjectTrace(ProfilePointRow point)
        {
            var rotated = RotateToWorkDrawingAxis(drawing, new RdPoint(point.X, point.Y));
            return (WorkDrawingX(drawing, point.Distance), WorkDrawingPlanY(drawing, rotated.Perp));
        }
        var sb = new StringBuilder();
        sb.AppendLine($"<svg viewBox=\"0 0 {N(drawing.Width)} {N(drawing.PlanHeight)}\" width=\"100%\" height=\"100%\" preserveAspectRatio=\"xMidYMid meet\" xmlns=\"http://www.w3.org/2000/svg\">");
        sb.AppendLine("<defs>");
        sb.AppendLine($"<clipPath id=\"work-plan-clip\"><rect x=\"{N(drawing.PlotLeft)}\" y=\"{N(drawing.PlanTop)}\" width=\"{N(drawing.Width - drawing.PlotLeft - drawing.PlotRight)}\" height=\"{N(drawing.PlanHeight - drawing.PlanTop - drawing.PlanBottom)}\"/></clipPath>");
        sb.AppendLine("</defs>");
        sb.AppendLine($"<rect x=\"0\" y=\"0\" width=\"{N(drawing.Width)}\" height=\"{N(drawing.PlanHeight)}\" fill=\"#ffffff\"/>");
        sb.AppendLine($"<rect x=\"{N(drawing.PlotLeft)}\" y=\"{N(drawing.PlanTop)}\" width=\"{N(drawing.Width - drawing.PlotLeft - drawing.PlotRight)}\" height=\"{N(drawing.PlanHeight - drawing.PlanTop - drawing.PlanBottom)}\" fill=\"#ffffff\" stroke=\"#CBD5E1\" stroke-width=\"0.8\"/>");
        sb.AppendLine("<g clip-path=\"url(#work-plan-clip)\">");
        foreach (var item in geometryLines.OrderBy(WorkDrawingPlanDrawOrder).ThenBy(item => DistanceToWorkDrawingTrace(drawing, item.Points)))
        {
            var projected = item.Points.Select(ProjectGeometry).ToList();
            if (projected.Count < 1) continue;
            var isBgt = IsBgt(item.Layer);
            var isBag = IsBag(item.Layer);
            var isKlic = IsKlic(item.Layer);
            var stroke = string.IsNullOrWhiteSpace(item.Color) ? (isBag ? "#0057D8" : isBgt ? "#94A3B8" : "#64748B") : item.Color;
            var fill = isBgt && ReportLooksClosed(item.Points) ? "#F8FAFC" : "none";
            var opacity = isBgt ? "0.72" : isBag ? "1" : isKlic ? "0.9" : "0.45";
            var widthStroke = isBag ? 2.4 : isKlic ? 1.7 : 0.9;
            if (ReportLooksClosed(item.Points) && projected.Count >= 4)
            {
                sb.AppendLine($"<polygon points=\"{Points(projected)}\" fill=\"{fill}\" stroke=\"{stroke}\" stroke-width=\"{N(widthStroke)}\" opacity=\"{opacity}\"/>");
            }
            else if (projected.Count == 1)
            {
                sb.AppendLine($"<circle cx=\"{N(projected[0].X)}\" cy=\"{N(projected[0].Y)}\" r=\"2.4\" fill=\"{stroke}\" opacity=\"{opacity}\"/>");
            }
            else
            {
                sb.AppendLine($"<polyline points=\"{Points(projected)}\" fill=\"none\" stroke=\"{stroke}\" stroke-width=\"{N(widthStroke)}\" stroke-linecap=\"round\" stroke-linejoin=\"round\" opacity=\"{opacity}\"/>");
            }
        }

        if (klicCount == 0 && klicCrossings.Count > 0)
        {
            var planBottomY = drawing.PlanHeight - drawing.PlanBottom;
            var planMiddleY = (drawing.PlanTop + planBottomY) / 2;
            foreach (var crossing in klicCrossings.Take(24))
            {
                var x = WorkDrawingX(drawing, crossing.Distance);
                sb.AppendLine($"<line x1=\"{N(x)}\" y1=\"{N(drawing.PlanTop)}\" x2=\"{N(x)}\" y2=\"{N(planBottomY)}\" stroke=\"{crossing.Color}\" stroke-width=\"2.2\" stroke-dasharray=\"8 5\" opacity=\"0.75\"/>");
                sb.AppendLine($"<circle cx=\"{N(x)}\" cy=\"{N(planMiddleY)}\" r=\"5\" fill=\"{crossing.Color}\" stroke=\"#ffffff\" stroke-width=\"1.5\" opacity=\"0.92\"/>");
            }
        }

        var trace = _profilePoints.Select(ProjectTrace).ToList();
        var planAreaHeight = Math.Max(1, drawing.PlanHeight - drawing.PlanTop - drawing.PlanBottom);
        var planMetersPerSvgUnit = Math.Max(0.001, drawing.CorridorMeters * 2 / planAreaHeight);
        var planBoringWidth = Math.Max(2, GetBoringDiameterMillimeters() / 1000d / planMetersPerSvgUnit);
        sb.AppendLine($"<polyline points=\"{Points(trace)}\" fill=\"none\" stroke=\"#FDA4AF\" stroke-width=\"{N(planBoringWidth)}\" stroke-linecap=\"round\" stroke-linejoin=\"round\" opacity=\"0.48\"/>");
        sb.AppendLine($"<polyline points=\"{Points(trace)}\" fill=\"none\" stroke=\"#E11D48\" stroke-width=\"3\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
        for (var i = 0; i < trace.Count; i++)
        {
            var label = i == 0 ? "S" : i == trace.Count - 1 ? "E" : (i + 1).ToString(CultureInfo.InvariantCulture);
            sb.AppendLine($"<circle cx=\"{N(trace[i].X)}\" cy=\"{N(trace[i].Y)}\" r=\"6\" fill=\"#FFFFFF\" stroke=\"#E11D48\" stroke-width=\"2.4\"/><text x=\"{N(trace[i].X + 8)}\" y=\"{N(trace[i].Y - 8)}\" font-size=\"12\" font-weight=\"700\" fill=\"#111827\">{label}</text>");
        }
        sb.AppendLine("</g>");
        sb.AppendLine($"<text x=\"{N(drawing.PlotLeft)}\" y=\"{N(drawing.PlanHeight - 40)}\" font-size=\"11\" fill=\"#334155\">BAG/Kadaster: {bagCount}/{bagSourceCount} · BGT: {bgtCount}/{bgtSourceCount} · KLIC: {klicCount}/{klicSourceCount} binnen beeld · contextband +/- {N(drawing.CorridorMeters)} m</text>");
        sb.AppendLine($"<text x=\"{N(drawing.PlotLeft)}\" y=\"{N(drawing.PlanHeight - 25)}\" font-size=\"10\" fill=\"#64748B\">RD boorlijn {traceBounds} · BAG/Kadaster {bagBounds} · BGT {bgtBounds}</text>");
        var warning = WorkDrawingPlanWarning(bagSourceCount, bagCount, bgtSourceCount, bgtCount, bagNearest, bgtNearest, drawing.CorridorMeters);
        if (!string.IsNullOrWhiteSpace(warning))
        {
            sb.AppendLine($"<text x=\"{N(drawing.PlotLeft)}\" y=\"{N(drawing.PlanHeight - 10)}\" font-size=\"10\" font-weight=\"700\" fill=\"#B45309\">{System.Net.WebUtility.HtmlEncode(warning)}</text>");
        }
        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    private string BuildWorkDrawingProfileSvg(WorkDrawingGeometry drawing)
    {
        if (_profilePoints.Count < 2) return "<div>Geen dwarsprofiel beschikbaar.</div>";
        var width = drawing.Width;
        var height = drawing.ProfileHeight;
        var plotLeft = drawing.PlotLeft;
        var plotRight = drawing.PlotRight;
        var plotTop = drawing.ProfileTop;
        var plotBottom = drawing.ProfileGraphBottom;
        var maxDistance = drawing.TotalDistance;
        var klicCrossings = GetVisibleKlicProfileCrossings(maxDistance);
        var minValue = Math.Min(_profilePoints.Min(point => point.Nap), _profilePoints.Min(point => point.Surface));
        var maxValue = Math.Max(_profilePoints.Max(point => point.Nap), _profilePoints.Max(point => point.Surface));
        if (klicCrossings.Count > 0)
        {
            minValue = Math.Min(minValue, klicCrossings.Min(crossing => crossing.Nap));
            maxValue = Math.Max(maxValue, klicCrossings.Max(crossing => crossing.Nap));
        }

        minValue -= 0.5;
        maxValue += 0.5;
        var span = Math.Max(1, maxValue - minValue);
        string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
        double XDistance(double distance) => WorkDrawingX(drawing, distance);
        double X(ProfilePointRow point) => XDistance(point.Distance);
        double Y(double value) => plotTop + (maxValue - value) / span * (plotBottom - plotTop);
        var boringDiameterMeters = Math.Max(0.075, GetBoringDiameterMillimeters() / 1000d);
        var boreBandWidth = Math.Max(2, boringDiameterMeters * (plotBottom - plotTop) / Math.Max(0.1, span));
        var boreCenterWidth = Math.Max(2, Math.Min(4, boreBandWidth * 0.45));
        // Zelfde dichte-bemonstering-aanpak als de app/rapportgrafiek: maaiveld uit AHN4
        // langs de hele boorlijn, boorlijn/hartlijn via InterpolateBoreNapAtDistance —
        // dezelfde bron als de dieptepunt-bolletjes hieronder, zodat die niet meer van
        // de lijn afwijken (was voorheen een losse Bezier-spline door slechts 4 punten).
        var denseSurfaceRows = GetAhnSurfaceProfileRows(maxDistance);
        var boreSampleDistances = denseSurfaceRows.Count > 0
            ? denseSurfaceRows.Select(row => row.Distance).ToList()
            : _profilePoints.Select(point => point.Distance).ToList();
        var borePoints = boreSampleDistances
            .Select(distance => new Point(XDistance(distance), Y(InterpolateBoreNapAtDistance(distance))))
            .ToList();
        var bore = string.Join(" ", borePoints.Select(point => $"{F(point.X)},{F(point.Y)}"));
        var surface = string.Join(" ", denseSurfaceRows.Count > 0
            ? denseSurfaceRows.Select(row => $"{F(XDistance(row.Distance))},{F(Y(row.Surface))}")
            : _profilePoints.Select(point => $"{F(X(point))},{F(Y(point.Surface))}"));
        var sb = new StringBuilder();
        sb.AppendLine($"<svg viewBox=\"0 0 {F(width)} {F(height)}\" width=\"100%\" height=\"100%\" preserveAspectRatio=\"xMidYMid meet\" xmlns=\"http://www.w3.org/2000/svg\">");
        sb.AppendLine($"<rect x=\"0\" y=\"0\" width=\"{F(width)}\" height=\"{F(height)}\" fill=\"#fff\" stroke=\"#cbd5e1\"/>");
        DrawWorkDrawingBgtStripSvg(sb, drawing, maxDistance, F);
        sb.AppendLine("<g stroke=\"#e2e8f0\" stroke-width=\"1\">");
        for (var i = 0; i <= 5; i++)
        {
            var y = plotTop + i * (plotBottom - plotTop) / 5;
            sb.AppendLine($"<line x1=\"{F(plotLeft)}\" y1=\"{F(y)}\" x2=\"{F(width - plotRight)}\" y2=\"{F(y)}\"/>");
            sb.AppendLine($"<text x=\"{F(plotLeft - 42)}\" y=\"{F(y + 4)}\" font-size=\"10\" fill=\"#64748B\">{F(maxValue - i * span / 5)}</text>");
        }
        sb.AppendLine("</g>");
        sb.AppendLine($"<polyline points=\"{surface}\" fill=\"none\" stroke=\"#475569\" stroke-width=\"2.2\"/>");
        sb.AppendLine($"<polyline points=\"{bore}\" fill=\"none\" stroke=\"#FDA4AF\" stroke-width=\"{F(boreBandWidth)}\" stroke-linecap=\"round\" stroke-linejoin=\"round\" opacity=\"0.55\"/>");
        sb.AppendLine($"<polyline points=\"{bore}\" fill=\"none\" stroke=\"#E11D48\" stroke-width=\"{F(boreCenterWidth)}\" stroke-dasharray=\"9 5\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
        foreach (var sample in BuildProfileEngineeringSamples())
        {
            var markerPoint = ProfileBorePointAtDistance(sample.Distance, XDistance, Y);
            var x = markerPoint.X;
            var y = markerPoint.Y;
            sb.AppendLine($"<circle cx=\"{F(x)}\" cy=\"{F(y)}\" r=\"3\" fill=\"#64748B\" stroke=\"#ffffff\" stroke-width=\"1\"/>");
            sb.AppendLine($"<text x=\"{F(x + 4)}\" y=\"{F(y + 11)}\" font-size=\"8\" font-weight=\"700\" fill=\"#475569\">{sample.Code}</text>");
        }
        var crossingIndex = 0;
        foreach (var crossing in klicCrossings)
        {
            var x = XDistance(crossing.Distance);
            var y = Y(crossing.Nap);
            var key = $"K{crossingIndex + 1}";
            sb.AppendLine($"<ellipse cx=\"{F(x)}\" cy=\"{F(y)}\" rx=\"10\" ry=\"10\" fill=\"none\" stroke=\"{crossing.Color}\" stroke-width=\"1.2\" stroke-dasharray=\"4 3\" opacity=\"0.82\"/>");
            sb.AppendLine($"<circle cx=\"{F(x)}\" cy=\"{F(y)}\" r=\"4\" fill=\"{crossing.Color}\" stroke=\"#ffffff\" stroke-width=\"1.5\"/>");
            sb.AppendLine($"<line x1=\"{F(x)}\" y1=\"{F(plotTop)}\" x2=\"{F(x)}\" y2=\"{F(plotBottom)}\" stroke=\"{crossing.Color}\" stroke-width=\"1\" stroke-dasharray=\"2 3\" opacity=\"0.7\"/>");
            sb.AppendLine($"<rect x=\"{F(x - 10)}\" y=\"{F(Math.Max(plotTop + 4, y - 28))}\" width=\"20\" height=\"14\" fill=\"#ffffff\" stroke=\"{crossing.Color}\" stroke-width=\"0.8\" rx=\"3\"/>");
            sb.AppendLine($"<text x=\"{F(x)}\" y=\"{F(Math.Max(plotTop + 15, y - 18))}\" text-anchor=\"middle\" font-size=\"9\" font-weight=\"700\" fill=\"{crossing.Color}\">{key}</text>");
            crossingIndex++;
        }
        foreach (var point in _profilePoints)
        {
            var x = X(point);
            var y = Y(point.Nap);
            var label = point.Index == 1 ? "S" : point.Index == _profilePoints.Count ? "E" : point.Index.ToString(CultureInfo.InvariantCulture);
            sb.AppendLine($"<circle cx=\"{F(x)}\" cy=\"{F(y)}\" r=\"4.5\" fill=\"#E11D48\"/><text x=\"{F(x + 7)}\" y=\"{F(y - 7)}\" font-size=\"10\" font-weight=\"700\" fill=\"#111827\">{label} · {F(point.Distance)} m / {F(point.Nap)} NAP</text>");
        }
        sb.AppendLine($"<text x=\"{F(plotLeft)}\" y=\"{F(plotBottom + 18)}\" font-size=\"11\" fill=\"#334155\">Dwarsprofiel boorpad - totale lengte {F(maxDistance)} m</text>");
        DrawWorkDrawingKlicTableSvg(sb, klicCrossings, drawing, F);
        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    private void DrawWorkDrawingBgtStripSvg(StringBuilder sb, WorkDrawingGeometry drawing, double total, Func<double, string> format)
    {
        var segments = GetBgtSurfaceSegments(total);
        if (segments.Count == 0) return;

        const double top = 14;
        const double height = 14;
        foreach (var segment in segments)
        {
            var x = WorkDrawingX(drawing, segment.Start);
            var w = Math.Max(2, WorkDrawingX(drawing, segment.End) - x);
            sb.AppendLine($"<rect x=\"{format(x)}\" y=\"{format(top)}\" width=\"{format(w)}\" height=\"{format(height)}\" fill=\"{segment.Color}\" opacity=\"0.72\" stroke=\"#ffffff\" stroke-width=\"0.8\"/>");
            if (w > 58)
            {
                sb.AppendLine($"<text x=\"{format(x + 4)}\" y=\"{format(top + 10)}\" font-size=\"9\" font-weight=\"700\" fill=\"#111827\">{format(segment.Length)} m {System.Net.WebUtility.HtmlEncode(segment.Label)}</text>");
            }
        }
        sb.AppendLine($"<text x=\"{format(drawing.PlotLeft - 60)}\" y=\"{format(top + 10)}\" font-size=\"9\" font-weight=\"700\" fill=\"#475569\">BGT</text>");
    }

    private static void DrawWorkDrawingKlicTableSvg(StringBuilder sb, IReadOnlyList<KlicProfileCrossing> crossings, WorkDrawingGeometry drawing, Func<double, string> format)
    {
        var tableTop = drawing.ProfileGraphBottom + 34;
        var rowHeight = 13d;
        var rows = crossings.Take(5).ToList();
        var tableHeight = 18 + Math.Max(1, rows.Count) * rowHeight;
        sb.AppendLine($"<rect x=\"{format(drawing.PlotLeft)}\" y=\"{format(tableTop)}\" width=\"{format(drawing.Width - drawing.PlotLeft - drawing.PlotRight)}\" height=\"{format(tableHeight)}\" fill=\"#ffffff\" stroke=\"#CBD5E1\" stroke-width=\"0.8\"/>");
        sb.AppendLine($"<text x=\"{format(drawing.PlotLeft + 8)}\" y=\"{format(tableTop + 12)}\" font-size=\"10\" font-weight=\"700\" fill=\"#111827\">KLIC-kruisingen</text>");
        if (rows.Count == 0)
        {
            sb.AppendLine($"<text x=\"{format(drawing.PlotLeft + 120)}\" y=\"{format(tableTop + 12)}\" font-size=\"10\" fill=\"#64748B\">Geen kruisingen gevonden binnen het boorpad.</text>");
            return;
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var crossing = rows[i];
            var y = tableTop + 18 + i * rowHeight;
            sb.AppendLine($"<circle cx=\"{format(drawing.PlotLeft + 12)}\" cy=\"{format(y + 4)}\" r=\"3\" fill=\"{crossing.Color}\"/>");
            sb.AppendLine($"<text x=\"{format(drawing.PlotLeft + 22)}\" y=\"{format(y + 7)}\" font-size=\"9\" fill=\"#111827\">K{i + 1}</text>");
            sb.AppendLine($"<text x=\"{format(drawing.PlotLeft + 58)}\" y=\"{format(y + 7)}\" font-size=\"9\" fill=\"#111827\">{System.Net.WebUtility.HtmlEncode(TruncateText(crossing.Label, 70))}</text>");
            sb.AppendLine($"<text x=\"{format(drawing.PlotLeft + 320)}\" y=\"{format(y + 7)}\" font-size=\"9\" fill=\"#334155\">afstand {format(crossing.Distance)} m</text>");
            sb.AppendLine($"<text x=\"{format(drawing.PlotLeft + 460)}\" y=\"{format(y + 7)}\" font-size=\"9\" fill=\"#334155\">NAP {format(crossing.Nap)} m</text>");
        }

        if (crossings.Count > rows.Count)
        {
            sb.AppendLine($"<text x=\"{format(drawing.PlotLeft + 620)}\" y=\"{format(tableTop + 25)}\" font-size=\"9\" fill=\"#64748B\">+{crossings.Count - rows.Count} extra kruising(en)</text>");
        }
    }
}
