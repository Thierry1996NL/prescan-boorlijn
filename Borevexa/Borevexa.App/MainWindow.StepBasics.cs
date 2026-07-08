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

// Stappen 0/1/2: rapportstart, projectinformatie en imports-UI.
// Fase 3-opsplitsing (07-07-2026) van MainWindow.xaml.cs; gedrag ongewijzigd.

public partial class MainWindow
{
    private void AddStepOneWorkflowParts(Action<string, string, FrameworkElement[]> add)
    {
        var substepNumber = _selectedSubstep?.Number;
        if (string.IsNullOrWhiteSpace(substepNumber))
        {
            add("projectgegevens", "Projectgegevens", [StepOneProjectInfoPanel]);
            add("inhoud", "Inhoud", [StepOneContentPanel]);
            add("vulgraad", "Vulgraad", [StepOneFillPanel]);
            add("machine", "Machinekeuze", [StepOneMachinePanel]);
            return;
        }

        switch (substepNumber)
        {
            case "1.1":
                add("projectgegevens", "Projectgegevens", [StepOneProjectInfoPanel]);
                break;
            case "1.2":
                add("inhoud", "Inhoud", [StepOneContentPanel]);
                break;
            case "1.3":
                add("vulgraad", "Vulgraad", [StepOneFillPanel]);
                break;
            case "1.4":
                add("machine", "Machinekeuze", [StepOneMachinePanel]);
                break;
        }
    }

