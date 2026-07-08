using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Borevexa.Core.Services;

namespace Borevexa.App;

public partial class MainWindow
{
    private static Border CreateReportParcelOwnerTable(IReadOnlyList<ParcelOwnerSegment> segments)
    {
        var table = CreateReportTable(["Van", "Tot", "Lengte", "Gemeente", "Sectie", "Perceel", "Object ID", "BGT bronhouder", "Categorie", "Naam", "ZRO status"]);
        foreach (var segment in segments)
        {
            AddReportTableRow(table, [
                $"{segment.Start:N1} m",
                $"{segment.End:N1} m",
                $"{segment.Length:N1} m",
                segment.CadastralMunicipality,
                segment.Section,
                segment.ParcelNumber,
                segment.CadastralObjectId,
                segment.BgtHolderCode,
                segment.BgtHolderCategory,
                segment.BgtHolderName,
                segment.ZroStatus
            ]);
        }
        if (segments.Count == 0) AddReportTableRow(table, ["-", "-", "-", "Geen perceelanalyse", "-", "-", "-", "-", "-", "-", "Handmatig controleren"]);
        return table;
    }

    private static Border CreateReportParcelSegmentSummaryTable(IReadOnlyList<ParcelOwnerSegment> segments)
    {
        var table = CreateReportTable(["#", "Afstand", "Lengte", "Perceel", "Object ID", "Bronhouder", "ZRO"]);
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            AddReportTableRow(table, [
                (i + 1).ToString(CultureInfo.InvariantCulture),
                $"{segment.Start:N1} - {segment.End:N1} m",
                $"{segment.Length:N1} m",
                ParcelLabel(segment),
                ShortReportCell(segment.CadastralObjectId, 22),
                ShortReportCell($"{segment.BgtHolderCode} {segment.BgtHolderName}".Trim(), 30),
                segment.ZroStatus
            ]);
        }

        if (segments.Count == 0)
        {
            AddReportTableRow(table, ["-", "-", "-", "Geen perceelanalyse", "-", "-", "Handmatig controleren"]);
        }

