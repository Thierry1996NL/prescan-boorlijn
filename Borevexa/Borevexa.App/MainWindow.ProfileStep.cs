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

// Dwarsprofiel (stap 7): profielpunten, profielcanvas, profielstaat,
// KLIC-kruisingen in het profiel en de bijbehorende rapportonderdelen.
// Fase 3-opsplitsing (07-07-2026) van MainWindow.xaml.cs; gedrag ongewijzigd.

public partial class MainWindow
{
    private void AddProfileActionButton(Panel parent, string label, int index, string action, string tooltip, bool enabled = true)
    {
        var button = new Button
        {
            Content = label,
            Tag = $"{action}:{index}",
            Width = 24,
            Height = 20,
            Padding = new Thickness(0),
            Margin = new Thickness(1),
            FontSize = 9,
            ToolTip = tooltip,
            IsEnabled = enabled
        };
        button.Click += ProfilePointAction_OnClick;
        parent.Children.Add(button);
    }

    private static void AddProfileEngineeringCell(Grid grid, int row, int column, string text, bool label)
    {
        var border = new Border
        {
            BorderBrush = Brush("#CBD5E1"),
            BorderThickness = new Thickness(column == 0 ? 0 : 1, row == 0 ? 1 : 0, 0, 1),
            Background = label ? Brush("#F8FAFC") : Brushes.White,
            Padding = new Thickness(6, 3, 6, 3),
            Child = new TextBlock
            {
                Text = text,
                FontSize = label ? 10.5 : 10,
                FontWeight = label ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = label ? Brush("#334155") : Brush("#071422"),
                TextAlignment = label ? TextAlignment.Left : TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap
            }
        };
        Grid.SetRow(border, row);
        Grid.SetColumn(border, column);
        grid.Children.Add(border);
    }

