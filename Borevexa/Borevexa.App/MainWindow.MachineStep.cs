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

// Machinekeuze en -plaatsing (stap 1.5/8): machinelijst, plaatsing op kaart.
// Fase 3-opsplitsing (07-07-2026) van MainWindow.xaml.cs; gedrag ongewijzigd.

public partial class MainWindow
{
    private static void AddMachineArc(List<MachineSymbolLine> segments, double cx, double cy, double radius, double startAngle, double endAngle)
    {
        if (radius <= 0) return;
        var span = endAngle - startAngle;
        if (span <= 0) span += 360;
        var steps = Math.Max(8, (int)Math.Ceiling(span / 12));
        Point? previous = null;
        for (var i = 0; i <= steps; i++)
        {
            var angle = (startAngle + span * i / steps) * Math.PI / 180;
            var point = new Point(cx + Math.Cos(angle) * radius, cy + Math.Sin(angle) * radius);
            if (previous.HasValue) AddMachineLine(segments, previous.Value.X, previous.Value.Y, point.X, point.Y);
            previous = point;
        }
    }

    private static void AddMachineLine(List<MachineSymbolLine> segments, double x1, double y1, double x2, double y2)
    {
        if (Math.Abs(x1 - x2) + Math.Abs(y1 - y2) < 0.0001) return;
        segments.Add(new MachineSymbolLine(x1, y1, x2, y2));
    }

    private void AddMachineRibbonButton(Panel parent, string label, string action, bool primary)
    {
        var button = new Button
        {
            Content = label,
            Tag = action,
            Height = 30,
            Margin = new Thickness(0, 0, 5, 5),
            Padding = new Thickness(4, 0, 4, 0),
            Background = primary ? Brush("#3F4750") : Brushes.White,
            Foreground = primary ? Brushes.White : Brush("#071422"),
            BorderBrush = primary ? Brush("#3F4750") : Brush("#DEE6EA"),
            BorderThickness = new Thickness(1),
            FontSize = 10.5,
            FontWeight = primary ? FontWeights.Bold : FontWeights.SemiBold
        };
        button.Click += StepAction_OnClick;
        parent.Children.Add(button);
    }

    private void AddStepEightMachineRibbon()
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
            Text = "Machineplaatsing",
            Foreground = Brush("#3F4750"),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var buttons = new UniformGrid { Columns = 2, Rows = 6 };
        AddMachineRibbonButton(buttons, "+ Boormachine", "Plaats boormachine", true);
        AddMachineRibbonButton(buttons, "+ Bentonietwagen", "Plaats bentonietwagen", true);
        AddMachineRibbonButton(buttons, "Uitlijnen boorlijn", "Lijn machine uit op boorlijn", false);
        AddMachineRibbonButton(buttons, "Zet op intrede", "Zet machine op intrede", false);
        AddMachineRibbonButton(buttons, "Roteer bentoniet", "Roteer bentonietwagen", false);
        AddMachineRibbonButton(buttons, "Maat toepassen", "Pas machinemaat toe", false);
        AddMachineRibbonButton(buttons, "Info klikken", "Machine info modus", false);
        AddMachineRibbonButton(buttons, "Handmodus", "Machine handmodus", false);
        AddMachineRibbonButton(buttons, "Verwijderen", "Verwijder machines", false);
        AddMachineRibbonButton(buttons, "Opslaan", "Sla machines op", true);
        AddMachineRibbonButton(buttons, "Stop plaatsen", "Stop machine plaatsen", false);
        panel.Children.Add(buttons);