        return table;
    }

    private static Border CreateReportParcelActionTable(IReadOnlyList<ParcelOwnerSegment> segments)
    {
        var table = CreateReportTable(["Perceel", "Bronhouder", "Stakeholder", "Actie", "Status", "Eigenaar intern", "Deadline"]);
        foreach (var segment in segments)
        {
            AddReportTableRow(table, [
                ParcelLabel(segment),
                ShortReportCell(segment.BgtHolderName, 22),
                SuggestedStakeholder(segment),
                SuggestedAction(segment),
                "Open",
                "Handmatig invullen",
                "Handmatig invullen"
            ]);
        }

        if (segments.Count == 0) AddReportTableRow(table, ["-", "-", "-", "Geen actiepunten", "-", "-", "-"]);
        return table;
    }

    private static Border CreateReportParcelRiskTable(IReadOnlyList<ParcelOwnerSegment> segments)
    {
        var table = CreateReportTable(["Perceel", "Lengte", "Bronhouder type", "Risico", "Reden"]);
        foreach (var segment in segments)
        {
            var (level, reason) = AssessParcelRisk(segment);
            AddReportTableRow(table, [
                ParcelLabel(segment),
                $"{segment.Length:N1} m",
                segment.BgtHolderCategory,
                level,
                reason
            ]);
        }

        if (segments.Count == 0) AddReportTableRow(table, ["-", "-", "-", "-", "Geen perceelsegmenten"]);
        return table;
    }

    private static Border CreateReportParcelBoundaryCrossingTable(IReadOnlyList<ParcelOwnerSegment> segments)
    {
        var table = CreateReportTable(["#", "Afstand", "Type", "Perceel"]);
        var rows = 0;
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            AddReportTableRow(table, [
                (++rows).ToString(CultureInfo.InvariantCulture),
                $"{segment.Start:N1} m",
                i == 0 ? "Start in perceel" : "Perceelgrens in",
                ParcelLabel(segment)
            ]);
            AddReportTableRow(table, [
                (++rows).ToString(CultureInfo.InvariantCulture),
                $"{segment.End:N1} m",
                i == segments.Count - 1 ? "Einde in perceel" : "Perceelgrens uit",
                ParcelLabel(segment)
            ]);
        }

        if (rows == 0) AddReportTableRow(table, ["-", "-", "Geen kruisingen", "-"]);
        return table;
    }

    private static Border CreateReportHolderSummaryTable(IReadOnlyList<ParcelOwnerSegment> segments)
    {
        var table = CreateReportTable(["Bronhouder", "Categorie", "Percelen", "Totale lengte", "ZRO status"]);
        foreach (var group in segments
                     .GroupBy(segment => $"{segment.BgtHolderCode}|{segment.BgtHolderCategory}|{segment.BgtHolderName}", StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(group => group.Sum(segment => segment.Length)))
        {
            var first = group.First();
            var parcelCount = group
                .Select(segment => segment.CadastralObjectId)
                .Where(value => !string.IsNullOrWhiteSpace(value) && value != "-")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            AddReportTableRow(table, [
                $"{first.BgtHolderCode} · {ShortReportCell(first.BgtHolderName, 24)}",
                first.BgtHolderCategory,
                parcelCount == 0 ? group.Count().ToString(CultureInfo.InvariantCulture) : parcelCount.ToString(CultureInfo.InvariantCulture),
                $"{group.Sum(segment => segment.Length):N1} m",
                "Handmatige controle"
            ]);
        }

        if (segments.Count == 0) AddReportTableRow(table, ["-", "-", "-", "-", "-"]);
        return table;
    }

    private static Border CreateReportZroChecklistTable(IReadOnlyList<ParcelOwnerSegment> segments)
    {
        var table = CreateReportTable(["Perceel", "Onbekend", "n.v.t.", "Aanwezig", "Aanvraag nodig", "Akkoord"]);
        foreach (var segment in segments)
        {
            AddReportTableRow(table, [
                ParcelLabel(segment),
                "[ ]",
                "[ ]",
                "[ ]",
                "[ ]",
                "[ ]"
            ]);
        }

        if (segments.Count == 0) AddReportTableRow(table, ["-", "[ ]", "[ ]", "[ ]", "[ ]", "[ ]"]);
        return table;
    }

    private static Border CreateReportParcelNotesTable(IReadOnlyList<ParcelOwnerSegment> segments)
    {
        var table = CreateReportTable(["Perceel", "Opmerking / contactmoment"]);
        foreach (var segment in segments)
        {
            AddReportTableRow(table, [
                ParcelLabel(segment),
                "Handmatig invullen"
            ]);
        }

        if (segments.Count == 0) AddReportTableRow(table, ["-", "Geen opmerkingen"]);
        return table;
    }

    private static Border CreateReportParcelSourceTable(
        IReadOnlyList<ProjectFileRecord> projectFiles,
        ParcelOwnerAnalysis analysis)
    {
        var table = CreateReportTable(["Bron", "Bestand", "Datum", "Aantal"]);
        var cadastralFiles = projectFiles
            .Where(file => file.FileType.Contains("BAG", StringComparison.OrdinalIgnoreCase) || file.FileType.Contains("Kadaster", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var bgtFiles = projectFiles
            .Where(file => file.FileType.Contains("BGT", StringComparison.OrdinalIgnoreCase))
            .ToList();

        AddReportTableRow(table, [
            "Kadaster/BAG percelen",
            cadastralFiles.Count == 0 ? "-" : ShortReportCell(string.Join(", ", cadastralFiles.Select(file => file.DisplayName)), 40),
            cadastralFiles.Count == 0 ? "-" : cadastralFiles.Max(file => file.CreatedAt).ToString("dd-MM-yyyy HH:mm", CultureInfo.CurrentCulture),
            analysis.ParcelPolygons.Count.ToString(CultureInfo.InvariantCulture)
        ]);
        AddReportTableRow(table, [
            "BGT bronhouder",
            bgtFiles.Count == 0 ? "-" : ShortReportCell(string.Join(", ", bgtFiles.Select(file => file.DisplayName)), 40),
            bgtFiles.Count == 0 ? "-" : bgtFiles.Max(file => file.CreatedAt).ToString("dd-MM-yyyy HH:mm", CultureInfo.CurrentCulture),
            analysis.HolderPolygons.Count.ToString(CultureInfo.InvariantCulture)
        ]);
        AddReportTableRow(table, [
            "Analyse",
            "Boorlijn x perceelpolygonen",
            DateTime.Now.ToString("dd-MM-yyyy HH:mm", CultureInfo.CurrentCulture),
            analysis.Segments.Count.ToString(CultureInfo.InvariantCulture)
        ]);

        return table;
    }

    private static Border CreateReportEnvironmentConclusion(ParcelOwnerAnalysis analysis)
    {
        return CreateReportStepTextBlock($"- {EnvironmentConclusionText(analysis)}");
    }

    private static string EnvironmentConclusionText(ParcelOwnerAnalysis analysis)
    {
        var segments = analysis.Segments;
        var holderCount = segments
            .Select(segment => segment.BgtHolderCode)
            .Where(code => !string.IsNullOrWhiteSpace(code) && code != "-")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var highRiskCount = segments.Count(segment => AssessParcelRisk(segment).Level == "Hoog");
        var zroOpenCount = segments.Count(segment =>
            segment.ZroStatus.Contains("Handmatig", StringComparison.OrdinalIgnoreCase) ||
            segment.ZroStatus.Contains("Onbekend", StringComparison.OrdinalIgnoreCase));

        return segments.Count == 0
            ? "Er zijn nog geen kadastrale perceelsegmenten gekoppeld aan de boorlijn. Controleer of de boorlijn is opgeslagen en of de Kadaster/BAG-import met perceelvlakken aanwezig is."
            : $"De boorlijn kruist {segments.Count} perceelsegment(en) over {segments.Sum(segment => segment.Length):N1} m. Er zijn {holderCount} bronhouder(s) herkend. Voor {zroOpenCount} segment(en) blijft ZRO/zakelijk recht handmatige controle. {(highRiskCount > 0 ? $"{highRiskCount} segment(en) hebben een hoge aandachtsscore." : "Er zijn op basis van de beschikbare brondata geen hoge risico's herkend.")}";
    }

    private static Border CreateReportResidualPointsTable(ParcelOwnerAnalysis analysis)
    {
        var table = CreateReportTable(["#", "Restpunt", "Prioriteit", "Actie"]);
        var rows = BuildEnvironmentResidualPoints(analysis).ToList();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            AddReportTableRow(table, [
                (i + 1).ToString(CultureInfo.InvariantCulture),
                row.Item,
                row.Priority,
                row.Action
            ]);
        }

        if (rows.Count == 0)
        {
            AddReportTableRow(table, ["-", "Geen automatische restpunten gevonden", "-", "-"]);
        }

        return table;
    }

    private static IEnumerable<(string Item, string Priority, string Action)> BuildEnvironmentResidualPoints(ParcelOwnerAnalysis analysis)
    {
        if (analysis.TraceRows.Count < 2)
        {
            yield return ("Boorlijn ontbreekt of heeft minder dan twee punten", "Hoog", $"Boorlijn opslaan in stap {DisplayStepNumber(3)}");
        }

        if (analysis.ParcelPolygons.Count == 0)
        {
            yield return ("Geen kadastrale perceelvlakken gevonden", "Hoog", "Kadaster/BAG ZIP met kadastralekaart_perceel.gml importeren");
        }

        if (analysis.Segments.Count == 0)
        {
            yield return ("Geen perceelsegmenten uit analyse", "Hoog", "Analyse opnieuw uitvoeren en brondata controleren");
        }

        foreach (var segment in analysis.Segments.Where(segment => segment.ZroStatus.Contains("Handmatig", StringComparison.OrdinalIgnoreCase)))
        {
            yield return ($"ZRO-status controleren voor {ParcelLabel(segment)}", "Middel", "ZRO/eigendom handmatig verifieren");
        }

        foreach (var segment in analysis.Segments.Where(segment => segment.BgtHolderCode == "-" || segment.BgtHolderName == "Onbekend"))
        {
            yield return ($"Bronhouder niet volledig herkend voor {ParcelLabel(segment)}", "Middel", "Bronhouder handmatig bepalen");
        }

        foreach (var segment in analysis.Segments.Where(segment => AssessParcelRisk(segment).Level == "Hoog"))
        {
            yield return ($"Hoog aandachtspunt: {ParcelLabel(segment)} ({segment.Length:N1} m)", "Hoog", SuggestedAction(segment));
        }
    }
    private Border CreateReportParcelOwnerMapGallery(ParcelOwnerAnalysis analysis)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        panel.Children.Add(new TextBlock
        {
            Text = "Kaartuitsneden per gevonden perceel",
            Foreground = Brush("#587080"),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var cards = analysis.Segments
            .Where(segment => segment.Length >= 0.2)
            .Select((segment, index) => CreateReportParcelOwnerMapCard(segment, index + 1, analysis.TraceRows, analysis.ParcelPolygons, analysis.TraceLength))
            .ToList();

        if (cards.Count == 0)
        {
            panel.Children.Add(CreateReportNote("Geen perceelsegmenten gevonden om als kaartuitsnede op te nemen."));
        }
        else
        {
            foreach (var card in cards)
            {
                panel.Children.Add(card);
            }
        }

        return new Border
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Child = panel
        };
    }

    private IReadOnlyList<Border> CreateReportParcelOwnerMapPages(ParcelOwnerAnalysis analysis, int startPageNumber)
    {
        var pages = new List<Border>();
        var segments = analysis.Segments
            .Where(segment => segment.Length >= 0.2)
            .ToList();

        if (segments.Count == 0)
        {
            return pages;
        }

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            var reportTitle = $"5.1 Perceelsegmenten - kaartbijlage perceel {i + 1}";
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = $"Kaartbijlage perceel {i + 1}",
                Foreground = Brush("#071422"),
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            });
            panel.Children.Add(new TextBlock
            {
                Text = $"Trajectdeel {segment.Start:N1} - {segment.End:N1} m ({segment.Length:N1} m)",
                Foreground = Brush("#587080"),
                FontSize = 11.5,
                Margin = new Thickness(0, -3, 0, 10)
            });
            panel.Children.Add(CreateReportParcelOwnerMapCanvas(
                segment,
                analysis.TraceRows,
                analysis.ParcelPolygons,
                analysis.TraceLength,
                724,
                520));
            panel.Children.Add(new Border
            {
                Margin = new Thickness(0, 14, 0, 0),
                Child = CreateReportKeyValues(
                    ("Kadastrale gemeente", segment.CadastralMunicipality),
                    ("Sectie / perceel", $"{segment.Section} {segment.ParcelNumber}".Trim()),
                    ("Object ID", segment.CadastralObjectId),
                    ("BGT bronhouder", $"{segment.BgtHolderCode} · {segment.BgtHolderCategory}"),
                    ("Bronhouder naam", segment.BgtHolderName),
                    ("ZRO status", segment.ZroStatus))
            });
            panel.Children.Add(CreateReportNote("Oranje perceelvlak en oranje boorlijnsegment horen bij dit perceel. De grijze lijn toont de volledige boorlijn als context."));

            pages.Add(CreateReportPage(
                startPageNumber + i,
                reportTitle,
                CreateReportSection(EnvironmentStepNumber, reportTitle, panel)));
        }

        return pages;
    }

    private string BuildReportParcelOwnerMapPagesHtml(ParcelOwnerAnalysis analysis, string headerLocation, ref int pageNumber)
    {
        if (_selectedProject is null) return "";
        var html = new System.Text.StringBuilder();
        var segments = analysis.Segments.Where(segment => segment.Length >= 0.2).ToList();
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            var reportTitle = $"5.1 Perceelsegmenten - kaartbijlage perceel {i + 1}";
            html.AppendLine("<div class=\"page\">");
            html.AppendLine("<div class=\"top\">");
            html.AppendLine($"<div><div class=\"brand\">{System.Net.WebUtility.HtmlEncode(DefaultCoverTitle)}</div><div class=\"sub\">HDD Horizontaal Gestuurd Boren</div></div>");
            html.AppendLine($"<div class=\"code\"><b>{System.Net.WebUtility.HtmlEncode(_selectedProject.Name)}</b><div>{System.Net.WebUtility.HtmlEncode(headerLocation)}</div><div class=\"sub\">Pagina {pageNumber++} - {System.Net.WebUtility.HtmlEncode(reportTitle)}</div></div>");
            html.AppendLine("</div>");
            html.AppendLine("<div class=\"section\">");
            html.AppendLine($"<h2>{System.Net.WebUtility.HtmlEncode(reportTitle)}</h2>");
            html.AppendLine($"<p><b>Perceel {i + 1}: {segment.Start:N1} - {segment.End:N1} m ({segment.Length:N1} m)</b></p>");
            html.AppendLine(BuildReportParcelOwnerMapSvg(segment, analysis.TraceRows, analysis.ParcelPolygons, analysis.TraceLength));
            html.AppendLine("<table class=\"kv\">");
            AppendHtmlKeyValueRow(html, "Kadastrale gemeente", segment.CadastralMunicipality);
            AppendHtmlKeyValueRow(html, "Sectie / perceel", $"{segment.Section} {segment.ParcelNumber}".Trim());
            AppendHtmlKeyValueRow(html, "Object ID", segment.CadastralObjectId);
            AppendHtmlKeyValueRow(html, "BGT bronhouder", $"{segment.BgtHolderCode} · {segment.BgtHolderCategory}");
            AppendHtmlKeyValueRow(html, "Bronhouder naam", segment.BgtHolderName);
            AppendHtmlKeyValueRow(html, "ZRO status", segment.ZroStatus);
            html.AppendLine("</table>");
            html.AppendLine("<p class=\"map-source\">Oranje perceelvlak en oranje boorlijnsegment horen bij dit perceel. De grijze lijn toont de volledige boorlijn als context.</p>");
            html.AppendLine("</div>");
            html.AppendLine($"<div class=\"footer\"><span></span><span>{DateTime.Now:dd-MM-yyyy}</span></div>");
            html.AppendLine("</div>");
        }
        return html.ToString();
    }

    private static void AppendHtmlKeyValueRow(System.Text.StringBuilder html, string key, string value)
    {
        html.AppendLine($"<tr><td>{System.Net.WebUtility.HtmlEncode(key)}</td><td>{System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(value) ? "-" : value)}</td></tr>");
    }

    private string BuildReportParcelOwnerMapSvg(
        ParcelOwnerSegment segment,
        IReadOnlyList<TracePointRow> traceRows,
        IReadOnlyList<CadastralParcelPolygon> parcelPolygons,
        double profileTotal)
    {
        static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        static string Points(IEnumerable<RdPoint> points, Func<RdPoint, (double X, double Y)> project)
        {
            return string.Join(" ", points.Select(point =>
            {
                var projected = project(point);
                return $"{N(projected.X)},{N(projected.Y)}";
            }));
        }

        const double width = 760;
        const double height = 440;
        var parcel = segment.Parcel ?? FindReportParcel(segment, parcelPolygons);
        var tracePoints = traceRows.Select(row => new RdPoint(row.X, row.Y)).Where(IsValidRdPoint).ToList();
        var segmentPoints = segment.TracePath.Count >= 2
            ? segment.TracePath.Where(IsValidRdPoint).ToList()
            : BuildReportSegmentFocusPoints(segment, traceRows, profileTotal).Where(IsValidRdPoint).ToList();
        var focusPoints = new List<RdPoint>();
        if (parcel is not null)
        {
            focusPoints.AddRange(parcel.Ring);
            foreach (var hole in parcel.Holes) focusPoints.AddRange(hole);
        }
        focusPoints.AddRange(segmentPoints);
        if (focusPoints.Count == 0) focusPoints.AddRange(tracePoints);
        if (focusPoints.Count == 0)
        {
            return "<div class=\"mapbox\"><p>Geen kaartgeometrie beschikbaar.</p></div>";
        }

        var minX = focusPoints.Min(point => point.X);
        var maxX = focusPoints.Max(point => point.X);
        var minY = focusPoints.Min(point => point.Y);
        var maxY = focusPoints.Max(point => point.Y);
        var spanX = Math.Max(1, maxX - minX);
        var spanY = Math.Max(1, maxY - minY);
        var padding = Math.Max(5, Math.Max(spanX, spanY) * 0.12);
        minX -= padding;
        maxX += padding;
        minY -= padding;
        maxY += padding;
        spanX = Math.Max(1, maxX - minX);
        spanY = Math.Max(1, maxY - minY);
        var scale = Math.Min((width - 28) / spanX, (height - 28) / spanY);
        var offsetX = 14 + (width - 28 - spanX * scale) / 2;
        var offsetY = 14 + (height - 28 - spanY * scale) / 2;

        (double X, double Y) Project(RdPoint point) => (offsetX + (point.X - minX) * scale, offsetY + (maxY - point.Y) * scale);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<div class=\"mapbox locked-mapbox\">");
        sb.AppendLine("<svg viewBox=\"0 0 760 440\" style=\"display:block;width:100%;height:124mm;border:1px solid #dbe4ea;background:#eef7fb\" xmlns=\"http://www.w3.org/2000/svg\">");
        sb.AppendLine("<rect x=\"0\" y=\"0\" width=\"760\" height=\"440\" fill=\"#eef7fb\"/>");
        foreach (var nearby in parcelPolygons.Where(candidate => ReportParcelIntersectsView(candidate, minX, minY, maxX, maxY)).Take(120))
        {
            sb.AppendLine($"<polyline points=\"{Points(nearby.Ring, Project)}\" fill=\"none\" stroke=\"#0b63ce\" stroke-width=\"0.8\"/>");
        }
        if (parcel is not null)
        {
            sb.AppendLine($"<polygon points=\"{Points(parcel.Ring, Project)}\" fill=\"#fff3c4\" stroke=\"#f97316\" stroke-width=\"3\"/>");
            foreach (var hole in parcel.Holes)
            {
                sb.AppendLine($"<polygon points=\"{Points(hole, Project)}\" fill=\"#eef7fb\" stroke=\"#f97316\" stroke-width=\"1.2\"/>");
            }
        }
        if (tracePoints.Count >= 2)
        {
            sb.AppendLine($"<polyline points=\"{Points(tracePoints, Project)}\" fill=\"none\" stroke=\"#ffffff\" stroke-width=\"5\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
            sb.AppendLine($"<polyline points=\"{Points(tracePoints, Project)}\" fill=\"none\" stroke=\"#94a3b8\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
        }
        var highlighted = segmentPoints.Count >= 2 ? segmentPoints : BuildReportSegmentFocusPoints(segment, traceRows, profileTotal);
        if (highlighted.Count >= 2)
        {
            sb.AppendLine($"<polyline points=\"{Points(highlighted, Project)}\" fill=\"none\" stroke=\"#fed7aa\" stroke-width=\"11\" stroke-linecap=\"round\" stroke-linejoin=\"round\" opacity=\".85\"/>");
            sb.AppendLine($"<polyline points=\"{Points(highlighted, Project)}\" fill=\"none\" stroke=\"#f97316\" stroke-width=\"4\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
        }
        var barMeters = Math.Max(10, Math.Round(Math.Min(200, (width * 0.28) / Math.Max(0.001, scale)) / 10) * 10);
        var barWidth = barMeters * scale;
        sb.AppendLine($"<line x1=\"18\" y1=\"404\" x2=\"{N(18 + barWidth)}\" y2=\"404\" stroke=\"#0f172a\" stroke-width=\"3\"/>");
        sb.AppendLine($"<line x1=\"18\" y1=\"396\" x2=\"18\" y2=\"412\" stroke=\"#0f172a\" stroke-width=\"2\"/>");
        sb.AppendLine($"<line x1=\"{N(18 + barWidth)}\" y1=\"396\" x2=\"{N(18 + barWidth)}\" y2=\"412\" stroke=\"#0f172a\" stroke-width=\"2\"/>");
        sb.AppendLine($"<text x=\"18\" y=\"392\" font-size=\"10\" fill=\"#334155\">0</text><text x=\"{N(18 + barWidth - 30)}\" y=\"392\" font-size=\"10\" fill=\"#334155\">{N(barMeters)} m</text>");
        sb.AppendLine($"<text x=\"18\" y=\"430\" font-size=\"14\" font-weight=\"700\" fill=\"#071422\">{System.Net.WebUtility.HtmlEncode($"{segment.Start:N1}-{segment.End:N1} m")}</text>");
        sb.AppendLine("</svg>");
        sb.AppendLine("</div>");
        return sb.ToString();
    }

    private Border CreateReportParcelOwnerMapCard(
        ParcelOwnerSegment segment,
        int index,
        IReadOnlyList<TracePointRow> traceRows,
        IReadOnlyList<CadastralParcelPolygon> parcelPolygons,
        double profileTotal)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(CreateReportParcelOwnerMapCanvas(segment, traceRows, parcelPolygons, profileTotal));

        var info = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        info.Children.Add(new TextBlock
        {
            Text = $"Perceel {index}: {segment.Start:N1} - {segment.End:N1} m ({segment.Length:N1} m)",
            Foreground = Brush("#071422"),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap
        });
        info.Children.Add(CreateReportKeyValues(
            ("Kadastrale gemeente", segment.CadastralMunicipality),
            ("Sectie / perceel", $"{segment.Section} {segment.ParcelNumber}".Trim()),
            ("Object ID", segment.CadastralObjectId),
            ("BGT bronhouder", $"{segment.BgtHolderCode} · {segment.BgtHolderCategory}"),
            ("Bronhouder naam", segment.BgtHolderName),
            ("ZRO status", segment.ZroStatus)));
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);

        return new Border
        {
            Background = Brush("#FFFFFF"),
            BorderBrush = Brush("#D7E8FA"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            ClipToBounds = true,
            Padding = new Thickness(10),
            Margin = new Thickness(0, 8, 0, 0),
            Child = grid
        };
    }

    private Canvas CreateReportParcelOwnerMapCanvas(
        ParcelOwnerSegment segment,
        IReadOnlyList<TracePointRow> traceRows,
        IReadOnlyList<CadastralParcelPolygon> parcelPolygons,
        double profileTotal,
        double width = 340,
        double height = 190)
    {
        var canvas = new Canvas { Width = width, Height = height, Background = Brush("#EEF7FB"), ClipToBounds = true };
        AddCanvasRect(canvas, 0, 0, width, height, "#EEF7FB", "#CBD5E1", 1);

        var parcel = segment.Parcel ?? FindReportParcel(segment, parcelPolygons);
        var tracePoints = traceRows.Select(row => new RdPoint(row.X, row.Y)).Where(IsValidRdPoint).ToList();
        var segmentPoints = segment.TracePath.Count >= 2
            ? segment.TracePath.Where(IsValidRdPoint).ToList()
            : BuildReportSegmentFocusPoints(segment, traceRows, profileTotal).Where(IsValidRdPoint).ToList();
        var focusPoints = new List<RdPoint>();
        if (parcel is not null)
        {
            focusPoints.AddRange(parcel.Ring);
            foreach (var hole in parcel.Holes) focusPoints.AddRange(hole);
        }
        focusPoints.AddRange(segmentPoints);
        if (focusPoints.Count == 0) focusPoints.AddRange(tracePoints);

        if (focusPoints.Count == 0)
        {
            AddCanvasText(canvas, "Geen kaartgeometrie", 110, 82, "#8FA6B2", 11, FontWeights.Normal);
            return canvas;
        }

        var minX = focusPoints.Min(point => point.X);
        var maxX = focusPoints.Max(point => point.X);
        var minY = focusPoints.Min(point => point.Y);
        var maxY = focusPoints.Max(point => point.Y);
        var spanX = Math.Max(1, maxX - minX);
        var spanY = Math.Max(1, maxY - minY);
        var padding = Math.Max(5, Math.Max(spanX, spanY) * 0.12);
        minX -= padding;
        maxX += padding;
        minY -= padding;
        maxY += padding;
        spanX = Math.Max(1, maxX - minX);
        spanY = Math.Max(1, maxY - minY);
        var scale = Math.Min((width - 28) / spanX, (height - 28) / spanY);
        var offsetX = 14 + (width - 28 - spanX * scale) / 2;
        var offsetY = 14 + (height - 28 - spanY * scale) / 2;

        Point Project(RdPoint point) => new(offsetX + (point.X - minX) * scale, offsetY + (maxY - point.Y) * scale);

        foreach (var nearby in parcelPolygons
                     .Where(candidate => ReportParcelIntersectsView(candidate, minX, minY, maxX, maxY))
                     .Take(80))
        {
            AddCanvasPolygon(canvas, "Transparent", "#0B63CE", 0.8, nearby.Ring.Select(Project).SelectMany(point => new[] { point.X, point.Y }).ToArray());
        }

        if (parcel is not null)
        {
            AddCanvasPolygon(canvas, "#FFF3C4", "#F97316", 3, parcel.Ring.Select(Project).SelectMany(point => new[] { point.X, point.Y }).ToArray());
            foreach (var hole in parcel.Holes)
            {
                AddCanvasPolygon(canvas, "#EEF7FB", "#F97316", 1.2, hole.Select(Project).SelectMany(point => new[] { point.X, point.Y }).ToArray());
            }
        }
        else
        {
            AddCanvasText(canvas, "Perceelgeometrie niet gevonden", 82, 74, "#8FA6B2", 10.5, FontWeights.Normal);
        }

        if (tracePoints.Count >= 2)
        {
            var traceCoordinates = tracePoints.Select(Project).SelectMany(point => new[] { point.X, point.Y }).ToArray();
            AddCanvasPolyline(canvas, "#FFFFFF", 5, traceCoordinates);
            AddCanvasPolyline(canvas, "#94A3B8", 2, traceCoordinates);
        }

        AddReportParcelSegmentOnTrace(canvas, segment, traceRows, profileTotal, Project);
        AddReportScaleBar(canvas, scale, 16, height - 58, Math.Min(120, width * 0.28));
        AddCanvasText(canvas, $"{segment.Start:N1}-{segment.End:N1} m", 16, height - 30, "#071422", width > 400 ? 14 : 10.5, FontWeights.Bold);
        return canvas;
    }

    private void AddReportParcelSegmentOnTrace(Canvas canvas, ParcelOwnerSegment segment, IReadOnlyList<TracePointRow> traceRows, double profileTotal, Func<RdPoint, Point> project)
    {
        if (segment.TracePath.Count >= 2)
        {
            var segmentCoords = segment.TracePath.Select(project).SelectMany(point => new[] { point.X, point.Y }).ToArray();
            AddCanvasPolyline(canvas, "#FFFFFF", 10, segmentCoords);
            AddCanvasPolyline(canvas, "#F97316", 6, segmentCoords);
            return;
        }

        if (traceRows.Count < 2) return;
        var distances = BuildTraceDistances(traceRows);
        if (distances.Count < 2 || distances[^1] <= 0) return;

        if (profileTotal <= 0) return;

        var traceStart = distances[^1] * Math.Clamp(segment.Start / profileTotal, 0, 1);
        var traceEnd = distances[^1] * Math.Clamp(segment.End / profileTotal, 0, 1);
        var line = new List<RdPoint>();
        var startPoint = InterpolateTracePoint(traceRows, distances, traceStart);
        line.Add(new RdPoint(startPoint.X, startPoint.Y));
        for (var i = 1; i < distances.Count - 1; i++)
        {
            if (distances[i] > traceStart && distances[i] < traceEnd)
            {
                line.Add(new RdPoint(traceRows[i].X, traceRows[i].Y));
            }
        }
        var endPoint = InterpolateTracePoint(traceRows, distances, traceEnd);
        line.Add(new RdPoint(endPoint.X, endPoint.Y));
        if (line.Count < 2) return;

        var coords = line.Select(project).SelectMany(point => new[] { point.X, point.Y }).ToArray();
        AddCanvasPolyline(canvas, "#FFFFFF", 10, coords);
        AddCanvasPolyline(canvas, "#F97316", 6, coords);
    }

    private static RdPoint? GetReportSegmentMidpoint(ParcelOwnerSegment segment, IReadOnlyList<TracePointRow> traceRows, double profileTotal)
    {
        if (traceRows.Count < 2) return null;
        var distances = BuildTraceDistances(traceRows);
        if (distances.Count < 2 || distances[^1] <= 0) return null;

        var midpointDistance = distances[^1] * Math.Clamp(((segment.Start + segment.End) / 2) / profileTotal, 0, 1);
        var point = InterpolateTracePoint(traceRows, distances, midpointDistance);
        return new RdPoint(point.X, point.Y);
    }

    private static IReadOnlyList<RdPoint> BuildReportSegmentFocusPoints(ParcelOwnerSegment segment, IReadOnlyList<TracePointRow> traceRows, double profileTotal)
    {
        if (traceRows.Count < 2) return [];
        var distances = BuildTraceDistances(traceRows);
        if (distances.Count < 2 || distances[^1] <= 0) return [];

        var traceStart = distances[^1] * Math.Clamp(segment.Start / profileTotal, 0, 1);
        var traceEnd = distances[^1] * Math.Clamp(segment.End / profileTotal, 0, 1);
        var points = new List<RdPoint>();
        var startPoint = InterpolateTracePoint(traceRows, distances, traceStart);
        points.Add(new RdPoint(startPoint.X, startPoint.Y));
        for (var i = 1; i < distances.Count - 1; i++)
        {
            if (distances[i] > traceStart && distances[i] < traceEnd)
            {
                points.Add(new RdPoint(traceRows[i].X, traceRows[i].Y));
            }
        }

        var endPoint = InterpolateTracePoint(traceRows, distances, traceEnd);
        points.Add(new RdPoint(endPoint.X, endPoint.Y));
        return points;
    }

    private static double GetReportProfileTotal(IReadOnlyList<TracePointRow> traceRows)
    {
        var distances = BuildTraceDistances(traceRows);
        return distances.Count > 0 ? Math.Max(1, distances[^1]) : 1;
    }

    private static string ParcelLabel(ParcelOwnerSegment segment)
    {
        var sectionParcel = $"{segment.Section} {segment.ParcelNumber}".Trim();
        return string.IsNullOrWhiteSpace(sectionParcel) || sectionParcel == "-"
            ? ShortReportCell(segment.CadastralObjectId, 22)
            : $"{ShortReportCell(segment.CadastralMunicipality, 14)} {sectionParcel}";
    }

    private static string SuggestedStakeholder(ParcelOwnerSegment segment)
    {
        if (!string.IsNullOrWhiteSpace(segment.BgtHolderName) && segment.BgtHolderName != "-")
        {
            return ShortReportCell(segment.BgtHolderName, 24);
        }

        return segment.BgtHolderCategory switch
        {
            "Gemeente" => "Gemeentelijk beheerder",
            "Waterschap" => "Waterschap / waterbeheer",
            "Provincie" => "Provinciale beheerder",
            "Rijk/landelijk" => "Landelijke beheerder",
            _ => "Handmatig bepalen"
        };
    }

    private static string SuggestedAction(ParcelOwnerSegment segment)
    {
        if (segment.BgtHolderCategory.Equals("Waterschap", StringComparison.OrdinalIgnoreCase))
        {
            return "Afstemming waterbeheer";
        }

        if (segment.BgtHolderCategory.Equals("Rijk/landelijk", StringComparison.OrdinalIgnoreCase))
        {
            return "Landelijke beheerder controleren";
        }

        return "Contact opnemen / toestemming controleren";
    }

    private static (string Level, string Reason) AssessParcelRisk(ParcelOwnerSegment segment)
    {
        var reasons = new List<string>();
        var score = 0;

        if (segment.Length >= 25)
        {
            score += 2;
            reasons.Add("lang traject door perceel");
        }
        else if (segment.Length >= 10)
        {
            score += 1;
            reasons.Add("middel-lang traject");
        }

        if (segment.BgtHolderCategory.Equals("Waterschap", StringComparison.OrdinalIgnoreCase) ||
            segment.BgtHolderCategory.Equals("Rijk/landelijk", StringComparison.OrdinalIgnoreCase))
        {
            score += 1;
            reasons.Add($"bronhouder {segment.BgtHolderCategory}");
        }

        if (segment.ZroStatus.Contains("Handmatig", StringComparison.OrdinalIgnoreCase) ||
            segment.ZroStatus.Contains("Onbekend", StringComparison.OrdinalIgnoreCase))
        {
            score += 1;
            reasons.Add("ZRO onbekend");
        }

        var level = score >= 3 ? "Hoog" : score >= 2 ? "Middel" : "Laag";
        return (level, reasons.Count == 0 ? "Geen bijzonderheden uit brondata" : string.Join(", ", reasons));
    }

    private static CadastralParcelPolygon? FindReportParcel(ParcelOwnerSegment segment, IReadOnlyList<CadastralParcelPolygon> parcelPolygons)
    {
        var objectId = NormalizeFeatureKey(segment.CadastralObjectId);
        return parcelPolygons
            .Where(parcel => NormalizeFeatureKey(parcel.CadastralObjectId).Equals(objectId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(parcel => parcel.Area)
            .FirstOrDefault()
            ?? parcelPolygons.FirstOrDefault(parcel =>
                parcel.CadastralMunicipality.Equals(segment.CadastralMunicipality, StringComparison.OrdinalIgnoreCase) &&
                parcel.Section.Equals(segment.Section, StringComparison.OrdinalIgnoreCase) &&
                parcel.ParcelNumber.Equals(segment.ParcelNumber, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ReportParcelIntersectsView(CadastralParcelPolygon parcel, double minX, double minY, double maxX, double maxY)
    {
        var xs = parcel.Ring.Select(point => point.X);
        var ys = parcel.Ring.Select(point => point.Y);
        return xs.Max() >= minX && xs.Min() <= maxX && ys.Max() >= minY && ys.Min() <= maxY;
    }

    private static bool IsValidRdPoint(RdPoint point) =>
        double.IsFinite(point.X) && double.IsFinite(point.Y) && point.X > 0 && point.Y > 0;

}