    private void ApplyStepOneLayoutState()
    {
        if (_selectedProject is null) return;
        var json = _projects.GetStepData(_selectedProject.Id, 1, "step1_layout");
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                var left = ReadJsonDoubleProperty(root, "left", "Left");
                var right = ReadJsonDoubleProperty(root, "right", "Right");
                if (left > 0 && right > 0)
                {
                    StepOneLeftColumn.Width = new GridLength(left, GridUnitType.Star);
                    StepOneRightColumn.Width = new GridLength(right, GridUnitType.Star);
                    return;
                }
            }
            catch (System.Exception swallowedException)
            {
                // Use the default center split when stored layout data is invalid.
                AppLog.Swallowed(swallowedException);
            }
        }

        StepOneLeftColumn.Width = new GridLength(1, GridUnitType.Star);
        StepOneRightColumn.Width = new GridLength(1, GridUnitType.Star);
    }

    private void ApplyStepOneSubstepLayout()
    {
        var substepNumber = _selectedSubstep?.Number;
        var showSingleSubstep = _selectedStep?.Number == 1 && !string.IsNullOrWhiteSpace(substepNumber);

        StepOneColumnSplitter.Visibility = showSingleSubstep ? Visibility.Collapsed : Visibility.Visible;
        StepOneLeftTopRowSplitter.Visibility = showSingleSubstep ? Visibility.Collapsed : Visibility.Visible;
        StepOneLeftBottomRowSplitter.Visibility = showSingleSubstep ? Visibility.Collapsed : Visibility.Visible;
        StepOneRightRowSplitter.Visibility = showSingleSubstep ? Visibility.Collapsed : Visibility.Visible;

        ResetStepOnePanelPlacement(StepOneProjectInfoPanel, 0, 0, 1, 1, new Thickness(0, 0, 8, 8));
        ResetStepOnePanelPlacement(StepOneContentPanel, 1, 0, 1, 1, new Thickness(0, 0, 8, 8));
        ResetStepOnePanelPlacement(StepOneFillPanel, 0, 1, 2, 1, new Thickness(0, 0, 0, 8));
        ResetStepOnePanelPlacement(StepOneMachinePanel, 2, 0, 1, 1, new Thickness(0, 0, 8, 0));
        ResetStepOnePanelPlacement(StepOneAiPanel, 2, 1, 1, 1, new Thickness(0));
        StepOneAiPanel.Visibility = Visibility.Collapsed;

        if (!showSingleSubstep)
        {
            StepOneProjectInfoPanel.Visibility = Visibility.Visible;
            StepOneContentPanel.Visibility = Visibility.Visible;
            StepOneFillPanel.Visibility = Visibility.Visible;
            StepOneMachinePanel.Visibility = Visibility.Visible;
            return;
        }

        if (IsChapterIntroductionSubstep(_selectedSubstep))
        {
            StepOneProjectInfoPanel.Visibility = Visibility.Collapsed;
            StepOneContentPanel.Visibility = Visibility.Collapsed;
            StepOneFillPanel.Visibility = Visibility.Collapsed;
            StepOneMachinePanel.Visibility = Visibility.Collapsed;
            return;
        }

        StepOneProjectInfoPanel.Visibility = substepNumber == "1.1" ? Visibility.Visible : Visibility.Collapsed;
        StepOneContentPanel.Visibility = substepNumber == "1.2" ? Visibility.Visible : Visibility.Collapsed;
        StepOneFillPanel.Visibility = substepNumber == "1.3" ? Visibility.Visible : Visibility.Collapsed;
        StepOneMachinePanel.Visibility = substepNumber == "1.4" ? Visibility.Visible : Visibility.Collapsed;

        var activePanel = substepNumber switch
        {
            "1.1" => StepOneProjectInfoPanel,
            "1.2" => StepOneContentPanel,
            "1.3" => StepOneFillPanel,
            "1.4" => StepOneMachinePanel,
            _ => StepOneProjectInfoPanel
        };

        Grid.SetRow(activePanel, 0);
        Grid.SetColumn(activePanel, 0);
        Grid.SetRowSpan(activePanel, 3);
        Grid.SetColumnSpan(activePanel, 2);
        activePanel.Margin = new Thickness(0);
    }

    private string BuildStepTwoRenderSignature()
    {
        var fileSignature = string.Join("|", _projectFiles
            .OrderBy(file => file.Id)
            .Select(file => $"{file.Id:N}:{file.FileType}:{file.SizeBytes}:{file.CreatedAt:O}"));
        return string.Join(";",
            _selectedProject?.Id.ToString("N") ?? "geen-project",
            _selectedSubstep?.Number ?? "geen-substap",
            fileSignature);
    }

    private void RenderStepOne()
    {
        ApplyStepOneLayoutState();
        EnsureBoringConfigLoaded();
        RenderProjectInfoRows();
        RenderBoringConfigurator();
        RefreshInlineReportPreviewIfVisible();
    }

    private void RenderStepOneAiSidebar()
    {
        StepThreeImportsPanel.Children.Clear();
        SurfaceAnalysisSidebarPanel.Children.Clear();
        StepThreeBaseLayersPanel.Children.Clear();
        StepThreeEsriLayersPanel.Children.Clear();
        StepThreeOverlaysPanel.Children.Clear();
        BoreTraceSidebarPanel.Children.Clear();
        ProfileToolsSidebarPanel.Children.Clear();
        WorkDrawingSidebarPanel.Children.Clear();
        MachineSidebarPanel.Children.Clear();
        MachineSidebarHost.Visibility = Visibility.Collapsed;
        KlicDocumentsPanel.Children.Clear();
        AiAnalysisPanel.Children.Clear();
        KlicInfoPanel.Children.Clear();
        BgtInfoPanel.Children.Clear();

        SidebarImportsTab.Visibility = Visibility.Collapsed;
        SidebarBoreTraceTab.Visibility = Visibility.Collapsed;
        SidebarProfileTab.Visibility = Visibility.Collapsed;
        SidebarAnalysisTab.Visibility = Visibility.Collapsed;
        SidebarMapLayersTab.Visibility = Visibility.Collapsed;
        SidebarKlicDocsTab.Visibility = Visibility.Collapsed;
        SidebarAiTab.Visibility = Visibility.Collapsed;
        SidebarKlicInfoTab.Visibility = Visibility.Collapsed;
        SidebarBgtInfoTab.Visibility = Visibility.Collapsed;

        SetSidebarTab("reportInfo");
    }

    private void RenderStepTwo()
    {
        if (_selectedProject is null) return;

        _projectFiles = _projects.GetProjectFiles(_selectedProject.Id);
        var signature = BuildStepTwoRenderSignature();
        if (string.Equals(_lastStepTwoRenderSignature, signature, StringComparison.Ordinal))
        {
            return;
        }

        _lastStepTwoRenderSignature = signature;
        ImportSectionOneTitle.Text = "Ontwerp importeren";
        ImportSectionOneDescription.Text = "Importeer ontwerpbestanden per discipline. Bestanden blijven lokaal gekoppeld aan dit project.";
        ImportSectionTwoTitle.Text = "KLIC melding (ZIP) importeren";
        ImportSectionTwoDescription.Text = "Importeer de KLIC levering als ZIP of losse GML. De lagen worden gebruikt in de GIS-kaart en profielen.";
        ImportSectionThreeTitle.Text = "Custom lagen importeren";
        ImportSectionThreeDescription.Text = "Klik op de naam om te hernoemen. Ondersteunt DXF, GML, KML, GeoJSON en ZIP.";

        StandardLayersPanel.Children.Clear();
        KlicLayerPanel.Children.Clear();
        CustomLayersPanel.Children.Clear();
        BagImportLayerPanel.Children.Clear();
        BgtImportLayerPanel.Children.Clear();

        var latestByType = _projectFiles
            .GroupBy(file => file.FileType)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(file => file.CreatedAt).First());

        foreach (var layer in StepTwoLayers.Where(layer => !layer.IsCustom && layer.Key != "KLIC"))
        {
            latestByType.TryGetValue(layer.Key, out var file);
            StandardLayersPanel.Children.Add(CreateLayerUploadRow(layer, file));
        }

        foreach (var layer in StepTwoLayers.Where(layer => layer.Key == "KLIC"))
        {
            latestByType.TryGetValue(layer.Key, out var file);
            KlicLayerPanel.Children.Add(CreateLayerUploadRow(layer, file));
        }

        foreach (var layer in StepTwoLayers.Where(layer => layer.IsCustom)
            .Concat(StepThreeImportLayers.Where(layer => layer.IsCustom)))
        {
            latestByType.TryGetValue(layer.Key, out var file);
            CustomLayersPanel.Children.Add(CreateLayerUploadRow(layer, file));
        }

        foreach (var layer in StepThreeImportLayers.Where(layer => layer.Key == "BAG"))
        {
            latestByType.TryGetValue(layer.Key, out var file);
            BagImportLayerPanel.Children.Add(CreateLayerUploadRow(layer, file));
        }

        foreach (var layer in StepThreeImportLayers.Where(layer => layer.Key == "BGT"))
        {
            latestByType.TryGetValue(layer.Key, out var file);
            BgtImportLayerPanel.Children.Add(CreateLayerUploadRow(layer, file));
        }
    }

    private void RenderStepZero()
    {
        var settings = ReadReportStartSettings();
        ReportCoverTitleInput.Text = FirstNonEmpty(settings.CoverTitle, DefaultCoverTitle);
        ReportCoverSubtitleInput.Text = settings.CoverSubtitle;
        ReportCoverRevisionInput.Text = settings.CoverRevision;
        ReportCoverNoteInput.Text = settings.CoverNote;
        ReportForewordTextInput.Text = settings.ForewordText;
        ReportForewordScopeInput.Text = settings.ForewordScope;
        ReportContentsTitleInput.Text = settings.ContentsTitle;
        ReportContentsIntroInput.Text = settings.ContentsIntro;
        ReportContentsIncludeAppendicesInput.IsChecked = settings.IncludeAppendices;

        var substep = _selectedSubstep?.Number ?? "0.1";
        StepZeroCoverFields.Visibility = string.Equals(substep, "0.1", StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        StepZeroForewordFields.Visibility = string.Equals(substep, "0.2", StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        StepZeroContentsFields.Visibility = string.Equals(substep, "0.3", StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        StepZeroPrimaryHeader.Text = substep switch
        {
            "0.2" => "Voorwoord",
            "0.3" => "Inhoudsopgave",
            _ => "Voorblad"
        };
        StepZeroEditorTitle.Text = $"{DisplaySubstepNumber(substep)} {StepZeroPrimaryHeader.Text}";
        StepZeroEditorDescription.Text = substep switch
        {
            "0.2" => "Bewerk hier het voorwoord en de uitgangspunten voor pagina 2 van de eindrapportage.",
            "0.3" => "Bewerk hier de toelichting en instellingen voor de inhoudsopgave op pagina 3.",
            _ => "Bewerk hier optionele velden voor het voorblad. De vaste projectgegevens blijven uit het project komen."
        };
        RenderStepZeroInfoPanel(substep, settings);
    }

    private void RenderStepZeroInfoPanel(string substepNumber, ReportStartSettings settings)
    {
        StepZeroInfoPanel.Children.Clear();
        var context = BuildReportStartContext();
        StepZeroInfoPanel.Children.Add(CreateReportKeyValues(
            ("Project", _selectedProject?.Name ?? "-"),
            ("Locatie", context.ReportLocation),
            ("Documenten/bijlagen", context.DocumentCount.ToString(CultureInfo.InvariantCulture)),
            ("Kaartlagen", context.LayerCount.ToString(CultureInfo.InvariantCulture)),
            ("Gekruiste percelen", context.ParcelCount.ToString(CultureInfo.InvariantCulture)),
            ("Boorlengte", $"{context.TraceLength:N1} m")));

        StepZeroInfoPanel.Children.Add(CreateReportSubheading("Wat wordt opgeslagen?"));
        var text = substepNumber switch
        {
            "0.2" => "Voorwoordtekst en uitgangspunten/scope. Deze tekst wordt gebruikt op pagina 2 en in de rapportpreview van 00.2.",
            "0.3" => "Titel, toelichting en of bijlagen/documenten in de inhoudsopgave worden getoond. De hoofdstukken zelf worden automatisch uit de stappen opgebouwd.",
            _ => "Optionele rapporttitel, subtitel, revisiestatus en voorbladopmerking. Projectnaam, locatie, opdrachtgever, status en boorlengte blijven gekoppeld aan de projectdata."
        };
        StepZeroInfoPanel.Children.Add(CreateReportNote(text));

        StepZeroInfoPanel.Children.Add(CreateReportSubheading("Huidige invulling"));
        StepZeroInfoPanel.Children.Add(CreateReportKeyValues(
            ("Voorblad titel", FirstNonEmpty(settings.CoverTitle, DefaultCoverTitle)),
            ("Voorwoord", string.IsNullOrWhiteSpace(settings.ForewordText) ? "Automatische standaardtekst" : "Eigen tekst opgeslagen"),
            ("Inhoudsopgave titel", FirstNonEmpty(settings.ContentsTitle, "Inhoudsopgave")),
            ("Bijlagen tonen", settings.IncludeAppendices ? "Ja" : "Nee")));
    }

    private static void ResetStepOnePanelPlacement(FrameworkElement panel, int row, int column, int rowSpan, int columnSpan, Thickness margin)
    {
        Grid.SetRow(panel, row);
        Grid.SetColumn(panel, column);
        Grid.SetRowSpan(panel, rowSpan);
        Grid.SetColumnSpan(panel, columnSpan);
        panel.Margin = margin;
    }

    private void SaveStepOneLayoutState()
    {
        if (_selectedProject is null) return;
        var left = StepOneLeftColumn.ActualWidth > 0 ? StepOneLeftColumn.ActualWidth : 1;
        var right = StepOneRightColumn.ActualWidth > 0 ? StepOneRightColumn.ActualWidth : 1;
        var payload = JsonSerializer.Serialize(new { savedAt = DateTimeOffset.Now, left, right }, JsonOptions);
        SaveSelectedProjectStepData(1, "step1_layout", payload);
    }
}