        var sizeGrid = new UniformGrid { Columns = 2, Rows = 2, Margin = new Thickness(0, 6, 0, 0) };
        sizeGrid.Children.Add(CreateMachineSizeInputPanel("Machine lengte", _machineLengthMeters, out _machineLengthInput));
        sizeGrid.Children.Add(CreateMachineSizeInputPanel("Machine breedte", _machineWidthMeters, out _machineWidthInput));
        sizeGrid.Children.Add(CreateMachineSizeInputPanel("Boorgat lengte", _borePitLengthMeters, out _borePitLengthInput));
        sizeGrid.Children.Add(CreateMachineSizeInputPanel("Boorgat breedte", _borePitWidthMeters, out _borePitWidthInput));
        panel.Children.Add(sizeGrid);

        panel.Children.Add(new TextBlock
        {
            Text = "Standaard: boormachine 5,42 x 2,90 m en boorgat 3,00 x 1,00 m. Klik om te plaatsen; sleep de rechthoek om het aansluitpunt op de intrede te zetten.",
            Foreground = Brush("#7F99AC"),
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        });

        _machinePlacementsTablePanel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(_machinePlacementsTablePanel);
        RenderMachinePlacementsTable();

        card.Child = panel;
        MachineSidebarPanel.Children.Add(card);
        MachineSidebarHost.Visibility = Visibility.Visible;
    }

    private string ApplySelectedMachineSize()
    {
        if (_selectedStep?.Number != MachineStepNumber) return $"Machinemaat aanpassen is alleen beschikbaar in stap {MachineStepNumber}.";
        ReadMachineDimensionsFromInputs();
        var payload = JsonSerializer.Serialize(new
        {
            type = "machineUpdateSelected",
            length = _machineLengthMeters,
            width = _machineWidthMeters,
            boreLength = _borePitLengthMeters,
            boreWidth = _borePitWidthMeters
        }, JsonOptions);
        SendMapMessage(payload);
        return $"Machinemaat toepassen\n\nGeselecteerde plaatsing wordt machine {_machineLengthMeters:0.##} x {_machineWidthMeters:0.##} m en boorgat {_borePitLengthMeters:0.##} x {_borePitWidthMeters:0.##} m.";
    }

    private object BuildMachineChoiceReportDataPayload()
    {
        var boring = ComputeBoring();
        var requiredBoringDiameter = boring.BoringDiameter;
        var selectedMachine = Machines.FirstOrDefault(machine => string.Equals(machine.Id, _selectedMachineId, StringComparison.OrdinalIgnoreCase));
        var preferredMachine = GetPreferredMachine(requiredBoringDiameter);
        var recommendation = BuildSelectedMachineRecommendation(requiredBoringDiameter);

        return new
        {
            selectedMachineId = _selectedMachineId,
            selectedMachine,
            drillingTechnique = GetDrillingTechniqueLabel(_selectedDrillingTechnique),
            drillingTechniqueKey = _selectedDrillingTechnique,
            requiredBoringDiameter,
            recommendedMachine = preferredMachine,
            recommendationLabel = recommendation.Label,
            recommendationReason = recommendation.Reason
        };
    }

    private static MachinePreference BuildMachinePreference(DrillMachine machine, int requiredBoringDiameter, DrillMachine? preferredMachine)
    {
        if (machine.MaxBoring < requiredBoringDiameter)
        {
            return new MachinePreference(
                "Niet passend",
                $"Max. boring Ø{machine.MaxBoring:N0} mm is kleiner dan de vereiste Ø{requiredBoringDiameter:N0} mm.",
                "#DC2626");
        }

        var margin = machine.MaxBoring - requiredBoringDiameter;
        if (preferredMachine is not null && string.Equals(machine.Id, preferredMachine.Id, StringComparison.OrdinalIgnoreCase))
        {
            return new MachinePreference(
                "Voorkeur",
                $"Kleinste passende machine: Ø{margin:N0} mm marge op de vereiste boring en geen onnodig groot materieel.",
                "#15803D");
        }

        return new MachinePreference(
            "Passend, maar ruimer dan nodig",
            $"Technisch passend met Ø{margin:N0} mm marge, maar groter dan nodig. Een compactere passende machine kan volstaan als die beschikbaar is.",
            "#B45309");
    }

    private static string BuildSelectedMachineDatasheetSummary(DrillMachine machine, int requiredBoringDiameter)
    {
        var margin = machine.MaxBoring - requiredBoringDiameter;
        var marginText = margin >= 0
            ? $"Er is circa Ø{margin:N0} mm marge ten opzichte van de berekende vereiste boring."
            : $"De berekende vereiste boring is circa Ø{Math.Abs(margin):N0} mm groter dan het opgegeven machinebereik.";
        var sourceNote = string.IsNullOrWhiteSpace(machine.SourceNote) ? "" : $" {machine.SourceNote}";
        return $"Datasheet samenvatting: {machine.Brand} {machine.Model} met {machine.Engine}, max. boring Ø{machine.MaxBoring:N0} mm, kracht {FormatMachineForceValue(machine)}, koppel {machine.TorqueNm:N0} Nm en stangenrek {machine.RodsMeters:N0} m. {marginText}{sourceNote}";
    }

    private MachinePreference BuildSelectedMachineRecommendation(int requiredBoringDiameter)
    {
        var preferredMachine = GetPreferredMachine(requiredBoringDiameter);
        var selectedMachine = Machines.FirstOrDefault(machine => string.Equals(machine.Id, _selectedMachineId, StringComparison.OrdinalIgnoreCase));
        if (selectedMachine is not null)
        {
            return BuildMachinePreference(selectedMachine, requiredBoringDiameter, preferredMachine);
        }

        if (preferredMachine is not null)
        {
            var preference = BuildMachinePreference(preferredMachine, requiredBoringDiameter, preferredMachine);
            return new MachinePreference(
                "Nog kiezen",
                $"Advies: kies de kleinste beschikbare passende machine. {preference.Reason}",
                preference.Color);
        }

        return new MachinePreference(
            "Geen passende machine",
            $"Geen machine in de catalogus haalt de vereiste boring Ø{requiredBoringDiameter:N0} mm.",
            "#DC2626");
    }

    private void CaptureMachinePlacements(string message)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            _currentMachinePlacementsJson = root.TryGetProperty("items", out var itemsElement) && itemsElement.ValueKind == JsonValueKind.Array
                ? itemsElement.GetRawText()
                : "[]";
            RenderMachinePlacementsTable();
        }
        catch
        {
            _currentMachinePlacementsJson = "[]";
            RenderMachinePlacementsTable();
        }
    }

    private string ClearMachines()
    {
        if (_selectedProject is null) return "Geen project actief.";
        _currentMachinePlacementsJson = "[]";
        SaveSelectedProjectStepData(MachineStepNumber, "machine_placements", "[]");
        RenderMachinePlacementsTable();
        SendMapMessage("{\"type\":\"machineClear\"}");
        return "Machineplaatsingen verwijderd\n\nAlle machine-rechthoeken zijn lokaal gewist.";
    }

    private TextBox CreateMachineSizeInput(string label, double value)
    {
        return new TextBox
        {
            Text = value.ToString("0.##", CultureInfo.CurrentCulture),
            ToolTip = label,
            Height = 28,
            FontSize = 11,
            Padding = new Thickness(6, 4, 6, 4),
            BorderBrush = Brush("#DEE6EA"),
            Background = Brushes.White
        };
    }

    private StackPanel CreateMachineSizeInputPanel(string label, double value, out TextBox input)
    {
        input = CreateMachineSizeInput(label, value);
        return new StackPanel
        {
            Margin = new Thickness(0, 0, 6, 6),
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    Foreground = Brush("#587080"),
                    FontSize = 9.5,
                    Margin = new Thickness(0, 0, 0, 2)
                },
                input
            }
        };
    }

    private static UIElement CreateSelectedMachineReportBlock(int requiredBoringDiameter, string? selectedMachineId)
    {
        var machine = Machines.FirstOrDefault(item => string.Equals(item.Id, selectedMachineId, StringComparison.OrdinalIgnoreCase));
        if (machine is null)
        {
            return CreateReportNote("Nog geen boormachine geselecteerd. Selecteer in stap 1 de beoogde machine zodat deze in het rapport wordt opgenomen.");
        }

        var compatible = machine.MaxBoring >= requiredBoringDiameter;
        var panel = new StackPanel();
        panel.Children.Add(CreateReportKeyValues(
            ("Geselecteerde machine", $"{machine.Brand} {machine.Model}"),
            ("Motor", machine.Engine),
            ("Max. boring", $"Ø{machine.MaxBoring:N0} mm"),
            ("Vereiste boring", $"Ø{requiredBoringDiameter:N0} mm"),
            ("Duw/trek", FormatMachineForceValue(machine)),
            ("Koppel", $"{machine.TorqueNm:N0} Nm"),
            ("Stangenrek", $"{machine.RodsMeters:N0} m"),
            ("Geschiktheid", compatible ? "Passend op basis van boordiameter" : "Controle nodig: vereiste boring groter dan opgegeven machinebereik")));
        panel.Children.Add(CreateReportNote(BuildSelectedMachineDatasheetSummary(machine, requiredBoringDiameter)));
        return panel;
    }

    private sealed record DrillMachine(string Id, string Brand, string Model, int MaxBoring, double PushKn, int TorqueNm, int RodsMeters, string Engine, double PullbackKn = 0, string SourceNote = "");

    private static string FormatMachineForceValue(DrillMachine machine)
    {
        var pullback = machine.PullbackKn > 0 ? machine.PullbackKn : machine.PushKn;
        return Math.Abs(pullback - machine.PushKn) < 0.05
            ? $"{machine.PushKn:N1} kN"
            : $"duw {machine.PushKn:N1} kN / trek {pullback:N1} kN";
    }

    private static string FormatMachineStatusMessage(string message)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            var count = root.TryGetProperty("items", out var itemsElement) && itemsElement.ValueKind == JsonValueKind.Array
                ? itemsElement.GetArrayLength()
                : 0;
            return $"Machineplaatsing bijgewerkt\n\n{count} object(en) op de kaart. Klik op 'Opslaan' om dit lokaal vast te leggen.";
        }
        catch
        {
            return $"Machineplaatsing bijgewerkt\n\n{message}";
        }
    }

    private MachineSymbol GetMachineSideSymbol() =>
        _machineSideSymbol ??= LoadMachineSymbol("boormachine-zijaanzicht.dxf");

    private MachineSymbol GetMachineTopSymbol() =>
        _machineTopSymbol ??= LoadMachineSymbol("boormachine-bovenaanzicht.dxf");

    private static DrillMachine? GetPreferredMachine(int requiredBoringDiameter) =>
        Machines
            .Where(machine => machine.MaxBoring >= requiredBoringDiameter)
            .OrderBy(machine => machine.MaxBoring)
            .ThenBy(machine => machine.PushKn)
            .ThenBy(machine => machine.TorqueNm)
            .ThenBy(machine => machine.RodsMeters)
            .FirstOrDefault();

    private string? GetStoredMachinePlacementsJson()
    {
        if (_selectedProject is null) return null;
        var json =
            _projects.GetStepData(_selectedProject.Id, MachineStepNumber, "machine_placements") ??
            _projects.GetStepData(_selectedProject.Id, LegacyMachineStepNumber, "machine_placements") ??
            _projects.GetStepData(_selectedProject.Id, 9, "machine_placements");
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                _currentMachinePlacementsJson = document.RootElement.GetRawText();
                return _currentMachinePlacementsJson;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static MachineSymbol LoadMachineSymbol(string fileName)
    {
        var path = MachineAssetPath(fileName);
        if (!System.IO.File.Exists(path)) return MachineSymbol.Empty;
        try
        {
            return ParseMachineDxf(System.IO.File.ReadAllLines(path));
        }
        catch
        {
            return MachineSymbol.Empty;
        }
    }

    private static string MachineAssetPath(string fileName) =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Machine", fileName);

    private static string MachineDisplayName(DrillMachine? machine) =>
        machine is null ? "-" : $"{machine.Brand} {machine.Model}";

    private sealed record MachinePlacementRow(string Label, double Length, double Width);

    private sealed record MachinePreference(string Label, string Reason, string Color);

    private sealed record MachineSymbol(IReadOnlyList<MachineSymbolLine> Lines, double MinX, double MinY, double MaxX, double MaxY, double Width, double Height)
    {
        public static MachineSymbol Empty { get; } = new([], 0, 0, 1, 1, 1, 1);
    }

    private sealed record MachineSymbolLine(double X1, double Y1, double X2, double Y2);

    private static double ParseMachineDimension(string? text, double fallback)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) ||
            double.TryParse(text?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return Math.Clamp(value, 0.5, 60);
        }

        return fallback;
    }

    private static MachineSymbol ParseMachineDxf(string[] lines)
    {
        var pairs = new List<(string Code, string Value)>();
        for (var i = 0; i < lines.Length - 1; i += 2)
        {
            pairs.Add((lines[i].Trim(), lines[i + 1].Trim()));
        }

        var segments = new List<MachineSymbolLine>();
        for (var i = 0; i < pairs.Count;)
        {
            if (pairs[i].Code != "0")
            {
                i++;
                continue;
            }

            var type = pairs[i].Value.ToUpperInvariant();
            var start = ++i;
            while (i < pairs.Count && pairs[i].Code != "0") i++;
            var entity = pairs.GetRange(start, i - start);

            if (type == "LINE")
            {
                if (TryReadDxfValue(entity, "10", out var x1) &&
                    TryReadDxfValue(entity, "20", out var y1) &&
                    TryReadDxfValue(entity, "11", out var x2) &&
                    TryReadDxfValue(entity, "21", out var y2))
                {
                    AddMachineLine(segments, x1, y1, x2, y2);
                }
            }
            else if (type == "LWPOLYLINE")
            {
                var points = ReadDxfPolylinePoints(entity);
                for (var p = 0; p < points.Count - 1; p++) AddMachineLine(segments, points[p].X, points[p].Y, points[p + 1].X, points[p + 1].Y);
                var closed = TryReadDxfInt(entity, "70", out var flags) && (flags & 1) == 1;
                if (closed && points.Count > 2) AddMachineLine(segments, points[^1].X, points[^1].Y, points[0].X, points[0].Y);
            }
            else if (type == "ARC")
            {
                if (TryReadDxfValue(entity, "10", out var cx) &&
                    TryReadDxfValue(entity, "20", out var cy) &&
                    TryReadDxfValue(entity, "40", out var radius) &&
                    TryReadDxfValue(entity, "50", out var startAngle) &&
                    TryReadDxfValue(entity, "51", out var endAngle))
                {
                    AddMachineArc(segments, cx, cy, radius, startAngle, endAngle);
                }
            }
            else if (type == "CIRCLE")
            {
                if (TryReadDxfValue(entity, "10", out var cx) &&
                    TryReadDxfValue(entity, "20", out var cy) &&
                    TryReadDxfValue(entity, "40", out var radius))
                {
                    AddMachineArc(segments, cx, cy, radius, 0, 360);
                }
            }
        }

        if (segments.Count == 0) return MachineSymbol.Empty;
        var minX = segments.Min(s => Math.Min(s.X1, s.X2));
        var minY = segments.Min(s => Math.Min(s.Y1, s.Y2));
        var maxX = segments.Max(s => Math.Max(s.X1, s.X2));
        var maxY = segments.Max(s => Math.Max(s.Y1, s.Y2));
        return new MachineSymbol(segments, minX, minY, maxX, maxY, Math.Max(0.01, maxX - minX), Math.Max(0.01, maxY - minY));
    }

    private void ReadMachineDimensionsFromInputs()
    {
        _machineLengthMeters = ParseMachineDimension(_machineLengthInput?.Text, 5.42);
        _machineWidthMeters = ParseMachineDimension(_machineWidthInput?.Text, 2.9);
        _borePitLengthMeters = ParseMachineDimension(_borePitLengthInput?.Text, 3);
        _borePitWidthMeters = ParseMachineDimension(_borePitWidthInput?.Text, 1);
        if (_machineLengthInput is not null) _machineLengthInput.Text = _machineLengthMeters.ToString("0.##", CultureInfo.CurrentCulture);
        if (_machineWidthInput is not null) _machineWidthInput.Text = _machineWidthMeters.ToString("0.##", CultureInfo.CurrentCulture);
        if (_borePitLengthInput is not null) _borePitLengthInput.Text = _borePitLengthMeters.ToString("0.##", CultureInfo.CurrentCulture);
        if (_borePitWidthInput is not null) _borePitWidthInput.Text = _borePitWidthMeters.ToString("0.##", CultureInfo.CurrentCulture);
    }

    private IReadOnlyList<MachinePlacementRow> ReadMachinePlacementRows() =>
        ReadMachinePlacementRows(_currentMachinePlacementsJson ?? GetStoredMachinePlacementsJson() ?? "[]");

    private static IReadOnlyList<MachinePlacementRow> ReadMachinePlacementRows(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return [];

            var rows = new List<MachinePlacementRow>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                var label = item.TryGetProperty("label", out var labelElement) ? labelElement.ToString() : "Machine";
                var length = item.TryGetProperty("length", out var lengthElement) && lengthElement.TryGetDouble(out var lengthValue) ? lengthValue : 4;
                var width = item.TryGetProperty("width", out var widthElement) && widthElement.TryGetDouble(out var widthValue) ? widthValue : 3;
                rows.Add(new MachinePlacementRow(label, length, width));
            }

            return rows;
        }
        catch
        {
            return [];
        }
    }

    private void RenderMachineCards(int boringDiameter)
    {
        MachineCardsPanel.Children.Clear();
        var preferredMachine = GetPreferredMachine(boringDiameter);
        foreach (var machine in Machines)
        {
            var compatible = machine.MaxBoring >= boringDiameter;
            var selected = _selectedMachineId == machine.Id;
            var preferred = preferredMachine is not null && string.Equals(machine.Id, preferredMachine.Id, StringComparison.OrdinalIgnoreCase);
            var preference = BuildMachinePreference(machine, boringDiameter, preferredMachine);
            var card = new Border
            {
                Background = selected ? Brush("#EEF2F5") : preferred ? Brush("#F0FDF4") : compatible ? Brushes.White : Brush("#F8FAFB"),
                BorderBrush = selected ? Brush("#374151") : preferred ? Brush("#86EFAC") : compatible ? Brush("#DEE6EA") : Brush("#F1F4F6"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 5),
                Opacity = compatible ? 1 : 0.48,
                Tag = machine.Id
            };
            card.MouseLeftButtonUp += MachineCard_OnClick;

            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = $"{machine.Brand} {machine.Model}", FontWeight = FontWeights.SemiBold, Foreground = Brush("#1B2B35"), FontSize = 12 });
            panel.Children.Add(new TextBlock { Text = machine.Engine, Foreground = Brush("#8FA6B2"), FontSize = 10, Margin = new Thickness(0, 1, 0, 5) });
            panel.Children.Add(new TextBlock { Text = $"Max boring Ø{machine.MaxBoring} mm   Kracht {FormatMachineForceValue(machine)}   Koppel {machine.TorqueNm:N0} Nm   Stangenrek {machine.RodsMeters} m", Foreground = Brush("#587080"), FontSize = 10, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = selected ? $"geselecteerd · {preference.Label}" : preference.Label, Foreground = selected ? Brush("#374151") : Brush(preference.Color), FontSize = 10, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 5, 0, 0) });
            panel.Children.Add(new TextBlock { Text = preference.Reason, Foreground = Brush("#587080"), FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            card.Child = panel;
            MachineCardsPanel.Children.Add(card);
        }
    }

    private void RenderMachinePlacementsTable()
    {
        if (_machinePlacementsTablePanel is null) return;
        _machinePlacementsTablePanel.Children.Clear();

        var items = ReadMachinePlacementRows();
        if (items.Count == 0)
        {
            _machinePlacementsTablePanel.Children.Add(new TextBlock
            {
                Text = "Nog geen machines geplaatst.",
                Foreground = Brush("#8FA6B2"),
                FontSize = 10.5
            });
            return;
        }

        foreach (var item in items)
        {
            _machinePlacementsTablePanel.Children.Add(new TextBlock
            {
                Text = $"{item.Label}: {item.Length:0.#} x {item.Width:0.#} m",
                Foreground = Brush("#071422"),
                FontSize = 10.5,
                Margin = new Thickness(0, 2, 0, 0)
            });
        }
    }

    private string SaveMachinePlacements()
    {
        if (_selectedProject is null) return "Geen project actief.";
        var json = string.IsNullOrWhiteSpace(_currentMachinePlacementsJson)
            ? GetStoredMachinePlacementsJson() ?? "[]"
            : _currentMachinePlacementsJson;
        SaveSelectedProjectStepData(MachineStepNumber, "machine_placements", json);
        var count = ReadMachinePlacementRows(json).Count;
        return $"Machineplaatsing opgeslagen\n\n{count} object(en) lokaal opgeslagen in stap {MachineStepNumber}.";
    }

    private void SendMachineStateToMap()
    {
        if (!_mapLibreLoaded || StepThreeMapView.CoreWebView2 is null)
        {
            QueueMapSync();
            return;
        }
        var visible = _mapOverlayStates.TryGetValue("machines", out var machineVisible) && machineVisible;
        var enabled = _selectedStep?.Number == MachineStepNumber && visible;
        var json = enabled ? (_currentMachinePlacementsJson ?? GetStoredMachinePlacementsJson()) : null;
        var symbolJson = JsonSerializer.Serialize(GetMachineTopSymbol(), JsonOptions);
        var payload = $"{{\"type\":\"machineState\",\"enabled\":{enabled.ToString().ToLowerInvariant()},\"items\":{(string.IsNullOrWhiteSpace(json) ? "[]" : json)},\"symbol\":{symbolJson}}}";
        SendMapMessage(payload);
    }

    private string StartMachinePlacement(string machineType, string label)
    {
        if (_selectedStep?.Number != MachineStepNumber) return $"Machine plaatsen is alleen beschikbaar in stap {MachineStepNumber}.";
        ReadMachineDimensionsFromInputs();
        var length = machineType == "bentonite" ? 12.5 : _machineLengthMeters;
        var width = machineType == "bentonite" ? 4.0 : _machineWidthMeters;
        var boreLength = machineType == "bentonite" ? 0.0 : _borePitLengthMeters;
        var boreWidth = machineType == "bentonite" ? 0.0 : _borePitWidthMeters;
        var payload = JsonSerializer.Serialize(new
        {
            type = "machinePlace",
            machineType,
            label,
            length,
            width,
            boreLength,
            boreWidth
        }, JsonOptions);
        SendMachineStateToMap();
        SendMapMessage(payload);
        return machineType == "rig"
            ? $"{label} plaatsen\n\nKlik op de kaart om het aansluitpunt op de intrede te zetten. Machine {_machineLengthMeters:0.##} x {_machineWidthMeters:0.##} m, boorgat {_borePitLengthMeters:0.##} x {_borePitWidthMeters:0.##} m."
            : $"{label} plaatsen\n\nKlik op de kaart om een vlak van {length:0.##} x {width:0.##} m te plaatsen.";
    }
}
