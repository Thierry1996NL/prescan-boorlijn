using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Xml.Linq;
using Borevexa.App.Models;
using Borevexa.App.Reports.Blocks;
using Borevexa.App.Services;
using Borevexa.Cad;
using Borevexa.Core.Models;
using Borevexa.Core.Services;
using Borevexa.Geo;
using Docnet.Core;
using Docnet.Core.Converters;
using Docnet.Core.Models;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingImageFormat = System.Drawing.Imaging.ImageFormat;
using DrawingImageLockMode = System.Drawing.Imaging.ImageLockMode;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using DrawingRectangle = System.Drawing.Rectangle;
using NtsCoordinate = NetTopologySuite.Geometries.Coordinate;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;
using NtsGeometryFactory = NetTopologySuite.Geometries.GeometryFactory;
using NtsLineString = NetTopologySuite.Geometries.LineString;
using NtsLinearRing = NetTopologySuite.Geometries.LinearRing;
using NtsPolygon = NetTopologySuite.Geometries.Polygon;
using UglyToad.PdfPig;

namespace Borevexa.App;

// Rapportbouwers: visuele opbouw van rapportpagina's, -kaarten, -tabellen en
// -blokken voor de preview en de export (CreateReport*/CreateInline*/AddReport*).
// Fase 3-opsplitsing (07-07-2026) van MainWindow.xaml.cs; gedrag ongewijzigd.