    private void AddProfileKlicLegendToPanel(Panel parent, IReadOnlyList<KlicProfileCrossing> crossings)
    {
        if (crossings.Count == 0) return;
        var wrap = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        wrap.Children.Add(new TextBlock
        {
            Text = "KLIC legenda:",
            Foreground = Brush("#334155"),
            FontSize = 10.5,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 8, 4)
        });
        for (var i = 0; i < Math.Min(12, crossings.Count); i++)
        {
            var crossing = crossings[i];
            wrap.Children.Add(new Border
            {
                BorderBrush = Brush(crossing.Color),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 2, 5, 2),
                Margin = new Thickness(0, 0, 6, 4),
                Child = new TextBlock
                {
                    Text = $"K{i + 1} {crossing.Label} {crossing.Distance:N1} m, ca. {crossing.Depth:N1} m diep",
                    Foreground = Brush(crossing.Color),
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 360
                }
            });
        }
        if (crossings.Count > 12)
        {
            wrap.Children.Add(new TextBlock { Text = $"+ {crossings.Count - 12} extra kruising(en)", Foreground = Brush("#64748B"), FontSize = 10, Margin = new Thickness(0, 2, 0, 4) });
        }
        parent.Children.Add(wrap);
    }

    private void AddStepSevenProfileRibbon()
    {
        var card = new Border
        {
            Background = Brush("#F8FAFB"),
            BorderBrush = Brush("#DEE6EA"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Dwarsprofiel",
            Foreground = Brush("#3F4750"),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var buttons = new UniformGrid { Columns = 1, Rows = 9 };
        AddBgtRibbonButton(buttons, "Genereer profiel", "Genereer profiel", true);
        AddBgtRibbonButton(buttons, "Boorlijn horizontaal", "Boorlijn horizontaal", true);
        AddBgtRibbonButton(buttons, "BGT oppervlakteanalyse", "BGT oppervlakteanalyse", false);
        AddBgtRibbonButton(buttons, "Kaart uitlijnen met profiel", "Kaart uitlijnen met profiel", false);
        AddBgtRibbonButton(buttons, _profileSmoothBore ? "Boorlijn hoekig" : "Boorlijn vloeiend", "Boorlijn vloeiend", false);
        AddBgtRibbonButton(buttons, _profileLayoutLocked ? "Profiel loszetten" : "Profiel vastzetten", "Profiel vastzetten", false);
        AddBgtRibbonButton(buttons, "+ Dieptepunt", "Voeg dieptepunt toe", false);
        AddBgtRibbonButton(buttons, "Sla dieptepunten op", "Sla dieptepunten op", false);
        AddBgtRibbonButton(buttons, "Download GeoJSON", "Download GeoJSON", false);
        panel.Children.Add(buttons);

        panel.Children.Add(new TextBlock
        {
            Text = "Kaart draait met de boorlijn mee. Het profiel eronder gebruikt dezelfde trace met X/Y en verstelbare Z/NAP-punten.",
            Foreground = Brush("#7F99AC"),
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        });

        card.Child = panel;
        StepSpecificRibbonPanel.Children.Add(card);
    }

    private string AlignBorelineHorizontalForProfile()
    {
        return AlignBorelineHorizontalForProfile(
            "Boorlijn horizontaal gezet",
            "De GIS-kaart wordt gedraaid en ingezoomd zodat de boorlijn horizontaal in beeld staat. Het dwarsprofiel gebruikt daarna dezelfde afstands-as.");
    }

    private string AlignBorelineHorizontalForProfile(string title, string body)
    {
        _profileAlignedToMap = true;
        _profileLayoutLocked = false;
        SendProfileModeToMap();
        SendMapMessage("{\"type\":\"profileAlignRequest\"}");
        SaveProfileVisualSettings();
        if (_selectedStep?.Number == ProfileStepNumber)
        {
            SaveMapStateForStep(ProfileStepNumber, false);
            QueueLiveMapReportCapture(ProfileStepNumber);
            RefreshInlineReportPreviewIfVisible();
        }
        RenderProfilePanel();
        return $"{title}\n\n{body}";
    }

    private string AlignProfileToMap()
    {
        return AlignBorelineHorizontalForProfile(
            "Dwarsprofiel uitlijnen aangevraagd",
            "De kaart boven het profiel stuurt de exacte schermposities van de boorpunten en BGT-oppervlaktes terug. Daardoor wordt de oppervlaktekaart horizontaal gelijkgezet met het dwarsprofiel.");
    }

    private static string BuildKlicProfileLabel(GeoJsonFeature feature, string theme)
    {
        var themeLabel = KlicThemeLabel(theme);
        var details = GetFeatureDetailProperties(feature);
        var content = GetDetailProperty(details, "networkContent");
        if (string.IsNullOrWhiteSpace(content)) return themeLabel;

        var manager = GetDetailProperty(details, "netbeheerderName");
        var suffix = string.IsNullOrWhiteSpace(manager) ? content : $"{content} ({manager})";
        return $"{themeLabel}: {TruncateText(suffix, 120)}";
    }

    private List<ProfileEngineeringSample> BuildProfileEngineeringSamples()
    {
        var total = Math.Max(0, _profilePoints[^1].Distance);

        // Snap elk label-punt naar een van de exacte afstanden die de boorlijn zelf
        // gebruikt om te tekenen (dezelfde bron als DrawProfileCanvas/
        // CreateReportProfileCanvas). Los berekende afstanden (bv. elke 3 m, afgerond
        // op 2 decimalen) konden net iets anders uitkomen dan de dicht bemonsterde
        // curve, en een Catmull-Rom-curve kan bij een steil stuk (zoals een uittrede
        // onder een grote hoek) tussen twee zulke net-niet-gelijke afstanden meetbaar
        // afwijken — het bolletje leek dan los van de lijn te staan.
        var denseRows = GetAhnSurfaceProfileRows(total);
        var denseDistances = (denseRows.Count > 0
                ? denseRows.Select(row => row.Distance)
                : _profilePoints.Select(point => point.Distance))
            .OrderBy(value => value)
            .ToArray();

        double SnapToDenseDistance(double target)
        {
            if (denseDistances.Length == 0) return target;
            var index = Array.BinarySearch(denseDistances, target);
            if (index >= 0) return denseDistances[index];
            index = ~index;
            if (index <= 0) return denseDistances[0];
            if (index >= denseDistances.Length) return denseDistances[^1];
            var before = denseDistances[index - 1];
            var after = denseDistances[index];
            return target - before <= after - target ? before : after;
        }

        var distances = new SortedSet<double>();
        for (var distance = 0d; distance <= total + 0.01; distance += 3d)
        {
            distances.Add(SnapToDenseDistance(Math.Min(total, distance)));
        }
        foreach (var point in _profilePoints) distances.Add(SnapToDenseDistance(point.Distance));
        distances.Add(SnapToDenseDistance(total));

        return distances.Select((distance, index) =>
        {
            // Zelfde dichte AHN4-maaiveldbron als de grafiek en de KLIC-kruisingen,
            // niet de oude sparse rechte-lijn-interpolatie tussen de 4 dieptepunten —
            // anders wijkt de Profielstaat-tabel af van wat de grafiek laat zien.
            var surface = InterpolateSurfaceNap(distance, total);
            var bore = InterpolateBoreNapAtDistance(distance);
            var depth = Math.Max(0, surface - bore);
            var angle = SegmentAngle(ProfileSegmentIndexAt(distance));
            var radius = EstimateVerticalRadiusAt(distance);
            var angleRadius = radius.HasValue ? $"{angle:+0.0;-0.0;0.0}° / Rv {radius.Value:N0}" : $"{angle:+0.0;-0.0;0.0}°";
            return new ProfileEngineeringSample(ProfileSampleCode(index), distance, BoreLengthAtDistance(distance), surface, bore, depth, _profilePoints[0].Nap - bore, angleRadius);
        }).ToList();
    }

    private object BuildProfileReportDataPayload(double traceLength)
    {
        EnsureProfilePoints();
        var crossings = GetVisibleKlicProfileCrossings(traceLength);
        return new
        {
            traceLength,
            profilePointCount = _profilePoints.Count,
            lowestNap = _profilePoints.Count == 0 ? (double?)null : _profilePoints.Min(point => point.Nap),
            points = _profilePoints.Select(point => new
            {
                point.Index,
                point.Distance,
                point.Depth,
                point.Surface,
                point.Nap
            }),
            klicCrossings = crossings.Select((crossing, index) => new
            {
                code = $"K{index + 1}",
                crossing.Distance,
                crossing.Nap,
                crossing.Label,
                crossing.Theme,
                crossing.Color,
                crossing.Depth,
                depthStatus = crossing.IsIndicativeDepth ? "indicatief per thema" : "bronwaarde"
            }),
            mapLocked = IsMapReportLocked(ProfileStepNumber)
        };
    }

    private static double CalculateProfileCanvasHeight(double width, bool expanded)
    {
        var target = width * (expanded ? 0.56 : 0.48);
        return expanded
            ? Math.Clamp(target, 430d, 610d)
            : Math.Clamp(target, 300d, 390d);
    }

    private static double CalculateProfileViewportHeight(double canvasHeight, bool expanded)
    {
        var target = canvasHeight + 28;
        return expanded
            ? Math.Clamp(target, 460d, 650d)
            : Math.Clamp(target, 330d, 420d);
    }

    private void CaptureProfileScreenMetrics(string message)
    {
        try
        {
            if (_profileLayoutLocked && _profileScreenMetrics is not null && !_surfaceAnalysisMetricsRefreshPending)
            {
                return;
            }

            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            var mapWidth = root.TryGetProperty("mapWidth", out var mapWidthElement) && mapWidthElement.TryGetDouble(out var mapWidthValue)
                ? mapWidthValue
                : 0;

            if (mapWidth <= 0 || !root.TryGetProperty("points", out var pointsElement) || pointsElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            var points = pointsElement.EnumerateArray()
                .Select(pointElement =>
                {
                    var index = pointElement.TryGetProperty("index", out var indexElement) && indexElement.TryGetInt32(out var indexValue)
                        ? indexValue
                        : 0;
                    var x = pointElement.TryGetProperty("x", out var xElement) && xElement.TryGetDouble(out var xValue)
                        ? xValue
                        : double.NaN;
                    return new ProfileScreenPoint(index, x);
                })
                .Where(point => point.Index > 0 && double.IsFinite(point.X))
                .OrderBy(point => point.Index)
                .ToList();

            if (points.Count < 2) return;

            _profileScreenMetrics = new ProfileScreenMetrics(mapWidth, points);
            _mapBgtSurfaceSamples = ReadMapBgtSurfaceSamples(root);
            if (_selectedProject is not null && _selectedStep?.Number == 4)
            {
                EnsureProfilePoints();
                var total = GetSurfaceAnalysisTraceLength();
                var segments = GetBgtSurfaceSegments(total);
                if (_surfaceAnalysisMetricsRefreshPending && segments.Count == 0)
                {
                    _surfaceAnalysisMetricsRefreshPending = false;
                    ActivateWorkflowPart(4, "oppervlakte");
                    SurfaceAnalysisBarCanvas.Children.Clear();
                    SurfaceAnalysisTablePanel.Children.Clear();
                    SurfaceAnalysisSummaryBar.Visibility = Visibility.Visible;
                    SurfaceAnalysisSummaryBarText.Text = "Geen actuele BGT-kaartmeting ontvangen.";
                    SurfaceAnalysisSummaryText.Text = "Controleer of BGT import zichtbaar is en voer de analyse opnieuw uit. Er is niets overschreven in de rapportdata.";
                    return;
                }

                _surfaceAnalysisMetricsRefreshPending = false;
                SaveSurfaceAnalysisSnapshot(total, segments);
                UpdateSurfaceAnalysisSummaryBar(segments, total);
                ActivateWorkflowPart(4, "oppervlakte");
                RenderSurfaceAnalysisPanel();
                RefreshWorkflowReportStatus(4);
                if (_selectedReportPreviewStepNumber == 4)
                {
                    RenderStepReportPreview(4);
                }
            }
            else
            {
                _surfaceAnalysisMetricsRefreshPending = false;
            }
            if ((_selectedStep?.Number == ProfileStepNumber || _selectedStep?.Number == WorkDrawingStepNumber) && _profileAlignedToMap)
            {
                RenderProfilePanel();
            }
        }
        catch
        {
            _profileScreenMetrics = null;
            _surfaceAnalysisMetricsRefreshPending = false;
        }
    }

    private UIElement CreateInlineAhnSurfaceProfileReportPage(int stepNumber, PrescanSubstep substep)
    {
        EnsureProfilePoints();
        var total = GetSurfaceAnalysisTraceLength();
        var rows = GetAhnSurfaceProfileRows(total);
        var mapPath = GetLiveMapReportPreviewImagePath(4);
        var minSurface = rows.Count == 0 ? 0 : rows.Min(row => row.Surface);
        var maxSurface = rows.Count == 0 ? 0 : rows.Max(row => row.Surface);

        var panel = new StackPanel();
        panel.Children.Add(CreateReportSubheading("GIS kaart met boorlijn en AHN4"));
        panel.Children.Add(CreateLiveMapReportImageCard(
            "GIS kaart met boorlijn en AHN4 maaiveldcontext",
            "Vastgezette rapportuitsnede uit stap 4 met boorlijn en zichtbare kaartlagen.",
            mapPath,
            4,
            null));
        panel.Children.Add(CreateReportSubheading("AHN4 maaiveldhoogteprofiel"));
        panel.Children.Add(new Border
        {
            BorderBrush = Brush("#D7E8FA"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Background = Brush("#FFFFFF"),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 4, 0, 12),
            Child = new Viewbox
            {
                Child = CreateReportAhnSurfaceHeightProfile(total, rows),
                MaxWidth = 640,
                HorizontalAlignment = HorizontalAlignment.Left,
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly
            }
        });
        panel.Children.Add(CreateReportKeyValues(
            ("Boorlijnlengte", $"{total:N1} m"),
            ("Profielpunten", rows.Count.ToString(CultureInfo.InvariantCulture)),
            ("Maaiveld minimaal", rows.Count == 0 ? "-" : $"{minSurface:N2} m NAP"),
            ("Maaiveld maximaal", rows.Count == 0 ? "-" : $"{maxSurface:N2} m NAP"),
            ("Kaartstatus", string.IsNullOrWhiteSpace(mapPath) ? "Nog geen kaartcapture" : "Opgeslagen voor rapportage"),
            ("Bron", "AHN4/maaiveldprofiel langs vastgelegde boorlijn")));
        panel.Children.Add(CreateReportNote(rows.Count < 2
            ? "Geen maaiveldprofiel beschikbaar. Genereer of sla de oppervlakteanalyse/profielpunten opnieuw op."
            : "Dit profiel toont de maaiveldhoogte langs de boorlijn als apart AHN4-rapportblok."));

        var sectionTitle = $"{DisplayReportSectionTitle(substep)} - AHN4 maaiveldprofiel";
        return CreateReportPage(stepNumber, sectionTitle, CreateReportSection(stepNumber, sectionTitle, panel));
    }

    private IReadOnlyList<UIElement> CreateInlineProfileEngineeringReportPages(int stepNumber, PrescanSubstep substep)
    {
        EnsureProfilePoints();
        var mapPanel = new StackPanel();
        var profileMapPath = GetLiveMapReportPreviewImagePath(ProfileStepNumber);
        mapPanel.Children.Add(CreateReportSubheading("GIS kaart met horizontale boorlijn"));
        mapPanel.Children.Add(CreateLiveMapReportImageCard(
            "GIS kaart met horizontale boorlijn",
            "Live rapportuitsnede uit stap 7.1: boorlijn horizontaal, kaartlagen zoals zichtbaar in de app.",
            profileMapPath,
            ProfileStepNumber,
            null,
            imageWidth: 648,
            imageHeight: 286));
        mapPanel.Children.Add(CreateReportNote(string.IsNullOrWhiteSpace(profileMapPath)
            ? "De profielkaart wordt gevuld zodra je 'Boorlijn horizontaal' of 'Opslaan voor rapportage' gebruikt."
            : "Deze kaartcapture volgt de horizontale GIS-weergave van stap 7.1. Bedieningsknoppen worden bij de capture verborgen. De dieptepunten (S/2/3/E) staan als oranje markering op de boorlijn."));
        mapPanel.Children.Add(CreateReportSubheading("Dwarsprofiel-punten"));
        mapPanel.Children.Add(CreateReportProfilePointTable(_profilePoints));
        var mapSectionTitle = $"{DisplayReportSectionTitle(substep)} - kaartbijlage";

        // Het dwarsprofiel en de segmententabel staan op een NORMALE portret-A4-pagina
        // (zelfde koptekst/voettekst-opmaak als de rest van het rapport), maar de
        // inhoud zelf (grafiek + tabellen) is 90° gedraaid — de lange boorlijn past zo
        // dwars over de pagina. Alleen de content roteren (niet de hele pagina, zoals
        // eerder geprobeerd via CreateReportRotatedLandscapePage) voorkomt scheve
        // koptekst en bijgesneden randen.
        var profilePanel = new StackPanel();
        profilePanel.Children.Add(CreateReportSubheading("Horizontaal dwarsprofiel"));
        profilePanel.Children.Add(new Viewbox
        {
            Child = CreateRotatedReportContent(CreateReportProfileCanvas(), 270),
            MaxWidth = 664,
            HorizontalAlignment = HorizontalAlignment.Left,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            Margin = new Thickness(0, 2, 0, 0)
        });
        profilePanel.Children.Add(CreateReportNote(_profilePoints.Count < 2
            ? $"Geen dwarsprofiel beschikbaar. Teken eerst een boorlijn in stap {DisplayStepNumber(3)}.1 en genereer daarna het profiel in stap {DisplayStepNumber(ProfileStepNumber)}.1."
            : "Het horizontale profiel wordt opgebouwd uit de opgeslagen boorlijn, profielpunten, maaiveldlijn, BGT-oppervlaktes en zichtbare KLIC-kruisingen. Maaiveldhoogtes komen uit AHN4 DTM waar beschikbaar; KLIC-dieptes zijn indicatief per thema tenzij de bron expliciete dieptegegevens bevat."));
        profilePanel.Children.Add(CreateReportSubheading("KLIC-kruisingen in profiel"));
        profilePanel.Children.Add(CreateReportKlicCrossingTable());
        profilePanel.Children.Add(CreateReportNote("KLIC-kruisingen in het dwarsprofiel zijn 2D-kruisingen met een indicatieve diepteligging. Gebruik de originele KLIC-documentatie en proefsleuven/grondradar voor definitieve verticale ligging."));
        var sectionTitle = $"{DisplayReportSectionTitle(substep)} - horizontaal profiel";

        // De segmentinformatie mag gewoon volledig horizontaal blijven (niet gedraaid).
        // De "Profielstaat boring"-tabel heeft één kolom per sample en kan bij een
        // lange boorlijn veel te breed worden voor de paginabreedte — die knipt
        // CreateReportProfileEngineeringTable daarom zelf op in meerdere blokken die
        // onder elkaar staan, in plaats van te roteren.
        var tablePanel = new StackPanel();
        tablePanel.Children.Add(CreateReportSubheading("Profielstaat boring"));
        tablePanel.Children.Add(CreateReportProfileEngineeringTable());
        tablePanel.Children.Add(CreateReportSubheading("Boorpunten en segmenten"));
        tablePanel.Children.Add(CreateReportProfileSegmentTable());
        var tableTitle = $"{DisplayReportSectionTitle(substep)} - profielstaat";

        return [
            CreateReportPage(stepNumber, mapSectionTitle, CreateReportSection(stepNumber, mapSectionTitle, mapPanel)),
            CreateReportPage(stepNumber, sectionTitle, CreateReportSection(stepNumber, sectionTitle, profilePanel)),
            CreateReportPage(stepNumber, tableTitle, CreateReportSection(stepNumber, tableTitle, tablePanel))
        ];
    }

    // Roteert alleen de inhoud 90° (via LayoutTransform, niet RenderTransform) zodat
    // de omringende Viewbox de GEROTEERDE afmetingen (breedte/hoogte omgewisseld) als
    // gemeten grootte ziet en er correct naar de beschikbare paginabreedte op schaalt
    // — in tegenstelling tot het roteren van een hele, kant-en-klare pagina (koptekst/
    // voettekst incluis), wat eerder tot bijgesneden/scheve content leidde.
    private static UIElement CreateRotatedReportContent(UIElement content, double angle = 90)
    {
        var host = new Grid { LayoutTransform = new RotateTransform(angle) };
        host.Children.Add(content);
        return host;
    }

    private Canvas CreateReportAhnSurfaceHeightProfile(double traceLength, IReadOnlyList<(double Distance, double Surface, double BoreNap)> rows)
    {
        const double canvasHeight = 262;
        var canvas = new Canvas { Width = 760, Height = canvasHeight, Background = Brushes.White };
        AddCanvasRect(canvas, 0, 0, 760, canvasHeight, "#FFFFFF", "#CBD5E1", 1);
        AddCanvasText(canvas, "AHN4 maaiveldhoogteprofiel langs boorlijn", 20, 16, "#071422", 13, FontWeights.Bold);

        const double left = 70;
        const double top = 54;
        const double plotWidth = 642;
        const double plotHeight = 128;
        AddCanvasRect(canvas, left, top, plotWidth, plotHeight, "#F8FAFC", "#E5E7EB", 1);

        if (rows.Count < 2)
        {
            AddCanvasText(canvas, "Geen AHN4/maaiveldprofiel beschikbaar", left + 185, top + 58, "#8FA6B2", 12, FontWeights.Normal);
            return canvas;
        }

        var total = Math.Max(1, Math.Max(traceLength, rows.Max(row => row.Distance)));
        var minValue = Math.Floor(rows.Min(row => row.Surface) - 0.5);
        var maxValue = Math.Ceiling(rows.Max(row => row.Surface) + 0.5);
        var range = Math.Max(1, maxValue - minValue);
        double X(double distance) => left + Math.Clamp(distance / total, 0, 1) * plotWidth;
        double Y(double value) => top + (maxValue - value) / range * plotHeight;

        for (var i = 0; i <= 4; i++)
        {
            var y = top + plotHeight * i / 4;
            var value = maxValue - range * i / 4;
            AddCanvasLine(canvas, left, y, left + plotWidth, y, "#E5ECF0", 1, null);
            AddCanvasText(canvas, $"{value:N1}", 24, y - 7, "#64748B", 9.5, FontWeights.Normal);
        }

        var distanceStep = total <= 40 ? 10 : total <= 100 ? 20 : 50;
        for (var distance = 0d; distance <= total + 0.001; distance += distanceStep)
        {
            var x = X(distance);
            AddCanvasLine(canvas, x, top, x, top + plotHeight, "#EEF2F4", 1, null);
            AddCanvasText(canvas, $"{distance:N0}", x - 8, top + plotHeight + 7, "#64748B", 9, FontWeights.Normal);
        }

        AddCanvasText(canvas, "m NAP", 18, top - 20, "#64748B", 9.5, FontWeights.SemiBold);
        var captionY = top + plotHeight + 26;
        AddCanvasText(canvas, $"Afstand langs boorlijn 0 - {total:N1} m", left, captionY, "#334155", 10.5, FontWeights.SemiBold);

        var surfaceCoordinates = rows.SelectMany(row => new[] { X(row.Distance), Y(row.Surface) }).ToArray();
        AddCanvasPolyline(canvas, "#059669", 2.8, surfaceCoordinates);

        // Legend goes on its own row well below the "Afstand langs boorlijn" caption so
        // the two never overlap (they used to sit almost on top of each other here).
        var legendY = captionY + 24;
        AddCanvasRect(canvas, left, legendY + 2, 10, 10, "#059669", "#059669", 1);
        AddCanvasText(canvas, "Maaiveld AHN4", left + 16, legendY, "#334155", 10.5, FontWeights.SemiBold);
        return canvas;
    }

    private Canvas CreateReportProfileCanvas()
    {
        EnsureProfilePoints();
        LoadProfileVisualSettings();
        // De pagina wordt een kwartslag gedraaide landscape-pagina (zie
        // CreateReportRotatedLandscapePage), dus deze canvas gebruikt landscape-
        // verhoudingen: breed genoeg voor de lange boorlijn, met een gematigde hoogte
        // in plaats van de extreme verticale uitrekking die nodig was om in een smalle
        // portret-pagina te passen.
        const double canvasWidth = 1000;
        const double canvasHeight = 640;
        var canvas = new Canvas { Width = canvasWidth, Height = canvasHeight, Background = Brushes.White, Margin = new Thickness(0, 12, 0, 0) };
        AddCanvasRect(canvas, 0, 0, canvasWidth, canvasHeight, "#FFFFFF", "#CBD5E1", 1);
        AddCanvasText(canvas, "Horizontaal dwarsprofiel langs boorlijn", 20, 15, "#071422", 13, FontWeights.Bold);

        if (_profilePoints.Count < 2)
        {
            AddCanvasRect(canvas, 38, 54, canvasWidth - 76, canvasHeight - 88, "#F8FAFC", "#E5E7EB", 1);
            AddCanvasText(canvas, "Geen opgeslagen dwarsprofiel beschikbaar", 82, 168, "#8FA6B2", 12, FontWeights.Normal);
            return canvas;
        }

        if (_profileGeometryDirty)
        {
            RecalculateProfileRolesAndNap();
            _profileGeometryDirty = false;
        }
        var minDistance = 0d;
        var maxDistance = Math.Max(1, _profilePoints.Max(point => point.Distance));
        var klicCrossings = GetVisibleKlicProfileCrossings(maxDistance);
        // Maaiveldlijn wordt dicht bemonsterd langs de hele boorlijn (zie verderop), dus
        // de verticale schaal moet ook dat bereik meenemen, niet alleen de 4 dieptepunten.
        var denseSurfaceRows = GetAhnSurfaceProfileRows(maxDistance);
        var minValue = Math.Min(_profilePoints.Min(point => point.Nap), _profilePoints.Min(point => point.Surface));
        var maxValue = Math.Max(_profilePoints.Max(point => point.Nap), _profilePoints.Max(point => point.Surface));
        if (denseSurfaceRows.Count > 0)
        {
            minValue = Math.Min(minValue, denseSurfaceRows.Min(row => row.Surface));
            maxValue = Math.Max(maxValue, denseSurfaceRows.Max(row => row.Surface));
        }
        if (klicCrossings.Count > 0)
        {
            minValue = Math.Min(minValue, klicCrossings.Min(crossing => crossing.Nap));
            maxValue = Math.Max(maxValue, klicCrossings.Max(crossing => crossing.Nap));
        }

        minValue = Math.Floor(minValue - 0.75);
        maxValue = Math.Ceiling(maxValue + 0.75);
        var range = Math.Max(1, maxValue - minValue);
        const double left = 70;
        const double top = 78;
        const double plotWidth = canvasWidth - left - 32;
        const double plotHeight = 460;
        var plotBottom = top + plotHeight;

        // Horizontal legend row, kept above the plot so it never overlaps the graph.
        const double legendY = 15;
        var legendX = 372d;
        AddCanvasLine(canvas, legendX, legendY + 6, legendX + 22, legendY + 6, "#475569", 2.2, null);
        AddCanvasText(canvas, "Maaiveld / AHN", legendX + 27, legendY, "#334155", 9, FontWeights.Normal);
        AddCanvasLine(canvas, legendX + 118, legendY + 6, legendX + 140, legendY + 6, "#E11D48", 3.2, null);
        AddCanvasText(canvas, "Hartlijn boring", legendX + 145, legendY, "#334155", 9, FontWeights.Normal);
        if (klicCrossings.Count > 0)
        {
            AddCanvasCircle(canvas, legendX + 240, legendY + 6, 4, klicCrossings[0].Color, "#FFFFFF", 1);
            AddCanvasText(canvas, "KLIC-kruising", legendX + 250, legendY, "#334155", 9, FontWeights.Normal);
        }

        double X(ProfilePointRow point) => XDistance(point.Distance);
        double XDistance(double distance) => left + Math.Clamp((distance - minDistance) / (maxDistance - minDistance), 0, 1) * plotWidth;
        double Y(double value) => top + (maxValue - value) / range * plotHeight;

        AddCanvasRect(canvas, left, top, plotWidth, plotHeight, "#F8FAFC", "#E5E7EB", 1);
        for (var i = 0; i <= 5; i++)
        {
            var y = top + plotHeight * i / 5;
            var value = maxValue - range * i / 5;
            AddCanvasLine(canvas, left, y, left + plotWidth, y, "#E5ECF0", 1, null);
            AddCanvasText(canvas, $"{value:N1}", 24, y - 7, "#64748B", 9.5, FontWeights.Normal);
        }

        var distanceStep = maxDistance <= 40 ? 10 : maxDistance <= 100 ? 20 : 50;
        for (var distance = 0d; distance <= maxDistance + 0.001; distance += distanceStep)
        {
            var x = XDistance(distance);
            AddCanvasLine(canvas, x, top, x, top + plotHeight, "#EEF2F4", 1, null);
            AddCanvasText(canvas, $"{distance:N0}", x - 8, top + plotHeight + 7, "#64748B", 9, FontWeights.Normal);
        }

        AddCanvasText(canvas, "m NAP", 18, top - 30, "#64748B", 9.5, FontWeights.SemiBold);
        AddCanvasText(canvas, "Afstand langs boorlijn (m)", left + plotWidth / 2 - 72, plotBottom + 24, "#64748B", 9.5, FontWeights.SemiBold);

        var segments = GetBgtSurfaceSegments(maxDistance);
        if (segments.Count > 0)
        {
            var stripTop = top - 18;
            const double stripHeight = 13;
            AddCanvasText(canvas, "BGT", 18, stripTop + 1, "#64748B", 8.5, FontWeights.Bold);
            // Een smal segment (bv. 0,6 m groenstrook of 6 m water) kreeg voorheen
            // helemaal geen label (w > 60 gate), waardoor de lengte nergens afleesbaar
            // was. Zet die labels op een eigen regel onder de strip met een klein
            // aanwijslijntje, net als bij de BGT-oppervlaktebalk in stap 4.
            var shortLabelY = stripTop + stripHeight + 11;
            var previousShortLabelEnd = left;
            foreach (var segment in segments)
            {
                var x = XDistance(segment.Start);
                var w = Math.Max(2, XDistance(segment.End) - x);
                AddCanvasRect(canvas, x, stripTop, w, stripHeight, segment.Color, "#FFFFFF", 0.5);
                if (w > 60)
                {
                    AddCanvasText(canvas, $"{segment.Length:N0} m {segment.Label}", x + 4, stripTop + 1, "#071422", 8, FontWeights.SemiBold);
                }
                else
                {
                    var shortLabel = $"{segment.Length:N1} m";
                    var estimatedWidth = shortLabel.Length * 5.2;
                    var minLabelX = previousShortLabelEnd + 4;
                    var maxLabelX = Math.Max(minLabelX, left + plotWidth - estimatedWidth);
                    var labelX = Math.Clamp(x + w / 2 - estimatedWidth / 2, minLabelX, maxLabelX);
                    AddCanvasLine(canvas, x + w / 2, stripTop + stripHeight, x + w / 2, shortLabelY - 2, "#94A3B8", 1, null);
                    AddCanvasText(canvas, shortLabel, labelX, shortLabelY, "#475569", 7.5, FontWeights.SemiBold);
                    previousShortLabelEnd = labelX + estimatedWidth;
                }
            }
        }

        var surface = new List<double>();
        var bore = new List<double>();
        var borePoints = new List<Point>();

        // Boorlijn (hartlijn) dicht bemonsterd via InterpolateBoreNapAtDistance — de
        // ZELFDE functie die de dieptepunt-bolletjes hieronder gebruikt. Eerst werd de
        // lijn als Bezier-spline door slechts de 4 dieptepunten getekend (uniforme
        // parametrisatie), terwijl de bolletjes een afstand-geparametriseerde Catmull-
        // Rom gebruikten — bij ongelijk verdeelde dieptepunten (0/25/65/100%) geven die
        // twee methodes een net iets andere curve, waardoor de bolletjes van de lijn
        // afdreven. Door dezelfde bron dicht te bemonsteren vallen ze altijd samen.
        var boreSampleDistances = denseSurfaceRows.Count > 0
            ? denseSurfaceRows.Select(row => row.Distance).ToList()
            : _profilePoints.Select(point => point.Distance).ToList();
        foreach (var distance in boreSampleDistances)
        {
            var boreNap = InterpolateBoreNapAtDistance(distance);
            bore.Add(XDistance(distance));
            bore.Add(Y(boreNap));
            borePoints.Add(new Point(XDistance(distance), Y(boreNap)));
        }

        // Maaiveldlijn dicht bemonsterd langs de hele boorlijn (zelfde bron als de AHN4-
        // grafiek in stap 4), niet alleen op de 4 dieptepunten.
        if (denseSurfaceRows.Count > 0)
        {
            foreach (var row in denseSurfaceRows)
            {
                surface.Add(XDistance(row.Distance));
                surface.Add(Y(row.Surface));
            }
        }
        else
        {
            foreach (var point in _profilePoints)
            {
                surface.Add(X(point));
                surface.Add(Y(point.Surface));
            }
        }

        var boringDiameterMeters = Math.Max(0.075, GetBoringDiameterMillimeters() / 1000d);
        var boreBandThickness = Math.Max(3, boringDiameterMeters * plotHeight / Math.Max(0.1, range));
        AddCanvasPolyline(canvas, "#475569", 2.2, surface.ToArray());

        var boreBand = new Polyline
        {
            Stroke = Brush("#FDA4AF"),
            StrokeThickness = boreBandThickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Opacity = 0.55
        };
        foreach (var point in borePoints)
        {
            boreBand.Points.Add(point);
        }
        canvas.Children.Add(boreBand);
        AddCanvasPolyline(canvas, "#E11D48", 3.2, bore.ToArray());

        // Alle sample-letters tonen, niet alleen "a/e/j/o" — de live Dwarsprofiel-
        // grafiek toont ze ook allemaal (DrawProfileEngineeringMarkers).
        foreach (var sample in BuildProfileEngineeringSamples())
        {
            var x = XDistance(sample.Distance);
            var y = Y(sample.BoreNap);
            AddCanvasCircle(canvas, x, y, 3.4, "#64748B", "White", 1);
            AddCanvasText(canvas, sample.Code, x + 4, y + 7, "#475569", 8.5, FontWeights.Bold);
        }

        // KLIC crossings: dashed guide line + a marker on the bore line, with the
        // K-labels de-cluttered into up to two rows along the top of the plot so they
        // never overlap each other or the bore-line labels, even when several
        // crossings sit close together.
        const double klicLabelWidth = 22;
        const double klicLabelHeight = 14;
        const double klicLabelGap = 3;
        var maxKlicLabelLeft = left + plotWidth - klicLabelWidth - 2;
        var klicRowNextLeft = new[] { left + 2, left + 2 };
        var klicRowY = new[] { top + 4, top + 4 + klicLabelHeight + klicLabelGap };
        for (var i = 0; i < klicCrossings.Count; i++)
        {
            var crossing = klicCrossings[i];
            var x = XDistance(crossing.Distance);
            var y = Y(crossing.Nap);
            AddCanvasLine(canvas, x, top, x, top + plotHeight, crossing.Color, 1, [2, 3]);
            AddCanvasCircle(canvas, x, y, 4.2, crossing.Color, "#FFFFFF", 1.2);

            var idealLeft = Math.Clamp(x - klicLabelWidth / 2, left + 2, maxKlicLabelLeft);
            var row = klicRowNextLeft[0] <= idealLeft ? 0
                : klicRowNextLeft[1] <= idealLeft ? 1
                : klicRowNextLeft[0] <= klicRowNextLeft[1] ? 0 : 1;
            var labelLeft = Math.Min(maxKlicLabelLeft, Math.Max(idealLeft, klicRowNextLeft[row]));
            klicRowNextLeft[row] = labelLeft + klicLabelWidth + klicLabelGap;
            var rowTop = klicRowY[row];
            var labelCenter = labelLeft + klicLabelWidth / 2;
            AddCanvasLine(canvas, labelCenter, rowTop + klicLabelHeight, x, Math.Min(y - 5, top + 52), crossing.Color, 0.8, [2, 2]);
            AddCanvasRect(canvas, labelLeft, rowTop, klicLabelWidth, klicLabelHeight, "#FFFFFF", crossing.Color, 0.8);
            AddCanvasText(canvas, $"K{i + 1}", labelLeft + 3, rowTop + 2, crossing.Color, 8.5, FontWeights.Bold);
        }

        foreach (var point in _profilePoints)
        {
            AddCanvasCircle(canvas, X(point), Y(point.Nap), 4, "#F97316", "White", 1);
            var label = point.Index == 1 ? "S" : point.Index == _profilePoints.Count ? "E" : point.Index.ToString(CultureInfo.InvariantCulture);
            AddCanvasText(canvas, label, X(point) + 6, Y(point.Nap) - 13, "#111827", 9.5, FontWeights.Bold);
        }

        // Segmentafstand + -hoek per stuk boorlijn (bv. "25,9 m -7,8°") — deze stond al
        // in de live Dwarsprofiel-grafiek maar ontbrak hier in de rapportversie.
        for (var i = 0; i < _profilePoints.Count - 1; i++)
        {
            if (SegmentDistance(i) < 2.5) continue;
            var a = _profilePoints[i];
            var b = _profilePoints[i + 1];
            var midX = (X(a) + X(b)) / 2;
            var midY = (Y(a.Nap) + Y(b.Nap)) / 2 - 24 - (i % 2) * 18;
            var angle = SegmentAngle(i);
            var color = angle < -8 ? "#DC2626" : angle > 8 ? "#16A34A" : "#F97316";
            var segmentLabel = $"{SegmentDistance(i):N1} m  {angle:+0.0;-0.0;0.0}°";
            var labelWidth = 16 + segmentLabel.Length * 5.4;
            var labelLeft = Math.Clamp(midX - labelWidth / 2, left + 2, left + plotWidth - labelWidth - 2);
            AddCanvasRect(canvas, labelLeft, midY, labelWidth, 18, "#FFFFFF", color, 1);
            AddCanvasText(canvas, segmentLabel, labelLeft + 6, midY + 3, color, 9.5, FontWeights.Bold);
        }

        var summaryLeft = 20d;
        var summaryTop = plotBottom + 46;
        AddCanvasText(canvas, $"Lengte: {maxDistance:N1} m", summaryLeft, summaryTop, "#334155", 10.5, FontWeights.SemiBold);
        AddCanvasText(canvas, $"Max. diepte: {_profilePoints.Max(point => point.Depth):N2} m", summaryLeft + 138, summaryTop, "#334155", 10.5, FontWeights.SemiBold);
        AddCanvasText(canvas, $"Laagste hartlijn: {_profilePoints.Min(point => point.Nap):N2} m NAP", summaryLeft + 316, summaryTop, "#334155", 10.5, FontWeights.SemiBold);
        AddCanvasText(canvas, $"Boring: Ø{GetBoringDiameterMillimeters()} mm", summaryLeft + 552, summaryTop, "#334155", 10.5, FontWeights.SemiBold);

        if (klicCrossings.Count > 0)
        {
            var klicRowTop = summaryTop + 22;
            AddCanvasText(canvas, "KLIC:", summaryLeft, klicRowTop, "#334155", 9.5, FontWeights.Bold);
            var legendText = string.Join("   ", klicCrossings.Take(5).Select((crossing, index) => $"K{index + 1} {TruncateText(crossing.Theme, 18)} {crossing.Distance:N1} m"));
            AddCanvasText(canvas, legendText + (klicCrossings.Count > 5 ? $"   +{klicCrossings.Count - 5}" : ""), summaryLeft + 38, klicRowTop, "#334155", 9, FontWeights.Normal);
        }
        return canvas;
    }

    private Border CreateReportProfileEngineeringTable()
    {
        var border = new Border
        {
            BorderBrush = Brush("#DEE6EA"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Background = Brushes.White,
            Padding = new Thickness(10),
            Margin = new Thickness(0, 10, 0, 0)
        };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Profielstaat boring", Foreground = Brush("#334155"), FontSize = 12, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
        if (_profilePoints.Count < 2)
        {
            panel.Children.Add(new TextBlock { Text = "Geen profielstaat beschikbaar.", Foreground = Brush("#7F99AC"), FontSize = 11 });
            border.Child = panel;
            return border;
        }

        // Voorheen .Take(12): bij een langere boorlijn (elke 3 m een sample) vielen dan
        // stilzwijgend de meeste segmenten buiten het rapport. Toon ze allemaal.
        var samples = BuildProfileEngineeringSamples();
        var rows = new (string Label, Func<ProfileEngineeringSample, string> Value)[]
        {
            ("Code", sample => sample.Code),
            ("Afstand", sample => $"{sample.Distance:N2}"),
            ("Boorlengte", sample => $"{sample.BoreLength:N2}"),
            ("Maaiveld NAP", sample => $"{sample.SurfaceNap:N2}"),
            ("Boring NAP", sample => $"{sample.BoreNap:N2}"),
            ("Diepte", sample => $"{sample.Depth:N2}")
        };
        // Deze tabel heeft één kolom per sample (elke ~3 m langs de boorlijn), dus bij
        // een langere boorlijn wordt hij al snel breder dan een A4-pagina. In plaats
        // van roteren of afkappen: knip de samples op in blokken die wel binnen de
        // paginabreedte passen, en zet die blokken (elk met de rijlabels herhaald)
        // onder elkaar — "een kolom erbij beginnen" zodra het niet meer past.
        const int labelColumnWidth = 100;
        const int sampleColumnWidth = 45;
        const int maxGridWidth = 620;
        var samplesPerBlock = Math.Max(1, (maxGridWidth - labelColumnWidth) / sampleColumnWidth);

        for (var blockStart = 0; blockStart < samples.Count; blockStart += samplesPerBlock)
        {
            var blockSamples = samples.Skip(blockStart).Take(samplesPerBlock).ToList();
            var grid = new Grid { MinWidth = labelColumnWidth + blockSamples.Count * sampleColumnWidth, Margin = new Thickness(0, blockStart == 0 ? 0 : 10, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelColumnWidth) });
            foreach (var _ in blockSamples) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(sampleColumnWidth) });
            for (var i = 0; i < rows.Length; i++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
            for (var row = 0; row < rows.Length; row++)
            {
                AddProfileEngineeringCell(grid, row, 0, rows[row].Label, true);
                for (var column = 0; column < blockSamples.Count; column++)
                {
                    AddProfileEngineeringCell(grid, row, column + 1, rows[row].Value(blockSamples[column]), false);
                }
            }
            panel.Children.Add(grid);
        }
        AddProfileKlicLegendToPanel(panel, GetVisibleKlicProfileCrossings(_profilePoints[^1].Distance));
        border.Child = panel;
        return border;
    }

    private static Border CreateReportProfilePointTable(IReadOnlyList<ProfilePointRow> rows)
    {
        var table = CreateReportTable(["#", "Rol", "X RD", "Y RD", "Afstand", "Diepte", "NAP", "Maaiveld"]);
        foreach (var row in rows)
        {
            AddReportTableRow(table, [
                row.Index.ToString(CultureInfo.InvariantCulture),
                row.Role,
                row.X.ToString("N2", CultureInfo.CurrentCulture),
                row.Y.ToString("N2", CultureInfo.CurrentCulture),
                $"{row.Distance:N1} m",
                $"{row.Depth:N2} m",
                $"{row.Nap:N2} m",
                $"{row.Surface:N2} m"
            ]);
        }
        if (rows.Count == 0) AddReportTableRow(table, ["-", "Geen profielpunten", "-", "-", "-", "-", "-", "-"]);
        return table;
    }

    private Border CreateReportProfileSegmentTable()
    {
        var table = CreateReportTable(["#", "Positie", "Diepte", "NAP", "Segment"]);
        for (var i = 0; i < _profilePoints.Count; i++)
        {
            var point = _profilePoints[i];
            var code = point.Index == 1
                ? "S"
                : point.Index == _profilePoints.Count
                    ? "E"
                    : point.Index.ToString(CultureInfo.InvariantCulture);
            var segment = i < _profilePoints.Count - 1
                ? $"{SegmentDistance(i):N1} m / {SegmentAngle(i):N1}°"
                : "einde";
            AddReportTableRow(table, [
                code,
                $"{point.Distance:N1} m",
                $"{point.Depth:N2} m",
                $"{point.Nap:N2} m",
                segment
            ]);
        }
        if (_profilePoints.Count == 0) AddReportTableRow(table, ["-", "Geen profielpunten", "-", "-", "-"]);
        return table;
    }

    private static UIElement CreateReportSoundingProfile(ReportRenderSoundingProfileBlock block)
    {
        var intervals = block.Intervals
            .Where(interval => double.IsFinite(interval.TopDepth)
                && double.IsFinite(interval.BottomDepth)
                && interval.BottomDepth > interval.TopDepth)
            .OrderBy(interval => interval.TopDepth)
            .ToList();

        if (intervals.Count == 0)
        {
            return CreateReportNote("Geen boormonsterprofiel beschikbaar voor dit kaartpunt.");
        }

        var endDepth = block.EndDepth is double depth && double.IsFinite(depth) && depth > 0
            ? depth
            : intervals.Max(interval => interval.BottomDepth);
        endDepth = Math.Max(endDepth, 0.5);

        const double width = 620;
        const double height = 330;
        const double plotTop = 56;
        const double plotHeight = 218;
        const double axisLeft = 58;
        const double geoLeft = 120;
        const double lithLeft = 255;
        const double barWidth = 82;
        const double legendLeft = 395;

        var canvas = new Canvas
        {
            Width = width,
            Height = height,
            Background = Brushes.White,
            ClipToBounds = true
        };

        AddCanvasRect(canvas, 0, 0, width, height, "#FFFFFF", "#CBD5E1", 1);
        AddCanvasText(canvas, block.Title, 18, 14, "#071422", 12.5, FontWeights.Bold);
        AddCanvasText(canvas, block.Subtitle, 18, 34, "#587080", 9.2, FontWeights.Normal);

        AddCanvasLine(canvas, axisLeft, plotTop, axisLeft, plotTop + plotHeight, "#64748B", 1, null);
        AddCanvasText(canvas, "Diepte t.o.v. maaiveld (m)", 14, plotTop + 86, "#475569", 8.8, FontWeights.SemiBold, -90);

        for (var i = 0; i <= 4; i++)
        {
            var depthValue = endDepth * i / 4.0;
            var y = SoundingProfileDepthY(depthValue, plotTop, plotHeight, endDepth);
            AddCanvasLine(canvas, axisLeft - 4, y, lithLeft + barWidth + 28, y, "#E5E7EB", 0.8, i == 0 ? null : [3, 3]);
            AddCanvasText(canvas, $"{depthValue:N1}", axisLeft - 35, y - 7, "#64748B", 8.2, FontWeights.Normal);
        }

        AddCanvasText(canvas, "Geologische eenheid", geoLeft - 10, plotTop - 21, "#334155", 9.3, FontWeights.Bold);
        AddCanvasText(canvas, "Lithologie", lithLeft + 8, plotTop - 21, "#334155", 9.3, FontWeights.Bold);

        foreach (var interval in intervals)
        {
            var y1 = SoundingProfileDepthY(interval.TopDepth, plotTop, plotHeight, endDepth);
            var y2 = SoundingProfileDepthY(interval.BottomDepth, plotTop, plotHeight, endDepth);
            var h = Math.Max(3, y2 - y1);
            var color = SoundingProfileFill(interval);
            AddCanvasRect(canvas, geoLeft, y1, barWidth, h, color, "#334155", 0.45);
            AddCanvasRect(canvas, lithLeft, y1, barWidth, h, color, "#334155", 0.45);

            if (h > 15)
            {
                AddCanvasText(canvas, CompactSoundingLabel(interval.Code), geoLeft + 6, y1 + 3, "#071422", 7.8, FontWeights.SemiBold);
                var lithology = string.IsNullOrWhiteSpace(interval.Lithology) || interval.Lithology == "-"
                    ? interval.Label
                    : interval.Lithology;
                AddCanvasText(canvas, CompactSoundingLabel(lithology), lithLeft + 6, y1 + 3, "#071422", 7.8, FontWeights.SemiBold);
            }
        }

        AddCanvasRect(canvas, legendLeft, 46, 202, 160, "#FBFCFD", "#CBD5E1", 1);
        AddCanvasText(canvas, "Legenda", legendLeft + 12, 58, "#334155", 9.5, FontWeights.Bold);
        var legendItems = intervals
            .GroupBy(SoundingProfileFill, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(7)
            .ToList();

        for (var i = 0; i < legendItems.Count; i++)
        {
            var item = legendItems[i];
            var y = 78 + i * 18;
            AddCanvasRect(canvas, legendLeft + 12, y, 11, 11, SoundingProfileFill(item), "#94A3B8", 0.5);
            var label = string.IsNullOrWhiteSpace(item.Lithology) || item.Lithology == "-"
                ? item.Label
                : item.Lithology;
            AddCanvasText(canvas, CompactSoundingLegend(label), legendLeft + 30, y - 2, "#334155", 8.2, FontWeights.Normal);
        }

        AddCanvasText(canvas, $"Maaiveld: {(block.SurfaceNap is double surface ? $"{surface:N2} m NAP" : "-")}", legendLeft + 12, height - 53, "#475569", 9, FontWeights.SemiBold);
        AddCanvasText(canvas, $"Einddiepte: {endDepth:N2} m", legendLeft + 12, height - 35, "#475569", 9, FontWeights.SemiBold);

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("#D7E8FA"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 8, 0, 10),
            Child = canvas
        };
    }

    private static System.Windows.Shapes.Path CreateSmoothProfilePath(IReadOnlyList<Point> points, Brush stroke, double thickness, DoubleCollection? dashArray, double opacity)
    {
        var figure = new PathFigure { StartPoint = points[0], IsClosed = false, IsFilled = false };
        for (var i = 0; i < points.Count - 1; i++)
        {
            var p0 = i == 0 ? points[i] : points[i - 1];
            var p1 = points[i];
            var p2 = points[i + 1];
            var p3 = i + 2 < points.Count ? points[i + 2] : p2;
            var c1 = new Point(p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6);
            var c2 = new Point(p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6);
            figure.Segments.Add(new BezierSegment(c1, c2, p2, true));
        }

        return new System.Windows.Shapes.Path
        {
            Data = new PathGeometry([figure]),
            Stroke = stroke,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeDashArray = dashArray,
            Opacity = opacity
        };
    }

    private void DrawMachineSideSymbolOnProfile(Func<double, double> xDistance, Func<double, double> yNap)
    {
        // The machine footprint box only belongs on the formal werktekening; the
        // dwarsprofiel (stap 7) shows the profile itself without the machine block.
        if (_selectedStep?.Number != WorkDrawingStepNumber || _profilePoints.Count < 2) return;

        var start = _profilePoints[0];
        var anchorX = xDistance(start.Distance);
        var anchorY = yNap(start.Surface);
        const double machineHeightMeters = 2.0;
        var machineLengthMeters = Math.Max(1, _machineLengthMeters);
        var machineWidthMeters = Math.Max(0.5, _machineWidthMeters);
        var rectWidth = Math.Max(56, Math.Abs(xDistance(Math.Min(_profilePoints[^1].Distance, machineLengthMeters)) - xDistance(0)));
        var rectHeight = Math.Max(22, Math.Abs(yNap(start.Surface + machineHeightMeters) - yNap(start.Surface)));
        var rectLeft = anchorX - rectWidth - 8;
        var rectTop = anchorY - rectHeight - 4;
        var machineStroke = Brush("#A52A08");

        var machineBox = new Rectangle
        {
            Width = rectWidth,
            Height = rectHeight,
            Fill = Brush("#FFF7ED"),
            Stroke = machineStroke,
            StrokeThickness = 2,
            Opacity = 0.95
        };
        Canvas.SetLeft(machineBox, rectLeft);
        Canvas.SetTop(machineBox, rectTop);
        ProfileCanvas.Children.Add(machineBox);

        ProfileCanvas.Children.Add(new Line
        {
            X1 = rectLeft + rectWidth,
            Y1 = rectTop + rectHeight,
            X2 = anchorX,
            Y2 = anchorY,
            Stroke = machineStroke,
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });

        var anchor = new Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = Brushes.White,
            Stroke = machineStroke,
            StrokeThickness = 1.8
        };
        Canvas.SetLeft(anchor, anchorX - 3.5);
        Canvas.SetTop(anchor, anchorY - 3.5);
        ProfileCanvas.Children.Add(anchor);

        var label = new Border
        {
            Background = Brushes.White,
            BorderBrush = machineStroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 1, 4, 1),
            Child = new TextBlock
            {
                Text = $"Machine {machineLengthMeters:0.##} x {machineWidthMeters:0.##} x {machineHeightMeters:0.##} m",
                Foreground = machineStroke,
                FontSize = 9,
                FontWeight = FontWeights.SemiBold
            }
        };
        Canvas.SetLeft(label, rectLeft);
        Canvas.SetTop(label, Math.Max(2, rectTop - 20));
        ProfileCanvas.Children.Add(label);
    }

    private void DrawProfileAngleCallout(string label, double angle, double x, double y, double offsetX, double offsetY)
    {
        if (!double.IsFinite(angle)) return;
        var color = Math.Abs(angle) >= 8 ? "#B91C1C" : "#B45309";
        var lineLength = 34d;
        var direction = offsetX >= 0 ? 1 : -1;
        ProfileCanvas.Children.Add(new Line
        {
            X1 = x,
            Y1 = y,
            X2 = x + direction * lineLength,
            Y2 = y,
            Stroke = Brush("#111827"),
            StrokeThickness = 1,
            Opacity = 0.75
        });
        ProfileCanvas.Children.Add(new Line
        {
            X1 = x,
            Y1 = y,
            X2 = x + direction * 28,
            Y2 = y + Math.Tan(angle * Math.PI / 180) * 28,
            Stroke = Brush(color),
            StrokeThickness = 1.6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });

        var border = new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush(color),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 2, 5, 2),
            Child = new TextBlock
            {
                Text = $"{label} {angle:+0.0;-0.0;0.0}°",
                Foreground = Brush(color),
                FontSize = 10,
                FontWeight = FontWeights.Bold
            }
        };
        Canvas.SetLeft(border, Math.Clamp(x + offsetX, 2d, Math.Max(2d, ProfileCanvas.Width - 132d)));
        Canvas.SetTop(border, Math.Clamp(y + offsetY, 2d, Math.Max(2d, ProfileCanvas.Height - 28d)));
        ProfileCanvas.Children.Add(border);
    }

    // Klikken op een leeg stuk van de Dwarsprofiel-grafiek voegt een nieuw dieptepunt
    // toe op die positie (afstand + NAP-hoogte). Klikken op een bestaand dieptepunt-
    // bolletje (zie de MouseLeftButtonDown hierboven in DrawProfileCanvas) verwijdert
    // dat punt in plaats van een nieuw punt toe te voegen — die handler zet
    // e.Handled = true zodat dit hier niet ook nog eens een punt toevoegt.
    private void ProfileCanvas_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_selectedStep?.Number != ProfileStepNumber || _profilePoints.Count < 2) return;
        if (!_profileCanvasInteractive || _profileCanvasPlotWidth <= 0 || _profileCanvasPlotHeight <= 0) return;

        var position = e.GetPosition(ProfileCanvas);
        var distance = Math.Clamp(
            (position.X - _profileCanvasPlotLeft) / _profileCanvasPlotWidth * _profileCanvasTotal,
            0,
            _profileCanvasTotal);
        var nap = _profileCanvasMaxY - (position.Y - _profileCanvasPlotTop) / _profileCanvasPlotHeight * (_profileCanvasMaxY - _profileCanvasMinY);

        // Niet vlak bij intrede/uittrede of een bestaand punt toevoegen (die heeft al
        // zijn eigen klikbare bolletje om te verwijderen).
        if (distance <= _profilePoints[0].Distance + 0.5 || distance >= _profilePoints[^1].Distance - 0.5) return;
        if (_profilePoints.Any(point => Math.Abs(point.Distance - distance) < 1.0)) return;

        var traceRows = GetTraceRowsForProfile();
        var traceDistances = BuildTraceDistances(traceRows);
        if (traceRows.Count < 2) return;

        var xy = InterpolateTracePoint(traceRows, traceDistances, distance);
        var total = Math.Max(1, _profilePoints[^1].Distance);
        var surface = GetProfileSurfaceNap(xy.X, xy.Y, distance, total, _profilePoints.Count);
        var depth = Math.Max(0, surface - nap);
        _profilePoints.Add(new ProfilePointRow(0, "Dieptepunt", xy.X, xy.Y, Math.Round(distance, 2), Math.Round(depth, 2), Math.Round(surface - depth, 2), Math.Round(surface, 2)));
        RecalculateProfileRolesAndNap();
        _profileHasUnsavedChanges = true;
        _profileGeometryDirty = false;
        RenderProfilePanel();
        OutputText.Text = $"Dieptepunt toegevoegd\n\nPositie: {distance:N1} m\nDiepte: {depth:N2} m.";
    }

    private void DrawProfileCanvas()
    {
        var viewportWidth = ProfileCanvasViewport.ActualWidth > 40 ? ProfileCanvasViewport.ActualWidth - 4 : 760;
        // Geef elke meter boorlijn een vaste hoeveelheid breedte zodat kruisingen die
        // dicht bij elkaar liggen (bv. bij intrede/uittrede) niet meer op elkaar
        // geplakt zitten. De canvas mag daardoor breder worden dan de zichtbare
        // ScrollViewer (HorizontalScrollBarVisibility="Auto"), zodat je er doorheen
        // kan scrollen in plaats van dat alles ingeklemd wordt weergegeven.
        var traceLengthForWidth = _profilePoints.Count > 0 ? Math.Max(1, _profilePoints[^1].Distance) : 1;
        const double pixelsPerMeter = 9d;
        var desiredWidth = traceLengthForWidth * pixelsPerMeter + 176;
        var maxWidth = _profileExpanded ? 2600d : 1800d;
        var width = Math.Clamp(desiredWidth, Math.Max(620d, viewportWidth), maxWidth);
        var height = CalculateProfileCanvasHeight(width, _profileExpanded);
        ProfileCanvas.Width = width;
        ProfileCanvas.Height = height;
        ProfileCanvas.LayoutTransform = new ScaleTransform(_profileViewZoom, _profileViewZoom);
        ProfileCanvasViewport.Height = CalculateProfileViewportHeight(height, _profileExpanded);
        ProfileExpandButton.Content = _profileExpanded ? "Dwarsprofiel inklappen" : "Dwarsprofiel vergroten";
        ProfileZoomText.Text = $"{_profileViewZoom * 100:0}%";
        // The profile is only truly map-aligned while the GIS map is visible next
        // to it. In the tabbed 7.2 layout the map is hidden on the profile tab, so
        // use the normal (wider right margin) distance-based layout in that case.
        var effectiveAligned = _profileAlignedToMap
            && StepThreeMapFrame.Visibility == Visibility.Visible
            && StepThreeMapFrame.ActualWidth >= 40;
        var left = 96d;
        var right = effectiveAligned ? 14d : 80d;
        const double top = 46;
        const double surfaceStripTop = 18;
        const double surfaceStripHeight = 20;
        const double bottom = 34;
        var plotWidth = Math.Max(100, width - left - right);
        var plotHeight = Math.Max(100, height - top - bottom);
        var total = Math.Max(1, _profilePoints[^1].Distance);
        var klicCrossings = GetVisibleKlicProfileCrossings(total);
        var klicNaps = klicCrossings.Select(crossing => crossing.Nap).ToList();
        // The maaiveldlijn is sampled densely along the whole boorlijn (see below), not
        // just at the 4 dieptepunten, so the verticale schaal must take its min/max mee
        // — anders wordt het echte maaiveldverloop afgeknipt bij een gestuurde boring.
        var denseSurfaceRows = GetAhnSurfaceProfileRows(total);
        var profileMin = Math.Min(_profilePoints.Min(p => p.Nap), _profilePoints.Min(p => p.Surface));
        var profileMax = Math.Max(_profilePoints.Max(p => p.Nap), _profilePoints.Max(p => p.Surface));
        if (denseSurfaceRows.Count > 0)
        {
            profileMin = Math.Min(profileMin, denseSurfaceRows.Min(row => row.Surface));
            profileMax = Math.Max(profileMax, denseSurfaceRows.Max(row => row.Surface));
        }
        if (klicNaps.Count > 0)
        {
            profileMin = Math.Min(profileMin, klicNaps.Min());
            profileMax = Math.Max(profileMax, klicNaps.Max());
        }

        var minY = Math.Floor(profileMin - 1);
        var maxY = Math.Ceiling(profileMax + 1);
        if (Math.Abs(maxY - minY) < 1) maxY = minY + 1;

        double FallbackX(double distance) => left + plotWidth * distance / total;
        double XDistance(double distance) => ProfileXFromMapMetrics(distance, FallbackX(distance), width, total);
        double X(ProfilePointRow point) => XDistance(point.Distance);
        double Y(double nap) => top + plotHeight * (maxY - nap) / (maxY - minY);
        var labelBounds = new List<Rect>();

        // Onthouden voor ProfileCanvas_OnMouseLeftButtonDown (klikken op de grafiek om
        // een dieptepunt toe te voegen) — dezelfde schaal als hierboven, maar dan
        // omgekeerd (canvaspositie -> afstand/NAP). Alleen geldig voor de eenvoudige
        // (niet-kaart-uitgelijnde) laag-out die de Dwarsprofiel-tab gebruikt; de kaart
        // is daar altijd verborgen, dus effectiveAligned is hier in de praktijk altijd
        // false en FallbackX is de daadwerkelijk gebruikte mapping.
        _profileCanvasInteractive = !effectiveAligned;
        _profileCanvasPlotLeft = left;
        _profileCanvasPlotTop = top;
        _profileCanvasPlotWidth = plotWidth;
        _profileCanvasPlotHeight = plotHeight;
        _profileCanvasMinY = minY;
        _profileCanvasMaxY = maxY;
        _profileCanvasTotal = total;

        ProfileCanvas.Children.Add(new Rectangle { Width = width, Height = height, Fill = Brushes.White });
        DrawBgtSurfaceStrip(XDistance, surfaceStripTop, surfaceStripHeight, total, left, plotWidth);
        for (var i = 0; i <= 4; i++)
        {
            var y = top + plotHeight * i / 4;
            ProfileCanvas.Children.Add(new Line { X1 = left, X2 = left + plotWidth, Y1 = y, Y2 = y, Stroke = Brush("#E5ECF0"), StrokeThickness = 1 });
            var label = new TextBlock { Text = $"{maxY - (maxY - minY) * i / 4:N1}", Foreground = Brush("#8FA6B2"), FontSize = 10 };
            Canvas.SetLeft(label, 5);
            Canvas.SetTop(label, y - 8);
            ProfileCanvas.Children.Add(label);
        }

        var surface = new Polyline { Stroke = Brush("#4B5563"), StrokeThickness = 2 };
        var boringDiameterMeters = Math.Max(0.075, GetBoringDiameterMillimeters() / 1000d);
        var boreBandThickness = Math.Max(2, boringDiameterMeters * plotHeight / Math.Max(0.1, maxY - minY));
        var boreBand = new Polyline
        {
            Stroke = Brush("#FDA4AF"),
            StrokeThickness = boreBandThickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Opacity = 0.55
        };
        var bore = new Polyline { Stroke = Brush("#F97316"), StrokeThickness = 3, StrokeDashArray = new DoubleCollection([5, 3]) };
        var borePoints = new List<Point>();
        // Dicht bemonsterd via InterpolateBoreNapAtDistance — dezelfde functie als de
        // dieptepunt-bolletjes (DrawProfileEngineeringMarkers) verderop gebruiken. De
        // lijn werd voorheen als Bezier-spline door alleen de 4 dieptepunten getekend
        // (uniforme parametrisatie) terwijl de bolletjes een afstand-geparametriseerde
        // Catmull-Rom gebruikten; bij ongelijk verdeelde punten (0/25/65/100%) week dat
        // net genoeg af dat de bolletjes niet meer op de lijn lagen.
        var boreSampleDistances = denseSurfaceRows.Count > 0
            ? denseSurfaceRows.Select(row => row.Distance).ToList()
            : _profilePoints.Select(point => point.Distance).ToList();
        foreach (var distance in boreSampleDistances)
        {
            var borePoint = new Point(XDistance(distance), Y(InterpolateBoreNapAtDistance(distance)));
            boreBand.Points.Add(borePoint);
            bore.Points.Add(borePoint);
            borePoints.Add(borePoint);
        }

        // Maaiveldlijn dicht bemonsterd langs de hele boorlijn (zelfde bron als de AHN4-
        // grafiek in stap 4), niet alleen op de 4 dieptepunten — anders klopt de diepte
        // t.o.v. het echte maaiveld niet over het volledige tracé bij een gestuurde boring.
        if (denseSurfaceRows.Count > 0)
        {
            foreach (var row in denseSurfaceRows)
            {
                surface.Points.Add(new Point(XDistance(row.Distance), Y(row.Surface)));
            }
        }
        else
        {
            foreach (var point in _profilePoints)
            {
                surface.Points.Add(new Point(X(point), Y(point.Surface)));
            }
        }
        ProfileCanvas.Children.Add(surface);
        DrawMachineSideSymbolOnProfile(XDistance, Y);

        var crossingIndex = 0;
        var showKlicBuffer = _mapOverlayStates.TryGetValue("klicBuffer", out var bufferVisible) && bufferVisible;
        foreach (var crossing in klicCrossings)
        {
            var x = XDistance(crossing.Distance);
            var y = Y(crossing.Nap);
            if (showKlicBuffer)
            {
                // "KLIC bufferzone 1 m links/rechts" is a horizontal veiligheidszone
                // langs de boorlijn (1 m aan weerszijden), geen verticale. Teken dit als
                // een horizontale tolerantie-indicator (streeplijn + twee verticale
                // eindstreepjes op exact -1 m en +1 m), zodat direct duidelijk is dat de
                // KLIC-kruising tot 1 m naar links of rechts kan afwijken — in plaats van
                // een vage ovaal/doosje zonder duidelijke schaal.
                var bufferHalfWidth = Math.Max(6, plotWidth / total);
                const double tickHeight = 12;
                var bufferBrush = Brush(crossing.Color);
                var horizontalLine = new Line
                {
                    X1 = x - bufferHalfWidth,
                    X2 = x + bufferHalfWidth,
                    Y1 = y,
                    Y2 = y,
                    Stroke = bufferBrush,
                    StrokeThickness = 1.5,
                    StrokeDashArray = new DoubleCollection([3, 3]),
                    Opacity = 0.85
                };
                var leftTick = new Line
                {
                    X1 = x - bufferHalfWidth,
                    X2 = x - bufferHalfWidth,
                    Y1 = y - tickHeight / 2,
                    Y2 = y + tickHeight / 2,
                    Stroke = bufferBrush,
                    StrokeThickness = 1.5,
                    Opacity = 0.85
                };
                var rightTick = new Line
                {
                    X1 = x + bufferHalfWidth,
                    X2 = x + bufferHalfWidth,
                    Y1 = y - tickHeight / 2,
                    Y2 = y + tickHeight / 2,
                    Stroke = bufferBrush,
                    StrokeThickness = 1.5,
                    Opacity = 0.85
                };
                ProfileCanvas.Children.Add(horizontalLine);
                ProfileCanvas.Children.Add(leftTick);
                ProfileCanvas.Children.Add(rightTick);
            }

            var marker = new Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = Brush(crossing.Color),
                Stroke = Brushes.White,
                StrokeThickness = 2
            };
            Canvas.SetLeft(marker, x - 6);
            Canvas.SetTop(marker, y - 6);
            ProfileCanvas.Children.Add(marker);

            var stem = new Line
            {
                X1 = x,
                X2 = x,
                Y1 = Math.Max(top, y - 18),
                Y2 = Math.Min(top + plotHeight, y + 18),
                Stroke = Brush(crossing.Color),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection([2, 2]),
                Opacity = 0.85
            };
            ProfileCanvas.Children.Add(stem);

            var key = $"K{crossingIndex + 1}";
            var label = new Border
            {
                Background = Brushes.White,
                BorderBrush = Brush(crossing.Color),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1, 4, 1),
                Child = new TextBlock
                {
                    Text = key,
                    Foreground = Brush(crossing.Color),
                    FontSize = 9,
                    FontWeight = FontWeights.Bold
                }
            };
            var labelPoint = PlaceProfileLabel(
                x - 11,
                y - 28 - (crossingIndex % 4) * 15,
                30,
                18,
                left,
                top,
                left + plotWidth,
                top + plotHeight,
                labelBounds);
            Canvas.SetLeft(label, labelPoint.X);
            Canvas.SetTop(label, labelPoint.Y);
            ProfileCanvas.Children.Add(label);
            crossingIndex++;
        }

        // De dichte bemonstering hierboven (InterpolateBoreNapAtDistance, met Catmull-
        // Rom-smoothing als "Boorlijn vloeiend" aanstaat) is zelf al vloeiend genoeg; een
        // aparte Bezier-spline (CreateSmoothProfilePath) door dezelfde punten is niet
        // meer nodig en zou de dieptepunt-bolletjes weer van de lijn kunnen laten afwijken.
        ProfileCanvas.Children.Add(boreBand);
        ProfileCanvas.Children.Add(bore);

        DrawProfileEngineeringMarkers(XDistance, Y);
        DrawProfileAngleCallout("Intrede", SegmentAngle(0), X(_profilePoints[0]), Y(_profilePoints[0].Nap), 24, -30);
        DrawProfileAngleCallout("Uittrede", SegmentAngle(_profilePoints.Count - 2), X(_profilePoints[^1]), Y(_profilePoints[^1].Nap), -122, -44);

        for (var i = 0; i < _profilePoints.Count - 1; i++)
        {
            var a = _profilePoints[i];
            var b = _profilePoints[i + 1];
            var midX = (X(a) + X(b)) / 2;
            var midY = (Y(a.Nap) + Y(b.Nap)) / 2;
            var angle = SegmentAngle(i);
            var color = angle < -8 ? "#DC2626" : angle > 8 ? "#16A34A" : "#F97316";
            if (SegmentDistance(i) < 2.5) continue;
            var labelText = $"{SegmentDistance(i):N1} m  {angle:+0.0;-0.0;0.0}°";
            var label = new Border
            {
                Background = Brushes.White,
                BorderBrush = Brush(color),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 2, 5, 2),
                Child = new TextBlock
                {
                    Text = labelText,
                    Foreground = Brush(color),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold
                }
            };
            var labelPoint = PlaceProfileLabel(
                midX - 42,
                midY - 28 - (i % 2) * 20,
                86,
                22,
                left,
                top,
                left + plotWidth,
                top + plotHeight,
                labelBounds);
            Canvas.SetLeft(label, labelPoint.X);
            Canvas.SetTop(label, labelPoint.Y);
            ProfileCanvas.Children.Add(label);
        }

        if (_mapOverlayStates.TryGetValue("profileTracePoints", out var showTracePoints) && showTracePoints)
        {
            foreach (var point in GetTraceReferenceProfilePoints(total))
            {
                var x = XDistance(point.Distance);
                var y = Y(point.Surface) - 20;
                var dot = new Ellipse { Width = 11, Height = 11, Fill = Brushes.White, Stroke = Brush("#E11D48"), StrokeThickness = 2 };
                Canvas.SetLeft(dot, x - 5.5);
                Canvas.SetTop(dot, y - 5.5);
                ProfileCanvas.Children.Add(dot);
                var label = new TextBlock { Text = point.Index.ToString(CultureInfo.InvariantCulture), Foreground = Brush("#E11D48"), FontSize = 9, FontWeight = FontWeights.Bold };
                Canvas.SetLeft(label, x - 4);
                Canvas.SetTop(label, y - 20);
                ProfileCanvas.Children.Add(label);
            }
        }

        foreach (var point in _profilePoints)
        {
            var x = X(point);
            var y = Y(point.Nap);
            var isDeletable = point.Index > 1 && point.Index < _profilePoints.Count;
            var dot = new Ellipse
            {
                Width = 13,
                Height = 13,
                Fill = point.Index == 1 ? Brush("#4B5563") : point.Index == _profilePoints.Count ? Brush("#DC2626") : Brush("#F97316"),
                Stroke = Brushes.White,
                StrokeThickness = 2,
                Cursor = isDeletable ? Cursors.Hand : Cursors.Arrow
            };
            if (isDeletable)
            {
                dot.ToolTip = "Klik om dit dieptepunt te verwijderen";
                var pointIndex = point.Index;
                dot.MouseLeftButtonDown += (_, args) =>
                {
                    var removeAt = _profilePoints.FindIndex(candidate => candidate.Index == pointIndex);
                    if (removeAt > 0 && removeAt < _profilePoints.Count - 1)
                    {
                        _profilePoints.RemoveAt(removeAt);
                        _profileGeometryDirty = true;
                        _profileHasUnsavedChanges = true;
                        RenderProfilePanel();
                        OutputText.Text = "Dieptepunt verwijderd.";
                    }
                    args.Handled = true;
                };
            }
            Canvas.SetLeft(dot, x - 6.5);
            Canvas.SetTop(dot, y - 6.5);
            ProfileCanvas.Children.Add(dot);

            var label = new TextBlock
            {
                Text = point.Index == 1 ? "S" : point.Index == _profilePoints.Count ? "E" : point.Index.ToString(CultureInfo.InvariantCulture),
                Foreground = Brush("#071422"),
                FontSize = 10,
                FontWeight = FontWeights.Bold
            };
            Canvas.SetLeft(label, x - 5);
            Canvas.SetTop(label, y - 24);
            ProfileCanvas.Children.Add(label);
        }
    }

    private void DrawProfileEngineeringMarkers(Func<double, double> xDistance, Func<double, double> yNap)
    {
        foreach (var sample in BuildProfileEngineeringSamples())
        {
            var point = ProfileBorePointAtDistance(sample.Distance, xDistance, yNap);
            var x = point.X;
            var y = point.Y;
            var dot = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = Brush("#64748B"),
                Stroke = Brushes.White,
                StrokeThickness = 1.5
            };
            Canvas.SetLeft(dot, x - 4);
            Canvas.SetTop(dot, y - 4);
            ProfileCanvas.Children.Add(dot);

            var label = new TextBlock
            {
                Text = sample.Code,
                Foreground = Brush("#475569"),
                FontSize = 9,
                FontWeight = FontWeights.Bold
            };
            Canvas.SetLeft(label, x - 4);
            Canvas.SetTop(label, y + 7);
            ProfileCanvas.Children.Add(label);
        }
    }

    private void EnsureProfilePoints(bool regenerate = false)
    {
        if (_selectedProject is null) return;
        LoadProfileVisualSettings();
        if (!regenerate && _profilePoints.Count >= 2) return;

        EnsureStoredBoreTraceLoaded();
        var traceRows = GetTraceRowsForProfile();
        if (traceRows.Count < 2)
        {
            if (!regenerate)
            {
                var loadedWithoutTrace = ReadStoredProfile(
                    _projects.GetStepData(_selectedProject.Id, ProfileStepNumber, "diepteprofiel_3d") ??
                    _projects.GetStepData(_selectedProject.Id, LegacyProfileStepNumber, "diepteprofiel_3d"));
                if (loadedWithoutTrace.Count >= 2)
                {
                    _profilePoints = RefreshProfileSurfaces(loadedWithoutTrace, preferStoredFallback: true);
                    _profileHasUnsavedChanges = false;
                    _profileGeometryDirty = false;
                    return;
                }
            }

            _profilePoints = [];
            return;
        }

        var distances = new List<double> { 0 };
        for (var i = 1; i < traceRows.Count; i++)
        {
            var previous = traceRows[i - 1];
            var current = traceRows[i];
            distances.Add(distances[^1] + Math.Sqrt(Math.Pow(current.X - previous.X, 2) + Math.Pow(current.Y - previous.Y, 2)));
        }

        var total = Math.Max(1, distances[^1]);

        if (!regenerate)
        {
            var loaded = ReadStoredProfile(
                _projects.GetStepData(_selectedProject.Id, ProfileStepNumber, "diepteprofiel_3d") ??
                _projects.GetStepData(_selectedProject.Id, LegacyProfileStepNumber, "diepteprofiel_3d"));
            // The stored dwarsprofiel can predate later edits that lengthened the
            // boorlijn (bv. opgeslagen op 54,6 m terwijl de boorlijn inmiddels 102,7 m
            // is) — the maaiveldlijn (dense-sampled straight from the actual trace) then
            // runs well past where the opgeslagen boorlijn/dieptepunten stoppen, which
            // looks like the two disagree. Treat a stored profile whose end distance
            // meaningfully undershoots the current boorlijn as stale and regenerate it
            // against the actual trace instead of silently keeping the shorter one.
            var isStale = loaded.Count >= 2 && total - loaded[^1].Distance > 0.5;
            if (loaded.Count >= 2 && !isStale)
            {
                _profilePoints = RefreshProfileSurfaces(loaded, preferStoredFallback: true);
                _profileHasUnsavedChanges = false;
                _profileGeometryDirty = false;
                return;
            }
        }
        var depthDistances = new[] { 0, total * 0.25, total * 0.65, total };
        _profilePoints = depthDistances.Select((distance, index) =>
        {
            var xy = InterpolateTracePoint(traceRows, distances, distance);
            var surface = GetProfileSurfaceNap(xy.X, xy.Y, distance, total, index);
            var depth = index == 0 || index == depthDistances.Length - 1
                ? 0
                : Math.Max(2.0, Math.Min(5.5, 1.4 + 3.2 * Math.Sin(Math.PI * distance / total)));
            var role = index == 0 ? "Intrede" : index == depthDistances.Length - 1 ? "Uittrede" : "Dieptepunt";
            return new ProfilePointRow(index + 1, role, xy.X, xy.Y, Math.Round(distance, 2), Math.Round(depth, 2), Math.Round(surface - depth, 2), surface);
        }).ToList();
        _profileHasUnsavedChanges = true;
        _profileGeometryDirty = false;
    }

    private string ExportDepthProfileGeoJson()
    {
        if (_selectedProject is null) return "Geen project actief.";
        EnsureProfilePoints();
        if (_profilePoints.Count < 2) return "Geen profiel beschikbaar.";

        var feature = new
        {
            type = "Feature",
            properties = new
            {
                name = _selectedProject.Name,
                source = "Borevexa stap 7",
                totalLength = Math.Round(_profilePoints[^1].Distance, 2)
            },
            geometry = new
            {
                type = "LineString",
                coordinates = _profilePoints.Select(point => new[] { Math.Round(point.X, 3), Math.Round(point.Y, 3), Math.Round(point.Nap, 2) })
            }
        };
        var geoJson = JsonSerializer.Serialize(new { type = "FeatureCollection", features = new[] { feature } }, JsonOptions);
        var exportDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Borevexa", "Exports");
        Directory.CreateDirectory(exportDir);
        var fileName = $"boorlijn-3d-{DateTime.Now:yyyyMMdd-HHmmss}.geojson";
        var path = System.IO.Path.Combine(exportDir, fileName);
        System.IO.File.WriteAllText(path, geoJson, Encoding.UTF8);
        return $"GeoJSON export gemaakt\n\n{path}";
    }

    private string GenerateDepthProfile()
    {
        EnsureProfilePoints(regenerate: true);
        _profileHasUnsavedChanges = _profilePoints.Count >= 2;
        _profileGeometryDirty = false;
        RenderProfilePanel();
        SendProfileModeToMap();
        return _profilePoints.Count >= 2
            ? $"Dwarsprofiel gegenereerd\n\n{_profilePoints.Count} punt(en)\nLengte: {_profilePoints[^1].Distance:N1} m\nBewerk de diepte of positie in de tabel en sla daarna op."
            : $"Geen boorlijn beschikbaar. Teken en sla eerst een boorlijn op in stap {DisplayStepNumber(3)}.1.";
    }

    // Samples the AHN4 maaiveldhoogte densely along the actual boorlijn (every ~3 m,
    // plus every trace vertex) instead of reusing the 4 fixed cross-section profile
    // points (0%/25%/65%/100%) from EnsureProfilePoints. Those 4 points exist for the
    // dwarsprofiel-diepteberekening in stap 7, not for an accurate terrain profile, and
    // reusing them here produced an almost straight-line "maaiveld" over the whole
    // trace instead of following the real AHN4 hoogteverloop.
    private IReadOnlyList<(double Distance, double Surface, double BoreNap)>? _cachedAhnSurfaceProfileRows;
    private string? _cachedAhnSurfaceProfileSignature;

    private IReadOnlyList<(double Distance, double Surface, double BoreNap)> GetAhnSurfaceProfileRows(double traceLength)
    {
        EnsureProfilePoints();
        var traceRows = GetTraceRowsForProfile();
        if (traceRows.Count < 2) return [];

        // DrawProfileCanvas/CreateReportProfileCanvas re-render on almost every UI
        // interaction (zoom, uitklappen, resize) — without this cache, every one of
        // those redid the full dense sampling below, and each sample that missed the
        // AHN4 point cache is a synchronous, blocking PDOK request. That repeated,
        // uncached cost is what made the chart briefly render in a corrupted/half-drawn
        // state. Only recompute when the actual boorlijn/trace changes.
        var signature = string.Join("|",
            _currentBoreTraceJson?.GetHashCode(StringComparison.Ordinal).ToString(CultureInfo.InvariantCulture) ?? "",
            traceRows.Count.ToString(CultureInfo.InvariantCulture),
            traceLength.ToString("F1", CultureInfo.InvariantCulture));
        if (_cachedAhnSurfaceProfileRows is not null && string.Equals(_cachedAhnSurfaceProfileSignature, signature, StringComparison.Ordinal))
        {
            return _cachedAhnSurfaceProfileRows;
        }

        var distances = new List<double> { 0 };
        for (var i = 1; i < traceRows.Count; i++)
        {
            var previous = traceRows[i - 1];
            var current = traceRows[i];
            distances.Add(distances[^1] + Math.Sqrt(Math.Pow(current.X - previous.X, 2) + Math.Pow(current.Y - previous.Y, 2)));
        }

        var traceTotal = distances[^1];
        var total = Math.Max(1, Math.Max(traceLength, traceTotal));

        // 0,5 m geeft de meest nauwkeurige maaiveldlijn, maar elke nieuwe sample-positie
        // is een synchrone AHN4-WMS-aanroep (TryFetchAhn4DtmSurfaceNap) — bij hele lange
        // boorlijnen zou dat honderden/duizenden blokkerende requests kunnen geven. Cap
        // daarom het aantal samples (max ~400) door de stap te vergroten boven ~200 m.
        const double preferredStepMeters = 0.5;
        const double maxSamples = 400;
        var stepMeters = Math.Max(preferredStepMeters, traceTotal / maxSamples);
        var sampleDistances = new SortedSet<double> { 0, traceTotal };
        for (var distance = stepMeters; distance < traceTotal; distance += stepMeters)
        {
            sampleDistances.Add(distance);
        }
        foreach (var vertexDistance in distances)
        {
            sampleDistances.Add(vertexDistance);
        }

        var rows = sampleDistances.Select((distance, index) =>
        {
            var xy = InterpolateTracePoint(traceRows, distances, distance);
            var surface = GetProfileSurfaceNap(xy.X, xy.Y, distance, total, index);
            return (Distance: distance, Surface: surface, BoreNap: InterpolateBoreNapAtDistance(distance));
        }).ToArray();

        DespikeAhnSurfaceRows(rows);

        _cachedAhnSurfaceProfileSignature = signature;
        _cachedAhnSurfaceProfileRows = rows;
        return rows;
    }

    // Een enkele foutieve AHN4-sample (bv. een WMS-glitch, of een brug/duiker die
    // per ongeluk in het "maaiveld"-model lekt) kan een scherpe piek geven die met
    // 0,5 m-bemonstering meteen opvalt tussen verder vlakke buurpunten. Vervang een
    // sample die sterk afwijkt van de mediaan van zijn buren door die mediaan, zodat
    // een geïsoleerde uitschieter het profiel en de diepteberekening niet verstoort.
    private static void DespikeAhnSurfaceRows((double Distance, double Surface, double BoreNap)[] rows)
    {
        if (rows.Length < 5) return;

        const int window = 2;
        const double spikeThresholdMeters = 1.0;
        var original = rows.Select(row => row.Surface).ToArray();

        for (var i = 0; i < rows.Length; i++)
        {
            var lo = Math.Max(0, i - window);
            var hi = Math.Min(rows.Length - 1, i + window);
            var neighbors = new List<double>();
            for (var j = lo; j <= hi; j++)
            {
                if (j == i) continue;
                neighbors.Add(original[j]);
            }
            if (neighbors.Count == 0) continue;

            neighbors.Sort();
            var median = neighbors[neighbors.Count / 2];
            if (Math.Abs(original[i] - median) > spikeThresholdMeters)
            {
                rows[i] = (rows[i].Distance, median, rows[i].BoreNap);
            }
        }
    }

    private double GetProfileSurfaceNap(double x, double y, double distance, double total, int index, double? storedFallback = null)
    {
        if (TryFetchAhn4DtmSurfaceNap(x, y, out var surfaceNap))
        {
            return Math.Round(surfaceNap, 2);
        }

        if (storedFallback is double fallback && fallback is > -100 and < 400)
        {
            return Math.Round(fallback, 2);
        }

        return DemoSurfaceNap(distance, total, index);
    }

    private IReadOnlyList<ProfilePointRow> GetTraceReferenceProfilePoints(double profileTotal)
    {
        var traceRows = GetTraceRowsForProfile();
        if (traceRows.Count < 2) return [];

        var distances = new List<double> { 0 };
        for (var i = 1; i < traceRows.Count; i++)
        {
            var previous = traceRows[i - 1];
            var current = traceRows[i];
            distances.Add(distances[^1] + Math.Sqrt(Math.Pow(current.X - previous.X, 2) + Math.Pow(current.Y - previous.Y, 2)));
        }

        var traceTotal = Math.Max(1, distances[^1]);
        return traceRows.Select((point, index) =>
        {
            var distance = profileTotal * distances[index] / traceTotal;
            var surface = GetProfileSurfaceNap(point.X, point.Y, distance, Math.Max(1, profileTotal), index);
            return new ProfilePointRow(index + 1, "Tracepunt", point.X, point.Y, distance, 0, surface, surface);
        }).ToList();
    }

    private IReadOnlyList<TracePointRow> GetTraceRowsForProfile()
    {
        if (_selectedProject is not null && _currentBoreTracePoints.Count < 2 && string.IsNullOrWhiteSpace(_currentBoreTraceJson))
        {
            EnsureStoredBoreTraceLoaded();
        }

        var traceJson = _currentBoreTraceJson ?? GetStoredBoreTraceJson();
        if (!string.IsNullOrWhiteSpace(traceJson))
        {
            try
            {
                using var document = JsonDocument.Parse(traceJson);
                var rows = ReadTracePointRows(document.RootElement);
                if (rows.Count >= 2) return rows;
            }
            catch (System.Exception swallowedException)
            {
                // Fall back to the live RD cache below.
                AppLog.Swallowed(swallowedException);
            }
        }

        return NormalizeTraceRowsToRd(_currentBoreTracePoints).Where(row => IsValidRdPoint(new RdPoint(row.X, row.Y))).ToList();
    }

    private IReadOnlyList<KlicProfileCrossing> GetVisibleKlicProfileCrossings(double profileTotal)
    {
        var workDrawingStep = _selectedStep?.Number == WorkDrawingStepNumber;
        if ((!workDrawingStep && (!_mapOverlayStates.TryGetValue("klic", out var showKlic) || !showKlic)) || _selectedProject is null) return [];

        var traceRows = GetTraceRowsForProfile();
        if (traceRows.Count < 2) return [];

        var traceDistances = BuildTraceDistances(traceRows);
        if (traceDistances.Count < 2 || traceDistances[^1] <= 0) return [];

        _projectFiles = _projects.GetProjectFiles(_selectedProject.Id);
        var layers = BuildProjectMapLayers(_projectFiles)
            .Where(layer => layer.Type.Contains("KLIC", StringComparison.OrdinalIgnoreCase))
            .ToList();
        SyncKlicThemeStates(layers);

        var crossings = new List<KlicProfileCrossing>();
        foreach (var layer in layers)
        {
            foreach (var feature in layer.FeatureCollection.Features)
            {
                var theme = feature.Properties.TryGetValue("theme", out var themeValue) ? themeValue?.ToString() ?? "overig" : "overig";
                if (!workDrawingStep && _klicThemeStates.TryGetValue(theme, out var visible) && !visible) continue;

                foreach (var sourceLine in EnumerateGeometryLines(feature.Geometry))
                {
                    var rdLine = sourceLine.Select(ToRdPoint).Where(point => point.X > 0 && point.Y > 0).ToList();
                    if (rdLine.Count < 2) continue;

                    for (var klicIndex = 1; klicIndex < rdLine.Count; klicIndex++)
                    {
                        var klicA = rdLine[klicIndex - 1];
                        var klicB = rdLine[klicIndex];

                        for (var traceIndex = 1; traceIndex < traceRows.Count; traceIndex++)
                        {
                            var traceA = traceRows[traceIndex - 1];
                            var traceB = traceRows[traceIndex];
                            var hasIntersection = TrySegmentIntersection(
                                traceA.X, traceA.Y, traceB.X, traceB.Y,
                                klicA.X, klicA.Y, klicB.X, klicB.Y,
                                out var traceRatio);

                            if (!hasIntersection)
                            {
                                continue;
                            }

                            var traceSegmentLength = Math.Max(0.001, traceDistances[traceIndex] - traceDistances[traceIndex - 1]);
                            var distanceOnTrace = traceDistances[traceIndex - 1] + traceSegmentLength * traceRatio;
                            var normalizedDistance = profileTotal * distanceOnTrace / Math.Max(1, traceDistances[^1]);
                            var depth = KlicThemeDepth(theme);
                            var nap = InterpolateSurfaceNap(normalizedDistance, profileTotal) - depth;
                            var label = BuildKlicProfileLabel(feature, theme);
                            crossings.Add(new KlicProfileCrossing(
                                Math.Round(normalizedDistance, 2),
                                Math.Round(nap, 2),
                                label,
                                theme,
                                KlicThemeColor(theme),
                                Math.Round(depth, 2),
                                true));
                        }
                    }
                }
            }
        }

        return crossings
            .GroupBy(crossing => $"{crossing.Theme}|{Math.Round(crossing.Distance, 1)}|{crossing.Label}")
            .Select(group => group.First())
            .OrderBy(crossing => crossing.Distance)
            .ThenBy(crossing => crossing.Nap)
            .Take(80)
            .ToList();
    }

    private double InterpolateProfileValue(double distance, Func<ProfilePointRow, double> selector)
    {
        if (_profilePoints.Count == 0) return 0;
        if (distance <= _profilePoints[0].Distance) return selector(_profilePoints[0]);
        for (var i = 1; i < _profilePoints.Count; i++)
        {
            var previous = _profilePoints[i - 1];
            var current = _profilePoints[i];
            if (distance > current.Distance && i < _profilePoints.Count - 1) continue;
            var segment = Math.Max(0.001, current.Distance - previous.Distance);
            var ratio = Math.Clamp((distance - previous.Distance) / segment, 0, 1);
            return selector(previous) + (selector(current) - selector(previous)) * ratio;
        }
        return selector(_profilePoints[^1]);
    }

    private static bool IsAhnSurfaceProfileReportSubstep(int stepNumber, string substepNumber) =>
        stepNumber == 4 && string.Equals(substepNumber, "4.3", StringComparison.OrdinalIgnoreCase);

    private static bool IsProfileReportSubstep(int stepNumber, string substepNumber) =>
        stepNumber == ProfileStepNumber &&
        string.Equals(substepNumber, "7.1", StringComparison.OrdinalIgnoreCase);

    private sealed record KlicProfileCrossing(double Distance, double Nap, string Label, string Theme, string Color, double Depth, bool IsIndicativeDepth);

    private void LoadProfileVisualSettings()
    {
        if (_selectedProject is null || _profileVisualSettingsProjectId == _selectedProject.Id) return;
        _profileVisualSettingsProjectId = _selectedProject.Id;
        var json =
            _projects.GetStepData(_selectedProject.Id, ProfileStepNumber, "profile_visual_settings") ??
            _projects.GetStepData(_selectedProject.Id, LegacyProfileStepNumber, "profile_visual_settings");
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.TryGetProperty("smooth", out var smooth)) _profileSmoothBore = smooth.GetBoolean();
            if (root.TryGetProperty("expanded", out var expanded)) _profileExpanded = expanded.GetBoolean();
            if (root.TryGetProperty("layoutLocked", out var locked)) _profileLayoutLocked = locked.GetBoolean();
            if (root.TryGetProperty("alignedToMap", out var aligned)) _profileAlignedToMap = aligned.GetBoolean();
            if (root.TryGetProperty("viewZoom", out var zoom) && zoom.TryGetDouble(out var zoomValue))
            {
                _profileViewZoom = Math.Clamp(zoomValue, 0.55, 3.0);
            }
        }
        catch (System.Exception swallowedException)
        {
            // Ignore invalid legacy visual settings.
            AppLog.Swallowed(swallowedException);
        }
    }

    private static Point PlaceProfileLabel(
        double desiredX,
        double desiredY,
        double width,
        double height,
        double minX,
        double minY,
        double maxX,
        double maxY,
        IList<Rect> occupied)
    {
        var candidates = new[]
        {
            desiredY,
            desiredY - height,
            desiredY + height,
            desiredY - 2 * height,
            desiredY + 2 * height,
            minY + 2,
            maxY - height - 2
        };

        foreach (var candidateY in candidates)
        {
            var x = Math.Clamp(desiredX, minX + 2, maxX - width - 2);
            var y = Math.Clamp(candidateY, minY + 2, maxY - height - 2);
            var rect = new Rect(x, y, width, height);
            if (occupied.Any(existing => existing.IntersectsWith(rect))) continue;
            occupied.Add(rect);
            return new Point(x, y);
        }

        var fallbackX = Math.Clamp(desiredX, minX + 2, maxX - width - 2);
        var fallbackY = Math.Clamp(desiredY, minY + 2, maxY - height - 2);
        var fallback = new Rect(fallbackX, fallbackY, width, height);
        occupied.Add(fallback);
        return new Point(fallbackX, fallbackY);
    }

    private Point ProfileBorePointAtDistance(double distance, Func<double, double> xDistance, Func<double, double> yNap)
    {
        if (!_profileSmoothBore || _profilePoints.Count < 3)
        {
            return new Point(xDistance(distance), yNap(InterpolateProfileValue(distance, point => point.Nap)));
        }

        if (distance <= _profilePoints[0].Distance)
        {
            return new Point(xDistance(_profilePoints[0].Distance), yNap(_profilePoints[0].Nap));
        }
        if (distance >= _profilePoints[^1].Distance)
        {
            return new Point(xDistance(_profilePoints[^1].Distance), yNap(_profilePoints[^1].Nap));
        }

        for (var i = 0; i < _profilePoints.Count - 1; i++)
        {
            var row1 = _profilePoints[i];
            var row2 = _profilePoints[i + 1];
            if (distance > row2.Distance && i < _profilePoints.Count - 2) continue;

            var row0 = i == 0 ? row1 : _profilePoints[i - 1];
            var row3 = i + 2 < _profilePoints.Count ? _profilePoints[i + 2] : row2;
            var p0 = new Point(xDistance(row0.Distance), yNap(row0.Nap));
            var p1 = new Point(xDistance(row1.Distance), yNap(row1.Nap));
            var p2 = new Point(xDistance(row2.Distance), yNap(row2.Nap));
            var p3 = new Point(xDistance(row3.Distance), yNap(row3.Nap));
            var c1 = new Point(p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6);
            var c2 = new Point(p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6);
            var segment = Math.Max(0.001, row2.Distance - row1.Distance);
            var t = Math.Clamp((distance - row1.Distance) / segment, 0, 1);
            return CubicBezier(p1, c1, c2, p2, t);
        }

        return new Point(xDistance(distance), yNap(InterpolateProfileValue(distance, point => point.Nap)));
    }

    private sealed record ProfileEngineeringSample(string Code, double Distance, double BoreLength, double SurfaceNap, double BoreNap, double Depth, double DropFromStart, string AngleRadius);

    private Grid ProfileGridRow(string[] values, bool header, int index)
    {
        var row = new Grid
        {
            Background = header ? Brush("#F8FAFB") : index == 0 ? Brush("#F0FDF4") : index == _profilePoints.Count - 1 ? Brush("#FFF1F2") : Brushes.White
        };
        foreach (var width in new[] { 30d, 58d, 58d, 58d, 88d, 84d })
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        }
        for (var column = 0; column < 5; column++)
        {
            var block = new TextBlock
            {
                Text = values[column],
                FontSize = header ? 10 : 9.5,
                FontWeight = header ? FontWeights.Bold : FontWeights.Normal,
                Foreground = header ? Brush("#587080") : Brush("#071422"),
                Margin = new Thickness(4),
                TextWrapping = TextWrapping.NoWrap
            };
            Grid.SetColumn(block, column);
            row.Children.Add(block);
        }
        if (!header)
        {
            var actions = new WrapPanel { Margin = new Thickness(0, 1, 0, 1) };
            AddProfileActionButton(actions, "Z+", index, "up", "Punt omhoog: diepte 0,10 m kleiner");
            AddProfileActionButton(actions, "Z-", index, "down", "Punt omlaag: diepte 0,10 m groter");
            AddProfileActionButton(actions, "<", index, "left", "Punt 1 m richting intrede", index > 0);
            AddProfileActionButton(actions, ">", index, "right", "Punt 1 m richting uittrede", index < _profilePoints.Count - 1);
            AddProfileActionButton(actions, "X", index, "delete", "Dieptepunt verwijderen", index > 0 && index < _profilePoints.Count - 1);
            Grid.SetColumn(actions, 5);
            row.Children.Add(actions);
        }
        else
        {
            var block = new TextBlock { Text = values[5], FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brush("#587080"), Margin = new Thickness(4) };
            Grid.SetColumn(block, 5);
            row.Children.Add(block);
        }
        return row;
    }

    private sealed record ProfilePointRow(int Index, string Role, double X, double Y, double Distance, double Depth, double Nap, double Surface);

    private static string ProfileSampleCode(int index)
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyz";
        if (index < alphabet.Length) return alphabet[index].ToString(CultureInfo.InvariantCulture);
        var first = (index / alphabet.Length) - 1;
        var second = index % alphabet.Length;
        return $"{alphabet[Math.Clamp(first, 0, alphabet.Length - 1)]}{alphabet[second]}";
    }

    private sealed record ProfileScreenMetrics(double MapWidth, IReadOnlyList<ProfileScreenPoint> Points);

    private sealed record ProfileScreenPoint(int Index, double X);

    private int ProfileSegmentIndexAt(double distance)
    {
        for (var i = 0; i < _profilePoints.Count - 1; i++)
        {
            if (distance <= _profilePoints[i + 1].Distance || i == _profilePoints.Count - 2) return i;
        }
        return 0;
    }

    private double ProfileXFromMapMetrics(double profileDistance, double fallbackX, double profileCanvasWidth, double profileTotal)
    {
        // Map-aligned X positions only make sense while the GIS map is actually
        // rendered next to the profile. With the tabbed 7.2 layout the map is
        // collapsed whenever the Dwarsprofiel/Profielstaat tab is active, leaving
        // stale screen metrics that bunch every point against one edge. Fall back
        // to the deterministic distance-based layout whenever the map is hidden.
        if (!_profileAlignedToMap
            || _profileScreenMetrics is null
            || _profileScreenMetrics.Points.Count < 2
            || StepThreeMapFrame.Visibility != Visibility.Visible
            || StepThreeMapFrame.ActualWidth < 40)
        {
            return fallbackX;
        }

        var traceRows = GetTraceRowsForProfile();
        if (traceRows.Count < 2 || traceRows.Count != _profileScreenMetrics.Points.Count)
        {
            return fallbackX;
        }

        var traceDistances = BuildTraceDistances(traceRows);
        if (traceDistances.Count != traceRows.Count || traceDistances[^1] <= 0 || profileTotal <= 0)
        {
            return fallbackX;
        }

        var targetDistance = traceDistances[^1] * profileDistance / profileTotal;
        var mapToCanvasOffset = Math.Max(0, (_profileScreenMetrics.MapWidth - profileCanvasWidth) / 2);

        for (var i = 1; i < traceDistances.Count; i++)
        {
            if (targetDistance > traceDistances[i] && i < traceDistances.Count - 1) continue;

            var segmentLength = Math.Max(0.001, traceDistances[i] - traceDistances[i - 1]);
            var ratio = Math.Clamp((targetDistance - traceDistances[i - 1]) / segmentLength, 0, 1);
            var from = _profileScreenMetrics.Points[i - 1].X;
            var to = _profileScreenMetrics.Points[i].X;
            return Math.Clamp(from + (to - from) * ratio - mapToCanvasOffset, 0, profileCanvasWidth);
        }

        return Math.Clamp(_profileScreenMetrics.Points[^1].X - mapToCanvasOffset, 0, profileCanvasWidth);
    }

    private List<ProfilePointRow> ReadStoredProfile(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("points", out var pointsElement) || pointsElement.ValueKind != JsonValueKind.Array) return [];
            var rows = new List<ProfilePointRow>();
            foreach (var point in pointsElement.EnumerateArray())
            {
                rows.Add(new ProfilePointRow(
                    GetJsonInt(point, "index", rows.Count + 1),
            point.TryGetProperty("role", out var roleElement) ? roleElement.GetString() ?? "Dieptepunt" : "Dieptepunt",
                    GetJsonDouble(point, "x", 0),
                    GetJsonDouble(point, "y", 0),
                    GetJsonDouble(point, "distance", 0),
                    GetJsonDouble(point, "depth", 0),
                    GetJsonDouble(point, "nap", 0),
                    GetJsonDouble(point, "surface", 0)));
            }
            return rows.OrderBy(row => row.Distance).Select((row, index) => row with
            {
                Index = index + 1,
                Role = index == 0 ? "Intrede" : index == rows.Count - 1 ? "Uittrede" : "Dieptepunt"
            }).ToList();
        }
        catch
        {
            return [];
        }
    }

    private void RecalculateProfileRolesAndNap()
    {
        var ordered = _profilePoints.OrderBy(point => point.Distance).ToList();
        var count = ordered.Count;
        var total = count > 0 ? Math.Max(1, ordered[^1].Distance) : 1;
        var traceRows = GetTraceRowsForProfile();
        var traceDistances = BuildTraceDistances(traceRows);
        _profilePoints = ordered.Select((point, index) =>
        {
            var role = index == 0 ? "Intrede" : index == count - 1 ? "Uittrede" : "Dieptepunt";
            var xy = traceRows.Count >= 2 ? InterpolateTracePoint(traceRows, traceDistances, point.Distance) : new TracePointRow(0, role, point.X, point.Y);
            var surface = GetProfileSurfaceNap(xy.X, xy.Y, point.Distance, total, index, point.Surface);
            return point with { Index = index + 1, Role = role, X = xy.X, Y = xy.Y, Surface = surface, Nap = Math.Round(surface - point.Depth, 2) };
        }).ToList();
    }

    private List<ProfilePointRow> RefreshProfileSurfaces(IReadOnlyList<ProfilePointRow> points, bool preferStoredFallback = false)
    {
        if (points.Count == 0) return [];
        var total = Math.Max(1, points[^1].Distance);
        return points.Select((point, index) =>
        {
            var hasStoredSurface = double.IsFinite(point.Surface) && point.Surface > -100 && point.Surface < 400;
            var surface = preferStoredFallback && hasStoredSurface
                ? Math.Round(point.Surface, 2)
                : GetProfileSurfaceNap(point.X, point.Y, point.Distance, total, index, point.Surface);
            return point with
            {
                Surface = surface,
                Nap = Math.Round(surface - point.Depth, 2)
            };
        }).ToList();
    }

    private void RenderProfileEngineeringTable()
    {
        ProfileEngineeringPanel.Children.Clear();
        if (_profilePoints.Count < 2)
        {
            ProfileEngineeringPanel.Children.Add(new TextBlock { Text = "Geen profielstaat beschikbaar.", Foreground = Brush("#7F99AC"), FontSize = 11 });
            return;
        }

        var samples = BuildProfileEngineeringSamples();
        var boring = ComputeBoring();
        ProfileEngineeringPanel.Children.Add(new TextBlock
        {
            Text = $"Hoogte t.o.v. N.A.P.   ·   Boringtype: Buis   ·   Totale boorlengte: {samples.Last().BoreLength:N2} m   ·   Boring Ø{boring.BoringDiameter:N0} mm",
            Foreground = Brush("#071422"),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var rows = new (string Label, Func<ProfileEngineeringSample, string> Value)[]
        {
            ("Code op boorlijn", sample => sample.Code),
            ("Hor. afstand t.o.v. intredepunt (m)", sample => sample.Distance.ToString("N2", CultureInfo.CurrentCulture)),
            ("Werkelijk geboorde lengte (m)", sample => sample.BoreLength.ToString("N2", CultureInfo.CurrentCulture)),
            ("Hoogte maaiveld t.o.v. NAP (m)", sample => sample.SurfaceNap.ToString("N2", CultureInfo.CurrentCulture)),
            ("Hartlijn boring t.o.v. NAP (m)", sample => sample.BoreNap.ToString("N2", CultureInfo.CurrentCulture)),
            ("Hartlijn boring t.o.v. maaiveld (m)", sample => sample.Depth.ToString("N2", CultureInfo.CurrentCulture)),
            ("Hartlijn boring t.o.v. intredepunt (m)", sample => sample.DropFromStart.ToString("N2", CultureInfo.CurrentCulture)),
            ("Verticale hoek / radius", sample => sample.AngleRadius)
        };

        var grid = new Grid { MinWidth = 230 + samples.Count * 72, SnapsToDevicePixels = true };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        foreach (var _ in samples) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        for (var i = 0; i < rows.Length; i++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });

        for (var row = 0; row < rows.Length; row++)
        {
            AddProfileEngineeringCell(grid, row, 0, rows[row].Label, true);
            for (var column = 0; column < samples.Count; column++)
            {
                AddProfileEngineeringCell(grid, row, column + 1, rows[row].Value(samples[column]), false);
            }
        }

        ProfileEngineeringPanel.Children.Add(grid);
        AddProfileKlicLegendToPanel(ProfileEngineeringPanel, GetVisibleKlicProfileCrossings(_profilePoints[^1].Distance));
    }

    private void RenderProfilePanel()
    {
        if (_selectedStep?.Number != ProfileStepNumber && _selectedStep?.Number != WorkDrawingStepNumber) return;
        if (_profilePoints.Count < 2)
        {
            EnsureProfilePoints();
        }

        ProfileCanvas.Children.Clear();
        ProfilePointsPanel.Children.Clear();
        ProfileEngineeringPanel.Children.Clear();
        var pointsInSidebar = _selectedStep?.Number == WorkDrawingStepNumber;
        ProfilePointsTitle.Visibility = pointsInSidebar ? Visibility.Collapsed : Visibility.Visible;
        ProfilePointsScroll.Visibility = pointsInSidebar ? Visibility.Collapsed : Visibility.Visible;
        WorkDrawingTitleBlock.Visibility = pointsInSidebar ? Visibility.Visible : Visibility.Collapsed;
        UpdateWorkDrawingTitleBlock();

        if (_profilePoints.Count < 2)
        {
            ProfileSummaryText.Text = $"Teken en sla eerst een boorlijn op in stap {DisplayStepNumber(3)}.1.";
            if (pointsInSidebar) RenderWorkDrawingProfileRowsSidebar();
            return;
        }

        if (_profileGeometryDirty)
        {
            RecalculateProfileRolesAndNap();
            _profileGeometryDirty = false;
        }
        UpdateWorkDrawingTitleBlock();
        ProfileSummaryText.Text = $"{_profilePoints.Count} punt(en) - {_profilePoints[^1].Distance:N1} m - laagste boorlijn {_profilePoints.Min(p => p.Nap):N2} m NAP - boring Ø{GetBoringDiameterMillimeters()} mm";
        if (_profileHasUnsavedChanges)
        {
            ProfileSummaryText.Text += " - niet opgeslagen";
        }
        DrawProfileCanvas();
        RenderProfileEngineeringTable();
        RenderProfileRows();
        if (pointsInSidebar) RenderWorkDrawingProfileRowsSidebar();
    }

    private void RenderProfileRows()
    {
        ProfilePointsPanel.Children.Add(ProfileGridRow(["#", "Pos.", "Diepte", "NAP", "Segment", "Acties"], true, -1));
        for (var i = 0; i < _profilePoints.Count; i++)
        {
            var point = _profilePoints[i];
            var segment = i < _profilePoints.Count - 1 ? $"{SegmentDistance(i):N1} m / {SegmentAngle(i):N1}°" : "einde";
            ProfilePointsPanel.Children.Add(ProfileGridRow([
                point.Index == 1 ? "S" : point.Index == _profilePoints.Count ? "E" : point.Index.ToString(CultureInfo.InvariantCulture),
                $"{point.Distance:N1}",
                $"{point.Depth:N2}",
                $"{point.Nap:N2}",
                segment,
                ""
            ], false, i));
        }
    }

    private void RenderProfileToolsSidebarPanel()
    {
        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("#D7E8FA"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 10)
        };

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Dwarsprofiel",
            Foreground = Brush("#315B7E"),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var buttons = new UniformGrid { Columns = 1, Rows = 9 };
        AddBgtRibbonButton(buttons, "Genereer profiel", "Genereer profiel", true);
        AddBgtRibbonButton(buttons, "Boorlijn horizontaal", "Boorlijn horizontaal", true);
        AddBgtRibbonButton(buttons, "BGT oppervlakteanalyse", "BGT oppervlakteanalyse", false);
        AddBgtRibbonButton(buttons, "Kaart uitlijnen met profiel", "Kaart uitlijnen met profiel", false);
        AddBgtRibbonButton(buttons, _profileSmoothBore ? "Boorlijn hoekig" : "Boorlijn vloeiend", "Boorlijn vloeiend", false);
        AddBgtRibbonButton(buttons, _profileLayoutLocked ? "Profiel loszetten" : "Profiel vastzetten", "Profiel vastzetten", false);
        AddBgtRibbonButton(buttons, "+ Dieptepunt", "Voeg dieptepunt toe", false);
        AddBgtRibbonButton(buttons, "Sla dieptepunten op", "Sla dieptepunten op", false);
        AddBgtRibbonButton(buttons, "Download GeoJSON", "Download GeoJSON", false);
        panel.Children.Add(buttons);

        panel.Children.Add(new TextBlock
        {
            Text = "Kaart draait met de boorlijn mee. Het profiel eronder gebruikt dezelfde trace met X/Y en verstelbare Z/NAP-punten.",
            Foreground = Brush("#7F99AC"),
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        });

        card.Child = panel;
        ProfileToolsSidebarPanel.Children.Add(card);
    }

    private void RenderWorkDrawingProfileRowsSidebar()
    {
        for (var i = WorkDrawingSidebarPanel.Children.Count - 1; i >= 0; i--)
        {
            if (WorkDrawingSidebarPanel.Children[i] is FrameworkElement { Tag: "profileRows" })
            {
                WorkDrawingSidebarPanel.Children.RemoveAt(i);
            }
        }

        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("#D7E8FA"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 10),
            Tag = "profileRows"
        };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Boorpunten en segmenten",
            Foreground = Brush("#315B7E"),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        if (_profilePoints.Count < 2)
        {
            panel.Children.Add(new TextBlock { Text = "Geen boorlijn beschikbaar.", Foreground = Brush("#7F99AC"), FontSize = 11 });
        }
        else
        {
            for (var i = 0; i < _profilePoints.Count; i++)
            {
                var point = _profilePoints[i];
                var segment = i < _profilePoints.Count - 1 ? $"{SegmentDistance(i):N1} m / {SegmentAngle(i):N1} graden" : "einde";
                var row = new Border
                {
                    Background = i == 0 ? Brush("#F0FDF4") : i == _profilePoints.Count - 1 ? Brush("#FFF1F2") : Brush("#FFFFFF"),
                    BorderBrush = Brush("#E6EEF4"),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(7),
                    Margin = new Thickness(0, 0, 0, 5),
                    Child = new TextBlock
                    {
                        Text = $"{(point.Index == 1 ? "S" : point.Index == _profilePoints.Count ? "E" : point.Index.ToString(CultureInfo.InvariantCulture))}  pos {point.Distance:N1} m | diepte {point.Depth:N2} m | NAP {point.Nap:N2} | {segment}",
                        Foreground = Brush("#071422"),
                        FontSize = 10.5,
                        TextWrapping = TextWrapping.Wrap
                    }
                };
                panel.Children.Add(row);
            }
        }
        card.Child = panel;
        WorkDrawingSidebarPanel.Children.Add(card);
    }

    private void RequestProfileMapAlignmentIfNeeded()
    {
        if (!_profileAlignedToMap) return;
        if (_selectedStep?.Number is not ProfileStepNumber and not WorkDrawingStepNumber) return;
        if (!_mapLibreLoaded || StepThreeMapView.CoreWebView2 is null)
        {
            QueueMapSync();
            return;
        }

        SendMapMessage("{\"type\":\"profileAlignRequest\"}");
    }

    private string SaveDepthProfile()
    {
        if (_selectedProject is null) return "Geen project actief.";
        EnsureProfilePoints();
        if (_profilePoints.Count < 2) return $"Geen boorlijn beschikbaar. Teken en sla eerst stap {DisplayStepNumber(3)}.1 op.";

        var payload = JsonSerializer.Serialize(new
        {
            type = "Borevexa3DBoreLine",
            sourceStep = 7,
            units = "m",
            points = _profilePoints.Select(point => new
            {
                point.Index,
                point.Role,
                x = Math.Round(point.X, 3),
                y = Math.Round(point.Y, 3),
                distance = Math.Round(point.Distance, 2),
                depth = Math.Round(point.Depth, 2),
                nap = Math.Round(point.Nap, 2),
                surface = Math.Round(point.Surface, 2)
            })
        }, JsonOptions);
        SaveSelectedProjectStepData(ProfileStepNumber, "diepteprofiel_3d", payload);
        SaveProfileVisualSettings();
        _profileHasUnsavedChanges = false;
        _profileGeometryDirty = false;
        RenderProfilePanel();
        return $"Diepteprofiel opgeslagen\n\n{_profilePoints.Count} punt(en)\nTotale lengte: {_profilePoints[^1].Distance:N1} m\nLaagste punt: {_profilePoints.Min(point => point.Nap):N2} m NAP\n\nDe boorlijn is nu als X/Y/Z-profiel lokaal opgeslagen.";
    }

    private void SaveProfileVisualSettings()
    {
        if (_selectedProject is null) return;
        var payload = JsonSerializer.Serialize(new
        {
            smooth = _profileSmoothBore,
            expanded = _profileExpanded,
            layoutLocked = _profileLayoutLocked,
            alignedToMap = _profileAlignedToMap,
            viewZoom = Math.Round(_profileViewZoom, 3),
            savedAt = DateTimeOffset.Now
        }, JsonOptions);
        SaveSelectedProjectStepData(ProfileStepNumber, "profile_visual_settings", payload);
    }

    private void SendProfileModeToMap()
    {
        if (!_mapLibreLoaded || StepThreeMapView.CoreWebView2 is null)
        {
            QueueMapSync();
            return;
        }
        var enabled = _selectedStep?.Number == ProfileStepNumber || _selectedStep?.Number == WorkDrawingStepNumber;
        var workDrawing = _selectedStep?.Number == WorkDrawingStepNumber;
        SendMapMessage($"{{\"type\":\"profileMode\",\"enabled\":{enabled.ToString().ToLowerInvariant()}}}");
        SendMapMessage($"{{\"type\":\"workDrawingMode\",\"enabled\":{workDrawing.ToString().ToLowerInvariant()},\"scale\":{_workDrawingScale}}}");

        if (enabled)
        {
            SendProfilePointLabelsToMap();
        }
        else
        {
            ClearProfilePointLabelsFromMap();
        }
    }

    // Toont de dieptepunten (S/2/3/E) als kleine labels bovenop de GIS-kaart, op de
    // exacte RD-positie van elk profielpunt — zowel live (stap 7.1 GIS kaart) als in
    // de vastgezette rapportcapture (CaptureLiveMapForReportPreviewAsync).
    private void SendProfilePointLabelsToMap()
    {
        if (!_mapLibreLoaded || StepThreeMapView.CoreWebView2 is null) return;
        if (_profilePoints.Count == 0)
        {
            ClearProfilePointLabelsFromMap();
            return;
        }

        var pointCount = _profilePoints.Count;
        var labels = _profilePoints
            .Select(point =>
            {
                var wgs = RdToWgs84(point.X, point.Y);
                var code = point.Index == 1 ? "S" : point.Index == pointCount ? "E" : point.Index.ToString(CultureInfo.InvariantCulture);
                return new { code, lon = wgs[0], lat = wgs[1] };
            })
            .Where(label => double.IsFinite(label.lon) && double.IsFinite(label.lat))
            .ToList();

        SendMapMessage(JsonSerializer.Serialize(new { type = "profilePointLabels", labels }, JsonOptions));
    }

    private void ClearProfilePointLabelsFromMap()
    {
        if (!_mapLibreLoaded || StepThreeMapView.CoreWebView2 is null) return;
        SendMapMessage(JsonSerializer.Serialize(new { type = "profilePointLabels", labels = Array.Empty<object>() }, JsonOptions));
    }

    private static double SoundingProfileDepthY(double depth, double plotTop, double plotHeight, double endDepth)
    {
        return plotTop + Math.Clamp(depth / Math.Max(0.1, endDepth), 0, 1) * plotHeight;
    }

    private static string SoundingProfileFill(ReportRenderSoundingInterval interval)
    {
        if (IsReportHexColor(interval.Color))
        {
            return interval.Color;
        }

        var text = $"{interval.Label} {interval.Lithology} {interval.Code}".ToLowerInvariant();
        if (text.Contains("klei") || text.Contains("clay")) return "#15803D";
        if (text.Contains("zand") || text.Contains("sand")) return "#FDE047";
        if (text.Contains("veen") || text.Contains("peat")) return "#A0523F";
        if (text.Contains("grind") || text.Contains("gravel")) return "#94A3B8";
        if (text.Contains("leem") || text.Contains("loam")) return "#C4B5FD";
        return "#CBD5E1";
    }

    private string ToggleProfileLayoutLock()
    {
        if (!_profileLayoutLocked && _profileScreenMetrics is null)
        {
            _profileAlignedToMap = true;
            SendMapMessage("{\"type\":\"profileAlignRequest\"}");
        }

        _profileLayoutLocked = !_profileLayoutLocked;
        SaveProfileVisualSettings();
        ProfileToolsSidebarPanel.Children.Clear();
        RenderProfileToolsSidebarPanel();
        RenderProfilePanel();
        return _profileLayoutLocked
            ? "Dwarsprofiel vastgezet\n\nDe horizontale uitlijning blijft nu staan. In- en uitzoomen verandert alleen de verticale schaal."
            : "Dwarsprofiel losgezet\n\nUitlijnen mag de horizontale posities weer opnieuw ophalen uit de kaart.";
    }

    private string ToggleSmoothProfile()
    {
        _profileSmoothBore = !_profileSmoothBore;
        SaveProfileVisualSettings();
        RenderProfilePanel();
        ProfileToolsSidebarPanel.Children.Clear();
        RenderProfileToolsSidebarPanel();
        return _profileSmoothBore
            ? "Boorlijn vloeiend\n\nDe boorzone wordt nu als vloeiende curve weergegeven. Dieptepunten blijven behouden."
            : "Boorlijn hoekig\n\nDe boorzone wordt weer als rechte segmenten tussen de dieptepunten weergegeven.";
    }
}