public partial class MainWindow
{
    private static void AddReportCompactTableRow(Grid grid, IReadOnlyList<string> cells, bool header = false, double bodyFontSize = 8.4, double bodyLineHeight = 11)
    {
        var rowIndex = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = new Border
            {
                BorderBrush = Brush("#E5E7EB"),
                BorderThickness = new Thickness(0, 0, i == cells.Count - 1 ? 0 : 1, 1),
                Background = header ? Brush("#F8FAFB") : Brushes.White,
                Padding = new Thickness(5, 4, 5, 4),
                Child = new TextBlock
                {
                    Text = cells[i],
                    Foreground = header ? Brush("#587080") : Brush("#334155"),
                    FontSize = header ? Math.Max(9.2, bodyFontSize) : bodyFontSize,
                    FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = header ? Math.Max(12, bodyLineHeight) : bodyLineHeight
                }
            };
            Grid.SetRow(cell, rowIndex);
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }
    }

    private static void AddReportConclusionAndRestPoints(
        Panel parent,
        string conclusion,
        IEnumerable<(string Item, string Priority, string Action)> restPoints)
    {
        AddReportUiBlock(parent, CreateReportSubheading("Eindconclusie"));
        AddReportUiBlock(parent, CreateReportConclusionBlock(conclusion));
        AddReportUiBlock(parent, CreateReportSubheading("Restpuntenlijst"));
        AddReportUiBlock(parent, CreateReportRestPointsTable(restPoints));
    }

    private static void AddReportDimensionLine(Canvas canvas, double x1, double y, double x2, double y2, string label, bool labelAbove)
    {
        AddCanvasLine(canvas, x1, y, x2, y2, "#94A3B8", 1.0, null);
        AddCanvasLine(canvas, x1, y - 6, x1, y + 6, "#94A3B8", 1.0, null);
        AddCanvasLine(canvas, x2, y2 - 6, x2, y2 + 6, "#94A3B8", 1.0, null);
        var labelWidth = Math.Max(118, label.Length * 5.8);
        var labelX = (x1 + x2) / 2d - labelWidth / 2d;
        var labelY = labelAbove ? y - 24 : y + 10;
        AddCanvasRect(canvas, labelX - 5, labelY - 2, labelWidth + 10, 17, "#FFFFFF", "#FFFFFF", 0);
        AddCanvasText(canvas, label, labelX, labelY, "#475569", 9.5, FontWeights.SemiBold);
    }

    private static void AddReportMachineMarkers(Canvas canvas)
    {
        AddCanvasRect(canvas, 430, 112, 76, 34, "#F1F5F9", "#64748B", 2);
        AddCanvasText(canvas, "Machine", 442, 122, "#334155", 11, FontWeights.SemiBold);
        AddCanvasRect(canvas, 520, 126, 48, 32, "#EFF6FF", "#60A5FA", 2);
        AddCanvasText(canvas, "Bentoniet", 516, 164, "#2563EB", 10, FontWeights.Normal);
    }

    private static void AddReportMapLegend(Canvas canvas, ReportMapRecipe recipe, double left, double top, bool showTrace = true)
    {
        AddCanvasRect(canvas, left, top, 204, recipe.ShowRisk ? 104 : showTrace ? 82 : 62, "#FFFFFFE8", "#CBD5E1", 1);
        AddCanvasText(canvas, "Legenda", left + 10, top + 8, "#334155", 10.5, FontWeights.Bold);
        var y = top + 34;
        if (showTrace)
        {
            AddCanvasLine(canvas, left + 12, y, left + 44, y, "#E11D48", 4, null);
            AddCanvasText(canvas, "Boorlijn", left + 52, y - 8, "#334155", 10, FontWeights.Normal);
            y += 20;
        }
        AddCanvasLine(canvas, left + 12, y, left + 44, y, "#0B63CE", 1.5, null);
        AddCanvasText(canvas, "PDOK kaartcontext", left + 52, y - 8, "#334155", 10, FontWeights.Normal);
        if (recipe.ShowRisk)
        {
            y += 22;
            AddCanvasLine(canvas, left + 12, y, left + 44, y, "#DC2626", 5, null);
            AddCanvasText(canvas, "Hoog risico/aandacht", left + 52, y - 8, "#334155", 10, FontWeights.Normal);
        }
    }

    private void AddReportMapsForStep(StackPanel panel, int stepNumber, IReadOnlyList<TracePointRow> traceRows, IReadOnlyList<ProjectMapLayer> layers, ParcelOwnerAnalysis parcelAnalysis)
    {
        var recipes = BuildDefaultReportMapRecipesForStep(stepNumber, traceRows, layers, parcelAnalysis).ToList();
        if (recipes.Count == 0) return;

        panel.Children.Add(CreateReportSubheading("Standaard rapportkaarten"));
        panel.Children.Add(CreateReportNote("Deze kaarten worden automatisch opgebouwd uit de projectdata, met vaste laagsets, schaalinstellingen, legenda en bronvermelding. Een handmatig opgeslagen kaart blijft beschikbaar als bewuste rapportage-override."));
        foreach (var recipe in recipes)
        {
            panel.Children.Add(CreateReportMapRecipeCard(recipe, traceRows, layers, parcelAnalysis));
        }
    }

    private static void AddReportRiskMarkers(Canvas canvas, ParcelOwnerAnalysis analysis, Func<RdPoint, Point> project, double minX, double minY, double maxX, double maxY)
    {
        foreach (var segment in analysis.Segments.Where(segment => segment.Length >= 0.2).Take(80))
        {
            var points = segment.TracePath.Count >= 2
                ? segment.TracePath
                : BuildReportSegmentFocusPoints(segment, analysis.TraceRows, analysis.TraceLength);
            var valid = points.Where(IsValidRdPoint).Where(point => point.X >= minX && point.X <= maxX && point.Y >= minY && point.Y <= maxY).ToList();
            if (valid.Count == 0) continue;
            var projected = valid.Select(project).ToList();
            var color = AssessParcelRisk(segment).Level switch
            {
                "Hoog" => "#DC2626",
                "Middel" => "#F97316",
                _ => "#16A34A"
            };
            AddCanvasPolyline(canvas, "#FFFFFF", 10, projected.SelectMany(point => new[] { point.X, point.Y }).ToArray());
            AddCanvasPolyline(canvas, color, 6, projected.SelectMany(point => new[] { point.X, point.Y }).ToArray());
        }
    }

    private static void AddReportScaleBar(Canvas canvas, double pixelsPerMeter, double left, double top, double maxPixels)
    {
        if (!double.IsFinite(pixelsPerMeter) || pixelsPerMeter <= 0) return;
        var meters = NiceScaleBarMeters(maxPixels / pixelsPerMeter);
        var width = Math.Max(28, meters * pixelsPerMeter);
        AddCanvasRect(canvas, left - 6, top - 8, width + 12, 30, "#FFFFFF", "#CBD5E1", 1);
        AddCanvasLine(canvas, left, top + 8, left + width, top + 8, "#111827", 3, null);
        AddCanvasLine(canvas, left, top + 3, left, top + 13, "#111827", 1.5, null);
        AddCanvasLine(canvas, left + width, top + 3, left + width, top + 13, "#111827", 1.5, null);
        AddCanvasText(canvas, "0", left, top - 5, "#334155", 9, FontWeights.SemiBold);
        AddCanvasText(canvas, FormatScaleMeters(meters), left + width - 24, top - 5, "#334155", 9, FontWeights.SemiBold);
    }

    private void AddReportStepText(Panel parent, int stepNumber)
    {
        // Oude vrije rapporttekst wordt niet meer weergegeven; rapportage gebruikt alleen gestructureerde stapdata.
    }

    private static void AddReportTableRow(Border table, IReadOnlyList<string> cells, bool header = false)
    {
        if (table.Child is Grid grid) AddReportTableRow(grid, cells, header);
    }

    private static void AddReportTableRow(Grid grid, IReadOnlyList<string> cells, bool header = false)
    {
        var rowIndex = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = new Border
            {
                BorderBrush = Brush("#DDE5EC"),
                BorderThickness = new Thickness(0, 0, i == cells.Count - 1 ? 0 : 0.6, 0.6),
                Background = header ? Brush("#F6F8FA") : Brushes.White,
                Padding = new Thickness(6, header ? 4 : 3.5, 6, header ? 4 : 3.5),
                Child = new TextBlock
                {
                    Text = cells[i],
                    Foreground = header ? Brush("#334155") : Brush("#333333"),
                    FontSize = header ? 8.2 : 8.7,
                    FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal,
                    TextWrapping = TextWrapping.Wrap
                }
            };
            Grid.SetRow(cell, rowIndex);
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }
    }

    private static void AddReportTrace(Canvas canvas, IReadOnlyList<TracePointRow> traceRows, bool showPoints, Func<RdPoint, Point>? project = null, bool smooth = false)
    {
        if (traceRows.Count < 2)
        {
            AddCanvasPolyline(canvas, "#2457D6", 4, [350, 166, 420, 112]);
            if (showPoints)
            {
                AddCanvasCircle(canvas, 350, 166, 7, "#16A34A", "White", 2);
                AddCanvasCircle(canvas, 420, 112, 7, "#DC2626", "White", 2);
            }
            return;
        }

        if (project is not null)
        {
            var projected = traceRows.Select(point => project(new RdPoint(point.X, point.Y))).ToList();
            var line = smooth && projected.Count >= 3 ? SmoothReportTrace(projected) : projected;
            AddCanvasPolyline(canvas, "#E11D48", 4, line.SelectMany(point => new[] { point.X, point.Y }).ToArray());
            if (!showPoints) return;
            for (var i = 0; i < projected.Count; i++)
            {
                var x = projected[i].X;
                var y = projected[i].Y;
                var isStart = i == 0;
                var isEnd = i == projected.Count - 1;
                AddCanvasCircle(canvas, x, y, 7, isStart ? "#16A34A" : isEnd ? "#DC2626" : "White", isStart || isEnd ? "White" : "#E11D48", 2);
                if (isStart) AddCanvasText(canvas, "Start", x + 10, y - 8, "#166534", 11, FontWeights.Bold);
                if (isEnd) AddCanvasText(canvas, "Einde", x + 10, y - 8, "#991B1B", 11, FontWeights.Bold);
            }
            return;
        }

        var minX = traceRows.Min(point => point.X);
        var maxX = traceRows.Max(point => point.X);
        var minY = traceRows.Min(point => point.Y);
        var maxY = traceRows.Max(point => point.Y);
        var spanX = Math.Max(1, maxX - minX);
        var spanY = Math.Max(1, maxY - minY);
        var scale = Math.Min(620 / spanX, 150 / spanY);
        var offsetX = 70 + (620 - spanX * scale) / 2;
        var offsetY = 42 + (150 - spanY * scale) / 2;

        var projectedTrace = new List<Point>();
        foreach (var point in traceRows)
        {
            projectedTrace.Add(new Point(offsetX + (point.X - minX) * scale, offsetY + 150 - (point.Y - minY) * scale));
        }
        var lineTrace = smooth && projectedTrace.Count >= 3 ? SmoothReportTrace(projectedTrace) : projectedTrace;
        AddCanvasPolyline(canvas, "#2457D6", 4, lineTrace.SelectMany(point => new[] { point.X, point.Y }).ToArray());

        if (!showPoints) return;
        for (var i = 0; i < traceRows.Count; i++)
        {
            var x = projectedTrace[i].X;
            var y = projectedTrace[i].Y;
            var isStart = i == 0;
            var isEnd = i == traceRows.Count - 1;
            AddCanvasCircle(canvas, x, y, 7, isStart ? "#16A34A" : isEnd ? "#DC2626" : "White", isStart || isEnd ? "White" : "#2457D6", 2);
            if (isStart) AddCanvasText(canvas, "Start", x + 10, y - 8, "#166534", 11, FontWeights.Bold);
            if (isEnd) AddCanvasText(canvas, "Einde", x + 10, y - 8, "#991B1B", 11, FontWeights.Bold);
        }
    }

    private static void AddReportUiBlock(Panel parent, UIElement block)
    {
        if (parent is Grid grid)
        {
            if (grid.RowDefinitions.Count == 0)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(block, grid.RowDefinitions.Count - 1);
            Grid.SetColumn(block, 0);
            Grid.SetColumnSpan(block, Math.Max(1, grid.ColumnDefinitions.Count));
        }

        parent.Children.Add(block);
    }

    private UIElement CreateInlineBgtAnalysisMapReportPage(int stepNumber, PrescanSubstep substep)
    {
        EnsureProfilePoints();
        var total = GetSurfaceAnalysisTraceLength();
        var segments = GetBgtSurfaceSegments(total);
        var measuredLength = segments.Sum(segment => Math.Max(0, segment.Length));
        var mapPath = GetLiveMapReportPreviewImagePath(4);

        var panel = new StackPanel();
        panel.Children.Add(CreateReportSubheading("BGT analysekaart"));
        panel.Children.Add(CreateLiveMapReportImageCard(
            "BGT analysekaart met boorlijn",
            "Vastgezette rapportuitsnede met BGT-vlakken, boorlijn en zichtbare kaartlagen.",
            mapPath,
            4,
            null));
        panel.Children.Add(CreateReportKeyValues(
            ("Boorlijnlengte", $"{total:N1} m"),
            ("BGT-segmenten", segments.Count.ToString(CultureInfo.InvariantCulture)),
            ("Gemeten oppervlaklengte", measuredLength > 0 ? $"{measuredLength:N1} m" : "-"),
            ("Kaartstatus", string.IsNullOrWhiteSpace(mapPath) ? "Nog geen kaartcapture" : "Opgeslagen voor rapportage")));
        panel.Children.Add(CreateReportNote(string.IsNullOrWhiteSpace(mapPath)
            ? "Zet de kaart in stap 4 vast om de BGT-analysekaart in de rapportage te vullen."
            : "Deze kaart hoort bij substap 4.1 en gebruikt de actuele BGT-analyse, zichtbare boorlijn en opgeslagen kaartuitsnede."));

        var sectionTitle = $"{DisplayReportSectionTitle(substep)} - BGT kaart";
        return CreateReportPage(stepNumber, sectionTitle, CreateReportSection(stepNumber, sectionTitle, panel));
    }

    private UIElement CreateInlineBoreTraceMapReportPage(int stepNumber, PrescanSubstep substep, JsonElement data)
    {
        var panel = new StackPanel();
        var bagMapPath = GetLiveMapReportPreviewImagePath(3, StepThreeReportMapBagVariant);
        var photoMapPath = GetLiveMapReportPreviewImagePath(3, StepThreeReportMapPhotoVariant);
        var fallbackMapPath = GetLiveMapReportPreviewImagePath(3);
        if (string.IsNullOrWhiteSpace(bagMapPath)) bagMapPath = fallbackMapPath;

        panel.Children.Add(CreateLiveMapReportImageCard(
            "Boorlijn met PDOK BAG/kaartachtergrond",
            "Vaste rapportuitsnede met boorlijn en kaartcontext.",
            bagMapPath,
            3,
            StepThreeReportMapBagVariant));
        panel.Children.Add(CreateLiveMapReportImageCard(
            "Boorlijn met PDOK luchtfoto",
            "Vaste rapportuitsnede met boorlijn en luchtfoto-context.",
            photoMapPath,
            3,
            StepThreeReportMapPhotoVariant));
        panel.Children.Add(CreateReportNote(string.IsNullOrWhiteSpace(bagMapPath) && string.IsNullOrWhiteSpace(photoMapPath)
            ? "De kaartbijlage wordt gevuld zodra de kaart voor rapportage is vastgezet."
            : "Kaartbijlage van de live kaart uit processtap 3.1. Bedieningsknoppen en tekengereedschap worden bij de capture verborgen."));

        var sectionTitle = $"{DisplayReportSectionTitle(substep)} - kaartbijlage";
        return CreateReportPage(stepNumber, sectionTitle, CreateReportSection(stepNumber, sectionTitle, panel));
    }

    private UIElement CreateInlineEnvironmentParcelSegmentPage(int stepNumber, PrescanSubstep substep)
    {
        var analysis = BuildParcelOwnerAnalysis();
        var panel = new StackPanel();
        panel.Children.Add(CreateReportSubheading("Segmenten per perceel"));
        panel.Children.Add(CreateReportParcelSegmentSummaryTable(analysis.Segments));
        panel.Children.Add(CreateReportNote(analysis.Segments.Count == 0
            ? "Geen perceelsegmenten gevonden. Controleer of de boorlijn is opgeslagen en of Kadaster/BAG-perceelvlakken zijn geimporteerd."
            : "Deze segmentstaat bevat de perceelstukken waar de boorlijn doorheen loopt. De kaartpagina's hierna tonen per perceel de perceelgrenzen en alleen het betreffende boorlijnsegment in oranje."));

        var sectionTitle = $"{DisplayReportSectionTitle(substep)} - segmenten per perceel";
        return CreateReportPage(stepNumber, sectionTitle, CreateReportSection(stepNumber, sectionTitle, panel));
    }

    private UIElement CreateInlineKlicCrossingDetailReportPage(int stepNumber, PrescanSubstep substep)
    {
        var crossings = GetCurrentKlicPlanCrossings();
        var panel = new StackPanel();
        panel.Children.Add(CreateReportSubheading("KLIC kruisingen - detailstaat"));
        panel.Children.Add(CreateReportKlicCrossingDetailTable(crossings));
        panel.Children.Add(CreateReportNote(crossings.Count == 0
            ? "Geen snijdende KLIC-leidingen gevonden. Alleen geometrieen die de vastgelegde boorlijn echt kruisen worden in deze detailstaat opgenomen."
            : "Deze detailstaat bevat alleen de KLIC-geometrieen die de boorlijn snijden. Niet-kruisende leidingen uit hetzelfde thema of dezelfde levering worden bewust niet opgenomen."));

        var sectionTitle = $"{DisplayReportSectionTitle(substep)} - detailstaat";
        return CreateReportPage(stepNumber, sectionTitle, CreateReportSection(stepNumber, sectionTitle, panel));
    }

    private UIElement CreateInlineKlicMapReportPage(int stepNumber, PrescanSubstep substep, JsonElement data)
    {
        var panel = new StackPanel();
        var bagMapPath = GetLiveMapReportPreviewImagePath(3, StepThreeKlicReportMapBagVariant);
        var photoMapPath = GetLiveMapReportPreviewImagePath(3, StepThreeKlicReportMapPhotoVariant);
        var fallbackMapPath = GetLiveMapReportPreviewImagePath(3);
        if (string.IsNullOrWhiteSpace(bagMapPath)) bagMapPath = fallbackMapPath;

        panel.Children.Add(CreateLiveMapReportImageCard(
            "KLIC kruisingen met PDOK kaart/BAG-context",
            "Vaste rapportuitsnede met boorlijn, KLIC-leidingen en bufferzone.",
            bagMapPath,
            3,
            StepThreeKlicReportMapBagVariant));
        panel.Children.Add(CreateLiveMapReportImageCard(
            "KLIC kruisingen met PDOK luchtfoto",
            "Vaste rapportuitsnede met KLIC-leidingen, bufferzone en luchtfoto-context.",
            photoMapPath,
            3,
            StepThreeKlicReportMapPhotoVariant));
        panel.Children.Add(CreateReportNote(string.IsNullOrWhiteSpace(bagMapPath) && string.IsNullOrWhiteSpace(photoMapPath)
            ? "De KLIC-kaartbijlage wordt gevuld zodra de kaart voor rapportage is vastgezet."
            : "Kaartbijlage van de live kaart uit processtap 3.2. De boorlijn is alleen-lezen; KLIC-lagen en bufferzone worden meegenomen in de capture."));

        var sectionTitle = $"{DisplayReportSectionTitle(substep)} - KLIC kaartbijlage";
        return CreateReportPage(stepNumber, sectionTitle, CreateReportSection(stepNumber, sectionTitle, panel));
    }

    private Border CreateInlineReportMapFromState(ReportMapRecipe recipe, IReadOnlyList<TracePointRow> traceRows, IReadOnlyList<ProjectMapLayer> layers, ParcelOwnerAnalysis parcelAnalysis, ReportMapState mapState)
    {
        var recipeLayers = layers.Where(layer => ReportRecipeIncludesLayer(recipe, layer)).ToList();
        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = recipe.Title,
            Foreground = Brush("#071422"),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6)
        });
        content.Children.Add(CreateReportMapControlCanvas(recipe, traceRows, recipeLayers, parcelAnalysis, mapState));
        return new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("#D7E8FA"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 8, 0, 0),
            Child = content
        };
    }

    private IReadOnlyList<UIElement> CreateInlineReportStartSubstepPages(PrescanSubstep substep)
    {
        var context = BuildReportStartContext();
        return substep.Number switch
        {
            "0.1" => [CreateReportCoverPage(1, context.DocumentCount, context.LayerCount, context.ParcelCount, context.TraceLength, context.ReportLocation)],
            "0.2" => [CreateReportForewordPage(2, context.DocumentCount, context.ParcelCount, context.TraceLength)],
            "0.3" => [CreateReportContentsPage(3, context.ContentsEntries)],
            _ => [CreateInlineSubstepReportPage(0, substep, default)]
        };
    }

    private UIElement CreateInlineStepReportPage(int stepNumber, JsonElement root)
    {
        var workspaceTitle = _workspaces.TryGetValue(stepNumber, out var workspace)
            ? workspace.Title
            : $"Stap {DisplayStepNumber(stepNumber)}";
        var panel = new StackPanel();
        if (root.ValueKind == JsonValueKind.Object)
        {
            panel.Children.Add(CreateReportKeyValues(
                ("Stap", DisplayStepNumber(stepNumber)),
                ("Titel", workspaceTitle),
                ("Status", JsonText(root, "status", GetStepCompletenessText(stepNumber))),
                ("Substappen", BuildStepSubstepStatusText(stepNumber))));
            panel.Children.Add(CreateReportSubheading("Rapportsecties"));
            panel.Children.Add(CreateReportSubstepOverviewTable(root, stepNumber));
        }
        else
        {
            panel.Children.Add(CreateReportKeyValues(
                ("Stap", DisplayStepNumber(stepNumber)),
                ("Titel", workspaceTitle),
                ("Status", "Nog geen rapportdata"),
                ("Substappen", BuildStepSubstepStatusText(stepNumber))));
            panel.Children.Add(CreateReportNote("Sla het project op of genereer rapportdata opnieuw om dit onderdeel in de eindrapportage te vullen."));
        }

        return CreateReportPage(stepNumber, $"Stap {DisplayStepNumber(stepNumber)} - {workspaceTitle}", CreateReportSection(stepNumber, workspaceTitle, panel));
    }

    private UIElement CreateInlineSubstepReportPage(
        int stepNumber,
        PrescanSubstep substep,
        JsonElement root,
        bool includeSubstepMedia = true,
        bool includeChapterIntro = false)
    {
        var panel = new StackPanel();
        var substepTitle = DisplayReportSectionTitle(substep);
        var sectionTitle = includeChapterIntro
            ? $"{DisplayStepNumber(stepNumber)} {GetReportStepTitle(stepNumber)}"
            : substepTitle;
        var quality = _selectedProject is null
            ? ReportQualitySummary.FromIssues([])
            : _reportQuality.EvaluateSubstep(_selectedProject.Id, stepNumber, substep.Number);
        if (IsChapterIntroductionSubstep(substep))
        {
            if (_workspaces.TryGetValue(stepNumber, out var workspace) && !string.IsNullOrWhiteSpace(workspace.Subtitle))
            {
                panel.Children.Add(CreateReportChapterIntro(workspace.Subtitle));
            }

            panel.Children.Add(CreateSubstepIntroductionReportBlock(stepNumber, substep, root));
            return CreateReportPage(stepNumber, sectionTitle, CreateReportSection(stepNumber, sectionTitle, panel));
        }

        if (includeChapterIntro)
        {
            if (_workspaces.TryGetValue(stepNumber, out var workspace) && !string.IsNullOrWhiteSpace(workspace.Subtitle))
            {
                panel.Children.Add(CreateReportChapterIntro(workspace.Subtitle));
            }

            panel.Children.Add(CreateReportSubstepHeading(substep));
        }

        if (TryGetSubstepElement(root, substep.Number, out var substepElement))
        {
            if (ShouldShowChapterTextSections(stepNumber, substep, includeChapterIntro))
            {
                panel.Children.Add(CreateSubstepIntroductionReportBlock(stepNumber, substep, root));
            }
            panel.Children.Add(CreateReportSubheading("Inhoud"));
            var data = JsonProperty(substepElement, "data") ?? default;
            panel.Children.Add(CreateReadableSubstepReportContent(substep.Number, data, includeSubstepMedia));
        }
        else
        {
            if (ShouldShowChapterTextSections(stepNumber, substep, includeChapterIntro))
            {
                panel.Children.Add(CreateSubstepIntroductionReportBlock(stepNumber, substep, root));
            }
            panel.Children.Add(CreateReportNote("Sla het project op om deze substap in de eindrapportage te vullen."));
        }

        return CreateReportPage(stepNumber, sectionTitle, CreateReportSection(stepNumber, sectionTitle, panel));
    }

    private IReadOnlyList<UIElement> CreateInlineSubstepReportPages(int stepNumber, PrescanSubstep substep, JsonElement root)
    {
        return CreateInlineSubstepReportPagesCore(stepNumber, substep, root, expandFinalConclusionPreview: true);
    }

    private IReadOnlyList<UIElement> CreateInlineSubstepReportPagesCore(
        int stepNumber,
        PrescanSubstep substep,
        JsonElement root,
        bool expandFinalConclusionPreview,
        bool includeChapterIntro = false)
    {
        // Report pages are built by looping over every substep of a step (see
        // CreateStepReportPreviewPages / CreateCustomerFinalReportPreviewPages), but
        // every map-state helper this method calls into (GetLiveMapReportPreviewImagePath,
        // GetReportLockJson, GetCurrentMapWorkspaceRuntime, ...) reads the *live*
        // _selectedStep/_selectedSubstep fields, not the substep whose page is currently
        // being generated. Without this temporary override, every substep's report page
        // resolves to whichever substep the user currently has open in the live app —
        // the "identical/duplicate map images across substeps" bug. Restore the real
        // selection afterwards so building a report has no visible effect on the live UI.
        var previousSelectedStep = _selectedStep;
        var previousSelectedSubstep = _selectedSubstep;
        if (_selectedProject is not null)
        {
            _selectedStep = _selectedProject.Steps.FirstOrDefault(step => step.Number == stepNumber) ?? _selectedStep;
            _selectedSubstep = substep;
        }

        try
        {
            return CreateInlineSubstepReportPagesCoreForSelectedSubstep(stepNumber, substep, root, expandFinalConclusionPreview, includeChapterIntro);
        }
        finally
        {
            _selectedStep = previousSelectedStep;
            _selectedSubstep = previousSelectedSubstep;
        }
    }

    private IReadOnlyList<UIElement> CreateInlineSubstepReportPagesCoreForSelectedSubstep(
        int stepNumber,
        PrescanSubstep substep,
        JsonElement root,
        bool expandFinalConclusionPreview,
        bool includeChapterIntro = false)
    {
        if (stepNumber == 0)
        {
            return CreateInlineReportStartSubstepPages(substep);
        }

        if (IsChapterIntroductionSubstep(substep))
        {
            return
            [
                CreateInlineSubstepReportPage(
                    stepNumber,
                    substep,
                    root,
                    includeSubstepMedia: false,
                    includeChapterIntro: true)
            ];
        }

        if (expandFinalConclusionPreview && IsFinalConclusionReportSubstep(stepNumber, substep.Number))
        {
            return CreateCustomerFinalReportPreviewPages();
        }

        if (stepNumber == 6 &&
            (string.Equals(substep.Number, "6.1", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(substep.Number, "6.2", StringComparison.OrdinalIgnoreCase)))
        {
            return CreateInlineBroProfileSourceReportPages(stepNumber, substep, root);
        }

        // Substep 4.3 gets its own dedicated kaart+grafiek pagina
        // (CreateInlineAhnSurfaceProfileReportPage) that already contains every figure
        // (boorlijnlengte, profielpunten, maaiveld min/max) plus the actual profile
        // chart. The generic "Inhoud" text page duplicates those same figures from a
        // separate (often stale, e.g. "0 profielpunten") data snapshot, so it is skipped
        // here rather than shown twice with conflicting numbers.
        if (IsAhnSurfaceProfileReportSubstep(stepNumber, substep.Number))
        {
            return [CreateInlineAhnSurfaceProfileReportPage(stepNumber, substep)];
        }

        // Zelfde reden als bij substep 4.3: de generieke "Inhoud"-pagina toont alleen
        // een kale profielpuntentabel op een verder lege pagina, terwijl
        // CreateInlineProfileEngineeringReportPages() de kaart, grafiek en tabellen al
        // compleet opbouwt (en de boorpuntentabel nu zelfs samen met de kaart op
        // dezelfde pagina zet).
        if (IsProfileReportSubstep(stepNumber, substep.Number))
        {
            return CreateInlineProfileEngineeringReportPages(stepNumber, substep);
        }

        var pages = new List<UIElement>
        {
            CreateInlineSubstepReportPage(
                stepNumber,
                substep,
                root,
                includeSubstepMedia: !IsBoreTraceReportMapSubstep(stepNumber, substep.Number) &&
                                     !IsKlicReportMapSubstep(stepNumber, substep.Number),
                includeChapterIntro: includeChapterIntro)
        };

        if (TryGetSubstepElement(root, substep.Number, out var substepElement))
        {
            var data = JsonProperty(substepElement, "data") ?? default;
            if (IsBoreTraceReportMapSubstep(stepNumber, substep.Number))
            {
                pages.Add(CreateInlineBoreTraceMapReportPage(stepNumber, substep, data));
            }
            else if (IsKlicReportMapSubstep(stepNumber, substep.Number))
            {
                pages.Add(CreateInlineKlicMapReportPage(stepNumber, substep, data));
                pages.Add(CreateInlineKlicCrossingDetailReportPage(stepNumber, substep));
            }
            else if (IsSubsurfaceMapReportSubstep(stepNumber, substep.Number))
            {
                pages.Add(CreateInlineSubsurfaceMapReportPage(stepNumber, substep, data));
                pages.AddRange(CreateInlineSubsurfaceLegendReportPages(stepNumber, substep));
            }
        }

        if (IsEnvironmentParcelReportSubstep(stepNumber, substep.Number))
        {
            pages.Add(CreateInlineEnvironmentParcelSegmentPage(stepNumber, substep));
            pages.AddRange(CreateReportParcelOwnerMapPages(BuildParcelOwnerAnalysis(), stepNumber));
        }

        return pages;
    }

    private static Canvas CreateReportBoringCanvas(BoringResult boring)
    {
        const double canvasWidth = 660;
        const double legendLeft = 404;
        const double legendWidth = 248;
        var canvas = new Canvas { Width = canvasWidth, Height = 460, Background = Brushes.White, Margin = new Thickness(0, 0, 0, 10) };
        const double cx = 214;
        const double cy = 188;
        const double boreRadius = 86;
        var scale = boring.BoringDiameter > 0 ? boreRadius / (boring.BoringDiameter / 2d) : 0.25d;

        AddBoringCanvasDimensions(canvas, boring, cx, cy, boreRadius, scale);
        AddCanvasCircle(canvas, cx, cy, boreRadius + 22, "#C4A45A", "#C4A45A", 1);
        for (var i = 0; i < 28; i++)
        {
            var angle = i * 137.5 * Math.PI / 180d;
            var distance = boreRadius + 13 + i % 5 * 1.6;
            AddCanvasCircle(canvas, cx + distance * Math.Cos(angle), cy + distance * Math.Sin(angle), 1.2, "#A0803A", "#A0803A", 1);
        }
        AddCanvasCircle(canvas, cx, cy, boreRadius, "#C2D6DF", "#7AAFC4", 1.5);
        AddCanvasLine(canvas, cx, cy - boreRadius + 8, cx, cy + boreRadius - 8, "#7AAFC4", 0.5, [3, 4]);
        AddCanvasLine(canvas, cx - boreRadius + 8, cy, cx + boreRadius - 8, cy, "#7AAFC4", 0.5, [3, 4]);

        if (boring.Processed.Count == 0)
        {
            AddCanvasText(canvas, "Geen boringconfiguratie", cx - 66, cy - 8, "#8FA6B2", 12, FontWeights.SemiBold);
            AddCanvasText(canvas, "Sla stap 1.2/1.3 opnieuw op", cx - 76, cy + 12, "#94A3B8", 10, FontWeights.Normal);
        }
        else
        {
            var positions = GravityPack(boring.Processed.Select(item => Math.Max((item.EffectiveOutsideDiameter / 2d) * scale, 4)).ToArray(), boreRadius);
            for (var i = 0; i < boring.Processed.Count; i++)
            {
                var item = boring.Processed[i];
                var position = positions[i];
                var x = cx + position.X;
                var y = cy + position.Y;
                var radius = Math.Max((item.EffectiveOutsideDiameter / 2d) * scale, 4);

                if (item.Item.Type == BoringItemType.Mantelbuis)
                {
                    var pe = PeSizes.First(size => size.Dn == item.Item.Dn);
                    var tubeColor = PeTubeColor(item.Item);
                    AddCanvasCircle(canvas, x, y, radius, tubeColor, tubeColor, 1);
                    AddCanvasCircle(canvas, x, y, Math.Max(radius - pe.Wall * scale, 2), "#EEF6F8", "#EEF6F8", 1);
                    var innerRadius = Math.Max(radius - pe.Wall * scale, 2);
                    var contentPositions = GravityPack(item.Item.Contents.Select(content => Math.Max((content.OutsideDiameter / 2d) * scale, 2.5)).ToArray(), innerRadius);
                    for (var c = 0; c < item.Item.Contents.Count; c++)
                    {
                        var content = item.Item.Contents[c];
                        var contentPosition = contentPositions[c];
                        AddCanvasCircle(canvas, x + contentPosition.X, y + contentPosition.Y, Math.Max((content.OutsideDiameter / 2d) * scale, 2.5), content.Color, content.Color, 1);
                    }
                }
                else
                {
                    AddCanvasCircle(canvas, x, y, radius, item.Color, item.Color, 1);
                }
            }
        }
        var legendBottom = AddBoringCanvasLegend(canvas, boring, legendLeft, 54, legendWidth);
        AddBoringCanvasDimensionTable(canvas, boring, legendLeft, legendBottom + 14, legendWidth);
        AddCanvasText(canvas, "Dwarsdoorsnede boring", cx - 56, 420, "#587080", 11, FontWeights.Normal);
        return canvas;
    }

    private static Border CreateReportBoringItemTable(IReadOnlyList<ProcessedBoringItem> items)
    {
        var table = CreateReportTable(["Onderdeel", "Type", "Diameter", "Vulling", "Inhoud / status"]);
        foreach (var item in items.Take(12))
        {
            var contents = item.Item.Contents.Count == 0
                ? (item.Fits ? "past" : "past niet")
                : string.Join(", ", item.Item.Contents.Select(content => $"{content.Label} Ø{content.OutsideDiameter:N0} mm"));
            AddReportTableRow(table, [
                item.Item.Label,
                FormatBoringItemType(item.Item),
                $"Ø{item.EffectiveOutsideDiameter:N0} mm",
                item.Item.Type == BoringItemType.Mantelbuis ? $"{item.FillPercentage:N0}%" : "-",
                contents
            ]);
        }
        if (items.Count == 0) AddReportTableRow(table, ["-", "Geen boringinhoud", "-", "-", "-"]);
        return table;
    }

    private static UIElement CreateReportChapterIntro(string text) => new TextBlock
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = Brush("#333333"),
        FontSize = 10.2,
        LineHeight = 14.2,
        Margin = new Thickness(0, 0, 0, 14)
    };

    private static Border CreateReportConclusionBlock(string conclusion)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "- " + conclusion,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#334155"),
            FontSize = 12
        });

        return new Border
        {
            Background = Brushes.Transparent,
            BorderBrush = Brush("#D7E8FA"),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(12, 2, 0, 2),
            Margin = new Thickness(0, 0, 0, 0),
            Child = panel
        };
    }

    private Border CreateReportContentsPage(int pageNumber, IReadOnlyList<ReportContentsEntry> contents)
    {
        var settings = ReadReportStartSettings();
        var panel = new StackPanel();
        if (!string.IsNullOrWhiteSpace(settings.ContentsIntro))
        {
            panel.Children.Add(new TextBlock
            {
                Text = settings.ContentsIntro,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("#334155"),
                FontSize = 12,
                LineHeight = 18,
                Margin = new Thickness(0, 0, 0, 12)
            });
        }
        panel.Children.Add(CreateReportContentsTable(contents));
        var title = FirstNonEmpty(settings.ContentsTitle, "Inhoudsopgave");
        return CreateReportPage(pageNumber, title, CreateReportSection(0, title, panel));
    }

    private static Border CreateReportContentsTable(IReadOnlyList<ReportContentsEntry> contents)
    {
        var list = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        foreach (var entry in contents)
        {
            var displayTitle = FormatReportContentsTitle(entry.Title);
            var isChapter = Regex.IsMatch(displayTitle, @"^\d+\s+\S+", RegexOptions.IgnoreCase) &&
                            !Regex.IsMatch(displayTitle, @"^\d+\.\d+\b");
            var isSubstep = Regex.IsMatch(displayTitle, @"^\d+\.\d+\b");
            var isAppendix = entry.Title.Contains("kaartbijlage", StringComparison.OrdinalIgnoreCase);
            var levelIndent = isAppendix ? 30 : isSubstep ? 18 : 0;
            var fontSize = isAppendix ? 8.0 : isSubstep ? 8.4 : 9.0;
            var lineHeight = isAppendix ? 10.2 : isSubstep ? 10.8 : 11.6;
            var weight = isChapter ? FontWeights.SemiBold : FontWeights.Normal;

            var row = new Grid
            {
                Margin = new Thickness(0, 0, 0, isChapter ? 5 : 3)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new TextBlock
            {
                Text = displayTitle,
                Foreground = Brush("#242424"),
                FontSize = fontSize,
                FontWeight = weight,
                LineHeight = lineHeight,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(levelIndent, 0, 8, 0),
                MaxWidth = Math.Max(240, 500 - levelIndent),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(title, 0);
            row.Children.Add(title);

            var leader = new TextBlock
            {
                Text = new string('.', 180),
                FontFamily = new FontFamily("Consolas"),
                FontSize = fontSize,
                FontWeight = FontWeights.Normal,
                Foreground = Brush(isChapter ? "#444444" : "#777777"),
                LineHeight = lineHeight,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.None,
                ClipToBounds = true,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(leader, 1);
            row.Children.Add(leader);

            var page = new TextBlock
            {
                Text = entry.Page,
                Foreground = Brush("#242424"),
                FontSize = fontSize,
                FontWeight = weight,
                LineHeight = lineHeight,
                TextAlignment = TextAlignment.Right,
                HorizontalAlignment = HorizontalAlignment.Right,
                MinWidth = 34,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(page, 2);
            row.Children.Add(page);
            list.Children.Add(row);
        }

        return new Border
        {
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 2, 0, 0),
            Child = list
        };
    }

    private Border CreateReportCoverPage(int pageNumber, int documentCount, int layerCount, int parcelSegmentCount, double traceLength, string reportLocation)
    {
        var settings = ReadReportStartSettings();
        var metadata = ReadProjectHeaderMetadata();
        var title = FirstNonEmpty(settings.CoverTitle, DefaultCoverTitle);
        var subtitle = FirstNonEmpty(settings.CoverSubtitle, "HDD Horizontaal Gestuurd Boren");
        var root = new Grid { Background = Brushes.White };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var panel = new StackPanel { Margin = new Thickness(58, 148, 58, 48) };
        panel.Children.Add(new TextBlock { Text = title, Foreground = Brush("#071422"), FontSize = 30, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = subtitle, Foreground = Brush("#587080"), FontSize = 15, Margin = new Thickness(0, 8, 0, 42) });
        var rows = new List<(string Label, string Value)>
        {
            ("Project", GetReportProjectDisplayName()),
            ("Locatie", reportLocation),
            ("Projectnummer intern", FirstNonEmpty(metadata.InternalProjectNumber, "-")),
            ("Projectnummer extern", FirstNonEmpty(metadata.ExternalProjectNumber, "-")),
            ("Opdrachtgever", _selectedProject?.Client ?? "-"),
            ("Status", _selectedProject?.Status ?? "-"),
            ("Boorlengte", $"{traceLength:N1} m"),
            ("Gegenereerd", DateTime.Now.ToString("dd-MM-yyyy HH:mm", CultureInfo.CurrentCulture))
        };
        if (!string.IsNullOrWhiteSpace(settings.CoverRevision))
        {
            rows.Add(("Rapportstatus", settings.CoverRevision));
        }
        panel.Children.Add(CreateReportKeyValues(rows.ToArray()));
        if (!string.IsNullOrWhiteSpace(settings.CoverNote))
        {
            panel.Children.Add(new TextBlock
            {
                Text = settings.CoverNote,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("#587080"),
                FontSize = 11.5,
                LineHeight = 17,
                Margin = new Thickness(0, 16, 0, 0)
            });
        }
        panel.Children.Add(CreateCoverBoreTraceMapBlock());
        Grid.SetRow(panel, 0);
        root.Children.Add(panel);

        var footer = new DockPanel { Margin = new Thickness(58, 0, 58, 42), LastChildFill = false };
        var pageNumberText = new TextBlock
        {
            Text = FormatReportPageNumber(pageNumber),
            Foreground = Brush("#94A3B8"),
            FontSize = 9.5,
            Width = 150,
            TextAlignment = TextAlignment.Left
        };
        DockPanel.SetDock(pageNumberText, Dock.Left);
        footer.Children.Add(pageNumberText);
        var tooling = new StackPanel
        {
            Width = 255,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        tooling.Children.Add(new TextBlock
        {
            Text = "Inpark Engineering since 1976",
            Foreground = Brush("#94A3B8"),
            FontSize = 9.5,
            TextAlignment = TextAlignment.Right,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            LineHeight = 13
        });
        tooling.Children.Add(new TextBlock
        {
            Text = "Borevexa HDD Prescan tooling",
            Foreground = Brush("#94A3B8"),
            FontSize = 9.5,
            Margin = new Thickness(0, 4, 0, 0),
            TextAlignment = TextAlignment.Right,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            LineHeight = 13
        });
        DockPanel.SetDock(tooling, Dock.Right);
        footer.Children.Add(tooling);
        Grid.SetRow(footer, 1);
        root.Children.Add(footer);

        return CreateReportPageBorder(pageNumber, root);
    }

    private static StackPanel CreateReportFileList(string title, IEnumerable<ProjectFileRecord> files)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), Foreground = Brush("#587080"), FontSize = 11, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });
        var rows = files.GroupBy(file => $"{file.FileType}|{file.DisplayName}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(file => file.CreatedAt).First())
            .Take(10)
            .ToList();
        if (rows.Count == 0)
        {
            panel.Children.Add(new TextBlock { Text = "Geen bestanden geladen.", Foreground = Brush("#8FA6B2"), FontStyle = FontStyles.Italic, FontSize = 12 });
            return panel;
        }
        foreach (var file in rows)
        {
            panel.Children.Add(new TextBlock { Text = $"{file.FileType}  {file.DisplayName}", Foreground = Brush("#334155"), FontSize = 12, Margin = new Thickness(0, 0, 0, 5) });
        }
        return panel;
    }

    private Border CreateReportForewordPage(int pageNumber, int documentCount, int parcelCount, double traceLength)
    {
        return CreateReportPage(pageNumber, "Voorwoord", CreateReportSection(0, "Voorwoord", CreateReportForewordPanel(documentCount, parcelCount, traceLength)));
    }

    private StackPanel CreateReportForewordPanel(int documentCount, int parcelCount, double traceLength)
    {
        var settings = ReadReportStartSettings();
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = FirstNonEmpty(settings.ForewordText, GenerateDefaultForewordText(documentCount, parcelCount, traceLength)),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#333333"),
            FontSize = 10.2,
            LineHeight = 14.2
        });
        var scope = FirstNonEmpty(settings.ForewordScope, GenerateDefaultForewordScope(documentCount, parcelCount, traceLength));
        if (!string.IsNullOrWhiteSpace(scope))
        {
            panel.Children.Add(CreateReportSubheading("Uitgangspunten"));
            panel.Children.Add(new TextBlock
            {
                Text = scope,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("#333333"),
                FontSize = 10.2,
                LineHeight = 14.2
            });
        }
        var summaryBlock = new StackPanel { Margin = new Thickness(0, 30, 0, 0) };
        var refreshNote = CreateReportNote("De inhoud wordt bij iedere nieuwe analyse of rapportgeneratie opnieuw opgebouwd op basis van de actuele projectdata.");
        refreshNote.Margin = new Thickness(0, 0, 0, 8);
        summaryBlock.Children.Add(refreshNote);
        var summaryRows = CreateReportKeyValues(
            ("Prescan", "Vooronderzoek horizontaal gestuurd boren"),
            ("Tracelengte", $"{traceLength:N1} m"),
            ("Documenten/bijlagen", documentCount.ToString(CultureInfo.InvariantCulture)),
            ("Gekruiste percelen", parcelCount.ToString(CultureInfo.InvariantCulture)),
            ("ZRO/eigendom", "Handmatige controle / aparte rechtenbron"));
        summaryRows.Margin = new Thickness(0, 0, 0, 0);
        summaryBlock.Children.Add(summaryRows);
        panel.Children.Add(summaryBlock);
        return panel;
    }

    private StackPanel CreateReportHeaderProjectInfoBlock()
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.Add(new TextBlock
        {
            Text = GetReportProjectDisplayName(),
            Foreground = Brush("#222222"),
            FontSize = 9.4,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Right,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.None,
            MaxWidth = 380
        });

        var metadata = ReadProjectHeaderMetadata();
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(metadata.InternalProjectNumber)) lines.Add($"Intern: {metadata.InternalProjectNumber}");
        if (!string.IsNullOrWhiteSpace(metadata.ExternalProjectNumber)) lines.Add($"Extern: {metadata.ExternalProjectNumber}");
        if (lines.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = string.Join("\n", lines),
                Foreground = Brush("#555555"),
                FontSize = 7.8,
                LineHeight = 10,
                TextAlignment = TextAlignment.Right,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0)
            });
        }

        return panel;
    }

    private static Border CreateReportImportedFilesTable(IEnumerable<ProjectFileRecord> files)
    {
        var table = CreateReportTable(["Bestand", "Type", "Duiding"]);
        var rows = files
            .GroupBy(file => $"{file.FileType}|{file.DisplayName}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(file => file.CreatedAt).First())
            .OrderBy(file => ImportedFileTypeOrder(file.FileType))
            .ThenBy(file => file.DisplayName)
            .ToList();
        foreach (var file in rows)
        {
            AddReportTableRow(table, [
                file.DisplayName,
                NormalizeImportedFileType(file.FileType),
                DescribeImportedFileType(file.FileType)
            ]);
        }
        if (rows.Count == 0) AddReportTableRow(table, ["Geen bestanden gekoppeld", "-", "-"]);
        return table;
    }

    private Border CreateReportIntroPage(int pageNumber, IReadOnlyList<ReportContentsEntry> contents, int documentCount, int parcelCount, double traceLength)
    {
        var panel = new StackPanel { Margin = new Thickness(28, 0, 28, 0) };
        panel.Children.Add(CreateReportSection(0, "Voorwoord", CreateReportForewordPanel(documentCount, parcelCount, traceLength)));
        panel.Children.Add(CreateReportSection(0, "Inhoudsopgave", CreateReportContentsTable(contents)));
        return CreateReportPage(pageNumber, "Voorwoord en inhoudsopgave", panel);
    }

    private static Grid CreateReportKeyValues(params (string Label, string Value)[] rows)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.28, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.72, GridUnitType.Star) });
        for (var i = 0; i < rows.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = rows[i].Label, Foreground = Brush("#555555"), FontSize = 10, Margin = new Thickness(0, 0, 10, 4) };
            var value = new TextBlock { Text = rows[i].Value, Foreground = Brush("#222222"), FontSize = 10, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4), TextAlignment = TextAlignment.Right };
            Grid.SetRow(label, i);
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 1);
            grid.Children.Add(label);
            grid.Children.Add(value);
        }
        return grid;
    }

    private Border CreateReportKlicContactPdfCard(IReadOnlyList<ProjectDocumentEntry> docs)
    {
        var doc = FindKlicContactPdf(docs);
        var panel = new StackPanel();
        if (doc is null)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Geen officiële KLIC-contactlijst PDF gevonden in de gekoppelde KLIC-levering.",
                Foreground = Brush("#8FA6B2"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
        }
        else
        {
            panel.Children.Add(new TextBlock
            {
                Text = "De officiële KLIC-contactlijst/netbeheerderslijst wordt als bron-PDF in de eindrapportage opgenomen. Deze weergave is betrouwbaarder dan een opnieuw opgebouwde tabel, omdat de originele KLIC-opmaak behouden blijft.",
                Foreground = Brush("#334155"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            panel.Children.Add(CreateReportKeyValues(
                ("Bronbestand", doc.Name),
                ("Herkomst", doc.ZipEntryName is null ? "Los PDF-bestand" : $"KLIC-ZIP: {doc.ZipEntryName}"),
                ("Grootte", $"{doc.SizeKb:N0} KB")));
            var button = new Button
            {
                Content = "Open officiële KLIC-contactlijst",
                Tag = doc,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 8, 0, 0),
                Padding = new Thickness(12, 6, 12, 6),
                Background = Brush("#3F4750"),
                Foreground = Brushes.White,
                BorderBrush = Brush("#3F4750"),
                FontWeight = FontWeights.SemiBold
            };
            button.Click += (_, _) => TryOpenDocument(doc);
            panel.Children.Add(button);
        }

        return new Border
        {
            BorderBrush = Brush("#E5EDF3"),
            BorderThickness = new Thickness(0, 1, 0, 1),
            Background = Brushes.Transparent,
            Padding = new Thickness(0, 10, 0, 10),
            Margin = new Thickness(0, 0, 0, 12),
            Child = panel
        };
    }

    private static Border CreateReportKlicContactTable(IReadOnlyList<KlicContactRow> contacts)
    {
        var table = CreateReportTable(["Code", "Netbeheerder", "Thema", "Contact", "Telefoon", "E-mail", "Schade/storing"]);
        foreach (var contact in contacts.Take(120))
        {
            AddReportTableRow(table, [
                contact.Code,
                contact.NetworkOperator,
                contact.Theme,
                contact.Contact,
                contact.Phone,
                contact.Email,
                contact.FaultPhone
            ]);
        }

        if (contacts.Count == 0)
        {
            AddReportTableRow(table, ["-", "Geen KLIC-contactlijst gevonden", "-", "-", "-", "-", "-"]);
        }

        return table;
    }

    private Border CreateReportKlicCrossingDetailTable(IReadOnlyList<KlicPlanCrossing> crossings)
    {
        var grid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        var weights = new[] { 0.34, 0.62, 1.05, 5.9 };
        foreach (var weight in weights)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(weight, GridUnitType.Star) });
        }

        AddReportCompactTableRow(grid, ["#", "Afstand", "Thema", "Leiding / kabel"], true, 10.5, 9.6);
        foreach (var crossing in crossings)
        {
            AddReportCompactTableRow(grid, [
                crossing.Code,
                $"{crossing.Distance:N2} m",
                DetailReportCell(crossing.ThemeLabel, 120),
                BuildKlicCrossingCableText(crossing)
            ], false, 10.3, 13.2);
        }

        if (crossings.Count == 0)
        {
            AddReportCompactTableRow(grid, ["-", "Geen echte snijpunten", "-", "-"], false, 10.3, 13.2);
        }

        return new Border
        {
            BorderBrush = Brush("#E5E7EB"),
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Child = grid
        };
    }

    private Border CreateReportKlicCrossingTable()
    {
        IReadOnlyList<KlicProfileCrossing> crossings = _profilePoints.Count >= 2 ? GetVisibleKlicProfileCrossings(_profilePoints[^1].Distance) : [];
        var table = CreateReportTable(["Code", "KLIC inhoud", "Afstand", "Diepte", "NAP", "Thema"]);
        foreach (var crossing in crossings.Select((value, index) => (value, index)))
        {
            AddReportTableRow(table, [
                $"K{crossing.index + 1}",
                crossing.value.Label,
                $"{crossing.value.Distance:N1} m",
                $"ca. {crossing.value.Depth:N1} m",
                $"{crossing.value.Nap:N2} m",
                crossing.value.Theme
            ]);
        }
        if (crossings.Count == 0) AddReportTableRow(table, ["-", "Geen KLIC-kruisingen", "-", "-", "-", "-"]);
        return table;
    }

    private Canvas CreateReportKlicPlanCanvas(IReadOnlyList<TracePointRow> traceRows, IReadOnlyList<ProjectMapLayer> layers, IReadOnlyList<KlicPlanCrossing> crossings)
    {
        const double width = 760;
        const double height = 260;
        const double left = 54;
        const double right = 24;
        const double centerY = 128;
        var canvas = new Canvas { Width = width, Height = height, Background = Brushes.White, Margin = new Thickness(0, 8, 0, 10), ClipToBounds = true };
        AddCanvasRect(canvas, 0, 0, width, height, "#F8FAFB", "#D7E8FA", 1);

        if (traceRows.Count < 2)
        {
            AddCanvasText(canvas, "Geen boorlijn beschikbaar voor KLIC-kruisingenanalyse.", 190, 118, "#8FA6B2", 12, FontWeights.Normal);
            return canvas;
        }

        var traceDistances = BuildTraceDistances(traceRows);
        var total = Math.Max(1, traceDistances[^1]);
        var xScale = (width - left - right) / total;
        var sourceLines = BuildKlicPlanLines(traceRows, layers);
        var maxOffset = Math.Max(8, sourceLines.SelectMany(line => line.Points).Select(point => Math.Abs(point.Offset)).Concat(crossings.Select(crossing => Math.Abs(crossing.Offset))).DefaultIfEmpty(8).Max());
        var yScale = 82 / maxOffset;
        double X(double station) => left + station * xScale;
        double Y(double offset) => centerY - offset * yScale;

        for (var meter = 0d; meter <= total + 0.001; meter += NiceScaleBarMeters(total / 5d))
        {
            var x = X(Math.Min(meter, total));
            AddCanvasLine(canvas, x, 34, x, height - 44, "#E5E7EB", 1, null);
            AddCanvasText(canvas, $"{meter:N0} m", x - 11, height - 35, "#64748B", 8.5, FontWeights.Normal);
        }
        AddCanvasLine(canvas, left, centerY, width - right, centerY, "#FFFFFF", 9, null);
        AddCanvasLine(canvas, left, centerY, width - right, centerY, "#E11D48", 5, null);
        AddCanvasText(canvas, "Boorlijn horizontaal uitgelegd", left, centerY - 24, "#991B1B", 10.5, FontWeights.SemiBold);

        foreach (var line in sourceLines.Take(1200))
        {
            var visible = line.Points.Any(point => point.Station >= -1 && point.Station <= total + 1 && Math.Abs(point.Offset) <= maxOffset + 3);
            if (!visible || line.Points.Count < 2) continue;
            var coords = line.Points.SelectMany(point => new[] { X(point.Station), Y(Math.Clamp(point.Offset, -maxOffset, maxOffset)) }).ToArray();
            AddCanvasPolyline(canvas, line.Color, 1.5, coords);
        }

        foreach (var crossing in crossings.Take(80))
        {
            var x = X(crossing.Distance);
            AddCanvasCircle(canvas, x, centerY, 5.5, crossing.Color, "#FFFFFF", 1.6);
            AddCanvasText(canvas, crossing.Code, x + 5, centerY - 17, "#334155", 8.5, FontWeights.Bold);
        }

        AddCanvasRect(canvas, width - 198, 18, 172, 54, "#FFFFFFE8", "#CBD5E1", 1);
        AddCanvasText(canvas, "Legenda", width - 186, 28, "#334155", 10, FontWeights.Bold);
        AddCanvasLine(canvas, width - 184, 52, width - 154, 52, "#E11D48", 4, null);
        AddCanvasText(canvas, "Boorlijn", width - 146, 44, "#334155", 9.5, FontWeights.Normal);
        AddCanvasLine(canvas, width - 184, 66, width - 174, 66, KlicThemeColor("laagspanning"), 2, null);
        AddCanvasLine(canvas, width - 174, 66, width - 164, 66, KlicThemeColor("gasLageDruk"), 2, null);
        AddCanvasLine(canvas, width - 164, 66, width - 154, 66, KlicThemeColor("water"), 2, null);
        AddCanvasText(canvas, "KLIC kruising", width - 146, 58, "#334155", 9.5, FontWeights.Normal);
        AddCanvasText(canvas, crossings.Count == 0 ? "Geen KLIC-kruisingen gevonden." : $"{crossings.Count} KLIC-kruising(en) gevonden.", left, height - 18, "#334155", 10.5, FontWeights.SemiBold);
        return canvas;
    }

    private Border CreateReportKlicPlanCrossingTable(IReadOnlyList<KlicPlanCrossing> crossings)
    {
        var table = CreateReportTable(["#", "Afstand", "Thema", "Leiding / netbeheerder", "RD"], [0.32, 0.55, 0.75, 1.45, 0.78]);
        foreach (var crossing in crossings.Take(18))
        {
            var networkText = $"{ShortReportCell(BuildKlicCrossingCableText(crossing), 90)}\n{ShortReportCell(crossing.NetworkOperator, 34)}";
            AddReportTableRow(table, [
                crossing.Code,
                $"{crossing.Distance:N1} m",
                ShortReportCell(crossing.ThemeLabel, 22),
                networkText,
                $"X {crossing.X:N1}\nY {crossing.Y:N1}"
            ]);
        }
        if (crossings.Count == 0) AddReportTableRow(table, ["-", "Geen kruisingen gevonden", "-", "-", "-"]);
        return table;
    }

    private Canvas CreateReportKlicSituationCanvas(IReadOnlyList<TracePointRow> traceRows, IReadOnlyList<ProjectMapLayer> layers, IReadOnlyList<KlicPlanCrossing> crossings)
    {
        const double width = 760;
        const double height = 300;
        var canvas = new Canvas { Width = width, Height = height, Background = Brushes.White, Margin = new Thickness(0, 8, 0, 10), ClipToBounds = true };
        AddCanvasRect(canvas, 0, 0, width, height, "#F8FAFB", "#D7E8FA", 1);

        var tracePoints = traceRows.Select(row => new RdPoint(row.X, row.Y)).Where(IsValidRdPoint).ToList();
        if (tracePoints.Count < 2)
        {
            AddCanvasText(canvas, "Geen boorlijn beschikbaar voor KLIC-situatiekaart.", 210, 138, "#8FA6B2", 12, FontWeights.Normal);
            return canvas;
        }

        var klicLines = BuildKlicSituationLines(traceRows, layers);
        var boundsPoints = tracePoints
            .Concat(crossings.Select(crossing => new RdPoint(crossing.X, crossing.Y)))
            .ToList();
        var bounds = ReportBoundsWithBuffer(boundsPoints, 8);
        var map = AddOsmBaseMapForRdBounds(canvas, bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY, width, height, showKadasterOverlay: true);
        var project = map.Project;

        foreach (var line in klicLines.Take(800))
        {
            var clipped = ClipRdLineToBounds(line.Points, bounds).ToList();
            var projected = clipped.Select(project).ToList();
            if (projected.Count < 2) continue;
            AddCanvasPolyline(canvas, line.Color, 2.0, projected.SelectMany(point => new[] { point.X, point.Y }).ToArray());
        }

        var trace = tracePoints.Select(project).ToList();
        AddCanvasPolyline(canvas, "#FFFFFF", 8, trace.SelectMany(point => new[] { point.X, point.Y }).ToArray());
        AddCanvasPolyline(canvas, "#E11D48", 5, trace.SelectMany(point => new[] { point.X, point.Y }).ToArray());
        AddCanvasCircle(canvas, trace[0].X, trace[0].Y, 6.5, "#16A34A", "#FFFFFF", 2);
        AddCanvasText(canvas, "Start", trace[0].X + 9, trace[0].Y - 14, "#166534", 10, FontWeights.Bold);
        AddCanvasCircle(canvas, trace[^1].X, trace[^1].Y, 6.5, "#DC2626", "#FFFFFF", 2);
        AddCanvasText(canvas, "Einde", trace[^1].X + 9, trace[^1].Y - 14, "#991B1B", 10, FontWeights.Bold);

        foreach (var crossing in crossings.Take(80))
        {
            var point = project(new RdPoint(crossing.X, crossing.Y));
            AddCanvasCircle(canvas, point.X, point.Y, 4.5, crossing.Color, "#FFFFFF", 1.5);
            AddCanvasText(canvas, crossing.Code, point.X + 5, point.Y - 8, "#334155", 8, FontWeights.Bold);
        }

        AddCanvasRect(canvas, width - 188, 18, 162, 74, "#FFFFFFE8", "#CBD5E1", 1);
        AddCanvasText(canvas, "Legenda", width - 176, 29, "#334155", 10, FontWeights.Bold);
        AddCanvasLine(canvas, width - 174, 52, width - 144, 52, "#E11D48", 4, null);
        AddCanvasText(canvas, "Boorlijn", width - 136, 44, "#334155", 9.5, FontWeights.Normal);
        AddCanvasLine(canvas, width - 174, 66, width - 144, 66, "#0057D8", 2, null);
        AddCanvasText(canvas, "Kadasterlijnen", width - 136, 58, "#334155", 9.5, FontWeights.Normal);
        AddCanvasLine(canvas, width - 174, 80, width - 164, 80, KlicThemeColor("laagspanning"), 2, null);
        AddCanvasLine(canvas, width - 164, 80, width - 154, 80, KlicThemeColor("gasLageDruk"), 2, null);
        AddCanvasLine(canvas, width - 154, 80, width - 144, 80, KlicThemeColor("water"), 2, null);
        AddCanvasText(canvas, "KLIC kruising", width - 136, 72, "#334155", 9.5, FontWeights.Normal);
        AddReportScaleBar(canvas, map.PixelsPerMeter, 18, height - 48, 130);
        AddCanvasText(canvas, $"PDOK/Kadaster + KLIC kruisingen: {crossings.Count}", 18, height - 18, "#334155", 10, FontWeights.SemiBold);
        return canvas;
    }

    private Border CreateReportLandscapePage(int pageNumber, string title, params UIElement[] sections)
    {
        var root = new DockPanel { LastChildFill = true };
        var header = new Grid { Margin = new Thickness(68, 44, 68, 18) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new StackPanel
        {
            Children =
            {
                new TextBlock { Text = DefaultCoverTitle, Foreground = Brush("#64748B"), FontSize = 9.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = title, Foreground = Brush("#64748B"), FontSize = 8.8, Margin = new Thickness(0, 2, 0, 0) }
            }
        });
        var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 250, MaxWidth = 380 };
        right.Children.Add(CreateReportHeaderProjectInfoBlock());
        Grid.SetColumn(right, 1);
        header.Children.Add(right);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = new Grid { Margin = new Thickness(68, 10, 68, 30) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var date = new TextBlock { Text = $"{FormatReportPageNumber(pageNumber)} · {DateTime.Now.ToString("dd-MM-yyyy", CultureInfo.CurrentCulture)}", Foreground = Brush("#94A3B8"), FontSize = 9, Width = 190, TextAlignment = TextAlignment.Right };
        date.Text = $"Gepubliceerd op: {DateTime.Now.ToString("dd-MM-yyyy", CultureInfo.CurrentCulture)}";
        date.FontSize = 8.4;
        date.Width = 220;
        Grid.SetColumn(date, 2);
        footer.Children.Add(date);
        var landscapePageNumberFooter = new TextBlock
        {
            Text = FormatReportPageNumber(pageNumber),
            Foreground = Brush("#94A3B8"),
            FontSize = 8.4,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
        Grid.SetColumn(landscapePageNumberFooter, 1);
        footer.Children.Add(landscapePageNumberFooter);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var content = new StackPanel { Margin = new Thickness(68, 0, 68, 0) };
        foreach (var section in sections)
        {
            content.Children.Add(section);
        }
        root.Children.Add(content);

        return CreateReportLandscapePageBorder(pageNumber, root);
    }

    private static Border CreateReportLandscapePageBorder(int pageNumber, UIElement child) => new()
    {
        Width = 1188,
        Height = 840,
        MinHeight = 840,
        Tag = pageNumber,
        Background = Brushes.White,
        BorderBrush = Brush("#CBD5E1"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(2),
        Margin = new Thickness(0, 0, 0, 22),
        Child = child
    };

    private static Border CreateReportLayerSummaryTable(IReadOnlyList<ProjectMapLayer> layers)
    {
        var table = CreateReportTable(["Laag", "Type", "Geometrieën", "Kleur"]);
        foreach (var layer in layers.OrderBy(layer => layer.Type).ThenBy(layer => layer.Name).Take(18))
        {
            AddReportTableRow(table, [
                layer.Name,
                layer.Type,
                layer.FeatureCollection.Features.Count.ToString(CultureInfo.InvariantCulture),
                layer.Color
            ]);
        }
        if (layers.Count == 0) AddReportTableRow(table, ["-", "Geen GIS-lagen", "-", "-"]);
        return table;
    }

    private static IReadOnlyList<IReadOnlyList<Int32Rect>> CreateReportLegendTileGroups(BitmapSource bitmap)
    {
        const int maxTileWidth = 300;
        const int maxTileHeight = 520;
        const double availableWidth = 1030;

        var tileWidth = Math.Max(1, Math.Min(bitmap.PixelWidth, maxTileWidth));
        var columnsPerPage = Math.Clamp((int)Math.Floor(availableWidth / (tileWidth + 12d)), 1, 5);
        var tiles = new List<Int32Rect>();

        for (var y = 0; y < bitmap.PixelHeight; y += maxTileHeight)
        {
            var height = Math.Min(maxTileHeight, bitmap.PixelHeight - y);
            for (var x = 0; x < bitmap.PixelWidth; x += tileWidth)
            {
                var width = Math.Min(tileWidth, bitmap.PixelWidth - x);
                tiles.Add(new Int32Rect(x, y, width, height));
            }
        }

        var groups = new List<IReadOnlyList<Int32Rect>>();
        for (var index = 0; index < tiles.Count; index += columnsPerPage)
        {
            groups.Add(tiles.Skip(index).Take(columnsPerPage).ToList());
        }

        return groups;
    }

    private static WrapPanel CreateReportLegendTileWrap(BitmapSource bitmap, IReadOnlyList<Int32Rect> tiles)
    {
        var wrap = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            MaxWidth = 1040,
            Margin = new Thickness(0, 2, 0, 8)
        };

        foreach (var tile in tiles)
        {
            var crop = new CroppedBitmap(bitmap, tile);
            crop.Freeze();
            wrap.Children.Add(new Border
            {
                Background = Brushes.White,
                BorderBrush = Brush("#E5EDF3"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4),
                Margin = new Thickness(0, 0, 8, 8),
                Child = new Image
                {
                    Source = crop,
                    Width = tile.Width,
                    Height = tile.Height,
                    Stretch = Stretch.None,
                    SnapsToDevicePixels = true
                }
            });
        }

        return wrap;
    }

    private static UIElement CreateReportLocationMapTraceDetails(IReadOnlyList<TracePointRow> traceRows)
    {
        if (traceRows.Count < 2) return CreateReportNote("Geen volledige boorlijn beschikbaar voor lengte en begin-/eindcoördinaten.");
        var distances = BuildTraceDistances(traceRows);
        var start = traceRows[0];
        var end = traceRows[^1];
        return CreateReportKeyValues(
            ("Boorlijnlengte", $"{distances[^1]:N1} m"),
            ("Beginpunt RD", $"X {start.X:N2} / Y {start.Y:N2}"),
            ("Eindpunt RD", $"X {end.X:N2} / Y {end.Y:N2}"));
    }

    private static Border CreateReportMachinePlacementTable(IReadOnlyList<MachinePlacementRow> machines)
    {
        var table = CreateReportTable(["Object", "Lengte", "Breedte", "Oppervlak"]);
        foreach (var machine in machines.Take(12))
        {
            AddReportTableRow(table, [
                machine.Label,
                $"{machine.Length:N1} m",
                $"{machine.Width:N1} m",
                $"{machine.Length * machine.Width:N1} m²"
            ]);
        }
        if (machines.Count == 0) AddReportTableRow(table, ["-", "Geen machineplaatsing", "-", "-"]);
        return table;
    }

    private Canvas CreateReportMapCanvas(ReportMapRecipe recipe, IReadOnlyList<TracePointRow> traceRows, IReadOnlyList<ProjectMapLayer> layers, ParcelOwnerAnalysis parcelAnalysis)
    {
        var width = recipe.Width;
        var height = recipe.Height;
        var canvas = new Canvas { Width = width, Height = height, Background = Brush("#F8FAFB"), Margin = new Thickness(0, 0, 0, 0), ClipToBounds = true };
        AddCanvasRect(canvas, 0, 0, width, height, "#F8FAFB", "#E5E7EB", 1);

        var selectedLayers = layers.Where(layer => ReportRecipeIncludesLayer(recipe, layer)).ToList();
        var geometryLines = selectedLayers
            .SelectMany(layer => EnumerateReportFeatureLines(layer).Select(line => (Layer: layer, Points: line.Where(IsValidRdPoint).ToList())))
            .Where(item => item.Points.Count > 0)
            .Take(1600)
            .ToList();
        var tracePoints = traceRows.Select(row => new RdPoint(row.X, row.Y)).Where(IsValidRdPoint).ToList();
        var bounds = CalculateReportRecipeBounds(recipe, traceRows, geometryLines.SelectMany(item => item.Points).Concat(tracePoints).ToList());

        if (bounds is null)
        {
            AddCanvasText(canvas, "Geen kaartgeometrie beschikbaar", 248, height / 2 - 8, "#8FA6B2", 12, FontWeights.Normal);
            AddCanvasText(canvas, recipe.Title, 18, height - 28, "#334155", 11, FontWeights.Normal);
            return canvas;
        }

        var (minX, minY, maxX, maxY, pixelsPerMeter) = bounds.Value;
        Point ProjectRd(RdPoint point) => new(24 + (point.X - minX) * pixelsPerMeter, height - 62 - (point.Y - minY) * pixelsPerMeter);
        Func<RdPoint, Point> project = ProjectRd;

        if (recipe.BaseMap.Equals("osm", StringComparison.OrdinalIgnoreCase))
        {
            var osmProjection = AddOsmBaseMapForRdBounds(canvas, minX, minY, maxX, maxY, width, height - 44, IsReportPresentationLocationMap(recipe));
            project = osmProjection.Project;
            pixelsPerMeter = osmProjection.PixelsPerMeter;
        }
        else if (IsPdokReportBaseMap(recipe.BaseMap))
        {
            var center = GetReportRecipeCenter(recipe, traceRows) ?? new RdPoint((minX + maxX) / 2d, (minY + maxY) / 2d);
            var centerWgs = RdToWgs84(center.X, center.Y);
            var tileZoom = ReportTileZoomForBounds(centerWgs[1], maxX - minX, maxY - minY, width, height - 44);
            var centerPixel = LonLatToWebMercatorPixel(centerWgs[0], centerWgs[1], tileZoom);
            var originX = centerPixel.X - width / 2d;
            var originY = centerPixel.Y - (height - 44) / 2d;
            AddBaseMapTilesForCamera(canvas, recipe.BaseMap, originX, originY, width, height - 44, tileZoom, showKadasterOverlay: recipe.LayerSet.Contains("cadastral", StringComparison.OrdinalIgnoreCase));
            project = point =>
            {
                var wgs = RdToWgs84(point.X, point.Y);
                var pixel = LonLatToWebMercatorPixel(wgs[0], wgs[1], tileZoom);
                return new Point(pixel.X - originX, pixel.Y - originY);
            };
            pixelsPerMeter = 1 / WebMercatorMetersPerPixel(centerWgs[1], tileZoom);
        }
        else
        {
            for (var x = 0; x <= width; x += 48) AddCanvasLine(canvas, x, 0, x, height, "#EDF2F4", 1, null);
            for (var y = 0; y <= height; y += 48) AddCanvasLine(canvas, 0, y, width, y, "#EDF2F4", 1, null);
        }

        if (!IsReportPresentationLocationMap(recipe))
        {
            foreach (var item in geometryLines.OrderBy(item => ReportLayerDrawOrder(item.Layer.Type)))
            {
                if (!ReportLineIntersectsBounds(item.Points, minX, minY, maxX, maxY)) continue;
                var coords = item.Points.Select(project).SelectMany(point => new[] { point.X, point.Y }).ToArray();
                if (coords.Length < 2) continue;
                var color = ReportLayerColor(item.Layer, item.Points);
                var thickness = ReportRecipeLayerStroke(recipe, item.Layer);
                if (ReportLooksClosed(item.Points) && item.Points.Count >= 4)
                {
                    AddCanvasPolygon(canvas, ReportRecipeLayerFill(recipe, item.Layer, color), color, thickness, coords);
                }
                else
                {
                    AddCanvasPolyline(canvas, color, thickness, coords);
                }
            }
        }

        if (recipe.ShowRisk)
        {
            AddReportRiskMarkers(canvas, parcelAnalysis, project, minX, minY, maxX, maxY);
        }

        AddReportTrace(canvas, traceRows, recipe.ShowTracePoints, project, _traceSmoothBore);
        if (recipe.ShowMachine) AddReportMachineMarkers(canvas);
        AddReportMapLegend(canvas, recipe, width - 222, 14);
        AddReportScaleBar(canvas, pixelsPerMeter, 18, height - 54, 130);
        AddCanvasText(canvas, recipe.ScaleDenominator is null ? "Schaal: passend op uitsnede" : $"Schaal 1:{recipe.ScaleDenominator}", 18, height - 24, "#334155", 10.5, FontWeights.SemiBold);
        if (!IsReportPresentationLocationMap(recipe))
        {
            AddCanvasText(canvas, $"Lagen: {selectedLayers.Count} · Geometrieen: {geometryLines.Count}", width - 230, height - 24, "#64748B", 10, FontWeights.Normal);
        }
        return canvas;
    }

    private Canvas CreateReportMapCanvas(string label, string mode, IReadOnlyList<TracePointRow> traceRows, IReadOnlyList<ProjectMapLayer> layers)
    {
        var canvas = new Canvas { Width = 760, Height = 250, Background = Brush("#F8FAFB"), Margin = new Thickness(0, 12, 0, 0) };
        AddCanvasRect(canvas, 0, 0, 760, 250, "#F8FAFB", "#E5E7EB", 1);
        for (var x = 0; x <= 760; x += 48) AddCanvasLine(canvas, x, 0, x, 250, "#EDF2F4", 1, null);
        for (var y = 0; y <= 250; y += 48) AddCanvasLine(canvas, 0, y, 760, y, "#EDF2F4", 1, null);

        var geometryLines = layers
            .SelectMany(layer => EnumerateReportFeatureLines(layer).Select(line => (Layer: layer, Points: line)))
            .Where(item => item.Points.Count > 0)
            .Take(1200)
            .ToList();
        var allPoints = geometryLines.SelectMany(item => item.Points)
            .Concat(traceRows.Select(point => new RdPoint(point.X, point.Y)))
            .Where(point => double.IsFinite(point.X) && double.IsFinite(point.Y))
            .ToList();

        if (allPoints.Count == 0)
        {
            AddCanvasText(canvas, "Geen GIS-geometrie gevonden in projectbestanden", 210, 104, "#8FA6B2", 12, FontWeights.Normal);
            AddReportTrace(canvas, traceRows, mode is "trace" or "machine" or "final", smooth: _traceSmoothBore);
            AddReportScaleBar(canvas, 1, 18, 198, 120);
            AddCanvasText(canvas, label, 18, 222, "#334155", 11, FontWeights.Normal);
            AddCanvasText(canvas, $"Imports/lagen: {layers.Count}", 640, 222, "#8FA6B2", 10, FontWeights.Normal);
            return canvas;
        }

        var minX = allPoints.Min(point => point.X);
        var maxX = allPoints.Max(point => point.X);
        var minY = allPoints.Min(point => point.Y);
        var maxY = allPoints.Max(point => point.Y);
        var spanX = Math.Max(1, maxX - minX);
        var spanY = Math.Max(1, maxY - minY);
        var scale = Math.Min(700 / spanX, 190 / spanY);
        var offsetX = 30 + (700 - spanX * scale) / 2;
        var offsetY = 24 + (190 - spanY * scale) / 2;
        Point Project(RdPoint point) => new(offsetX + (point.X - minX) * scale, offsetY + 190 - (point.Y - minY) * scale);

        foreach (var item in geometryLines.OrderBy(item => ReportLayerDrawOrder(item.Layer.Type)))
        {
            var coords = item.Points.Select(Project).SelectMany(point => new[] { point.X, point.Y }).ToArray();
            if (coords.Length < 4) continue;
            var color = ReportLayerColor(item.Layer, item.Points);
            var thickness = item.Layer.Type.Equals("BGT", StringComparison.OrdinalIgnoreCase) || item.Layer.Type.Contains("BAG", StringComparison.OrdinalIgnoreCase) ? 1.2 : 2.6;
            if (ReportLooksClosed(item.Points) && item.Points.Count >= 4)
            {
                AddCanvasPolygon(canvas, ReportLayerFill(item.Layer, color), color, 0.8, coords);
            }
            else
            {
                AddCanvasPolyline(canvas, color, thickness, coords);
            }
        }

        AddReportTrace(canvas, traceRows, mode is "trace" or "machine" or "final", Project, _traceSmoothBore);
        if (mode is "machine" or "final") AddReportMachineMarkers(canvas);

        AddReportScaleBar(canvas, scale, 18, 198, 130);
        AddCanvasText(canvas, label, 18, 222, "#334155", 11, FontWeights.Normal);
        AddCanvasText(canvas, $"GIS geometrieen: {geometryLines.Count} / lagen: {layers.Count}", 540, 222, "#8FA6B2", 10, FontWeights.Normal);
        return canvas;
    }

    private Canvas CreateReportMapControlCanvas(ReportMapRecipe recipe, IReadOnlyList<TracePointRow> traceRows, IReadOnlyList<ProjectMapLayer> layers, ParcelOwnerAnalysis parcelAnalysis, ReportMapState mapState)
    {
        if (mapState.CenterLon is null || mapState.CenterLat is null || mapState.Zoom is null)
        {
            return CreateReportMapCanvas(recipe, traceRows, layers, parcelAnalysis);
        }

        var width = recipe.Width;
        var height = recipe.Height;
        var canvas = new Canvas { Width = width, Height = height, Background = Brush("#F8FAFB"), Margin = new Thickness(0, 0, 0, 0), ClipToBounds = true };
        AddCanvasRect(canvas, 0, 0, width, height, "#F8FAFB", "#E5E7EB", 1);

        var zoom = Math.Clamp(mapState.Zoom.Value, 10, 19);
        var tileZoom = (int)Math.Clamp(Math.Round(zoom), 10, 19);
        var centerLon = mapState.CenterLon.Value;
        var centerLat = mapState.CenterLat.Value;
        var centerPixel = LonLatToWebMercatorPixel(centerLon, centerLat, tileZoom);
        var originX = centerPixel.X - width / 2d;
        var originY = centerPixel.Y - (height - 44) / 2d;

        Point Project(RdPoint point)
        {
            var wgs = RdToWgs84(point.X, point.Y);
            var pixel = LonLatToWebMercatorPixel(wgs[0], wgs[1], tileZoom);
            return new Point(pixel.X - originX, pixel.Y - originY);
        }

        var topLeft = WebMercatorPixelToLonLat(originX, originY, tileZoom);
        var bottomRight = WebMercatorPixelToLonLat(originX + width, originY + Math.Max(1, height - 44), tileZoom);
        var rdA = Wgs84ToRd(topLeft.Lon, topLeft.Lat);
        var rdB = Wgs84ToRd(bottomRight.Lon, bottomRight.Lat);
        var minX = Math.Min(rdA.X, rdB.X);
        var maxX = Math.Max(rdA.X, rdB.X);
        var minY = Math.Min(rdA.Y, rdB.Y);
        var maxY = Math.Max(rdA.Y, rdB.Y);

        var baseVisible = IsReportMapOverlayVisible(mapState, "baseMap", defaultVisible: true);
        if (baseVisible)
        {
            AddBaseMapTilesForCamera(canvas, mapState.BaseLayer, originX, originY, width, height - 44, tileZoom, showKadasterOverlay: false);
        }
        else
        {
            AddCanvasRect(canvas, 0, 0, width, height - 44, "#F8FAFB", "#E5E7EB", 1);
            for (var x = 0; x <= width; x += 64) AddCanvasLine(canvas, x, 0, x, height - 44, "#EEF2F6", 1, null);
            for (var y = 0; y <= height - 44; y += 64) AddCanvasLine(canvas, 0, y, width, y, "#EEF2F6", 1, null);
        }

        var geometryLines = layers
            .SelectMany(layer => EnumerateReportFeatureLines(layer).Select(line => (Layer: layer, Points: line.Where(IsValidRdPoint).ToList())))
            .Where(item => item.Points.Count > 0)
            .Take(1800)
            .ToList();

        foreach (var item in geometryLines.OrderBy(item => ReportLayerDrawOrder(item.Layer.Type)))
        {
            if (!ReportLineIntersectsBounds(item.Points, minX, minY, maxX, maxY)) continue;
            var coords = item.Points.Select(Project).SelectMany(point => new[] { point.X, point.Y }).ToArray();
            if (coords.Length < 2) continue;
            var color = ReportLayerColor(item.Layer, item.Points);
            var thickness = ReportRecipeLayerStroke(recipe, item.Layer);
            if (ReportLooksClosed(item.Points) && item.Points.Count >= 4)
            {
                AddCanvasPolygon(canvas, ReportRecipeLayerFill(recipe, item.Layer, color), color, thickness, coords);
            }
            else
            {
                AddCanvasPolyline(canvas, color, thickness, coords);
            }
        }

        var traceVisible = IsReportMapOverlayVisible(mapState, "boreTrace", defaultVisible: true);
        if (traceVisible)
        {
            var tracePointsVisible = IsReportMapOverlayVisible(mapState, "boreTraceNumbers", defaultVisible: recipe.ShowTracePoints);
            AddReportTrace(canvas, traceRows, tracePointsVisible, Project, _traceSmoothBore);
        }
        AddReportMapLegend(canvas, recipe, width - 222, 14, traceVisible);
        AddReportScaleBar(canvas, 1 / WebMercatorMetersPerPixel(centerLat, tileZoom), 18, height - 54, 130);
        var scaleText = mapState.MapScale is int reportScale ? $"Schaal 1:{reportScale}" : $"Zoom {mapState.Zoom.Value:N2}";
        AddCanvasText(canvas, $"{scaleText} · {(baseVisible ? DescribeMapBaseLayer(mapState.BaseLayer) : "ondergrond uit")}", 18, height - 24, "#334155", 10.5, FontWeights.SemiBold);
        AddCanvasText(canvas, $"Zichtbare lagen: {layers.Count} · geometrieen: {geometryLines.Count}", width - 254, height - 24, "#64748B", 10, FontWeights.Normal);
        return canvas;
    }

    private static Border CreateReportMapMetadataBlock(ReportMapRecipe recipe, IReadOnlyList<ProjectMapLayer> layers, ParcelOwnerAnalysis parcelAnalysis)
    {
        var visibleSources = layers
            .Where(layer => ReportRecipeIncludesLayer(recipe, layer))
            .Select(layer => layer.Type)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .DefaultIfEmpty("Geen GIS-bronlagen")
            .ToList();

        var rows = CreateReportKeyValues(
            ("Hoofdstuk", $"Stap {recipe.StepNumber}"),
            ("Instelling", recipe.ScaleDenominator is null ? "Automatisch passend" : $"Vaste schaal 1:{recipe.ScaleDenominator}"),
            ("Ondergrond", DescribeMapBaseLayer(recipe.BaseMap)),
            ("Laagset", recipe.LayerSet),
            ("Bronnen", string.Join(", ", visibleSources)),
            ("Betrouwbaarheid", recipe.LayerSet.Contains("cadastral", StringComparison.OrdinalIgnoreCase) || recipe.LayerSet.Contains("environment", StringComparison.OrdinalIgnoreCase)
                ? "Kadastrale ligging/bronhouder uit importdata; eigendom en ZRO blijven handmatige controle."
                : "Automatisch gegenereerd uit opgeslagen projectdata."));
        return new Border
        {
            Background = Brushes.Transparent,
            BorderBrush = Brush("#E5EDF3"),
            BorderThickness = new Thickness(0, 1, 0, 1),
            Padding = new Thickness(0, 8, 0, 8),
            Margin = new Thickness(0, 8, 0, 0),
            Child = rows
        };
    }

    private Border CreateReportMapRecipeCard(ReportMapRecipe recipe, IReadOnlyList<TracePointRow> traceRows, IReadOnlyList<ProjectMapLayer> layers, ParcelOwnerAnalysis parcelAnalysis)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = recipe.Title,
            Foreground = Brush("#071422"),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        panel.Children.Add(new TextBlock
        {
            Text = recipe.Purpose,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#587080"),
            FontSize = 10.5,
            Margin = new Thickness(0, 0, 0, 8)
        });
        panel.Children.Add(CreateReportMapCanvas(recipe, traceRows, layers, parcelAnalysis));
        if (!IsReportPresentationLocationMap(recipe))
        {
            panel.Children.Add(CreateReportMapMetadataBlock(recipe, layers, parcelAnalysis));
        }

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("#D7E8FA"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 8, 0, 0),
            Child = panel
        };
    }

    private static TextBlock CreateReportNote(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = Brush("#555555"),
        FontSize = 8.8,
        LineHeight = 12.2,
        Margin = new Thickness(0, 5, 0, 0)
    };

    private Canvas CreateReportOpenStreetMapCanvas(string label, IReadOnlyList<TracePointRow> traceRows)
    {
        const double width = 760;
        const double height = 330;
        var canvas = new Canvas { Width = width, Height = height, Background = Brush("#F8FAFB"), Margin = new Thickness(0, 12, 0, 0), ClipToBounds = true };
        AddCanvasRect(canvas, 0, 0, width, height, "#F8FAFB", "#E5E7EB", 1);

        var wgsTrace = traceRows
            .Where(point => LooksLikeRd(point.X, point.Y))
            .Select(point =>
            {
                var wgs = RdToWgs84(point.X, point.Y);
                return new LonLat(wgs[0], wgs[1]);
            })
            .Where(point => double.IsFinite(point.Lon) && double.IsFinite(point.Lat))
            .ToList();

        if (wgsTrace.Count < 2)
        {
            AddCanvasText(canvas, "Geen boorlijncoördinaten beschikbaar voor OpenStreetMap.", 190, 146, "#8FA6B2", 12, FontWeights.Normal);
            AddCanvasText(canvas, label, 18, height - 28, "#334155", 11, FontWeights.Normal);
            return canvas;
        }

        var minLon = wgsTrace.Min(point => point.Lon);
        var maxLon = wgsTrace.Max(point => point.Lon);
        var minLat = wgsTrace.Min(point => point.Lat);
        var maxLat = wgsTrace.Max(point => point.Lat);
        var centerLon = (minLon + maxLon) / 2d;
        var centerLat = (minLat + maxLat) / 2d;
        var zoom = ChooseOsmZoom(wgsTrace, width, height);
        var centerPixel = LonLatToWebMercatorPixel(centerLon, centerLat, zoom);
        var originX = centerPixel.X - width / 2d;
        var originY = centerPixel.Y - height / 2d;

        var startTileX = (int)Math.Floor(originX / 256d);
        var endTileX = (int)Math.Floor((originX + width) / 256d);
        var startTileY = (int)Math.Floor(originY / 256d);
        var endTileY = (int)Math.Floor((originY + height) / 256d);
        var maxTile = (1 << zoom) - 1;
        for (var tileX = startTileX; tileX <= endTileX; tileX++)
        {
            for (var tileY = startTileY; tileY <= endTileY; tileY++)
            {
                if (tileY < 0 || tileY > maxTile) continue;
                var wrappedX = ((tileX % (maxTile + 1)) + maxTile + 1) % (maxTile + 1);
                var left = tileX * 256d - originX;
                var top = tileY * 256d - originY;
                var image = CreateOsmTileImage(zoom, wrappedX, tileY);
                Canvas.SetLeft(image, left);
                Canvas.SetTop(image, top);
                canvas.Children.Add(image);
            }
        }

        Point Project(LonLat point)
        {
            var pixel = LonLatToWebMercatorPixel(point.Lon, point.Lat, zoom);
            return new Point(pixel.X - originX, pixel.Y - originY);
        }

        var projectedTrace = wgsTrace.Select(Project).ToList();
        AddCanvasPolyline(canvas, "#FFFFFF", 8, projectedTrace.SelectMany(point => new[] { point.X, point.Y }).ToArray());
        AddCanvasPolyline(canvas, "#E11D48", 5, projectedTrace.SelectMany(point => new[] { point.X, point.Y }).ToArray());
        for (var i = 0; i < projectedTrace.Count; i++)
        {
            var point = projectedTrace[i];
            var isStart = i == 0;
            var isEnd = i == projectedTrace.Count - 1;
            AddCanvasCircle(canvas, point.X, point.Y, 7, isStart ? "#16A34A" : isEnd ? "#DC2626" : "White", "White", 2);
            if (isStart) AddCanvasText(canvas, "Start", point.X + 10, point.Y - 8, "#166534", 11, FontWeights.Bold);
            if (isEnd) AddCanvasText(canvas, "Einde", point.X + 10, point.Y - 8, "#991B1B", 11, FontWeights.Bold);
        }

        var metersPerPixel = WebMercatorMetersPerPixel(centerLat, zoom);
        AddReportScaleBar(canvas, 1 / metersPerPixel, 18, height - 56, 130);
        AddCanvasText(canvas, label, 18, height - 28, "#334155", 11, FontWeights.Normal);
        AddCanvasText(canvas, $"OpenStreetMap · zoom {zoom}", width - 160, height - 28, "#64748B", 10, FontWeights.Normal);
        return canvas;
    }

    private Border CreateReportPage(int pageNumber, string title, params UIElement[] sections)
    {
        var root = new DockPanel { LastChildFill = true };
        var header = new Grid { Margin = new Thickness(86, 56, 86, 22) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new StackPanel
        {
            Children =
            {
                new TextBlock { Text = DefaultCoverTitle, Foreground = Brush("#64748B"), FontSize = 9.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = title, Foreground = Brush("#64748B"), FontSize = 8.8, Margin = new Thickness(0, 2, 0, 0) }
            }
        });
        var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 250, MaxWidth = 380 };
        right.Children.Add(CreateReportHeaderProjectInfoBlock());
        Grid.SetColumn(right, 1);
        header.Children.Add(right);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = new Grid { Margin = new Thickness(86, 12, 86, 38) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var date = new TextBlock { Text = $"{FormatReportPageNumber(pageNumber)} · {DateTime.Now.ToString("dd-MM-yyyy", CultureInfo.CurrentCulture)}", Foreground = Brush("#94A3B8"), FontSize = 9, Width = 190, TextAlignment = TextAlignment.Right };
        date.Text = $"Gepubliceerd op: {DateTime.Now.ToString("dd-MM-yyyy", CultureInfo.CurrentCulture)}";
        date.FontSize = 8.4;
        date.Width = 220;
        Grid.SetColumn(date, 2);
        footer.Children.Add(date);
        var pageNumberFooter = new TextBlock
        {
            Text = FormatReportPageNumber(pageNumber),
            Foreground = Brush("#94A3B8"),
            FontSize = 8.4,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
        Grid.SetColumn(pageNumberFooter, 1);
        footer.Children.Add(pageNumberFooter);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var content = new StackPanel { Margin = new Thickness(86, 0, 86, 0) };
        foreach (var section in sections)
        {
            content.Children.Add(section);
        }
        root.Children.Add(content);

        return CreateReportPageBorder(pageNumber, root);
    }

    private static Border CreateReportPageBorder(int pageNumber, UIElement child) => new()
    {
        Width = 840,
        MinHeight = 1188,
        Tag = pageNumber,
        Background = Brushes.White,
        BorderBrush = Brush("#CBD5E1"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(2),
        Margin = new Thickness(0, 0, 0, 22),
        Child = child
    };

    private static Border CreateReportPreviewPlaceholder(string message) =>
        CreateReportPageBorder(0, new Border
        {
            Padding = new Thickness(28),
            Child = CreateReportStepPreviewText(message)
        });

    private Border CreateReportProjectIntro(IReadOnlyList<TracePointRow> traceRows, double traceLength, ReportLocationContext locationContext)
    {
        var projectName = _selectedProject?.Name ?? "dit project";
        var location = BuildReportProjectLocation(locationContext);
        var discipline = _boringItems.Count == 0
            ? "de ingevoerde kabel- of leidingdiscipline"
            : string.Join(", ", _boringItems.SelectMany(item => item.Contents.Select(content => content.Label)).DefaultIfEmpty(_selectedProject?.Material ?? "boring").Distinct(StringComparer.OrdinalIgnoreCase).Take(4));
        var pointText = traceRows.Count >= 2
            ? $"De boorlijn is vastgelegd met {traceRows.Count} tracepunt(en) en heeft in deze prescan een lengte van circa {traceLength:N1} meter."
            : $"Voor {projectName} is nog geen volledige boorlijn vastgelegd.";
        var text = $"Voor {projectName} is een prescan opgesteld voor een horizontaal gestuurde boring ter plaatse van {location}. De ligging is automatisch bepaald als {locationContext.Summary}. {pointText} De boring heeft betrekking op {discipline} en wordt in dit rapport uitgewerkt met projectgegevens, GIS-context, omgevingsinformatie, technische boringconfiguratie, machinekeuze en dwarsprofiel.";

        return new Border
        {
            Background = Brush("#FBFCFD"),
            BorderBrush = Brush("#D7E8FA"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 10),
            Child = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("#334155"),
                FontSize = 12,
                LineHeight = 18
            }
        };
    }

    private Border CreateReportQualityDashboard(ReportQualitySummary projectSummary)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        panel.Children.Add(CreateReportSubheading("Rapportcontrole"));
        panel.Children.Add(CreateReportQualityNote(projectSummary));
        panel.Children.Add(CreateReportKeyValues(
            ("Status", projectSummary.StatusLabel),
            ("Aandachtspunten", projectSummary.TotalIssues.ToString(CultureInfo.InvariantCulture)),
            ("Hoog", projectSummary.HighIssues.ToString(CultureInfo.InvariantCulture)),
            ("Middel", projectSummary.MediumIssues.ToString(CultureInfo.InvariantCulture)),
            ("Laag", projectSummary.LowIssues.ToString(CultureInfo.InvariantCulture))));

        var table = CreateReportTable(["Stap", "Status", "Eerstvolgende actie"], [0.95, 0.55, 1.5]);
        if (_selectedProject is not null)
        {
            foreach (var stepNumber in ReportContractCatalog.GetAll()
                         .Select(contract => contract.StepNumber)
                         .Distinct()
                         .OrderBy(step => step))
            {
                var summary = _reportQuality.EvaluateStep(_selectedProject.Id, stepNumber);
                AddReportTableRow(table, [
                    $"{DisplayStepNumber(stepNumber)} {GetReportStepTitle(stepNumber)}",
                    summary.StatusLabel,
                    summary.IsReady ? "Geen actie nodig." : BuildFirstReportQualityAction(summary)
                ]);
            }
        }

        panel.Children.Add(table);

        return new Border
        {
            BorderBrush = Brush("#E5EDF3"),
            BorderThickness = new Thickness(0, 1, 0, 1),
            Background = Brushes.Transparent,
            Padding = new Thickness(0, 10, 0, 10),
            Margin = new Thickness(0, 10, 0, 14),
            Child = panel
        };
    }

    private static Border CreateReportQualityNote(ReportQualitySummary summary)
    {
        var message = summary.IsReady
            ? "Rapportklaar: dit onderdeel voldoet aan het rapportcontract."
            : BuildReportQualityActions(summary);

        return new Border
        {
            Background = ReportStatusBackground(summary),
            BorderBrush = ReportStatusBorder(summary),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 8, 0, 0),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = summary.StatusLabel,
                        Foreground = Brush(summary.StatusColor),
                        FontWeight = FontWeights.Bold,
                        FontSize = 10.5
                    },
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = Brush("#334155"),
                        FontSize = 10,
                        Margin = new Thickness(0, 3, 0, 0)
                    }
                }
            }
        };
    }

    private static UIElement CreateReportRemoteLegendImage(string url)
    {
        if (!TryLoadReportLegendBitmap(url, out var bitmap))
        {
            return new TextBlock
            {
                Text = "Legenda kon niet geladen worden.",
                Foreground = Brush("#8FA6B2"),
                FontSize = 9.5,
                TextWrapping = TextWrapping.Wrap
            };
        }

        return CreateReportLegendTileWrap(bitmap, CreateReportLegendTileGroups(bitmap).FirstOrDefault() ?? []);
    }

    private UIElement CreateReportRenderBlock(ReportRenderBlock block)
    {
        return block switch
        {
            ReportRenderKeyValuesBlock keyValues => CreateReportKeyValues(keyValues.Rows.Select(row => (row.Label, row.Value)).ToArray()),
            ReportRenderTableBlock table => CreateReportRenderTable(table),
            ReportRenderHeadingBlock heading => CreateReportSubheading(heading.Text),
            ReportRenderCardsBlock cards => CreateReportRenderCards(cards),
            ReportRenderSoundingProfileBlock profile => CreateReportSoundingProfile(profile),
            ReportRenderNoteBlock note => CreateReportNote(note.Text),
            _ => CreateReportNote("Onbekend rapportblok.")
        };
    }

    private static StackPanel CreateReportRenderCards(ReportRenderCardsBlock block)
    {
        var panel = new StackPanel();
        foreach (var cardModel in block.Cards)
        {
            var card = new StackPanel();
            var header = new DockPanel { LastChildFill = true };
            header.Children.Add(new TextBlock
            {
                Text = cardModel.Title,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("#333333"),
                FontSize = 10.2,
                TextWrapping = TextWrapping.Wrap
            });
            card.Children.Add(header);

            foreach (var line in cardModel.Lines)
            {
                var row = new DockPanel { Margin = new Thickness(12, 5, 0, 0), LastChildFill = true };
                row.Children.Add(new TextBlock
                {
                    Text = line.Text,
                    Foreground = Brush("#333333"),
                    FontSize = 9.4,
                    TextWrapping = TextWrapping.Wrap
                });
                card.Children.Add(row);
            }

            panel.Children.Add(new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = Brush("#E5EDF3"),
                BorderThickness = new Thickness(0, 1, 0, 1),
                Padding = new Thickness(0, 8, 0, 8),
                Margin = new Thickness(0, 0, 0, 8),
                Child = card
            });
        }

        return panel;
    }

    private UIElement CreateReportRenderDocument(string substepNumber, JsonElement data, ReportRenderDocument document, bool includeSubstepMedia = true)
    {
        var panel = new StackPanel();
        foreach (var block in document.Blocks)
        {
            panel.Children.Add(CreateReportRenderBlock(block));
        }

        if (substepNumber == "1.3")
        {
            panel.Children.Add(CreateReportSubheading("Dwarsdoorsnede"));
            panel.Children.Add(CreateReportBoringCanvas(ComputeBoring()));
            panel.Children.Add(CreateReportNote("De doorsnede en legenda worden automatisch opgebouwd uit de actuele inhoud van substap 1.2 en de berekening van substap 1.3."));
        }
        else if (substepNumber == "3.1" && includeSubstepMedia)
        {
            AddMapControlReportImage(panel, data);
        }
        else if (substepNumber == "3.2" && includeSubstepMedia)
        {
            AddKlicCrossingReportMedia(panel);
        }
        else if (substepNumber == "4.1" && includeSubstepMedia)
        {
            AddBgtSurfaceReportMedia(panel);
        }

        return panel;
    }

    private static Border CreateReportRenderTable(ReportRenderTableBlock block)
    {
        var table = CreateReportTable(block.Headers.ToArray());
        foreach (var row in block.Rows)
        {
            AddReportTableRow(table, row.ToArray());
        }
        return table;
    }

    private static Border CreateReportRestPointsTable(IEnumerable<(string Item, string Priority, string Action)> rows)
    {
        var table = CreateReportTable(["#", "Restpunt", "Prioriteit", "Actie"]);
        var items = rows.ToList();
        for (var i = 0; i < items.Count; i++)
        {
            AddReportTableRow(table, [
                (i + 1).ToString(CultureInfo.InvariantCulture),
                items[i].Item,
                items[i].Priority,
                items[i].Action
            ]);
        }

        if (items.Count == 0)
        {
            AddReportTableRow(table, ["-", "Geen automatische restpunten gevonden", "-", "-"]);
        }

        return table;
    }

    private Border CreateReportRotatedLandscapePage(int pageNumber, string title, params UIElement[] sections)
    {
        var landscapePage = CreateReportLandscapePage(pageNumber, title, sections);
        landscapePage.Margin = new Thickness(0);
        landscapePage.Width = 1188;
        landscapePage.Height = 840;
        landscapePage.MinHeight = 840;
        landscapePage.HorizontalAlignment = HorizontalAlignment.Center;
        landscapePage.VerticalAlignment = VerticalAlignment.Center;
        landscapePage.RenderTransformOrigin = new Point(0.5, 0.5);
        landscapePage.RenderTransform = new RotateTransform(-90);

        var host = new Grid
        {
            Width = 840,
            Height = 1188,
            Background = Brushes.White,
            ClipToBounds = true
        };
        host.Children.Add(landscapePage);

        return CreateReportPageBorder(pageNumber, host);
    }

    private static Border CreateReportSection(int number, string title, UIElement content)
    {
        var root = new StackPanel();
        var headerParts = SplitReportSectionHeading(title, number);
        var hasNumber = !string.IsNullOrWhiteSpace(headerParts.Number);
        var isChapterHeading = hasNumber && !headerParts.Number.Contains('.', StringComparison.Ordinal);
        var header = new Grid { Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 7) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = hasNumber ? new GridLength(44) : new GridLength(0) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var numberText = new TextBlock
        {
            Text = headerParts.Number,
            Foreground = Brush("#333333"),
            FontSize = isChapterHeading ? 14.5 : 10.6,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 1, 8, 0)
        };
        header.Children.Add(numberText);

        var titleStack = new StackPanel();
        if (!string.IsNullOrWhiteSpace(headerParts.Chapter))
        {
            titleStack.Children.Add(new TextBlock
            {
                Text = headerParts.Chapter,
                Foreground = Brush("#555555"),
                FontSize = 8,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 1),
                TextWrapping = TextWrapping.Wrap
            });
        }
        titleStack.Children.Add(new TextBlock
        {
            Text = headerParts.Title,
            Foreground = Brush("#333333"),
            FontSize = isChapterHeading ? 14.5 : 10.8,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        Grid.SetColumn(titleStack, 1);
        header.Children.Add(titleStack);
        root.Children.Add(header);
        root.Children.Add(new Border { Background = Brushes.White, Padding = new Thickness(0), Child = content });

        return new Border
        {
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 16),
            Child = root
        };
    }

    private static Border CreateReportStepPreviewText(string text) => new()
    {
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(0),
        Margin = new Thickness(0, 3, 0, 10),
        Child = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#333333"),
            FontSize = 10.2,
            LineHeight = 14.2
        }
    };

    private static Border CreateReportStepTextBlock(string text)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Ingevuld rapportonderdeel",
            Foreground = Brush("#587080"),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 5)
        });
        panel.Children.Add(new TextBlock
        {
            Text = NormalizeReportText(text),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#334155"),
            FontSize = 12
        });

        return new Border
        {
            Background = Brush("#FBFCFD"),
            BorderBrush = Brush("#D7E8FA"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 12, 0, 0),
            Child = panel
        };
    }

    private static TextBlock CreateReportSubheading(string text) => new()
    {
        Text = text,
        Foreground = Brush("#333333"),
        FontSize = 9.8,
        FontWeight = FontWeights.Bold,
        Margin = new Thickness(0, 10, 0, 3)
    };

    private static UIElement CreateReportSubstepHeading(PrescanSubstep substep)
    {
        var header = new Grid { Margin = new Thickness(0, 2, 0, 7) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.Children.Add(new TextBlock
        {
            Text = DisplaySubstepNumber(substep),
            Foreground = Brush("#333333"),
            FontSize = 10.6,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 8, 0)
        });
        var title = new TextBlock
        {
            Text = substep.Title,
            Foreground = Brush("#333333"),
            FontSize = 10.6,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(title, 1);
        header.Children.Add(title);
        return header;
    }

    private static Border CreateReportSubstepOverviewTable(JsonElement root, int stepNumber)
    {
        var table = CreateReportTable(["Substap", "Status", "Bronnen"]);
        if (root.TryGetProperty("substeps", out var substeps) && substeps.ValueKind == JsonValueKind.Array)
        {
            foreach (var substep in substeps.EnumerateArray())
            {
                var substepNumber = JsonText(substep, "number", "-");
                AddReportTableRow(table, [
                    DisplaySubstepNumber(substepNumber),
                    JsonText(substep, "status", "-"),
                    JoinJsonStringArray(substep, "sourceKeys", "-")
                ]);
            }
        }

        if (table.Child is Grid { RowDefinitions.Count: <= 1 })
        {
            foreach (var substep in StepReportCatalog.GetSubsteps(stepNumber))
            {
                AddReportTableRow(table, [DisplaySubstepNumber(substep), "Nog niet opgeslagen", substep.Description]);
            }
        }

        return table;
    }

    private Canvas CreateReportSurfaceBar(double traceLength)
    {
        var canvas = new Canvas { Width = 760, Height = 140, Background = Brushes.White };
        var total = Math.Max(1, traceLength);
        var segments = GetBgtSurfaceSegments(total);
        const double left = 0;
        const double top = 28;
        const double width = 704;
        const double height = 26;
        AddCanvasText(canvas, "0 m", left, top + height + 7, "#587080", 11, FontWeights.Normal);

        if (segments.Count == 0)
        {
            AddCanvasRect(canvas, left, top, width, height, "#F8FAFC", "#CBD5E1", 1);
            AddCanvasText(canvas, "Geen BGT-oppervlakteprofiel beschikbaar", left + 12, top + 34, "#8FA6B2", 11, FontWeights.Normal);
            return canvas;
        }

        var axisY = top + height + 7;
        var shortLabelY = axisY + 15;
        var previousShortLabelEnd = left + 30;
        foreach (var segment in segments)
        {
            var segLeft = Math.Clamp(segment.Start / total * width, 0, width);
            var segWidth = Math.Max(3, Math.Clamp((segment.End - segment.Start) / total * width, 0, width - segLeft));
            AddCanvasRect(canvas, left + segLeft, top, segWidth, height, segment.Color, segment.Color, 1);
            if (segWidth > 56)
            {
                var insideLabel = segWidth > 130 ? $"{segment.Length:N0} m {segment.Label}" : $"{segment.Length:N0} m";
                AddCanvasText(canvas, insideLabel, left + segLeft + 6, top + 5, "#071422", 10.5, FontWeights.Bold);
            }
            else
            {
                var label = $"{segment.Length:N1} m";
                var estimatedWidth = label.Length * 6;
                var minLabelX = previousShortLabelEnd + 6;
                var maxLabelX = Math.Max(minLabelX, left + width - estimatedWidth);
                var labelX = Math.Clamp(left + segLeft + segWidth / 2 - estimatedWidth / 2, minLabelX, maxLabelX);
                AddCanvasText(canvas, label, labelX, shortLabelY, "#334155", 10, FontWeights.SemiBold);
                AddCanvasRect(canvas, left + segLeft + segWidth / 2, top + height, 1, shortLabelY - (top + height) - 2, "#94A3B8", "#94A3B8", 0);
                previousShortLabelEnd = labelX + estimatedWidth;
            }
        }

        var grouped = segments
            .GroupBy(segment => segment.Label, StringComparer.OrdinalIgnoreCase)
            .Select(group => new { Label = group.Key, Meters = group.Sum(segment => Math.Max(0, segment.End - segment.Start)), Color = group.First().Color })
            .OrderByDescending(item => item.Meters)
            .Take(4)
            .ToList();
        var legendY = shortLabelY + 20;
        var x = left;
        foreach (var item in grouped)
        {
            AddCanvasRect(canvas, x, legendY + 4, 10, 10, item.Color, item.Color, 1);
            AddCanvasText(canvas, $"{item.Label} {item.Meters:N0} m", x + 16, legendY, "#334155", 11, FontWeights.SemiBold);
            x += Math.Min(220, 78 + item.Label.Length * 6);
        }

        AddCanvasText(canvas, $"Totale lengte: {traceLength:N1} m", left, legendY + 22, "#334155", 11, FontWeights.Bold);
        return canvas;
    }

    private UIElement CreateReportSurfaceMapCard(double traceLength)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "BGT analysekaart langs boorlijn",
            Foreground = Brush("#071422"),
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var lockedMap = GetLiveMapReportPreviewImagePath(4);
        if (!string.IsNullOrWhiteSpace(lockedMap) && System.IO.File.Exists(lockedMap))
        {
            var image = new Image
            {
                Width = 760,
                Height = 265,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            ApplyLocalImageSource(image, lockedMap);
            panel.Children.Add(new Border
            {
                BorderBrush = Brush("#D7E8FA"),
                BorderThickness = new Thickness(1),
                Background = Brush("#F8FAFB"),
                Child = image
            });
            panel.Children.Add(CreateReportNote("Kaartbron: opgeslagen kaart uit stap 4 in de app. Deze kaart gebruikt de zichtbare GIS-lagen, boorlijn en actieve filters op het moment van opslaan."));
        }
        else
        {
            panel.Children.Add(CreateReportSurfaceBar(traceLength));
            panel.Children.Add(CreateReportNote("Geen opgeslagen BGT-kaart gevonden. Gebruik in stap 4 'Opslaan voor rapportage' voor een exacte kaartafbeelding in dit hoofdstuk."));
        }

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("#DEE6EA"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 12, 0, 8),
            Child = panel
        };
    }

    private static Border CreateReportSurfaceSegmentTable(IReadOnlyList<BgtSurfaceSegment> segments)
    {
        var table = CreateReportTable(["Oppervlak", "Van", "Tot", "Lengte"]);
        foreach (var segment in segments.Take(14))
        {
            AddReportTableRow(table, [
                segment.Label,
                $"{segment.Start:N1} m",
                $"{segment.End:N1} m",
                $"{segment.Length:N1} m"
            ]);
        }
        if (segments.Count == 0) AddReportTableRow(table, ["-", "Geen BGT-profiel", "-", "-"]);
        return table;
    }

    private static Border CreateReportTable(IReadOnlyList<string> headers)
    {
        return CreateReportTable(headers, null);
    }

    private static Border CreateReportTable(IReadOnlyList<string> headers, IReadOnlyList<double>? columnWeights)
    {
        var grid = new Grid { Margin = new Thickness(0, 7, 0, 0) };
        for (var i = 0; i < headers.Count; i++)
        {
            var weight = columnWeights is not null && i < columnWeights.Count ? Math.Max(0.1, columnWeights[i]) : 1;
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(weight, GridUnitType.Star) });
        }
        AddReportTableRow(grid, headers, true);
        return new Border
        {
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Background = Brushes.White,
            Child = grid
        };
    }

    private static Border CreateReportTracePointTable(IReadOnlyList<TracePointRow> rows)
    {
        var table = CreateReportTable(["#", "Rol", "X RD", "Y RD"]);
        foreach (var row in rows.Take(10))
        {
            AddReportTableRow(table, [
                row.Index.ToString(CultureInfo.InvariantCulture),
                row.Role,
                row.X.ToString("N2", CultureInfo.CurrentCulture),
                row.Y.ToString("N2", CultureInfo.CurrentCulture)
            ]);
        }
        if (rows.Count == 0) AddReportTableRow(table, ["-", "Geen opgeslagen boorlijn", "-", "-"]);
        return table;
    }
}
