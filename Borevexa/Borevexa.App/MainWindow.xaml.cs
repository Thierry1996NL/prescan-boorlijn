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

public partial class MainWindow : Window
{
    private const double FillFactor = 0.40;
    private const double BoringFactor = 1.50;
    private const int MaxImklPopupProperties = 32;
    private const int EnvironmentStepNumber = WorkflowCatalog.EnvironmentStepNumber;
    private const int MachineStepNumber = WorkflowCatalog.MachineStepNumber;
    private const int ProfileStepNumber = WorkflowCatalog.ProfileStepNumber;
    private const int ReportStepNumber = WorkflowCatalog.ReportStepNumber;
    private const int ThreeDStepNumber = WorkflowCatalog.ThreeDStepNumber;
    private const int WorkDrawingStepNumber = WorkflowCatalog.WorkDrawingStepNumber;
    private const int LegacyProfileStepNumber = WorkflowCatalog.LegacyProfileStepNumber;
    private const int LegacyMachineStepNumber = WorkflowCatalog.LegacyMachineStepNumber;
    private static readonly HttpClient ReportTileHttpClient = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly HttpClient BroCptHttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly HttpClient DinoModelHttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly HttpClient AhnHeightHttpClient = new() { Timeout = TimeSpan.FromSeconds(4) };
    private static readonly Dictionary<string, IReadOnlyList<BroWmsTraceFinding>> BroWmsTraceFindingCache = new(StringComparer.OrdinalIgnoreCase);
    private const string BroCptCharacteristicsEndpoint = "https://publiek.broservices.nl/sr/cpt/v1/characteristics/searches?requestReference=BorevexaPrescan";
    private const string DinoVirtualColumnsEndpoint = "https://www.dinoloket.nl/javascriptmodelviewer-web/rest/models/columns/virtual";
    private const string DinoModelPointLayerEndpoint = "https://www.dinoloket.nl/standalone/rest/services/ondergrond_bro/lks_mdl_rd_v1/MapServer";
    private const double BroCptSearchBufferMeters = 3000d;
    private const double DinoModelPointSearchBufferMeters = 15000d;
    private const int MaxDinoModelPointResults = 2000;
    private const string BroDgmModelType = "DGM";
    private const string BroRegisModelType = "RGS";
    private const string BroGeomorphologyModelType = "GMF";
    private const string BroSoilMapModelType = "BDM";
    private const string BroGroundwaterGhgModelType = "GWD_GHG";
    private const string BroGroundwaterGlgModelType = "GWD_GLG";
    private const string BroGroundwaterGvgModelType = "GWD_GVG";
    private const string BroGroundwaterGtModelType = "GWD_GT";
    private const string BroGroundwaterDocumentationModelType = "GWD_DOC";
    private const string BroGroundwaterDepthModelType = BroGroundwaterGhgModelType;
    private const int MaxBroDgmReportSoundings = 2;
    private static readonly string[] BroUndergroundModelTypes =
    [
        BroDgmModelType,
        BroRegisModelType,
        BroGeomorphologyModelType,
        BroSoilMapModelType,
        BroGroundwaterGhgModelType,
        BroGroundwaterGlgModelType,
        BroGroundwaterGvgModelType,
        BroGroundwaterGtModelType,
        BroGroundwaterDocumentationModelType
    ];
    private const string ReportSectionDataKey = "report_section";
    private const string ReportSnapshotDataKey = "report_snapshot";
    private const string StepReportIntroductionDataKey = "step_report_introductions";
    private const string KnowledgeLibraryDataKey = "knowledge_library_documents";
    private const string BroImportedProfilesDataKeyPrefix = "bro_imported_profiles_";
    private const string ReportStartSettingsDataKey = "report_start_settings";
    private const string DefaultCoverTitle = "Prescan rapportage Haalbaarheidsonderzoek";
    private const string StepReportDataKey = "step_report_data";
    private const int ReportSchemaVersion = 2;
    private const string StepThreeCoverOsmReportMapVariant = "cover-osm";
    private const string StepThreeReportMapBagVariant = "pdok-bag";
    private const string StepThreeReportMapPhotoVariant = "pdok-foto";
    private const string StepThreeKlicReportMapBagVariant = "klic-pdok-bag";
    private const string StepThreeKlicReportMapPhotoVariant = "klic-pdok-foto";
    private const double KlicEvZoneSearchBufferMeters = 5d;
    private const double ReportPreviewExportScale = 3d;
    private const double BroReportMapAspectRatio = 724d / 245d;
    private const double BroReportMapMinAppHeight = 170d;
    private const double BroReportMapMaxAppHeight = 320d;
    private const double StandardGisMapCompactHeight = 320d;
    private const double StandardGisMapRegularHeight = 400d;
    private const int MaxKlicContactPdfDocs = 4;
    private const int MaxKlicContactPdfBytes = 4 * 1024 * 1024;
    private const int MaxKlicContactPdfStreamBytes = 768 * 1024;
    private const int MaxKlicContactPdfInflatedBytes = 768 * 1024;
    private const int MaxKlicContactPdfStreams = 80;
    private const int MaxKnowledgeDocumentTextChars = 120_000;
    private const int MaxKnowledgeDocumentsForPrompt = 5;
    private const int MaxBroImportedProfilesPerModel = 2;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly IReadOnlyList<SubstepTextSectionDefinition> SubstepTextSectionDefinitions =
    [
        new("introduction", "Inleiding"),
        new("assumptions", "Uitgangspunten en bronnen"),
        new("findings", "Bevindingen langs de boorlijn"),
        new("risks", "Risico's en aandachtspunten"),
        new("advice", "Advies / beheersmaatregelen"),
        new("conclusion", "Conclusie haalbaarheid"),
        new("openPoints", "Open punten / vervolgactie")
    ];

    private readonly Dictionary<string, TextBox> _substepTextSectionInputs = new(StringComparer.OrdinalIgnoreCase);

    private sealed record SubstepTextSectionDefinition(string Key, string Title);

    private static readonly GisFeatureDetailStore MapFeatureDetails = new();
    private static readonly GisCoordinateService GisCoordinates = new();
    private static readonly GisFeatureParserService GisFeatureParser = new(GisCoordinates);
    private static readonly GisDxfParserService GisDxfParser = new(GisCoordinates);
    private static readonly GisBgtKadasterParserService GisBgtKadasterParser = new(
        GisCoordinates,
        MapFeatureDetails,
        BgtSurfaceLabel,
        BgtSurfaceColor,
        NormalizeBgtSurfaceKey);
    private static readonly GisImklParserService GisImklParser = new(
        GisCoordinates,
        MapFeatureDetails,
        KlicThemeColor);
    private readonly ProjectRepository _projects = new();
    private readonly MapStateService _mapState;
    private readonly ReportPreviewService _reportPreview;
    private readonly ReportRenderService _reportRender;
    private readonly ReportQualityService _reportQuality;
    private readonly ReportExportService _reportExport;
    private readonly ReportMapCaptureCoordinator _reportMapCapture;
    private readonly GisMapWorkspaceRegistry _gisMapWorkspaces;
    private readonly GisMapController _gisMap = new();
    private readonly GisSidebarBuilder _gisSidebar = new();
    private static readonly GisDocumentIndexService GisDocuments = new();
    private readonly GeoDataService _geo = new();
    private readonly CadExportService _cad = new();
    private readonly IReadOnlyDictionary<int, StepWorkspace> _workspaces;
    private readonly List<BoringItem> _boringItems = [];
    private readonly GisProjectLayerBuilder _mapLayerBuilder = new();
    private readonly Dictionary<string, ProjectDocumentEntry> _mapDocumentEntries = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<ProjectFileRecord> _projectFiles = [];
    private Guid? _lastEnvironmentAnalysisProjectId;
    private ParcelOwnerAnalysis? _lastEnvironmentAnalysis;
    private bool _mapLibreLoaded;
    private bool _mapInitializationStarted;
    private int _mapLoadingDepth;
    private bool _suppressProjectLayerSend;
    private bool _forceStepThreeKlicMapForReportCapture;
    private bool _showingStepThreeDocs;
    private bool _mapLocked;
    private JsonElement? _lastMapCamera;
    private string _activeSidebarMainMode = "steps";
    private string _activeSidebarTab = "reportInfo";
    private string _selectedEnvironmentSegmentKey = "";
    private readonly GisLayerStateService _gisLayerState = new();
    private string _selectedMapBaseLayer
    {
        get => _gisLayerState.BaseLayer;
        set => _gisLayerState.BaseLayer = value;
    }
    private Dictionary<string, bool> _mapOverlayStates => _gisLayerState.Overlays;
    private Dictionary<string, bool> _projectLayerStates => _gisLayerState.ProjectLayers;
    private Dictionary<string, bool> _bgtSurfaceStates => _gisLayerState.BgtSurfaces;
    private Dictionary<string, bool> _klicThemeStates => _gisLayerState.KlicThemes;
    private string? _currentBoreTraceJson;
    private IReadOnlyList<TracePointRow> _currentBoreTracePoints = [];
    private bool _stepThreeKlicDefaultsApplied;
    private List<ProfilePointRow> _profilePoints = [];
    private readonly Dictionary<string, double> _ahnSurfaceNapCache = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _ahnSurfaceSamplingSuspendedUntilUtc = DateTime.MinValue;
    private IReadOnlyList<BroSoundingPoint> _broSoundings = [];
    private string? _selectedBroSoundingId;
    private string _broSoundingLoadStatus = "Nog niet geladen.";
    private string _selectedBroModelType = BroDgmModelType;
    private readonly Dictionary<string, IReadOnlyList<BroSoundingPoint>> _broModelSoundings = new(StringComparer.OrdinalIgnoreCase)
    {
        [BroDgmModelType] = [],
        [BroRegisModelType] = [],
        [BroGeomorphologyModelType] = [],
        [BroSoilMapModelType] = [],
        [BroGroundwaterGhgModelType] = [],
        [BroGroundwaterGlgModelType] = [],
        [BroGroundwaterGvgModelType] = [],
        [BroGroundwaterGtModelType] = [],
        [BroGroundwaterDocumentationModelType] = []
    };
    private readonly Dictionary<string, string?> _selectedBroModelSoundingIds = new(StringComparer.OrdinalIgnoreCase)
    {
        [BroDgmModelType] = null,
        [BroRegisModelType] = null,
        [BroGeomorphologyModelType] = null,
        [BroSoilMapModelType] = null,
        [BroGroundwaterGhgModelType] = null,
        [BroGroundwaterGlgModelType] = null,
        [BroGroundwaterGvgModelType] = null,
        [BroGroundwaterGtModelType] = null,
        [BroGroundwaterDocumentationModelType] = null
    };
    private readonly Dictionary<string, List<string>> _selectedBroModelSoundingIdLists = new(StringComparer.OrdinalIgnoreCase)
    {
        [BroDgmModelType] = [],
        [BroRegisModelType] = [],
        [BroGeomorphologyModelType] = [],
        [BroSoilMapModelType] = [],
        [BroGroundwaterGhgModelType] = [],
        [BroGroundwaterGlgModelType] = [],
        [BroGroundwaterGvgModelType] = [],
        [BroGroundwaterGtModelType] = [],
        [BroGroundwaterDocumentationModelType] = []
    };
    private readonly Dictionary<string, string> _broModelLoadStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        [BroDgmModelType] = "Nog niet geladen.",
        [BroRegisModelType] = "Nog niet geladen.",
        [BroGeomorphologyModelType] = "BRO Geomorfologie 2025-01 kaartlaag beschikbaar via PDOK WMS.",
        [BroSoilMapModelType] = "BRO Bodemkaart 2025-01 kaartlaag beschikbaar via PDOK WMS.",
        [BroGroundwaterGhgModelType] = "BRO Grondwaterspiegeldiepte GHG 2025-01 kaartlaag beschikbaar via PDOK WMS.",
        [BroGroundwaterGlgModelType] = "BRO Grondwaterspiegeldiepte GLG 2025-01 kaartlaag beschikbaar via PDOK WMS.",
        [BroGroundwaterGvgModelType] = "BRO Grondwaterspiegeldiepte GVG 2025-01 kaartlaag beschikbaar via PDOK WMS.",
        [BroGroundwaterGtModelType] = "BRO Grondwatertrappen Gt 2025-01 kaartlaag beschikbaar via PDOK WMS.",
        [BroGroundwaterDocumentationModelType] = "BRO Grondwaterspiegeldiepte modeldocumentatie 2025-01 kaartlaag beschikbaar via PDOK WMS."
    };
    private readonly HashSet<string> _broAutoLoadKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _broLoadingModelTypes = new(StringComparer.OrdinalIgnoreCase);
    private double _profileViewZoom = 1.0;
    private bool _profileAlignedToMap;
    private bool _profileSmoothBore;
    private bool _profileLayoutLocked;
    private bool _profileExpanded;
    private bool _profileHasUnsavedChanges;
    private bool _profileGeometryDirty;
    private bool _surfaceAnalysisMetricsRefreshPending;
    private Guid? _profileVisualSettingsProjectId;
    private bool _profileCanvasInteractive;
    private double _profileCanvasPlotLeft;
    private double _profileCanvasPlotTop;
    private double _profileCanvasPlotWidth;
    private double _profileCanvasPlotHeight;
    private double _profileCanvasMinY;
    private double _profileCanvasMaxY;
    private double _profileCanvasTotal;
    private bool _traceSmoothBore;
    private Guid? _traceVisualSettingsProjectId;
    private int _workDrawingScale = 200;
    private ProfileScreenMetrics? _profileScreenMetrics;
    private IReadOnlyList<BgtSurfaceSample> _mapBgtSurfaceSamples = [];
    private StackPanel? _tracePointsTablePanel;
    private StackPanel? _machinePlacementsTablePanel;
    private TextBox? _aiQuestionInput;
    private TextBlock? _sidebarAiAnalysisText;
    private bool _sidebarCollapsed;
    private double _expandedSidebarWidth = 340;
    private string? _currentMachinePlacementsJson;
    private double _machineLengthMeters = 5.42;
    private double _machineWidthMeters = 2.9;
    private double _borePitLengthMeters = 3;
    private double _borePitWidthMeters = 1;
    private MachineSymbol? _machineTopSymbol;
    private MachineSymbol? _machineSideSymbol;
    private TextBox? _machineLengthInput;
    private TextBox? _machineWidthInput;
    private TextBox? _borePitLengthInput;
    private TextBox? _borePitWidthInput;
    private string? _selectedMachineId;
    private string _selectedDrillingTechnique = "walkover";
    private Guid? _loadedBoringConfigProjectId;
    private PrescanProject? _selectedProject;
    private PrescanStep? _selectedStep;
    private PrescanSubstep? _selectedSubstep;
    private IReadOnlyList<StepNavigationItem> _stepNavigationItems = [];
    private int? _selectedReportPreviewStepNumber;
    private string? _activeWorkflowPartKey;
    private bool _syncingProjectSelection;
    private bool _syncingStepSelection;
    private bool _inlineReportPreviewCollapsed = true;
    private double _inlineReportPreviewWidth = 410;
    private double _inlineReportPreviewZoom = 0.55;
    private ReportPreviewWindowScope _reportPreviewWindowScope = ReportPreviewWindowScope.Substep;
    private Window? _reportPreviewWindow;
    private StackPanel? _reportPreviewWindowContent;
    private TextBlock? _reportPreviewWindowTitle;
    private TextBlock? _reportPreviewWindowSubtitle;
    private Button? _reportPreviewWindowZoomButton;
    private string? _lastStepThreeRenderSignature;
    private string? _lastStepTwoRenderSignature;
    private string? _lastWorkDrawingSidebarSignature;
    private string? _lastGisMapRenderSignature;
    private string? _lastInlineReportPreviewSignature;
    private string? _lastWorkflowReportStatusSignature;
    private string? _lastFinalReportPanelSignature;
    private string? _lastAllStepReportRefreshSignature;
    private int _reportUiDataVersion;
    private bool _uiInitialized;
    private readonly DispatcherTimer _mapStatePersistenceTimer = new() { Interval = TimeSpan.FromMilliseconds(850) };
    private readonly DispatcherTimer _inlineReportPreviewRefreshTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private int? _pendingMapStateStepNumber;
    private int _backgroundTaskDepth;
    private bool _applyingStepThreeMapFrameBounds;

    static MainWindow()
    {
        ReportTileHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Borevexa/1.0 (report map rendering)");
        BroCptHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Borevexa/1.0 (BRO CPT lookup)");
        DinoModelHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Borevexa/1.0 (DINO model lookup)");
        AhnHeightHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Borevexa/1.0 (AHN profile sampling)");
    }

    public MainWindow()
    {
        StartupCacheCleanupService.CleanTemporaryCaches();
        InitializeComponent();
        MoveStepFilterSidebarToUnifiedHost();
        _uiInitialized = true;
        _mapState = new MapStateService(_projects, JsonOptions);
        _reportPreview = new ReportPreviewService(_projects, JsonOptions);
        _reportRender = new ReportRenderService();
        _reportQuality = new ReportQualityService(_projects);
        _reportExport = new ReportExportService(_projects, JsonOptions);
        _gisMapWorkspaces = new GisMapWorkspaceRegistry(ThreeDStepNumber, ReportStepNumber, ProfileStepNumber);
        _reportMapCapture = new ReportMapCaptureCoordinator(
            _reportPreview,
            () => _selectedProject?.Id,
            BuildLiveMapCaptureMetadata,
            OnLiveMapCaptureCompleted,
            AppendMapDiagnostic);
        _mapStatePersistenceTimer.Tick += MapStatePersistenceTimer_OnTick;
        _inlineReportPreviewRefreshTimer.Tick += InlineReportPreviewRefreshTimer_OnTick;
        _workspaces = CreateWorkspaces();
        SizeChanged += (_, _) => ApplyResponsiveChromeLayout();
        SizeChanged += (_, _) => ApplyStepThreeLayoutBounds();
        SizeChanged += (_, _) => UpdateProcessStepsScrollHeight();
        SidebarBorder.SizeChanged += (_, _) => UpdateProcessStepsScrollHeight();
        RefreshProjects();
        ProjectsList.SelectedIndex = 0;
    }

    private void ProjectsList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingProjectSelection) return;
        if (ProjectsList.SelectedItem is PrescanProject project)
        {
            ActivateProject(project, syncProjectsPageSelection: true);
        }
    }

    private void ProjectsPageList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingProjectSelection) return;
        if (ProjectsPageList.SelectedItem is not PrescanProject project)
        {
            UpdateProjectsPageSelectionUi();
            return;
        }

        ActivateProject(project, syncProjectsPageSelection: false);
        UpdateProjectsPageSelectionUi();
    }

    private void ActivateProject(PrescanProject project, bool syncProjectsPageSelection)
    {
        _selectedProject = project;
        EnsureProjectStepsMatchWorkspaces(_selectedProject);
        _currentBoreTraceJson = null;
        _currentBoreTracePoints = [];
        ClearEnvironmentAnalysisCache();
        _currentMachinePlacementsJson = null;
        _loadedBoringConfigProjectId = null;
        _boringItems.Clear();
        _selectedMachineId = null;
        _selectedDrillingTechnique = "walkover";
        _broAutoLoadKeys.Clear();
        _broLoadingModelTypes.Clear();
        RenderProject();

        if (syncProjectsPageSelection && ProjectsPageList.SelectedItem != project)
        {
            _syncingProjectSelection = true;
            ProjectsPageList.SelectedItem = project;
            _syncingProjectSelection = false;
        }

        UpdateProjectsPageSelectionUi();
    }

    private void StepsList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingStepSelection) return;

        var item = (sender as ListBox)?.SelectedItem as StepNavigationItem;
        if (item is null) return;
        if (!item.IsSelectable)
        {
            var fallback = FindFirstSelectableStepItem(item.Step.Number);
            if (fallback is not null)
            {
                _syncingStepSelection = true;
                SideStepsList.SelectedItem = fallback;
                SidebarCompactStepsList.SelectedItem = fallback;
                _syncingStepSelection = false;
                ApplyStepNavigationItem(fallback);
            }
            return;
        }

        ApplyStepNavigationItem(item);
    }

    private const string LastViewedStepDataKey = "last_viewed_step";

    private void ApplyStepNavigationItem(StepNavigationItem item)
    {
        _selectedStep = item.Step;
        _selectedSubstep = item.Substep;
        _selectedReportPreviewStepNumber = item.IsReportPreview ? item.Step.Number : null;
        _syncingStepSelection = true;
        SideStepsList.SelectedItem = item;
        SidebarCompactStepsList.SelectedItem = item;
        _syncingStepSelection = false;
        SaveLastViewedStep(item);
        ShowWorkflowPage();
        RenderWorkspace();
    }

    private void SaveLastViewedStep(StepNavigationItem item)
    {
        if (_selectedProject is null || item.IsReportPreview) return;

        var payload = JsonSerializer.Serialize(new
        {
            stepNumber = item.Step.Number,
            substepNumber = item.Substep?.Number
        }, JsonOptions);
        _projects.SaveStepData(_selectedProject.Id, 0, LastViewedStepDataKey, payload);
    }

    private (int StepNumber, string? SubstepNumber)? ReadLastViewedStep(Guid projectId)
    {
        var json = _projects.GetStepData(projectId, 0, LastViewedStepDataKey);
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("stepNumber", out var stepNumberElement) || stepNumberElement.ValueKind != JsonValueKind.Number)
            {
                return null;
            }

            var substepNumber = root.TryGetProperty("substepNumber", out var substepElement) && substepElement.ValueKind == JsonValueKind.String
                ? substepElement.GetString()
                : null;
            return (stepNumberElement.GetInt32(), substepNumber);
        }
        catch
        {
            return null;
        }
    }

    private void EnsureProjectStepsMatchWorkspaces(PrescanProject project)
    {
        if (_workspaces.Count == 0) return;

        var existing = project.Steps.ToDictionary(step => step.Number);
        var normalized = _workspaces.Values
            .OrderBy(workspace => workspace.StepNumber)
            .Select(workspace => existing.TryGetValue(workspace.StepNumber, out var step)
                ? new PrescanStep
                {
                    Number = step.Number,
                    Title = workspace.Title,
                    Description = workspace.StepNumber == WorkDrawingStepNumber ? "Werktekening" : workspace.Subtitle,
                    State = step.State,
                    Substeps = StepReportCatalog.GetSubsteps(workspace.StepNumber)
                }
                : new PrescanStep
                {
                    Number = workspace.StepNumber,
                    Title = workspace.Title,
                    Description = workspace.StepNumber == WorkDrawingStepNumber ? "Werktekening" : workspace.Subtitle,
                    State = StepState.Todo,
                    Substeps = StepReportCatalog.GetSubsteps(workspace.StepNumber)
                })
            .ToArray();

        project.Steps = normalized;
    }

    private void RenderProject()
    {
        if (_selectedProject is null) return;
        EnsureProjectStepsMatchWorkspaces(_selectedProject);

        ProjectsList.ItemsSource = new[] { _selectedProject };
        _stepNavigationItems = BuildStepNavigationItems(_selectedProject.Steps);
        SideStepsList.ItemsSource = _stepNavigationItems;
        SidebarCompactStepsList.ItemsSource = _stepNavigationItems;
        UpdateTopProjectButton();

        var lastViewed = ReadLastViewedStep(_selectedProject.Id);
        var lastViewedItem = lastViewed is { } lastViewedStep
            ? FindStepNavigationItem(lastViewedStep.StepNumber, isReportPreview: false, lastViewedStep.SubstepNumber)
              ?? FindFirstSelectableStepItem(lastViewedStep.StepNumber)
            : null;
        var selectedItem = lastViewedItem ?? FindFirstSelectableStepItem(_selectedProject.Steps.First().Number);
        _selectedStep = selectedItem?.Step ?? _selectedProject.Steps.First();
        _selectedSubstep = selectedItem?.Substep;
        _selectedReportPreviewStepNumber = null;

        _syncingStepSelection = true;
        SideStepsList.SelectedItem = selectedItem;
        SidebarCompactStepsList.SelectedItem = selectedItem;
        _syncingStepSelection = false;

        RenderWorkspace();
    }

    private void RefreshProjects()
    {
        var projects = _projects.GetProjects();
        ProjectsList.ItemsSource = _selectedProject is null ? projects.Take(1).ToArray() : new[] { _selectedProject };
        ProjectsPageList.ItemsSource = projects;
        if (_selectedProject is not null)
        {
            var pageSelection = projects.FirstOrDefault(project => project.Id == _selectedProject.Id);
            if (pageSelection is not null && ProjectsPageList.SelectedItem != pageSelection)
            {
                _syncingProjectSelection = true;
                ProjectsPageList.SelectedItem = pageSelection;
                _syncingProjectSelection = false;
            }
        }
        ProjectCountText.Text = projects.Count == 1 ? "1 project" : $"{projects.Count} projecten";
        UpdateTopProjectButton();
        UpdateProjectsPageSelectionUi();
    }

    private void UpdateTopProjectButton()
    {
        TopActiveProjectButton.Content = _selectedProject is null
            ? "Geen project"
            : $"{_selectedProject.Name} - {GetDeclaredReportLocation()}";
    }

    private void UpdateProjectsPageSelectionUi()
    {
        var project = ProjectsPageList.SelectedItem as PrescanProject ?? _selectedProject;
        var hasSelection = project is not null;

        OpenSelectedProjectButton.IsEnabled = hasSelection;
        DeleteSelectedProjectButton.IsEnabled = hasSelection;

        if (project is null)
        {
            ProjectsPageSelectedProjectNameText.Text = "Geen project geselecteerd";
            ProjectsPageSelectedProjectDetailsText.Text = "Selecteer een project om het te openen of te verwijderen.";
            return;
        }

        ProjectsPageSelectedProjectNameText.Text = project.Name;
        ProjectsPageSelectedProjectDetailsText.Text =
            $"{project.Client} · {project.Location} · {project.BoreLengthMeters:0.#} m · Ø{project.DiameterMillimeters} mm · {project.Material}";
    }

    private void ShowProjectsPage_OnClick(object sender, RoutedEventArgs e) => ShowProjectsPage();

    private void OpenSelectedProject_OnClick(object sender, RoutedEventArgs e)
    {
        if (ProjectsPageList.SelectedItem is not PrescanProject project)
        {
            OutputText.Text = "Selecteer eerst een project.";
            return;
        }

        ActivateProject(project, syncProjectsPageSelection: false);
        ShowWorkflowPage();
    }

    private void DeleteSelectedProject_OnClick(object sender, RoutedEventArgs e)
    {
        if (ProjectsPageList.SelectedItem is not PrescanProject project)
        {
            OutputText.Text = "Selecteer eerst een project om te verwijderen.";
            return;
        }

        var result = MessageBox.Show(
            this,
            $"Weet je zeker dat je project '{project.Name}' wilt verwijderen?\n\n" +
            "Alle lokaal opgeslagen projectdata, stapdata en gekoppelde lokale projectbestanden worden verwijderd. Deze actie kan niet ongedaan worden gemaakt.",
            "Project verwijderen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            if (!_projects.DeleteProject(project.Id))
            {
                OutputText.Text = $"Project '{project.Name}' kon niet worden gevonden of was al verwijderd.";
                RefreshProjects();
                ShowProjectsPage();
                return;
            }

            DeleteProjectScopedLocalArtifacts(project.Id);
        }
        catch (Exception exception)
        {
            OutputText.Text = $"Project verwijderen mislukt\n\n{exception.Message}";
            return;
        }

        var remainingProjects = _projects.GetProjects();
        if (remainingProjects.Count > 0)
        {
            ActivateProject(remainingProjects[0], syncProjectsPageSelection: false);
        }
        else
        {
            ClearActiveProjectSelection();
        }

        RefreshProjects();
        ShowProjectsPage();
        OutputText.Text = $"Project '{project.Name}' is verwijderd.";
    }

    private void ShowWorkflowPage_OnClick(object sender, RoutedEventArgs e) => ShowWorkflowPage();


    private async void TopProjectSave_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedProject is null)
        {
            OutputText.Text = "Geen project actief. Open of maak eerst een project.";
            return;
        }

        try
        {
            SaveWholeProject();
        }
        catch (Exception exception)
        {
            OutputText.Text = $"Project opslaan mislukt\n\n{exception.Message}";
            return;
        }

        var oldContent = TopProjectSaveButton.Content;
        var oldBackground = TopProjectSaveButton.Background;
        var oldForeground = TopProjectSaveButton.Foreground;
        TopProjectSaveButton.Content = "Opgeslagen";
        TopProjectSaveButton.Background = Brush("#DCFCE7");
        TopProjectSaveButton.Foreground = Brush("#15803D");
        await System.Threading.Tasks.Task.Delay(1000);
        TopProjectSaveButton.Content = oldContent;
        TopProjectSaveButton.Background = oldBackground;
        TopProjectSaveButton.Foreground = oldForeground;
    }

    private void ShowProjectsPage()
    {
        WorkflowPanel.Visibility = Visibility.Collapsed;
        ProjectsPagePanel.Visibility = Visibility.Visible;
        StepRibbonHost.Visibility = Visibility.Collapsed;
        StepFilterSidebar.Visibility = Visibility.Collapsed;
        UnifiedStepContextHost.Visibility = Visibility.Collapsed;
        _activeSidebarMainMode = "steps";
        ApplySidebarState();
        StepFilterColumn.Width = new GridLength(0);
        ReportPreviewSplitter.Visibility = Visibility.Collapsed;
        ReportPreviewSplitterColumn.Width = new GridLength(0);
        InlineReportPreviewSidebar.Visibility = Visibility.Collapsed;
        ReportPreviewColumn.Width = new GridLength(0);
        PreviousStepButton.Visibility = Visibility.Collapsed;
        NextStepButton.Visibility = Visibility.Collapsed;
        SubstepReportPreviewButton.Visibility = Visibility.Collapsed;
        ChapterReportPreviewButton.Visibility = Visibility.Collapsed;
        HeaderMapSaveButton.Visibility = Visibility.Collapsed;
        ActionsSubtitle.Text = "Projecten";
        ActionButtonsPanel.Children.Clear();
        OutputText.Text = "Projectenblad geopend. Selecteer een project of maak een nieuw lokaal project aan.";
    }

    private void ShowWorkflowPage()
    {
        ProjectsPagePanel.Visibility = Visibility.Collapsed;
        WorkflowPanel.Visibility = Visibility.Visible;
        if (_selectedStep is not null)
        {
            ActionsSubtitle.Text = $"Stap {DisplayStepNumber(_selectedStep.Number)} acties";
        }
    }

    private void ClearActiveProjectSelection()
    {
        _selectedProject = null;
        _selectedStep = null;
        _selectedSubstep = null;
        _selectedReportPreviewStepNumber = null;
        _currentBoreTraceJson = null;
        _currentBoreTracePoints = [];
        _currentMachinePlacementsJson = null;
        _loadedBoringConfigProjectId = null;
        _boringItems.Clear();
        _selectedMachineId = null;
        _selectedDrillingTechnique = "walkover";
        _broAutoLoadKeys.Clear();
        _broLoadingModelTypes.Clear();
        ClearEnvironmentAnalysisCache();
        _stepNavigationItems = [];

        _syncingProjectSelection = true;
        ProjectsList.SelectedItem = null;
        ProjectsPageList.SelectedItem = null;
        _syncingProjectSelection = false;

        ProjectsList.ItemsSource = Array.Empty<PrescanProject>();
        SideStepsList.ItemsSource = Array.Empty<StepNavigationItem>();
        SidebarCompactStepsList.ItemsSource = Array.Empty<StepNavigationItem>();
        UpdateTopProjectButton();
        UpdateProjectsPageSelectionUi();
    }

    private static void DeleteProjectScopedLocalArtifacts(Guid projectId)
    {
        var root = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Borevexa");

        var projectKey = projectId.ToString("N");
        TryDeleteDirectoryInsideRoot(root, System.IO.Path.Combine(root, "KnowledgeLibrary", projectKey));
        TryDeleteDirectoryInsideRoot(root, System.IO.Path.Combine(root, "BroProfiles", projectKey));

        var reportMapDirectory = System.IO.Path.Combine(root, "ReportLiveMaps");
        TryDeleteFilesInsideRoot(root, reportMapDirectory, $"project-{projectId}-*");
        TryDeleteFilesInsideRoot(root, reportMapDirectory, $"project-{projectKey}-*");
    }

    private static void TryDeleteDirectoryInsideRoot(string root, string path)
    {
        try
        {
            if (!Directory.Exists(path) || !IsInsideRoot(root, path)) return;
            Directory.Delete(path, recursive: true);
        }
        catch (System.Exception swallowedException)
        {
            // Best effort cleanup; project deletion itself has already been handled.
            AppLog.Swallowed(swallowedException);
        }
    }

    private static void TryDeleteFilesInsideRoot(string root, string directory, string pattern)
    {
        try
        {
            if (!Directory.Exists(directory) || !IsInsideRoot(root, directory)) return;
            foreach (var file in Directory.GetFiles(directory, pattern))
            {
                if (IsInsideRoot(root, file))
                {
                    File.Delete(file);
                }
            }
        }
        catch (System.Exception swallowedException)
        {
            // Best effort cleanup; project deletion itself has already been handled.
            AppLog.Swallowed(swallowedException);
        }
    }

    private static bool IsInsideRoot(string root, string path)
    {
        var fullRoot = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        var fullPath = System.IO.Path.GetFullPath(path);
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private void NextStep_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedProject is null || _selectedStep is null) return;
        var selectableItems = _stepNavigationItems.Where(item => item.IsSelectable && !item.IsReportPreview).ToList();
        var currentIndex = selectableItems.FindIndex(IsCurrentStepNavigationItem);
        var next = currentIndex >= 0 && currentIndex + 1 < selectableItems.Count ? selectableItems[currentIndex + 1] : null;
        if (next is not null)
        {
            ApplyStepNavigationItem(next);
        }
    }

    private void PreviousStep_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedProject is null || _selectedStep is null) return;
        var selectableItems = _stepNavigationItems.Where(item => item.IsSelectable && !item.IsReportPreview).ToList();
        var currentIndex = selectableItems.FindIndex(IsCurrentStepNavigationItem);
        var previous = currentIndex > 0 ? selectableItems[currentIndex - 1] : null;
        if (previous is not null)
        {
            ApplyStepNavigationItem(previous);
        }
    }

    private bool IsCurrentStepNavigationItem(StepNavigationItem item) =>
        _selectedStep is not null &&
        item.Step.Number == _selectedStep.Number &&
        item.IsReportPreview == (_selectedReportPreviewStepNumber is not null) &&
        string.Equals(item.Substep?.Number, _selectedSubstep?.Number, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<StepNavigationItem> BuildStepNavigationItems(IEnumerable<PrescanStep> steps)
    {
        var items = new List<StepNavigationItem>();
        foreach (var step in steps.OrderBy(step => step.Number))
        {
            if (IsHiddenWorkflowStep(step.Number)) continue;
            items.Add(new StepNavigationItem(step, null, false));
            foreach (var substep in step.Substeps.Count == 0 ? StepReportCatalog.GetSubsteps(step.Number) : step.Substeps)
            {
                items.Add(new StepNavigationItem(step, substep, false));
            }
        }

        return items;
    }

    private StepNavigationItem? FindStepNavigationItem(int stepNumber, bool isReportPreview, string? substepNumber = null) =>
        _stepNavigationItems.FirstOrDefault(item =>
            item.Step.Number == (IsHiddenWorkflowStep(stepNumber) ? 3 : stepNumber) &&
            item.IsReportPreview == isReportPreview &&
            string.Equals(item.Substep?.Number, substepNumber, StringComparison.OrdinalIgnoreCase));

    private StepNavigationItem? FindFirstSelectableStepItem(int stepNumber) =>
        _stepNavigationItems.FirstOrDefault(item =>
            item.Step.Number == (IsHiddenWorkflowStep(stepNumber) ? 3 : stepNumber) &&
            item.IsSelectable);

    private void SelectStepNavigationItem(int stepNumber, bool isReportPreview)
    {
        var item = FindStepNavigationItem(stepNumber, isReportPreview);
        item ??= FindFirstSelectableStepItem(stepNumber);
        if (item is not null)
        {
            SideStepsList.SelectedItem = item;
            SidebarCompactStepsList.SelectedItem = item;
        }
    }

    private void MarkReportUiDataChanged()
    {
        _reportUiDataVersion++;
        _lastInlineReportPreviewSignature = null;
        _lastWorkflowReportStatusSignature = null;
        _lastFinalReportPanelSignature = null;
        _lastAllStepReportRefreshSignature = null;
        _lastStepTwoRenderSignature = null;
        _lastStepThreeRenderSignature = null;
        _lastWorkDrawingSidebarSignature = null;
        _lastWorkDrawingPreviewSignature = null;
        _lastGisMapRenderSignature = null;
    }

    private void SaveSelectedProjectStepData(int stepNumber, string key, string jsonValue)
    {
        if (_selectedProject is null) return;
        if (string.Equals(_projects.GetStepData(_selectedProject.Id, stepNumber, key), jsonValue, StringComparison.Ordinal))
        {
            return;
        }

        _projects.SaveStepData(_selectedProject.Id, stepNumber, key, jsonValue);
        MarkReportUiDataChanged();
    }






    private static string ExtractPdfText(string path)
    {
        var builder = new StringBuilder();
        using var document = PdfDocument.Open(path);
        foreach (var page in document.GetPages().Take(120))
        {
            if (builder.Length >= MaxKnowledgeDocumentTextChars) break;
            builder.AppendLine(page.Text);
            builder.AppendLine();
        }

        return NormalizeKnowledgeText(builder.ToString());
    }

    private static string ExtractDocxText(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var parts = archive.Entries
            .Where(entry => entry.FullName.Equals("word/document.xml", StringComparison.OrdinalIgnoreCase) ||
                            entry.FullName.StartsWith("word/header", StringComparison.OrdinalIgnoreCase) ||
                            entry.FullName.StartsWith("word/footer", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var builder = new StringBuilder();
        foreach (var part in parts)
        {
            using var stream = part.Open();
            var document = XDocument.Load(stream);
            foreach (var textNode in document.Descendants().Where(element => element.Name.LocalName == "t"))
            {
                builder.Append(textNode.Value);
                builder.Append(' ');
            }
            builder.AppendLine();
        }

        return NormalizeKnowledgeText(builder.ToString());
    }



    private static string ToSafeFileName(string value)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        }

        var result = Regex.Replace(builder.ToString(), @"\s+", "_").Trim('_');
        return string.IsNullOrWhiteSpace(result) ? "documentatie" : result;
    }








    // Render-cache per sessie: het PDF→PNG-renderen kost ~1,5s en de aanroepers
    // persisteren het resultaatpad niet altijd terug. Zonder cache werd hetzelfde
    // profiel bij elke sidebar/rapport-refresh opnieuw gerenderd (zichtbaar in de
    // performance.log als een eindeloze reeks "BRO profielbeeld renderen").
    // Mislukte renders worden ook onthouden (lege string), anders blijft een kapotte
    // PDF elke refresh 1,5s kosten.
    private static readonly Dictionary<string, string> BroProfileRenderCache = new(StringComparer.OrdinalIgnoreCase);



    private static DrawingBitmap CreateBitmapFromBgra(byte[] bytes, int width, int height)
    {
        var bitmap = new DrawingBitmap(width, height, DrawingPixelFormat.Format32bppArgb);
        var bounds = new DrawingRectangle(0, 0, width, height);
        var data = bitmap.LockBits(bounds, DrawingImageLockMode.WriteOnly, DrawingPixelFormat.Format32bppArgb);
        try
        {
            Marshal.Copy(bytes, 0, data.Scan0, Math.Min(bytes.Length, Math.Abs(data.Stride) * height));
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }




    private static DrawingRectangle FindNonWhiteBounds(DrawingBitmap bitmap)
    {
        var minX = bitmap.Width;
        var minY = bitmap.Height;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                if (color.A < 20) continue;
                if (color.R > 248 && color.G > 248 && color.B > 248) continue;

                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        return maxX < minX || maxY < minY
            ? DrawingRectangle.Empty
            : DrawingRectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }









    private static string FormatFileSize(long bytes)
    {
        if (bytes >= 1024 * 1024) return $"{bytes / 1024d / 1024d:N1} MB";
        return $"{Math.Max(1, bytes / 1024d):N0} KB";
    }

    private async void AddKnowledgeDocument_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedProject is null)
        {
            OutputText.Text = "Open eerst een project om documentatie toe te voegen.";
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Voeg norm, richtlijn of referentiedocument toe",
            Filter = "Documentatie (*.pdf;*.docx;*.txt;*.md)|*.pdf;*.docx;*.txt;*.md|Alle bestanden (*.*)|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true) return;

        BeginBackgroundTask("Documentatie indexeren...");
        try
        {
            var current = ReadKnowledgeDocuments().ToList();
            var imported = await Task.Run(() => dialog.FileNames.Select(ImportKnowledgeDocument).ToList());
            current.AddRange(imported);
            SaveKnowledgeDocuments(current);
            RenderKnowledgeLibraryPanel();
            OutputText.Text = $"{imported.Count} document(en) toegevoegd aan de documentatiebibliotheek.";
        }
        catch (Exception exception)
        {
            OutputText.Text = $"Documentatie toevoegen is niet gelukt\n\n{exception.Message}";
        }
        finally
        {
            EndBackgroundTask();
        }
    }

    private async void RefreshKnowledgeLibrary_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedProject is null)
        {
            OutputText.Text = "Open eerst een project om documentatie te vernieuwen.";
            return;
        }

        BeginBackgroundTask("Documentatie opnieuw indexeren...");
        try
        {
            var refreshed = await Task.Run(() => ReadKnowledgeDocuments()
                .Select(document =>
                {
                    var text = File.Exists(document.LocalPath) ? ExtractKnowledgeDocumentText(document.LocalPath) : "";
                    return document with
                    {
                        IndexedAt = DateTimeOffset.Now,
                        ExtractedText = TruncateKnowledgeText(text, MaxKnowledgeDocumentTextChars),
                        ImportStatus = string.IsNullOrWhiteSpace(text)
                            ? "Bestand gekoppeld; er kon geen tekst worden geindexeerd."
                            : $"Tekst geindexeerd ({text.Length:N0} tekens)."
                    };
                })
                .ToList());
            SaveKnowledgeDocuments(refreshed);
            RenderKnowledgeLibraryPanel();
            OutputText.Text = "Documentatiebibliotheek opnieuw geindexeerd.";
        }
        catch (Exception exception)
        {
            OutputText.Text = $"Documentatie vernieuwen is niet gelukt\n\n{exception.Message}";
        }
        finally
        {
            EndBackgroundTask();
        }
    }

    private void RemoveKnowledgeDocument_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedProject is null || (sender as Button)?.Tag is not Guid id) return;

        var documents = ReadKnowledgeDocuments().ToList();
        var document = documents.FirstOrDefault(document => document.Id == id);
        if (document is null) return;

        documents.RemoveAll(item => item.Id == id);
        SaveKnowledgeDocuments(documents);
        if (!string.IsNullOrWhiteSpace(document.LocalPath) && File.Exists(document.LocalPath))
        {
            try { File.Delete(document.LocalPath); }
            catch (System.Exception swallowedException)
            {
                AppLog.Swallowed(swallowedException);
            }
        }

        RenderKnowledgeLibraryPanel();
        OutputText.Text = $"Documentatie verwijderd: {document.DisplayName}.";
    }

    private void KnowledgeLibrary_OnClick(object sender, RoutedEventArgs e)
    {
        KnowledgeLibraryButton.Visibility = Visibility.Collapsed;
        SidebarKnowledgeTab.Visibility = Visibility.Collapsed;
        SetSidebarTab("reportInfo");
    }




    private async Task RunUiBackgroundOperationAsync(string busyText, Func<Task<string>> operation)
    {
        var stopwatch = Stopwatch.StartNew();
        Exception? failure = null;
        BeginBackgroundTask(busyText);
        try
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            OutputText.Text = await operation();
        }
        catch (Exception exception)
        {
            failure = exception;
            OutputText.Text = $"{busyText} is niet gelukt\n\n{exception.Message}";
        }
        finally
        {
            stopwatch.Stop();
            LogPerformanceTiming(busyText, stopwatch.Elapsed, failure);
            EndBackgroundTask();
        }
    }

    private static void LogPerformanceTiming(string action, TimeSpan elapsed, Exception? exception = null)
    {
        try
        {
            var logDirectory = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Borevexa",
                "Logs");
            Directory.CreateDirectory(logDirectory);

            var status = exception is null ? "ok" : "error";
            var message = exception?.Message ?? "";
            var line = string.Join('\t',
                DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
                elapsed.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture),
                status,
                SanitizePerformanceLogValue(action),
                SanitizePerformanceLogValue(message));
            File.AppendAllText(
                System.IO.Path.Combine(logDirectory, "performance.log"),
                line + Environment.NewLine,
                Encoding.UTF8);
            Debug.WriteLine($"Borevexa performance: {line}");
        }
        catch (System.Exception swallowedException)
        {
            // Performance logging must never interrupt the workflow.
            AppLog.Swallowed(swallowedException);
        }
    }

    private static void LogPerformanceTimingIfSlow(string action, TimeSpan elapsed, double minimumMilliseconds)
    {
        if (elapsed.TotalMilliseconds < minimumMilliseconds)
        {
            return;
        }

        LogPerformanceTiming(action, elapsed);
    }

    private static string SanitizePerformanceLogValue(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "-"
            : value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
    }

    private void BeginBackgroundTask(string text)
    {
        _backgroundTaskDepth++;
        GlobalBusyText.Text = text;
        GlobalBusyIndicator.Visibility = Visibility.Visible;
        SetBackgroundTaskControlsEnabled(false);
    }

    private void EndBackgroundTask()
    {
        _backgroundTaskDepth = Math.Max(0, _backgroundTaskDepth - 1);
        if (_backgroundTaskDepth > 0) return;

        GlobalBusyIndicator.Visibility = Visibility.Collapsed;
        SetBackgroundTaskControlsEnabled(true);
    }

    private void SetBackgroundTaskControlsEnabled(bool enabled)
    {
        UpdateStepNavigationButtons();
        HeaderStepSaveButton.IsEnabled = enabled;
        HeaderMapSaveButton.IsEnabled = enabled;
        MapToolbarSaveButton.IsEnabled = enabled;
        FinalReportExportButton.IsEnabled = enabled;
        FinalReportRegenerateButton.IsEnabled = enabled;
        ActionButtonsPanel.IsEnabled = enabled;
        StepSpecificRibbonPanel.IsEnabled = enabled;
        WorkflowSubstepsPanel.IsEnabled = enabled;
        WorkflowPartsPanel.IsEnabled = enabled;
    }

    private void UpdateStepNavigationButtons(IReadOnlyList<StepNavigationItem>? selectableItems = null, int? currentNavigationIndex = null)
    {
        PreviousStepButton.Visibility = Visibility.Visible;
        NextStepButton.Visibility = Visibility.Visible;

        if (_selectedProject is null || _selectedStep is null || _backgroundTaskDepth > 0)
        {
            PreviousStepButton.IsEnabled = false;
            NextStepButton.IsEnabled = false;
            return;
        }

        selectableItems ??= _stepNavigationItems.Where(item => item.IsSelectable && !item.IsReportPreview).ToList();
        var currentIndex = currentNavigationIndex ?? selectableItems.ToList().FindIndex(IsCurrentStepNavigationItem);

        PreviousStepButton.IsEnabled = currentIndex > 0;
        NextStepButton.IsEnabled = currentIndex >= 0 && currentIndex < selectableItems.Count - 1;
    }

    private void RenderWorkspace()
    {
        if (_selectedStep is null) return;
        _activeWorkflowPartKey = null;
        var workspace = _workspaces[_selectedStep.Number];
        var reportPreviewStepNumber = _selectedReportPreviewStepNumber;

        var selectableItems = _stepNavigationItems.Where(item => item.IsSelectable && !item.IsReportPreview).ToList();
        var workflowStepCount = _stepNavigationItems.Count(item => item.IsWorkflowStep);
        var currentNavigationIndex = selectableItems.FindIndex(IsCurrentStepNavigationItem);
        StepCounterText.Text = reportPreviewStepNumber is null
            ? _selectedSubstep is null
                ? $"Stap {DisplayStepNumber(workspace.StepNumber)} van {workflowStepCount}"
                : $"Substap {DisplaySubstepNumber(_selectedSubstep)} van stap {DisplayStepNumber(workspace.StepNumber)}"
            : $"Stap {DisplayStepNumber(workspace.StepNumber)} rapportpreview";
        StepHeaderTitle.Text = reportPreviewStepNumber is null
            ? _selectedSubstep is null ? workspace.Title : DisplayReportSectionTitle(_selectedSubstep)
            : $"{workspace.Title} - Rapport preview";
        UpdateStepNavigationButtons(selectableItems, currentNavigationIndex);
        WorkspaceTitle.Text = $"Stap {DisplayStepNumber(workspace.StepNumber)}: {workspace.Title}";
        WorkspaceSubtitle.Text = workspace.Subtitle;
        MapTitle.Text = workspace.MapTitle;
        MapSubtitle.Text = workspace.MapSubtitle;
        ActionsSubtitle.Text = $"Stap {DisplayStepNumber(workspace.StepNumber)} acties";
        NativeNotes.Text = GetNativeNote(workspace.StepNumber);
        RefreshWorkflowReportStatus(workspace.StepNumber);

        if (reportPreviewStepNumber is not null)
        {
            StepZeroPanel.Visibility = Visibility.Collapsed;
            StepOnePanel.Visibility = Visibility.Collapsed;
            StepTwoPanel.Visibility = Visibility.Collapsed;
            StepThreePanel.Visibility = Visibility.Collapsed;
            MapWorkspaceGrid.Visibility = Visibility.Collapsed;
            StepTenReportGrid.Visibility = Visibility.Visible;
            StepElevenWorkDrawingGrid.Visibility = Visibility.Collapsed;
            SubstepIntroductionEditorPanel.Visibility = Visibility.Collapsed;
            StepRibbonHost.Visibility = Visibility.Collapsed;
            HeaderMapSaveButton.Visibility = Visibility.Collapsed;
            ApplyStepFilterSidebarState(false);
            ApplyInlineReportPreviewState();
            StepCardsPanel.Children.Clear();
            RenderWorkflowParts(workspace);
            RenderStepReportPreview(reportPreviewStepNumber.Value);
            ActionButtonsPanel.Children.Clear();
            OutputText.Text = $"Rapportpreview stap {workspace.StepNumber} geopend.";
            return;
        }

        RenderSubstepIntroductionEditor();

        var isChapterIntroductionSubstep =
            _selectedSubstep is not null && IsChapterOpeningSubstep(workspace.StepNumber, _selectedSubstep);

        StepZeroPanel.Visibility = workspace.StepNumber == 0 && !isChapterIntroductionSubstep
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (isChapterIntroductionSubstep)
        {
            StepOnePanel.Visibility = Visibility.Collapsed;
            StepTwoPanel.Visibility = Visibility.Collapsed;
            StepThreePanel.Visibility = Visibility.Collapsed;
            MapWorkspaceGrid.Visibility = Visibility.Collapsed;
            StepTenReportGrid.Visibility = Visibility.Collapsed;
            StepElevenWorkDrawingGrid.Visibility = Visibility.Collapsed;
            StepRibbonHost.Visibility = Visibility.Collapsed;
            HeaderMapSaveButton.Visibility = Visibility.Collapsed;
            MachineSidebarHost.Visibility = Visibility.Collapsed;
            ApplyStepFilterSidebarState(true);
            SidebarToolsTabsPanel.Visibility = Visibility.Visible;
            SidebarToolContentHost.Visibility = Visibility.Visible;
            ApplySidebarToolTabAvailability(false);
            if (_activeSidebarTab is not "reportInfo" and not "knowledge" and not "projectInfo")
            {
                _activeSidebarTab = "reportInfo";
            }
        }
        else
        {
            StepOnePanel.Visibility = workspace.StepNumber == 1 ? Visibility.Visible : Visibility.Collapsed;
            StepTwoPanel.Visibility = workspace.StepNumber == 2 ? Visibility.Visible : Visibility.Collapsed;
            var usesMapLibreWorkspace = workspace.StepNumber is >= 3 and <= ThreeDStepNumber && workspace.StepNumber != ReportStepNumber;
            var usesReportMapLock = IsMapWorkspaceStep(workspace.StepNumber);
            var usesSidebarTools = usesMapLibreWorkspace || workspace.StepNumber == WorkDrawingStepNumber;
            var usesRightSidebar = true;
            var usesStepRibbon = workspace.StepNumber >= 5 && workspace.StepNumber != WorkDrawingStepNumber && workspace.StepNumber != ReportStepNumber;
            StepRibbonHost.Visibility = usesStepRibbon ? Visibility.Visible : Visibility.Collapsed;
            ApplyStepFilterSidebarState(usesRightSidebar);
            SidebarToolsTabsPanel.Visibility = Visibility.Visible;
            SidebarToolContentHost.Visibility = Visibility.Visible;
            ApplySidebarToolTabAvailability(usesSidebarTools);
            if (!usesSidebarTools)
            {
                MachineSidebarHost.Visibility = Visibility.Collapsed;
                if (_activeSidebarTab is not "reportInfo" and not "knowledge" and not "projectInfo")
                {
                    _activeSidebarTab = "reportInfo";
                }
            }
            HeaderMapSaveButton.Visibility = Visibility.Collapsed;
            if (usesReportMapLock) UpdateMapReportLockButton();
            StepThreePanel.Visibility = usesMapLibreWorkspace ? Visibility.Visible : Visibility.Collapsed;
            MapWorkspaceGrid.Visibility = workspace.StepNumber is >= 0 and <= WorkDrawingStepNumber ? Visibility.Collapsed : Visibility.Visible;
            StepTenReportGrid.Visibility = workspace.StepNumber == ReportStepNumber ? Visibility.Visible : Visibility.Collapsed;
            StepElevenWorkDrawingGrid.Visibility = workspace.StepNumber == WorkDrawingStepNumber ? Visibility.Visible : Visibility.Collapsed;
            if (workspace.StepNumber == 0)
            {
                RenderStepZero();
            }
            else if (workspace.StepNumber == 1)
            {
                RenderStepOne();
                ApplyStepOneSubstepLayout();
            }
            else if (workspace.StepNumber == 2)
            {
                RenderStepTwo();
            }
            else if (usesMapLibreWorkspace)
            {
                ApplyStoredMapStateForCurrentStep();
                ApplyStepThreeLayoutBounds();
                RenderStepThree();
            }
            else if (workspace.StepNumber == WorkDrawingStepNumber)
            {
                EnsureProfilePoints();
                RenderWorkDrawingStepSidebar();
                RenderWorkDrawingPreview();
            }
            else
            {
                if (usesStepRibbon)
                {
                    RenderGenericStepRibbon(workspace);
                }
                RenderGisMap(workspace);
            }
        }

        StepCardsPanel.Children.Clear();
        if (!isChapterIntroductionSubstep)
        {
            foreach (var card in workspace.Cards)
            {
                StepCardsPanel.Children.Add(CreateCard(card));
            }
            foreach (var substep in StepReportCatalog.GetSubsteps(workspace.StepNumber))
            {
                StepCardsPanel.Children.Add(CreateSubstepReportCard(substep));
            }
        }
        RenderWorkflowParts(workspace);
        // Navigating to a map step can land on a WebView2 whose WebGL context was
        // dropped while it was collapsed on another step/tab. Ask the map to repaint
        // (self-heals once the canvas is visible again) without a jarring host resize.
        NudgeMapRenderIfVisible();
        if (workspace.StepNumber == ReportStepNumber)
        {
            RenderFinalReportPanel(workspace.StepNumber);
            RenderStepTenReport();
        }
        else
        {
            StepFinalReportPanel.Visibility = Visibility.Collapsed;
        }
        ApplyInlineReportPreviewState();

        ActionButtonsPanel.Children.Clear();
        foreach (var action in workspace.Actions)
        {
            var button = new Button { Content = action, Tag = action };
            button.Click += StepAction_OnClick;
            ActionButtonsPanel.Children.Add(button);
        }

        OutputText.Text = $"Stap {workspace.StepNumber} geladen. Kies een actie om de native module te testen.";
    }





    private UIElement GetReportPreviewPage(int stepNumber)
    {
        for (var i = 0; i < StepTenReportContent.Children.Count; i++)
        {
            if (StepTenReportContent.Children[i] is FrameworkElement { Tag: int pageStepNumber } && pageStepNumber == stepNumber)
            {
                var page = StepTenReportContent.Children[i];
                StepTenReportContent.Children.RemoveAt(i);
                return page;
            }
        }

        var title = _workspaces.TryGetValue(stepNumber, out var workspace)
            ? $"Stap {stepNumber} - {workspace.Title}"
            : $"Stap {stepNumber}";
        var placeholder = new StackPanel();
        placeholder.Children.Add(CreateReportNote("Voor dit rapportonderdeel is nog geen automatische inhoud ingericht. Vul de rapporttekst hierboven om dit onderdeel alvast vast te leggen."));
        AddReportStepText(placeholder, stepNumber);
        return CreateReportPage(stepNumber, title, CreateReportSection(stepNumber, title, placeholder));
    }

    private void SaveReportPreviewPart_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedProject is null || _selectedStep is null || _selectedReportPreviewStepNumber is null)
        {
            OutputText.Text = "Geen project actief. Open of maak eerst een project.";
            return;
        }

        var stepNumber = _selectedReportPreviewStepNumber.Value;
        var text = ReportPreviewPartText.Text.Trim();
        var payload = JsonSerializer.Serialize(new
        {
            text,
            updatedAt = DateTimeOffset.Now
        }, JsonOptions);
        SaveSelectedProjectStepData(stepNumber, ReportSectionDataKey, payload);
        OutputText.Text = string.IsNullOrWhiteSpace(text)
            ? $"Rapportonderdeel stap {stepNumber} leeg opgeslagen."
            : $"Rapportonderdeel stap {stepNumber} opgeslagen voor het eindrapport.";

        RenderStepReportPreview(stepNumber);
    }

    private void RefreshInlineReportPreviewIfVisible()
    {
        if (!CanRefreshInlineReportPreview())
        {
            return;
        }

        _inlineReportPreviewRefreshTimer.Stop();
        _inlineReportPreviewRefreshTimer.Start();
    }

    private bool IsReportPreviewWindowOpen() =>
        _reportPreviewWindow is { IsVisible: true } && _reportPreviewWindowContent is not null;

    private bool CanRefreshInlineReportPreview()
    {
        var inlineVisible = !_inlineReportPreviewCollapsed && InlineReportPreviewSidebar.Visibility == Visibility.Visible;
        if ((!inlineVisible && !IsReportPreviewWindowOpen()) ||
            _selectedProject is null ||
            _selectedStep is null ||
            _selectedReportPreviewStepNumber is not null)
        {
            return false;
        }

        return true;
    }

    private void InlineReportPreviewRefreshTimer_OnTick(object? sender, EventArgs e)
    {
        _inlineReportPreviewRefreshTimer.Stop();
        RefreshInlineReportPreviewIfVisibleNow();
    }

    private void RefreshInlineReportPreviewIfVisibleNow()
    {
        if (!CanRefreshInlineReportPreview() || _selectedStep is not { } selectedStep)
        {
            return;
        }

        SaveStepReportDataForStep(selectedStep.Number);
        if (!_inlineReportPreviewCollapsed && InlineReportPreviewSidebar.Visibility == Visibility.Visible)
        {
            RenderInlineReportPreview();
        }

        if (IsReportPreviewWindowOpen())
        {
            RefreshReportPreviewWindow();
        }
    }








    private void ReportPreviewZoomOut_OnClick(object sender, RoutedEventArgs e)
    {
        _inlineReportPreviewZoom = Math.Max(0.30, _inlineReportPreviewZoom - 0.10);
        ApplyInlineReportPreviewZoom();
    }

    private void ReportPreviewZoomIn_OnClick(object sender, RoutedEventArgs e)
    {
        _inlineReportPreviewZoom = Math.Min(1.50, _inlineReportPreviewZoom + 0.10);
        ApplyInlineReportPreviewZoom();
    }

    private void ReportPreviewZoomReset_OnClick(object sender, RoutedEventArgs e)
    {
        _inlineReportPreviewZoom = 0.55;
        ApplyInlineReportPreviewZoom();
    }

    private void ApplyInlineReportPreviewZoom()
    {
        InlineReportPreviewContent.LayoutTransform = new ScaleTransform(_inlineReportPreviewZoom, _inlineReportPreviewZoom);
        ReportPreviewZoomTextButton.Content = $"{_inlineReportPreviewZoom * 100:N0}%";
        if (_reportPreviewWindowContent is not null)
        {
            _reportPreviewWindowContent.LayoutTransform = new ScaleTransform(_inlineReportPreviewZoom, _inlineReportPreviewZoom);
        }

        if (_reportPreviewWindowZoomButton is not null)
        {
            _reportPreviewWindowZoomButton.Content = $"{_inlineReportPreviewZoom * 100:N0}%";
        }
    }

    private void RenderInlineReportPreview()
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            RenderCurrentReportPreviewInto(InlineReportPreviewTitle, InlineReportPreviewSubtitle, InlineReportPreviewContent, useSignatureCache: true);
            ApplyInlineReportPreviewZoom();
        }
        finally
        {
            stopwatch.Stop();
            LogPerformanceTimingIfSlow($"Inline rapportpreview {_selectedStep?.Number.ToString(CultureInfo.InvariantCulture) ?? "-"} {_selectedSubstep?.Number ?? ""}".Trim(), stopwatch.Elapsed, 100);
        }
    }

    private void RefreshReportPreviewWindow()
    {
        if (_reportPreviewWindowContent is null ||
            _reportPreviewWindowTitle is null ||
            _reportPreviewWindowSubtitle is null)
        {
            return;
        }

        RenderCurrentReportPreviewInto(_reportPreviewWindowTitle, _reportPreviewWindowSubtitle, _reportPreviewWindowContent, useSignatureCache: false);
        if (_reportPreviewWindow is not null)
        {
            _reportPreviewWindow.Title = $"Rapportpreview - {_reportPreviewWindowTitle.Text}";
        }

        ApplyInlineReportPreviewZoom();
    }

    private void RenderCurrentReportPreviewInto(TextBlock titleBlock, TextBlock subtitleBlock, StackPanel content, bool useSignatureCache)
    {
        if (_selectedProject is null || _selectedStep is null)
        {
            content.Children.Clear();
            if (useSignatureCache)
            {
                _lastInlineReportPreviewSignature = null;
            }

            titleBlock.Text = "Rapportpreview";
            subtitleBlock.Text = "Geen project actief";
            content.Children.Add(CreateReportPreviewPlaceholder("Open of maak eerst een project."));
            return;
        }

        var signature = BuildInlineReportPreviewSignature();
        if (useSignatureCache &&
            string.Equals(_lastInlineReportPreviewSignature, signature, StringComparison.Ordinal) &&
            content.Children.Count > 0)
        {
            return;
        }

        if (useSignatureCache)
        {
            _lastInlineReportPreviewSignature = signature;
        }

        content.Children.Clear();
        TryReadStepReportRoot(_selectedStep.Number, out var root);

        if (_reportPreviewWindowScope == ReportPreviewWindowScope.Chapter)
        {
            var chapterTitle = _workspaces.TryGetValue(_selectedStep.Number, out var chapterWorkspace)
                ? $"{DisplayStepNumber(_selectedStep.Number)} {chapterWorkspace.Title}"
                : $"Stap {DisplayStepNumber(_selectedStep.Number)}";
            titleBlock.Text = chapterTitle;
            subtitleBlock.Text = "Hoofdstuk in eindrapportage";

            var pages = CreateStepReportPreviewPages(_selectedStep.Number).ToList();
            if (pages.Count == 0)
            {
                pages.Add(CreateInlineStepReportPage(_selectedStep.Number, root));
            }

            foreach (var page in pages)
            {
                content.Children.Add(page);
            }

            return;
        }

        if (_selectedSubstep is not null)
        {
            titleBlock.Text = DisplayReportSectionTitle(_selectedSubstep);
            subtitleBlock.Text = "Substap in eindrapportage";
            foreach (var page in CreateInlineSubstepReportPages(_selectedStep.Number, _selectedSubstep, root))
            {
                content.Children.Add(page);
            }

            return;
        }

        var title = _workspaces.TryGetValue(_selectedStep.Number, out var workspace)
            ? $"{DisplayStepNumber(_selectedStep.Number)} {workspace.Title}"
            : $"Stap {DisplayStepNumber(_selectedStep.Number)}";
        titleBlock.Text = title;
        subtitleBlock.Text = "Stap in eindrapportage";
        content.Children.Add(CreateInlineStepReportPage(_selectedStep.Number, root));
    }

    private string BuildInlineReportPreviewSignature()
    {
        return string.Join(";",
            _selectedProject?.Id.ToString("N") ?? "geen-project",
            _selectedStep?.Number.ToString(CultureInfo.InvariantCulture) ?? "geen-stap",
            _selectedSubstep?.Number ?? "geen-substap",
            _reportPreviewWindowScope.ToString(),
            _reportUiDataVersion.ToString(CultureInfo.InvariantCulture),
            _projectFiles.Count.ToString(CultureInfo.InvariantCulture),
            _selectedMachineId ?? "",
            _profilePoints.Count.ToString(CultureInfo.InvariantCulture),
            _selectedBroModelType);
    }



    private IReadOnlyList<UIElement> CreateCustomerFinalReportPreviewPages(bool refreshStepData = true)
    {
        if (_selectedProject is null) return [];

        var stopwatch = Stopwatch.StartNew();
        var pages = new List<UIElement>();
        var stepRootCache = new Dictionary<int, JsonElement>();
        int? currentChapterStep = null;
        try
        {
            if (refreshStepData)
            {
                _projectFiles = _projects.GetProjectFiles(_selectedProject.Id);
            }

            foreach (var (stepNumber, substep) in EnumerateFinalReportSubsteps())
            {
                var includeChapterIntro = stepNumber > 0 && currentChapterStep != stepNumber;
                if (includeChapterIntro) currentChapterStep = stepNumber;

                if (!stepRootCache.TryGetValue(stepNumber, out var root))
                {
                    if (refreshStepData)
                    {
                        SaveStepReportDataForStep(stepNumber, refreshProjectFiles: false);
                    }

                    TryReadStepReportRoot(stepNumber, out root);
                    stepRootCache[stepNumber] = root;
                }

                pages.AddRange(CreateInlineSubstepReportPagesCore(stepNumber, substep, root, expandFinalConclusionPreview: false, includeChapterIntro: includeChapterIntro));
            }

            if (pages.Count >= 3)
            {
                pages[2] = CreateReportContentsPage(3, BuildCustomerFinalReportContentsEntries(pages));
            }
            RenumberReportPreviewPages(pages);
            return pages;
        }
        finally
        {
            stopwatch.Stop();
            LogPerformanceTimingIfSlow($"Eindrapport preview-opbouw {pages.Count} pagina's", stopwatch.Elapsed, 200);
        }
    }

    private IReadOnlyList<ReportContentsEntry> BuildCustomerFinalReportContentsEntries(IReadOnlyList<UIElement> pages)
    {
        var entries = new List<ReportContentsEntry>();
        var groups = new List<(string Title, string Description, int FirstPage, int LastPage)>();

        for (var index = 0; index < pages.Count; index++)
        {
            var pageNumber = index + 1;
            var title = BuildContentsEntryTitle(pageNumber, ExtractReportPageTitle(pages[index]));
            var description = BuildContentsGroupDescription(title);
            if (groups.Count > 0 && string.Equals(groups[^1].Title, title, StringComparison.OrdinalIgnoreCase))
            {
                groups[^1] = (groups[^1].Title, groups[^1].Description, groups[^1].FirstPage, pageNumber);
            }
            else
            {
                groups.Add((title, description, pageNumber, pageNumber));
            }
        }

        foreach (var group in groups)
        {
            var pageText = group.FirstPage == group.LastPage
                ? group.FirstPage.ToString(CultureInfo.InvariantCulture)
                : $"{group.FirstPage}-{group.LastPage}";
            entries.Add(new ReportContentsEntry(pageText, group.Title, group.Description));
        }

        return entries;
    }

    private static string BuildContentsEntryTitle(int pageNumber, string pageTitle)
    {
        if (pageNumber == 1) return "Voorblad";
        if (pageNumber == 2) return "Voorwoord";
        if (pageNumber == 3) return "Inhoudsopgave";

        var title = Regex.Replace(pageTitle.Trim(), @"\s+", " ");
        if (string.IsNullOrWhiteSpace(title)) return "Rapportonderdeel";

        return title;
    }

    private static string BuildContentsGroupTitle(int pageNumber, string pageTitle)
    {
        if (pageNumber == 1) return "Voorblad";
        if (pageNumber == 2) return "Voorwoord";
        if (pageNumber == 3) return "Inhoudsopgave";

        var title = pageTitle.Trim();
        var stepMatch = Regex.Match(title, @"^(?<step>\d+)(?:\.\d+)?\b");
        if (!stepMatch.Success)
        {
            stepMatch = Regex.Match(title, @"^Stap\s+(?<step>\d+)\b", RegexOptions.IgnoreCase);
        }

        if (!stepMatch.Success || !int.TryParse(stepMatch.Groups["step"].Value, out var stepNumber))
        {
            return string.IsNullOrWhiteSpace(title) ? "Rapportonderdeel" : title;
        }

        return stepNumber switch
        {
            1 => "Stap 1 - Projectinformatie",
            2 => "Stap 2 - Ontwerp, KLIC, BAG & BGT inladen",
            3 => "Stap 3 - Boorlijn",
            4 => "Stap 4 - Oppervlakteanalyse",
            5 => "Stap 5 - Omgevingsmanagement",
            6 => "Stap 6 - Ondergrondanalyse",
            7 => "Stap 7 - Dwarsprofiel",
            8 => "Stap 8 - Machine locatie",
            9 => "Stap 9 - Sonderingen",
            10 => "Stap 10 - Eindrapport & Export",
            _ => $"Stap {stepNumber} - Rapportonderdeel"
        };
    }

    private static string BuildContentsGroupDescription(string title)
    {
        if (title.Equals("Voorblad", StringComparison.OrdinalIgnoreCase)) return "Project, locatie, opdrachtgever, boorlengte en rapportdatum.";
        if (title.Equals("Voorwoord", StringComparison.OrdinalIgnoreCase)) return "Toelichting op de prescan, uitgangspunten en rapportcontext.";
        if (title.Equals("Inhoudsopgave", StringComparison.OrdinalIgnoreCase)) return "Overzicht van de rapportonderdelen met actuele bladzijden.";
        if (title.Contains("Projectinformatie", StringComparison.OrdinalIgnoreCase)) return "Projectgegevens, productbundel, vulgraad, dwarsdoorsnede en machinekeuze.";
        if (title.Contains("Ontwerp", StringComparison.OrdinalIgnoreCase)) return "Ingeladen projectbestanden, documenten, GIS-lagen en KLIC-context.";
        if (title.Contains("Boorlijn", StringComparison.OrdinalIgnoreCase)) return "Vastgelegde boorlijn, kaartbijlagen en KLIC-kruisingen.";
        if (title.Contains("Oppervlakteanalyse", StringComparison.OrdinalIgnoreCase)) return "BGT-oppervlaktes, segmenten en AHN4-maaiveldprofiel.";
        if (title.Contains("Omgevingsmanagement", StringComparison.OrdinalIgnoreCase)) return "Perceelsegmenten, ZRO-controle en kaartuitsneden per perceel.";
        if (title.Contains("Ondergrondanalyse", StringComparison.OrdinalIgnoreCase)) return "BRO/DINOloket datasets, bronpunten en geselecteerde ondergrondinformatie.";
        if (title.Contains("Dwarsprofiel", StringComparison.OrdinalIgnoreCase)) return "Horizontaal profiel, profielstaat, boorpunten en KLIC-kruisingen.";
        if (title.Contains("Machine", StringComparison.OrdinalIgnoreCase)) return "Machine, werkvak en plaatsingen.";
        if (title.Contains("Sonderingen", StringComparison.OrdinalIgnoreCase)) return "Sonderingen en geotechnische aandachtspunten.";
        if (title.Contains("Eindrapport", StringComparison.OrdinalIgnoreCase)) return "Eindconclusie en resterende rapportcontrole.";
        return "Rapportonderdeel uit de opgeslagen processtappen.";
    }




    private void GenerateForeword_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedProject is null)
        {
            OutputText.Text = "Geen project actief.";
            return;
        }

        var context = BuildReportStartContext();
        ReportForewordTextInput.Text = GenerateDefaultForewordText(context.DocumentCount, context.ParcelCount, context.TraceLength);
        ReportForewordScopeInput.Text = GenerateDefaultForewordScope(context.DocumentCount, context.ParcelCount, context.TraceLength);
        OutputText.Text = "Voorwoord gegenereerd. Pas de tekst eventueel aan en klik daarna op Rapportstart opslaan.";
    }

    private void SaveReportStart_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedProject is null)
        {
            OutputText.Text = "Geen project actief.";
            return;
        }

        var settings = new ReportStartSettings(
            ReportCoverTitleInput.Text.Trim(),
            ReportCoverSubtitleInput.Text.Trim(),
            ReportCoverRevisionInput.Text.Trim(),
            ReportCoverNoteInput.Text.Trim(),
            ReportForewordTextInput.Text.Trim(),
            ReportForewordScopeInput.Text.Trim(),
            ReportContentsTitleInput.Text.Trim(),
            ReportContentsIntroInput.Text.Trim(),
            ReportContentsIncludeAppendicesInput.IsChecked != false);
        SaveSelectedProjectStepData(0, ReportStartSettingsDataKey, JsonSerializer.Serialize(settings, JsonOptions));
        SaveStepReportDataForStep(0);
        RefreshWorkflowReportStatus(0);
        RenderStepZeroInfoPanel(_selectedSubstep?.Number ?? "0.1", settings);
        RenderInlineReportPreview();
        OutputText.Text = "Rapportstart opgeslagen voor 00.1, 00.2 en 00.3.";
    }



    private static bool IsSurfaceSegmentsReportSubstep(int stepNumber, string substepNumber) =>
        stepNumber == 4 && string.Equals(substepNumber, "4.1", StringComparison.OrdinalIgnoreCase);



    private static bool IsEnvironmentParcelReportSubstep(int stepNumber, string substepNumber) =>
        stepNumber == EnvironmentStepNumber &&
        string.Equals(substepNumber, "5.1", StringComparison.OrdinalIgnoreCase);


    private static bool IsFinalConclusionReportSubstep(int stepNumber, string substepNumber) =>
        stepNumber == ReportStepNumber &&
        string.Equals(substepNumber, "10.2", StringComparison.OrdinalIgnoreCase);






    private UIElement CreateCompactReportChapterHeader(int stepNumber)
    {
        var title = $"{DisplayStepNumber(stepNumber)} {GetReportStepTitle(stepNumber)}";
        var subtitle = _workspaces.TryGetValue(stepNumber, out var workspace)
            ? workspace.Subtitle
            : "Rapportonderdelen uit deze processtap.";
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brush("#071422"),
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = subtitle,
            Foreground = Brush("#587080"),
            FontSize = 10.5,
            LineHeight = 14,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });

        return new Border
        {
            BorderBrush = Brush("#D7E8FA"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 0, 0, 12),
            Margin = new Thickness(0, 0, 0, 14),
            Child = panel
        };
    }

    private static UIElement CreateCompactSubstepMetaBlock(PrescanSubstep substep, string status, ReportQualitySummary quality)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = $"{DisplaySubstepNumber(substep)} · {status}",
            Foreground = Brush("#555555"),
            FontSize = 8.8,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = quality.IsReady
                ? "Rapportstatus: compleet"
                : $"Rapportstatus: {CustomerReportQualityLabel(quality)}",
            Foreground = quality.IsReady ? Brush("#555555") : Brush(quality.StatusColor),
            FontSize = 8.4,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        });

        return new Border
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 8),
            Child = panel
        };
    }

    private UIElement CreateSubstepIntroductionReportBlock(int stepNumber, PrescanSubstep substep, JsonElement root)
    {
        var panel = new StackPanel();
        var sections = GetSubstepTextSections(stepNumber, substep, root);
        foreach (var definition in SubstepTextSectionDefinitions)
        {
            if (!sections.TryGetValue(definition.Key, out var text) || string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            panel.Children.Add(CreateReportSubheading(definition.Title));
            panel.Children.Add(CreateReportStepPreviewText(CleanCustomerReportIntroduction(text)));
        }

        return panel;
    }

    private static bool ShouldShowChapterTextSections(int stepNumber, PrescanSubstep substep, bool includeChapterIntro = false)
    {
        return stepNumber > 0 && IsChapterIntroductionSubstep(substep);
    }

    private static bool IsChapterOpeningSubstep(int stepNumber, PrescanSubstep substep)
    {
        return stepNumber > 0 && IsChapterIntroductionSubstep(substep);
    }

    private static bool IsChapterIntroductionSubstep(PrescanSubstep? substep) =>
        substep?.IsChapterIntroduction == true;












    private static double? FirstNullableDouble(params double?[] values)
    {
        foreach (var value in values)
        {
            if (value is double number && !double.IsNaN(number) && !double.IsInfinity(number))
            {
                return number;
            }
        }

        return null;
    }











    private static string FormatReportNullable(double? value, int decimals, string suffix = "")
    {
        return value is double number && double.IsFinite(number)
            ? FormatReportNumber(number, decimals, suffix)
            : "-";
    }









    private static string FormatReportNumber(double value, int decimals, string suffix = "")
    {
        if (!double.IsFinite(value)) return "-";
        var format = decimals <= 0 ? "N0" : $"N{decimals}";
        return string.IsNullOrWhiteSpace(suffix)
            ? value.ToString(format, CultureInfo.CurrentCulture)
            : $"{value.ToString(format, CultureInfo.CurrentCulture)}{suffix}";
    }






    private static bool TryLoadReportLegendBitmap(string url, out BitmapSource bitmap)
    {
        bitmap = null!;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var bytes = client.GetByteArrayAsync(url).ConfigureAwait(false).GetAwaiter().GetResult();
            using var stream = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) return false;
            bitmap = decoder.Frames[0];
            if (bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0) return false;
            bitmap.Freeze();
            return true;
        }
        catch
        {
            return false;
        }
    }




    private static UIElement CreateLegendRow(string color, string label)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var swatch = new Border
        {
            Width = 10,
            Height = 10,
            CornerRadius = new CornerRadius(5),
            Background = Brush(color),
            BorderBrush = Brush("#CBD5E1"),
            BorderThickness = new Thickness(0.6),
            Margin = new Thickness(0, 2, 8, 0)
        };
        row.Children.Add(swatch);

        var text = new TextBlock
        {
            Text = label,
            Foreground = Brush("#333333"),
            FontSize = 9,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(text, 1);
        row.Children.Add(text);

        return row;
    }










    private UIElement CreateReadableSubstepReportContent(string substepNumber, JsonElement data, bool includeSubstepMedia = true)
    {
        if (_reportRender.TryRenderSubstep(substepNumber, data, out var document))
        {
            return CreateReportRenderDocument(substepNumber, data, document, includeSubstepMedia);
        }

        return CreateMissingSubstepRendererPreview(substepNumber);
    }

    private static UIElement CreateMissingSubstepRendererPreview(string substepNumber) =>
        CreateReportNote($"Voor substap {substepNumber} is nog geen specifieke rapportrenderer geregistreerd. De ruwe rapportdata wordt bewust niet getoond; voeg eerst een renderer toe voordat dit onderdeel als eindrapportsectie wordt vrijgegeven.");


    private void AddBgtSurfaceReportMedia(Panel panel)
    {
        EnsureProfilePoints();
        var total = GetSurfaceAnalysisTraceLength();
        var segments = GetBgtSurfaceSegments(total);
        var mapPath = GetLiveMapReportPreviewImagePath(4);

        panel.Children.Add(CreateReportSubheading("BGT kaartbeeld"));
        panel.Children.Add(CreateLiveMapReportImageCard(
            "Boorlijn op BGT-achtergrond",
            "Vastgezette rapportuitsnede met BGT-oppervlakken, boorlijn en kaartcontext.",
            mapPath,
            4,
            null));
        panel.Children.Add(CreateBgtSurfaceLegendBlock(segments, total));
        panel.Children.Add(CreateReportNote(string.IsNullOrWhiteSpace(mapPath)
            ? "Zet de kaart in stap 4.1 vast of voer de oppervlakteanalyse opnieuw uit om het BGT-kaartbeeld in het rapport te vullen."
            : "De BGT-legenda hieronder gebruikt dezelfde oppervlaktesegmenten als de kaart en het oppervlakteprofiel."));

        panel.Children.Add(CreateReportSubheading("BGT oppervlakteprofiel"));
        panel.Children.Add(new Border
        {
            BorderBrush = Brush("#D7E8FA"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Background = Brush("#FFFFFF"),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 4, 0, 10),
            Child = new Viewbox
            {
                Child = CreateReportSurfaceBar(total),
                MaxWidth = 640,
                HorizontalAlignment = HorizontalAlignment.Left,
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly
            }
        });
        panel.Children.Add(CreateReportSubheading("Segmenten langs boorlijn"));
        panel.Children.Add(CreateReportSurfaceSegmentTable(segments));
        panel.Children.Add(CreateReportNote(segments.Count == 0
            ? "Geen BGT-oppervlaktesegmenten gevonden. Voer de oppervlakteanalyse uit nadat de BGT-import zichtbaar is en de boorlijn is opgeslagen."
            : "De oppervlakteanalyse en segmenten gebruiken dezelfde BGT-vlakberekening als het oppervlakteprofiel in de app."));
    }

    private static Border CreateBgtSurfaceLegendBlock(IReadOnlyList<BgtSurfaceSegment> segments, double total)
    {
        var grouped = segments
            .GroupBy(segment => segment.Label, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Label = group.Key,
                Color = group.First().Color,
                Length = group.Sum(segment => Math.Max(0, segment.Length))
            })
            .OrderByDescending(item => item.Length)
            .ToList();

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Legenda BGT-oppervlakken",
            Foreground = Brush("#333333"),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });

        if (grouped.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Geen BGT-oppervlaktesegmenten beschikbaar.",
                Foreground = Brush("#64748B"),
                FontSize = 9,
                TextWrapping = TextWrapping.Wrap
            });
        }
        else
        {
            foreach (var item in grouped.Take(12))
            {
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var swatch = new Border
                {
                    Width = 10,
                    Height = 10,
                    Background = Brush(item.Color),
                    BorderBrush = Brush("#CBD5E1"),
                    BorderThickness = new Thickness(0.6),
                    Margin = new Thickness(0, 2, 8, 0)
                };
                Grid.SetColumn(swatch, 0);
                row.Children.Add(swatch);

                var label = new TextBlock
                {
                    Text = item.Label,
                    Foreground = Brush("#333333"),
                    FontSize = 9,
                    TextWrapping = TextWrapping.Wrap
                };
                Grid.SetColumn(label, 1);
                row.Children.Add(label);

                var length = new TextBlock
                {
                    Text = total > 0 ? $"{item.Length:N1} m" : "-",
                    Foreground = Brush("#475569"),
                    FontSize = 9,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(12, 0, 0, 0)
                };
                Grid.SetColumn(length, 2);
                row.Children.Add(length);

                panel.Children.Add(row);
            }
        }

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("#E5EDF3"),
            BorderThickness = new Thickness(0, 1, 0, 1),
            Padding = new Thickness(0, 8, 0, 8),
            Margin = new Thickness(0, 8, 0, 6),
            Child = panel
        };
    }






    private void AddMapControlReportImage(Panel panel, JsonElement data)
    {
        panel.Children.Add(CreateReportSubheading("Kaartprojectie"));

        var bagMapPath = GetLiveMapReportPreviewImagePath(3, StepThreeReportMapBagVariant);
        var photoMapPath = GetLiveMapReportPreviewImagePath(3, StepThreeReportMapPhotoVariant);
        var fallbackMapPath = GetLiveMapReportPreviewImagePath(3);
        if (string.IsNullOrWhiteSpace(bagMapPath)) bagMapPath = fallbackMapPath;

        panel.Children.Add(CreateLiveMapReportImageCard(
            "Boorlijn met PDOK BAG/kaartachtergrond",
            "Vaste rapportuitsnede met boorlijn en PDOK kaartcontext.",
            bagMapPath,
            3,
            StepThreeReportMapBagVariant));
        panel.Children.Add(CreateLiveMapReportImageCard(
            "Boorlijn met PDOK luchtfoto",
            "Vaste rapportuitsnede met boorlijn en PDOK foto-ondergrond.",
            photoMapPath,
            3,
            StepThreeReportMapPhotoVariant));
        panel.Children.Add(CreateReportNote(string.IsNullOrWhiteSpace(bagMapPath) && string.IsNullOrWhiteSpace(photoMapPath)
            ? "De twee rapportkaarten worden gevuld zodra de kaart voor rapportage is vastgezet."
            : "De rapportage gebruikt twee aparte kaartcaptures: een PDOK kaart/BAG-context en een PDOK luchtfoto-context. Bedieningsknoppen en tekengereedschap worden bij de capture verborgen."));

        panel.Children.Add(CreateReportSubheading("Kaartinstellingen"));
        panel.Children.Add(CreateReportKeyValues(
            ("Rapportbeelden", "PDOK kaart/BAG + PDOK luchtfoto"),
            ("Boorlijn", _currentBoreTracePoints.Count >= 2 ? $"{_currentBoreTracePoints.Count} punt(en)" : "Nog niet ingetekend"),
            ("Schaal", JsonInt(data, "mapScale") > 0 ? $"1:{JsonInt(data, "mapScale")}" : "Automatisch"),
            ("Bron", string.IsNullOrWhiteSpace(bagMapPath) && string.IsNullOrWhiteSpace(photoMapPath) ? "Nog geen kaartcapture" : "Live kaartbeeld uit processtap 3.1")));
    }


    private static ReportMapState ForceReportMapStateBase(ReportMapState source, string baseLayer)
    {
        var overlays = new Dictionary<string, bool>(source.Overlays, StringComparer.OrdinalIgnoreCase)
        {
            ["baseMap"] = true,
            ["boreTrace"] = true,
            ["parcels"] = source.Overlays.TryGetValue("parcels", out var parcels) && parcels
        };
        return source with { BaseLayer = baseLayer, Overlays = overlays };
    }

    private static bool IsDesignImportType(string fileType) =>
        fileType.Contains("LS", StringComparison.OrdinalIgnoreCase) ||
        fileType.Contains("MS", StringComparison.OrdinalIgnoreCase) ||
        fileType.Contains("Gas", StringComparison.OrdinalIgnoreCase) ||
        fileType.Contains("Water", StringComparison.OrdinalIgnoreCase) ||
        fileType.Contains("Data", StringComparison.OrdinalIgnoreCase) ||
        fileType.Contains("custom", StringComparison.OrdinalIgnoreCase);

    private static bool IsEnvironmentImportType(string fileType) =>
        fileType.Contains("BAG", StringComparison.OrdinalIgnoreCase) ||
        fileType.Contains("Kadaster", StringComparison.OrdinalIgnoreCase) ||
        fileType.Contains("BGT", StringComparison.OrdinalIgnoreCase);

    private static string FormatBytes(double bytes)
    {
        if (!double.IsFinite(bytes) || bytes <= 0) return "-";
        if (bytes >= 1024 * 1024) return $"{bytes / 1024d / 1024d:N1} MB";
        return $"{bytes / 1024d:N0} KB";
    }



    private Border CreateLiveMapReportImageCard(
        string title,
        string subtitle,
        string path,
        int stepNumber,
        string? variantKey,
        double imageWidth = 640,
        double imageHeight = 274)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brush("#071422"),
            FontWeight = FontWeights.Bold,
            FontSize = 11.5,
            Margin = new Thickness(0, 0, 0, 2)
        });
        panel.Children.Add(new TextBlock
        {
            Text = subtitle,
            Foreground = Brush("#64748B"),
            FontSize = 9.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        });
        panel.Children.Add(CreateLiveMapReportImage(path, stepNumber, variantKey, imageWidth, imageHeight));

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("#D7E8FA"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 3, 0, 7),
            Child = panel
        };
    }

    private Border CreateLocalReportImageCard(
        string title,
        string subtitle,
        string path,
        double imageWidth = 724,
        double imageHeight = 330)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brush("#071422"),
            FontWeight = FontWeights.Bold,
            FontSize = 11.5,
            Margin = new Thickness(0, 0, 0, 2)
        });
        panel.Children.Add(new TextBlock
        {
            Text = subtitle,
            Foreground = Brush("#64748B"),
            FontSize = 9.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        });
        panel.Children.Add(CreateLocalReportImage(path, imageWidth, imageHeight));

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("#D7E8FA"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 3, 0, 7),
            Child = panel
        };
    }

    private Border CreateLocalReportImage(string path, double imageWidth, double imageHeight)
    {
        var image = new Image
        {
            Width = imageWidth,
            Height = imageHeight,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true,
            ClipToBounds = true
        };

        if (string.IsNullOrWhiteSpace(path) || !TryApplyLocalImageSource(image, path))
        {
            return CreateReportStepPreviewText("Profielbeeld ontbreekt nog. Importeer het BRO/DINOloket boormonsterprofiel opnieuw om de grafiek en legenda in het rapport op te nemen.");
        }

        return new Border
        {
            BorderBrush = Brush("#D7E8FA"),
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            ClipToBounds = true,
            Width = imageWidth,
            Height = imageHeight,
            Margin = new Thickness(0, 2, 0, 0),
            Child = image
        };
    }

    private Border CreateLiveMapReportImage(
        string path,
        int stepNumber,
        string? variantKey = null,
        double reportMapWidth = 724,
        double reportMapHeight = 310)
    {
        // Het rapport toont het opgeslagen kaartbeeld VOLLEDIG (Stretch.Uniform).
        // Voorheen stond hier UniformToFill in een vast kader: de capture is breder
        // van verhouding dan het kader, waardoor de zijkanten van de boorlijn werden
        // afgesneden. De kaderhoogte volgt nu de echte beeldverhouding van de capture,
        // met een plafond zodat de A4-opmaak intact blijft.
        var image = new Image
        {
            Width = reportMapWidth,
            Height = reportMapHeight,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true,
            ClipToBounds = true
        };
        if (!TryApplyLocalImageSource(image, path))
        {
            var refreshedPath = GetLiveMapReportPreviewImagePath(stepNumber, variantKey);
            if (string.IsNullOrWhiteSpace(refreshedPath) ||
                string.Equals(refreshedPath, path, StringComparison.OrdinalIgnoreCase) ||
                !TryApplyLocalImageSource(image, refreshedPath))
            {
                return CreateReportStepPreviewText("Kaartbeeld wordt nog opgeslagen. Open de rapportpreview opnieuw of zet de kaart opnieuw vast.");
            }
        }

        var frameHeight = reportMapHeight;
        if (image.Source is BitmapSource bitmap && bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0)
        {
            frameHeight = Math.Min(reportMapWidth * bitmap.PixelHeight / bitmap.PixelWidth, 380);
            image.Height = frameHeight;
        }

        return new Border
        {
            BorderBrush = Brush("#D7E8FA"),
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            ClipToBounds = true,
            Width = reportMapWidth,
            Height = frameHeight,
            Margin = new Thickness(0, 2, 0, 0),
            Child = image
        };
    }

    private ReportMapState ReadReportMapState(int stepNumber, JsonElement fallbackData)
    {
        var baseLayer = JsonText(fallbackData, "baseLayer", _selectedMapBaseLayer);
        var overlays = new Dictionary<string, bool>(_mapOverlayStates, StringComparer.OrdinalIgnoreCase);
        var klicThemes = new Dictionary<string, bool>(_klicThemeStates, StringComparer.OrdinalIgnoreCase);
        var projectLayerVisibility = new Dictionary<string, bool>(_projectLayerStates, StringComparer.OrdinalIgnoreCase);
        JsonElement? camera = null;
        var mapScale = default(int?);

        if (_selectedProject is not null)
        {
            var json = GetCurrentMapStateJson(stepNumber);
            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    using var document = JsonDocument.Parse(json);
                    var root = document.RootElement;
                    baseLayer = JsonText(root, "baseLayer", baseLayer);
                    overlays = ReadBooleanMap(root, "overlays", overlays);
                    klicThemes = ReadBooleanMap(root, "klicThemes", klicThemes);
                    projectLayerVisibility = ReadBooleanMap(root, "projectLayerVisibility", projectLayerVisibility);
                    if (root.TryGetProperty("camera", out var savedCamera) && savedCamera.ValueKind == JsonValueKind.Object)
                    {
                        camera = savedCamera.Clone();
                    }
                    if (root.TryGetProperty("mapScale", out var savedMapScale) && savedMapScale.TryGetInt32(out var scaleValue))
                    {
                        mapScale = scaleValue;
                    }
                }
                catch (System.Exception swallowedException)
                {
                    // Fall back to the live settings already available in the app.
                    AppLog.Swallowed(swallowedException);
                }
            }
        }

        var centerLon = default(double?);
        var centerLat = default(double?);
        var zoom = default(double?);
        if (stepNumber == 3)
        {
            baseLayer = NormalizeStepThreeReportBaseLayer(baseLayer);
            overlays = NormalizeStepThreeReportOverlays(overlays);
            klicThemes = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            projectLayerVisibility = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }

        if (camera is JsonElement cameraElement)
        {
            if (cameraElement.TryGetProperty("center", out var center) &&
                center.ValueKind == JsonValueKind.Array &&
                center.GetArrayLength() >= 2 &&
                center[0].TryGetDouble(out var lon) &&
                center[1].TryGetDouble(out var lat))
            {
                centerLon = lon;
                centerLat = lat;
            }

            if (cameraElement.TryGetProperty("zoom", out var zoomElement) && zoomElement.TryGetDouble(out var zoomValue))
            {
                zoom = Math.Clamp(zoomValue, 5, 22);
            }
        }

        return new ReportMapState(baseLayer, overlays, klicThemes, projectLayerVisibility, centerLon, centerLat, zoom, mapScale);
    }

    private static string NormalizeStepThreeReportBaseLayer(string baseLayer)
    {
        return baseLayer switch
        {
            "pdok-brt" or "pdok-gray" or "pdok-pastel" or "pdok-bgt-pastel" or "pdok-aerial" or "osm" => baseLayer,
            _ => "pdok-brt"
        };
    }

    private static Dictionary<string, bool> NormalizeStepThreeReportOverlays(IReadOnlyDictionary<string, bool> source)
    {
        bool Visible(string key, bool defaultValue) =>
            source.TryGetValue(key, out var value) ? value : defaultValue;

        return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["baseMap"] = true,
            ["parcels"] = Visible("parcels", false),
            ["boreTrace"] = Visible("boreTrace", true),
            ["boreTraceNumbers"] = Visible("boreTraceNumbers", true),
            ["boreTraceLengths"] = Visible("boreTraceLengths", true),
            ["boreTraceInfo"] = Visible("boreTraceInfo", true)
        };
    }

    private static Dictionary<string, bool> ReadBooleanMap(JsonElement root, string propertyName, IReadOnlyDictionary<string, bool> fallback)
    {
        var result = new Dictionary<string, bool>(fallback, StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty(propertyName, out var source) || source.ValueKind != JsonValueKind.Object) return result;

        foreach (var property in source.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                result[property.Name] = property.Value.GetBoolean();
            }
        }

        return result;
    }

    private static bool IsLayerVisibleInReportMapState(ProjectMapLayer layer, ReportMapState state)
    {
        if (state.ProjectLayerVisibility.TryGetValue(layer.Id, out var visible) && !visible) return false;
        if (IsBgtLayer(layer)) return IsReportMapOverlayVisible(state, "bgt", defaultVisible: true);
        if (IsBagOrKadasterLayer(layer)) return IsReportMapOverlayVisible(state, "bagImport", defaultVisible: true);
        if (IsKlicLayer(layer))
        {
            if (!IsReportMapOverlayVisible(state, "klic", defaultVisible: true)) return false;
            return state.KlicThemes.Count == 0 || state.KlicThemes.Values.Any(value => value);
        }
        if (layer.Type.Contains("custom", StringComparison.OrdinalIgnoreCase)) return IsReportMapOverlayVisible(state, "customImport", defaultVisible: true);
        return IsReportMapOverlayVisible(state, "designImport", defaultVisible: true);
    }

    private static bool IsReportMapOverlayVisible(ReportMapState state, string key, bool defaultVisible) =>
        state.Overlays.TryGetValue(key, out var visible) ? visible : defaultVisible;

    private static string DescribeMapBaseLayer(string baseLayer) =>
        baseLayer switch
        {
            "pdok-brt" => "PDOK BRT standaard",
            "pdok-gray" => "PDOK BRT grijs",
            "pdok-pastel" => "PDOK BRT pastel",
            "pdok-bgt-pastel" => "PDOK BGT standaardvisualisatie",
            "pdok-aerial" => "PDOK luchtfoto",
            "osm" => "OpenStreetMap",
            "" => "-",
            _ => baseLayer
        };


    private static bool TryGetSubstepElement(JsonElement root, string number, out JsonElement substepElement)
    {
        substepElement = default;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("substeps", out var substeps) ||
            substeps.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var substep in substeps.EnumerateArray())
        {
            if (string.Equals(JsonText(substep, "number", ""), number, StringComparison.OrdinalIgnoreCase))
            {
                substepElement = substep.Clone();
                return true;
            }
        }

        return false;
    }

    private static string JoinJsonStringArray(JsonElement element, string propertyName, string fallback)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var array) ||
            array.ValueKind != JsonValueKind.Array)
        {
            return fallback;
        }

        var values = array.EnumerateArray()
            .Select(item => JsonText(item, ""))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
        return values.Count == 0 ? fallback : string.Join(", ", values);
    }

    private void RenderSubstepIntroductionEditor()
    {
        if (_selectedProject is null ||
            _selectedStep is null ||
            _selectedSubstep is null ||
            _selectedStep.Number == 0 ||
            !IsChapterOpeningSubstep(_selectedStep.Number, _selectedSubstep) ||
            _selectedReportPreviewStepNumber is not null)
        {
            SubstepIntroductionEditorPanel.Visibility = Visibility.Collapsed;
            SidebarIntroductionTab.Visibility = Visibility.Collapsed;
            return;
        }

        SaveStepReportDataForStep(_selectedStep.Number);
        TryReadStepReportRoot(_selectedStep.Number, out var root);
        SubstepIntroductionEditorPanel.Visibility = Visibility.Visible;
        SidebarIntroductionTab.Visibility = Visibility.Collapsed;
        SubstepIntroductionTitle.Text = "Tekstvelden haalbaarheidsanalyse";
        RenderSubstepTextSectionInputs(GetSubstepTextSections(_selectedStep.Number, _selectedSubstep, root));
    }

    private void RenderSubstepTextSectionInputs(IReadOnlyDictionary<string, string> sections)
    {
        _substepTextSectionInputs.Clear();
        SubstepIntroductionFieldsPanel.Children.Clear();

        foreach (var definition in SubstepTextSectionDefinitions)
        {
            var value = sections.TryGetValue(definition.Key, out var text) ? text : "";
            SubstepIntroductionFieldsPanel.Children.Add(CreateSubstepTextSectionEditor(definition, value));
        }
    }

    private UIElement CreateSubstepTextSectionEditor(SubstepTextSectionDefinition definition, string text)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        panel.Children.Add(new TextBlock
        {
            Text = definition.Title,
            Foreground = Brush("#334155"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 3)
        });

        var editorGrid = new Grid();
        var textBox = new TextBox
        {
            Text = text ?? "",
            MinHeight = definition.Key == "introduction" ? 96 : 76,
            MaxHeight = 190,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderBrush = Brush("#D7E8FA"),
            Background = Brushes.White,
            Padding = new Thickness(8),
            FontSize = 12
        };

        var placeholder = new TextBlock
        {
            Text = "Leeg veld: wordt niet getoond in de rapportage.",
            Foreground = Brush("#8FA6B2"),
            FontStyle = FontStyles.Italic,
            FontSize = 11,
            Margin = new Thickness(12, 9, 8, 0),
            IsHitTestVisible = false
        };

        void SyncPlaceholder()
        {
            placeholder.Visibility = string.IsNullOrWhiteSpace(textBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        textBox.TextChanged += (_, _) => SyncPlaceholder();
        editorGrid.Children.Add(textBox);
        editorGrid.Children.Add(placeholder);
        SyncPlaceholder();

        _substepTextSectionInputs[definition.Key] = textBox;
        panel.Children.Add(editorGrid);
        return panel;
    }

    private bool HasSubstepIntroductionSidebar()
    {
        return _selectedProject is not null &&
               _selectedStep is not null &&
               _selectedSubstep is not null &&
               _selectedStep.Number != 0 &&
               _selectedReportPreviewStepNumber is null;
    }

    private bool ShouldShowSidebarProjectInformation()
    {
        return _selectedProject is not null &&
               _selectedStep?.Number == 1 &&
               string.Equals(_selectedSubstep?.Number, "1.1", StringComparison.OrdinalIgnoreCase) &&
               _selectedReportPreviewStepNumber is null;
    }

    private void GenerateSubstepIntroduction_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedProject is null || _selectedStep is null || _selectedSubstep is null)
        {
            OutputText.Text = "Geen substap geselecteerd.";
            return;
        }

        SaveStepReportDataForStep(_selectedStep.Number);
        TryReadStepReportRoot(_selectedStep.Number, out var root);
        RenderSubstepTextSectionInputs(GenerateSubstepTextSections(_selectedStep.Number, _selectedSubstep, root));
        OutputText.Text = $"Tekstvelden voor {DisplayReportSectionTitle(_selectedSubstep)} opnieuw gevuld. Pas de tekst eventueel aan en klik op Opslaan.";
    }

    private void SaveSubstepIntroduction_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedProject is null || _selectedStep is null || _selectedSubstep is null)
        {
            OutputText.Text = "Geen substap geselecteerd.";
            return;
        }

        var sections = SubstepTextSectionDefinitions.ToDictionary(
            definition => definition.Key,
            definition => _substepTextSectionInputs.TryGetValue(definition.Key, out var input) ? input.Text.Trim() : "",
            StringComparer.OrdinalIgnoreCase);
        SaveSubstepTextSections(_selectedStep.Number, _selectedSubstep.Number, sections);
        RefreshInlineReportPreviewIfVisible();
        OutputText.Text = $"Tekstvelden voor {DisplayReportSectionTitle(_selectedSubstep)} opgeslagen.";
    }

    private string GetSubstepIntroductionText(int stepNumber, PrescanSubstep substep, JsonElement root)
    {
        var sections = GetSubstepTextSections(stepNumber, substep, root);
        return string.Join("\n\n", SubstepTextSectionDefinitions
            .Select(definition => sections.TryGetValue(definition.Key, out var text) ? text : "")
            .Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    private Dictionary<string, string> GetSubstepTextSections(int stepNumber, PrescanSubstep substep, JsonElement root)
    {
        var stored = ReadSubstepTextSectionState(stepNumber, substep.Number);
        if (stored.HasStoredSections)
        {
            return NormalizeSubstepTextSections(stored.Sections);
        }

        var generated = GenerateSubstepTextSections(stepNumber, substep, root);
        var legacyIntroduction = ReadSubstepIntroduction(stepNumber, substep.Number);
        if (!string.IsNullOrWhiteSpace(legacyIntroduction) && !ShouldRegenerateStoredSubstepIntroduction(stepNumber, substep, legacyIntroduction))
        {
            generated["introduction"] = legacyIntroduction;
        }

        return generated;
    }

    private Dictionary<string, string> GenerateSubstepTextSections(int stepNumber, PrescanSubstep substep, JsonElement root)
    {
        var status = "nog niet opgeslagen";
        var dataSummary = "Er is nog geen aanvullende rapportdata opgeslagen voor deze substap.";
        var data = default(JsonElement);

        if (TryGetSubstepElement(root, substep.Number, out var substepElement))
        {
            status = JsonText(substepElement, "status", status);
            data = JsonProperty(substepElement, "data") ?? default;
            dataSummary = BuildSubstepDataSummary(data);
        }

        var introduction = GenerateSubstepIntroductionText(stepNumber, substep, root);
        var sourceText = BuildSubstepSourceSectionText(stepNumber, substep, data, dataSummary);
        var findingsText = BuildSubstepFindingsSectionText(stepNumber, substep, data);
        var riskText = BuildSubstepRiskSectionText(stepNumber, substep, data, status);
        var adviceText = BuildSubstepAdviceSectionText(stepNumber, substep, data);
        var conclusionText = BuildSubstepConclusionSectionText(stepNumber, substep, data, status);

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["introduction"] = introduction,
            ["assumptions"] = sourceText,
            ["findings"] = findingsText,
            ["risks"] = riskText,
            ["advice"] = adviceText,
            ["conclusion"] = conclusionText,
            ["openPoints"] = ""
        };
    }

    private string BuildSubstepSourceSectionText(int stepNumber, PrescanSubstep substep, JsonElement data, string dataSummary)
    {
        var sources = ReadSubstepSourceLabels(data);
        var sourceText = sources.Count == 0
            ? "De beoordeling gebruikt de beschikbare projectdata en de opgeslagen rapportdata van deze substap."
            : $"Gebruikte bronnen: {string.Join(", ", sources)}.";
        return $"{sourceText} {dataSummary}";
    }

    private string BuildSubstepFindingsSectionText(int stepNumber, PrescanSubstep substep, JsonElement data)
    {
        if (stepNumber == 6)
        {
            var modelType = SubsurfaceModelTypeForSubstep(substep.Number);
            if (modelType is not null && IsBroWmsMapLayer(modelType))
            {
                return BuildBroWmsTraceFindingIntroduction(data, modelType);
            }
        }

        var traceLength = JsonDouble(data, "traceLength", _selectedProject?.BoreLengthMeters ?? 0);
        var traceText = traceLength > 0
            ? $"over circa {traceLength:N1} m boorlijn"
            : "langs de ingetekende boorlijn";
        return $"De beschikbare gegevens zijn beoordeeld in relatie tot de boorlijn {traceText}. Controleer de kaartbeelden, tabellen en opgeslagen rapportdata in deze substap voor de projectspecifieke bevindingen.";
    }

    private string BuildSubstepRiskSectionText(int stepNumber, PrescanSubstep substep, JsonElement data, string status)
    {
        var statusText = string.IsNullOrWhiteSpace(status) ? "nog niet volledig beoordeeld" : status;
        return $"Aandachtspunt voor deze haalbaarheidsfase is dat de gegevens uit deze substap indicatief blijven tot de brondata, kaartuitsneden en eventuele veldinformatie definitief zijn gecontroleerd. Huidige status: {statusText}.";
    }

    private string BuildSubstepAdviceSectionText(int stepNumber, PrescanSubstep substep, JsonElement data)
    {
        return "Gebruik deze substap als onderbouwing voor de haalbaarheidsbeoordeling. Verwerk afwijkingen, kruisingen, onzekerheden of bronbeperkingen als restpunt voordat het ontwerp of de uitvoering definitief wordt vrijgegeven.";
    }

    private string BuildSubstepConclusionSectionText(int stepNumber, PrescanSubstep substep, JsonElement data, string status)
    {
        if (string.IsNullOrWhiteSpace(status) || status.Contains("geen", StringComparison.OrdinalIgnoreCase) || status.Contains("nodig", StringComparison.OrdinalIgnoreCase))
        {
            return "Voor dit onderdeel is aanvullende controle nodig voordat het als definitieve onderbouwing in de haalbaarheidsanalyse kan worden gebruikt.";
        }

        return "Op basis van de huidige projectdata is dit onderdeel bruikbaar als input voor de haalbaarheidsanalyse. Definitieve beoordeling blijft afhankelijk van controle van de actuele brongegevens en projectspecifieke randvoorwaarden.";
    }

    private static IReadOnlyList<string> ReadSubstepSourceLabels(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object) return [];

        var labels = new List<string>();
        foreach (var property in data.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
            if (property.Name.Contains("source", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("bron", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("layer", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("file", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("bestand", StringComparison.OrdinalIgnoreCase))
            {
                var label = HumanizeReportDataKey(property.Name);
                if (!labels.Contains(label, StringComparer.OrdinalIgnoreCase))
                {
                    labels.Add(label);
                }
            }
        }

        return labels.Take(6).ToList();
    }

    private static Dictionary<string, string> NormalizeSubstepTextSections(IReadOnlyDictionary<string, string> sections)
    {
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in SubstepTextSectionDefinitions)
        {
            normalized[definition.Key] = sections.TryGetValue(definition.Key, out var text) ? text ?? "" : "";
        }

        return normalized;
    }

    private string GenerateSubstepIntroductionText(int stepNumber, PrescanSubstep substep, JsonElement root)
    {
        var projectName = _selectedProject?.Name ?? "het project";
        if (IsChapterIntroductionSubstep(substep))
        {
            var chapterTitle = GetReportStepTitle(stepNumber);
            return $"Dit hoofdstuk beschrijft {DisplayStepNumber(stepNumber)} {chapterTitle.ToLower(CultureInfo.CurrentCulture)} voor project '{projectName}'. {substep.Description}\n\n"
                + "De onderdelen in dit hoofdstuk zijn bedoeld als onderbouwing van de haalbaarheidsanalyse. Vul de tekst aan met projectspecifieke bevindingen, aandachtspunten en conclusies voordat het eindrapport definitief wordt gemaakt.";
        }

        var status = "nog niet opgeslagen";
        var dataSummary = "Er is nog geen aanvullende rapportdata opgeslagen voor deze substap.";
        var data = default(JsonElement);

        if (TryGetSubstepElement(root, substep.Number, out var substepElement))
        {
            status = JsonText(substepElement, "status", status);
            data = JsonProperty(substepElement, "data") ?? default;
            dataSummary = BuildSubstepDataSummary(data);
        }

        if (TryBuildStepSixWmsIntroduction(stepNumber, substep, data, status, out var stepSixIntroduction))
        {
            return stepSixIntroduction;
        }

        var introduction = $"Deze substap beschrijft {DisplayReportSectionTitle(substep).ToLower(CultureInfo.CurrentCulture)} voor project '{projectName}'. {substep.Description}\n\n"
            + $"Status van dit rapportonderdeel: {status}. {dataSummary}";
        return introduction;
    }

    private static bool ShouldRegenerateStoredSubstepIntroduction(int stepNumber, PrescanSubstep substep, string stored)
    {
        var normalized = Regex.Replace(stored.Trim(), @"\s+", " ");
        if (normalized.Contains("De gekoppelde documentatie", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (stepNumber != 6) return false;
        var modelType = SubsurfaceModelTypeForSubstep(substep.Number);
        if (modelType is null || !IsBroWmsMapLayer(modelType)) return false;

        if (normalized.StartsWith("Deze substap beschrijft ", StringComparison.OrdinalIgnoreCase) &&
            (normalized.Contains("Status van dit rapportonderdeel:", StringComparison.OrdinalIgnoreCase) ||
             normalized.Contains("Belangrijkste rapportdata:", StringComparison.OrdinalIgnoreCase) ||
             normalized.Contains("Er is nog geen aanvullende rapportdata opgeslagen", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return (normalized.StartsWith("Deze stap ", StringComparison.OrdinalIgnoreCase) &&
             normalized.Contains("Rapportstatus:", StringComparison.OrdinalIgnoreCase) &&
             normalized.Contains("Bron:", StringComparison.OrdinalIgnoreCase) &&
             !normalized.Contains("Aangetroffen", StringComparison.OrdinalIgnoreCase));
    }

    private bool TryBuildStepSixWmsIntroduction(int stepNumber, PrescanSubstep substep, JsonElement data, string status, out string introduction)
    {
        introduction = "";
        if (stepNumber != 6) return false;

        var modelType = SubsurfaceModelTypeForSubstep(substep.Number);
        if (modelType is null || !IsBroWmsMapLayer(modelType)) return false;

        var projectName = _selectedProject?.Name ?? "het project";
        var traceLength = JsonDouble(data, "traceLength", _selectedProject?.BoreLengthMeters ?? 0);
        var hasMap = JsonBool(data, "mapStateAvailable") || !string.IsNullOrWhiteSpace(JsonText(data, "wmsOverlayKey", ""));
        var mapStatus = hasMap
            ? "De kaartlaag is in de GIS-kaart beschikbaar en wordt samen met de boorlijn als rapportkaart vastgelegd."
            : "Zet de kaartlaag aan en sla de kaart opnieuw op als dit onderdeel definitief in het rapport moet komen.";
        var traceText = traceLength > 0
            ? $"over het HDD-trace van circa {traceLength:N1} m"
            : "over het ingetekende HDD-trace";
        var traceFindingText = BuildBroWmsTraceFindingIntroduction(data, modelType);

        introduction = NormalizeBroModelType(modelType) switch
        {
            BroGeomorphologyModelType =>
                $"Deze stap beoordeelt de geomorfologische context {traceText} voor project '{projectName}'. De kaart komt uit BRO Geomorfologie 2025-01 en wordt via DINOloket/PDOK WMS over de GIS-kaart gelegd. Deze bron beschrijft landschapsvormen en geomorfologische eenheden aan maaiveld, zoals ruggen, laagten, dijken, watergerelateerde vormen en gebieden met geomorfologisch belang.\n\n" +
                $"Voor een gestuurde boring is dit vooral relevant bij intrede, uittrede en kleur- of vlakovergangen langs de boorlijn. {traceFindingText} Zulke zones kunnen invloed hebben op bereikbaarheid, herstel van maaiveld, lokale bodemopbouw en de aandachtspunten voor uitvoering. {mapStatus}\n\n" +
                "De legenda in de rapportage hoort bij de actieve geomorfologische kaartlaag. Omdat de officiele legenda lang kan zijn, toont het rapport een compacte legenda met de kaartklassen die voor de rapportuitsnede bruikbaar zijn.",

            BroSoilMapModelType =>
                $"Deze stap gebruikt de BRO Bodemkaart 2025-01 voor de bodemkundige beoordeling {traceText} voor project '{projectName}'. De bodemkaart wordt via DINOloket/PDOK WMS opgehaald en toont bodemkundige eenheden in de bovengrond en eventuele vlakken van bodemkundig belang.\n\n" +
                $"De bodemkaart helpt om de verwachte maaiveldbodem, vochtgevoeligheid en overgangszones rond de boorlijn te herkennen. {traceFindingText} Overgangen tussen bodemtypen kunnen aanleiding geven tot extra aandacht voor werkterreininrichting, sleuf- of kuilgedrag, boorvloeistofbeheersing en herstel na uitvoering. " +
                $"{mapStatus}\n\n" +
                "De legenda is gekoppeld aan de actieve bodemkaartlaag. Lange legenda's worden in het rapport compact gehouden, zodat de kaart en de verklaring samen leesbaar op A4 blijven.",

            BroGroundwaterGhgModelType =>
                $"Deze stap beoordeelt de GHG, de gemiddeld kleinste grondwaterspiegeldiepte, {traceText} voor project '{projectName}'. De kaart komt uit BRO Grondwaterspiegeldiepte 2025-01 via DINOloket/PDOK WMS. GHG geeft de relatief hoogste grondwaterstand weer: hoe kleiner de diepteklasse, hoe dichter het grondwater gemiddeld bij maaiveld kan staan.\n\n" +
                $"Voor HDD is dit relevant voor intrede- en uittredekuilen, tijdelijke bemaling, natte werkcondities en het risico op instromend grondwater. {traceFindingText} Vooral ondiepe klassen en overgangen langs de boorlijn verdienen aandacht. " +
                $"{mapStatus}\n\n" +
                "De legenda in het rapport geeft de diepteklassen van de actieve GHG-kaart weer en moet samen met de kaart worden gelezen.",

            BroGroundwaterGlgModelType =>
                $"Deze stap beoordeelt de GLG, de gemiddeld grootste grondwaterspiegeldiepte, {traceText} voor project '{projectName}'. De gegevens komen uit BRO Grondwaterspiegeldiepte 2025-01 via DINOloket/PDOK WMS. GLG beschrijft de relatief lage grondwaterstand en geeft daarmee inzicht in de drogere kant van het grondwaterregime.\n\n" +
                $"De GLG-kaart helpt om de bandbreedte tussen natte en droge perioden beter te begrijpen. {traceFindingText} In combinatie met GHG en GVG kan dit wijzen op seizoensdynamiek of juist structureel natte omstandigheden. " +
                $"{mapStatus}\n\n" +
                "De legenda toont de GLG-diepteklassen uit de actieve BRO/PDOK kaartlaag.",

            BroGroundwaterGvgModelType =>
                $"Deze stap beoordeelt de GVG, de gemiddelde voorjaarsgrondwaterspiegeldiepte, {traceText} voor project '{projectName}'. De kaartlaag komt uit BRO Grondwaterspiegeldiepte 2025-01 en wordt via DINOloket/PDOK WMS in de GIS-kaart getoond.\n\n" +
                $"GVG is bruikbaar als representatieve indicatie van de grondwatersituatie in het voorjaar, een periode die vaak relevant is voor uitvoering en terreintoegang. {traceFindingText} Dat bepaalt of nader aandacht nodig is voor bemaling, kuilstabiliteit en werkbaarheid. " +
                $"{mapStatus}\n\n" +
                "De legenda in de rapportage geeft de GVG-diepteklassen weer zoals ze in de actieve kaartlaag worden gebruikt.",

            BroGroundwaterGtModelType =>
                $"Deze stap beoordeelt de grondwatertrappen {traceText} voor project '{projectName}'. De kaart komt uit BRO Grondwaterspiegeldiepte 2025-01 via DINOloket/PDOK WMS. Grondwatertrappen vatten het grondwaterregime samen in klassen op basis van hoge en lage grondwaterstanden.\n\n" +
                $"Voor een HDD-trace geeft deze kaart snel inzicht in structureel natte of juist drogere zones langs de boorlijn. {traceFindingText} Overgangen kunnen wijzen op wisselende ontwatering, bodemvocht en uitvoeringsrisico's. " +
                $"{mapStatus}\n\n" +
                "De legenda toont de officiele grondwatertrapklassen van de actieve kaartlaag.",

            BroGroundwaterDocumentationModelType =>
                $"Deze stap controleert de modeldocumentatie bij de BRO Grondwaterspiegeldiepte 2025-01 {traceText} voor project '{projectName}'. De kaartlaag wordt via DINOloket/PDOK WMS getoond en geeft aan welke documentatie- of kwaliteitsinformatie beschikbaar is rond de gebruikte grondwatermodellen.\n\n" +
                $"Deze laag is bedoeld als controle op de onderbouwing van de GHG-, GLG-, GVG- en grondwatertrappenkaarten. {traceFindingText} Wanneer de modeldocumentatie beperkt is, blijft aanvullende controle met lokale bronnen, metingen of terreinervaring verstandig. " +
                $"{mapStatus}\n\n" +
                "De legenda verklaart de documentatieklassen van de actieve kaartlaag en wordt in het rapport volledig of compact weergegeven, afhankelijk van de beschikbare ruimte.",

            _ => ""
        };

        if (string.IsNullOrWhiteSpace(introduction)) return false;

        introduction += $"\n\nRapportstatus: {status}. Bron: {DinoModelLabel(modelType)} via PDOK WMS ({BroWmsLayerDescription(modelType)}).";
        return true;
    }


    private static string CleanCustomerReportIntroduction(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        var normalized = Regex.Replace(text.Trim(), @"\r\n?", "\n");
        normalized = Regex.Replace(
            normalized,
            @"\s*De gebruikte brondata is:\s*[^.]+\.?",
            "",
            RegexOptions.IgnoreCase);
        normalized = Regex.Replace(
            normalized,
            @"\s*De rapportstatus voor dit onderdeel is:",
            "\n\nStatus van dit rapportonderdeel:",
            RegexOptions.IgnoreCase);
        normalized = Regex.Replace(
            normalized,
            @"\b(project_info|boring_config|map_state|boortrace_geojson|report_lock|report_snapshot|surface_analysis_generated|machine_placements|bro_dgm|bro_regis|bro_geomorfologie|bro_bodemkaart|bro_grondwaterspiegeldiepte)\b",
            match => HumanizeReportDataKey(match.Value),
            RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");
        normalized = Regex.Replace(normalized, @"[ \t]{2,}", " ");
        return normalized.Trim();
    }

    private static string CustomerReportQualityLabel(ReportQualitySummary quality)
    {
        if (quality.IsReady) return "compleet";
        return quality.HighIssues > 0
            ? "kritisch aandachtspunt"
            : "aandachtspunt voor afronding";
    }

    private static string BuildSubstepDataSummary(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object)
        {
            return "Er is nog geen gestructureerde inhoud beschikbaar.";
        }

        var parts = new List<string>();
        foreach (var property in data.EnumerateObject())
        {
            var label = HumanizeReportDataKey(property.Name);
            switch (property.Value.ValueKind)
            {
                case JsonValueKind.Array:
                    var count = property.Value.GetArrayLength();
                    if (count > 0) parts.Add($"{label}: {count}");
                    break;
                case JsonValueKind.Number:
                    if (property.Value.TryGetDouble(out var number) && double.IsFinite(number))
                    {
                        parts.Add($"{label}: {number:N1}");
                    }
                    break;
                case JsonValueKind.String:
                    var text = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(text) && text.Length <= 80)
                    {
                        parts.Add($"{label}: {text}");
                    }
                    break;
                case JsonValueKind.True:
                case JsonValueKind.False:
                    parts.Add($"{label}: {(property.Value.GetBoolean() ? "ja" : "nee")}");
                    break;
            }

            if (parts.Count >= 5) break;
        }

        return parts.Count == 0
            ? "De inhoud bestaat uit kaart- of analysegegevens die in de rapporttabellen en afbeeldingen hieronder worden uitgewerkt."
            : $"Belangrijkste rapportdata: {string.Join("; ", parts)}.";
    }


    private string ReadSubstepIntroduction(int stepNumber, string substepNumber)
    {
        var introductions = ReadSubstepIntroductions(stepNumber);
        return introductions.TryGetValue(substepNumber, out var text) ? text : "";
    }

    private Dictionary<string, string> ReadSubstepIntroductions(int stepNumber)
    {
        var introductions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (_selectedProject is null) return introductions;

        var json = _projects.GetStepData(_selectedProject.Id, stepNumber, StepReportIntroductionDataKey);
        if (string.IsNullOrWhiteSpace(json)) return introductions;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.TryGetProperty("introductions", out var entries) && entries.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in entries.EnumerateObject())
                {
                    if (entry.Value.ValueKind == JsonValueKind.String)
                    {
                        introductions[entry.Name] = entry.Value.GetString() ?? "";
                    }
                }
            }
        }
        catch (System.Exception swallowedException)
        {
            // Onleesbare oude data negeren; de generator vult dan opnieuw.
            AppLog.Swallowed(swallowedException);
        }

        return introductions;
    }

    private Dictionary<string, Dictionary<string, string>> ReadSubstepTextSectionsBySubstep(int stepNumber)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        if (_selectedProject is null) return result;

        var json = _projects.GetStepData(_selectedProject.Id, stepNumber, StepReportIntroductionDataKey);
        if (string.IsNullOrWhiteSpace(json)) return result;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("sections", out var sectionsRoot) || sectionsRoot.ValueKind != JsonValueKind.Object)
            {
                return result;
            }

            foreach (var substepEntry in sectionsRoot.EnumerateObject())
            {
                if (substepEntry.Value.ValueKind != JsonValueKind.Object) continue;

                var sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var sectionEntry in substepEntry.Value.EnumerateObject())
                {
                    if (sectionEntry.Value.ValueKind == JsonValueKind.String)
                    {
                        sections[sectionEntry.Name] = sectionEntry.Value.GetString() ?? "";
                    }
                }

                result[substepEntry.Name] = sections;
            }
        }
        catch (System.Exception swallowedException)
        {
            // Onleesbare oude data negeren; de generator vult dan opnieuw.
            AppLog.Swallowed(swallowedException);
        }

        return result;
    }

    private (bool HasStoredSections, Dictionary<string, string> Sections) ReadSubstepTextSectionState(int stepNumber, string substepNumber)
    {
        var allSections = ReadSubstepTextSectionsBySubstep(stepNumber);
        if (allSections.TryGetValue(substepNumber, out var sections))
        {
            return (true, sections);
        }

        return (false, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private void SaveSubstepTextSections(int stepNumber, string substepNumber, IReadOnlyDictionary<string, string> sections)
    {
        if (_selectedProject is null) return;

        var introductions = ReadSubstepIntroductions(stepNumber);
        var allSections = ReadSubstepTextSectionsBySubstep(stepNumber);
        var normalized = NormalizeSubstepTextSections(sections);
        allSections[substepNumber] = normalized;

        var introductionText = normalized.TryGetValue("introduction", out var text) ? text : "";
        if (string.IsNullOrWhiteSpace(introductionText))
        {
            introductions.Remove(substepNumber);
        }
        else
        {
            introductions[substepNumber] = introductionText;
        }

        var payload = new
        {
            schema = "borevexa.step-report-introductions.v2",
            updatedAt = DateTimeOffset.Now,
            introductions,
            sections = allSections
        };
        SaveSelectedProjectStepData(stepNumber, StepReportIntroductionDataKey, JsonSerializer.Serialize(payload, JsonOptions));
    }

    private void RefreshWorkflowReportStatus(int currentStepNumber)
    {
        if (_selectedProject is null)
        {
            _lastWorkflowReportStatusSignature = null;
            WorkflowStepDataStatusText.Text = "Geen project actief";
            WorkflowReportDataStatusText.Text = "-";
            WorkflowMapStatusText.Text = "-";
            WorkflowReportGeneratedText.Text = "-";
            WorkflowReportQualityText.Text = "-";
            WorkflowReportQualityActionsText.Text = "";
            SidebarStepDataStatusText.Text = "Geen project actief";
            SidebarReportDataStatusText.Text = "-";
            SidebarMapStatusText.Text = "-";
            SidebarReportGeneratedText.Text = "-";
            SidebarReportQualityText.Text = "-";
            SidebarReportQualityActionsText.Text = "";
            UpdateSidebarProjectInformation();
            WorkflowSubstepsPanel.Children.Clear();
            WorkflowPartsPanel.Children.Clear();
            WorkflowPartsHost.Visibility = Visibility.Collapsed;
            return;
        }

        var signature = BuildWorkflowReportStatusSignature(currentStepNumber);
        if (string.Equals(_lastWorkflowReportStatusSignature, signature, StringComparison.Ordinal))
        {
            return;
        }

        _lastWorkflowReportStatusSignature = signature;
        var stepDataStatus = BuildStepDataStatusText(currentStepNumber);
        var reportDataStatus = BuildReportDataStatusText();
        var mapStatus = BuildMapReportStatusText(currentStepNumber);
        var generatedStatus = BuildLastReportGeneratedText();
        WorkflowStepDataStatusText.Text = stepDataStatus;
        WorkflowReportDataStatusText.Text = reportDataStatus;
        WorkflowMapStatusText.Text = mapStatus;
        WorkflowReportGeneratedText.Text = generatedStatus;
        SidebarStepDataStatusText.Text = stepDataStatus;
        SidebarReportDataStatusText.Text = reportDataStatus;
        SidebarMapStatusText.Text = mapStatus;
        SidebarReportGeneratedText.Text = generatedStatus;
        UpdateSidebarProjectInformation();
        UpdateWorkflowReportQualityStatus(currentStepNumber);
        RenderWorkflowSubsteps(currentStepNumber);
    }

    private void UpdateSidebarProjectInformation()
    {
        if (_selectedProject is null)
        {
            SidebarProjectNameText.Text = "-";
            SidebarProjectDateText.Text = "-";
            SidebarProjectInternalText.Text = "-";
            SidebarProjectExternalText.Text = "-";
            SidebarProjectClientText.Text = "-";
            SidebarProjectLocationText.Text = "-";
            SidebarProjectBoreLengthText.Text = "-";
            SidebarProjectDiameterText.Text = "-";
            SidebarProjectStatusText.Text = "-";
            return;
        }

        var metadata = ReadProjectHeaderMetadata();
        SidebarProjectNameText.Text = FirstNonEmpty(_selectedProject.Name, "-");
        SidebarProjectDateText.Text = FirstNonEmpty(metadata.ReportDate, "-");
        SidebarProjectInternalText.Text = FirstNonEmpty(metadata.InternalProjectNumber, "-");
        SidebarProjectExternalText.Text = FirstNonEmpty(metadata.ExternalProjectNumber, "-");
        SidebarProjectClientText.Text = FirstNonEmpty(_selectedProject.Client, "-");
        SidebarProjectLocationText.Text = FirstNonEmpty(_selectedProject.Location, "-");
        SidebarProjectBoreLengthText.Text = $"{_selectedProject.BoreLengthMeters:N1} m";
        SidebarProjectDiameterText.Text = _selectedProject.DiameterMillimeters > 0 ? $"Ø{_selectedProject.DiameterMillimeters} mm" : "-";
        SidebarProjectStatusText.Text = FirstNonEmpty(_selectedProject.Status, "-");
    }

    private string BuildWorkflowReportStatusSignature(int currentStepNumber)
    {
        return string.Join(";",
            _selectedProject?.Id.ToString("N") ?? "geen-project",
            currentStepNumber.ToString(CultureInfo.InvariantCulture),
            _selectedSubstep?.Number ?? "geen-substap",
            _reportUiDataVersion.ToString(CultureInfo.InvariantCulture),
            _projectFiles.Count.ToString(CultureInfo.InvariantCulture),
            _selectedMachineId ?? "",
            _profilePoints.Count.ToString(CultureInfo.InvariantCulture),
            _currentBoreTracePoints.Count.ToString(CultureInfo.InvariantCulture),
            _selectedBroModelType);
    }

    private void UpdateWorkflowReportQualityStatus(int currentStepNumber)
    {
        if (_selectedProject is null)
        {
            WorkflowReportQualityText.Text = "-";
            WorkflowReportQualityActionsText.Text = "";
            SidebarReportQualityText.Text = "-";
            SidebarReportQualityActionsText.Text = "";
            return;
        }

        var summary = _selectedSubstep is null
            ? _reportQuality.EvaluateStep(_selectedProject.Id, currentStepNumber)
            : _reportQuality.EvaluateSubstep(_selectedProject.Id, currentStepNumber, _selectedSubstep.Number);

        var headline = BuildReportQualityHeadline(summary);
        var actions = BuildReportQualityActions(summary);
        WorkflowReportQualityText.Text = headline;
        WorkflowReportQualityText.Foreground = Brush(summary.StatusColor);
        WorkflowReportQualityActionsText.Text = actions;
        SidebarReportQualityText.Text = headline;
        SidebarReportQualityText.Foreground = Brush(summary.StatusColor);
        SidebarReportQualityActionsText.Text = actions;
    }

    private static string BuildReportQualityHeadline(ReportQualitySummary summary)
    {
        if (summary.TotalIssues == 0)
        {
            return summary.StatusLabel;
        }

        return $"{summary.StatusLabel}: {summary.TotalIssues} punt(en) - {summary.HighIssues} hoog, {summary.MediumIssues} middel, {summary.LowIssues} laag";
    }

    private static string BuildReportQualityActions(ReportQualitySummary summary)
    {
        if (summary.TotalIssues == 0)
        {
            return "Alle verplichte bronnen voor dit rapportonderdeel zijn aanwezig.";
        }

        var lines = summary.Issues
            .Take(3)
            .Select(issue => $"{DisplayIssueNumber(issue)}: {issue.Action}");
        var suffix = summary.TotalIssues > 3 ? $" Nog {summary.TotalIssues - 3} extra punt(en)." : "";
        return string.Join("  ", lines) + suffix;
    }

    private static string BuildReportQualityTooltip(ReportQualitySummary summary)
    {
        if (summary.TotalIssues == 0)
        {
            return "Rapportklaar: alle verplichte bronnen zijn aanwezig.";
        }

        return $"{BuildReportQualityHeadline(summary)}\n{BuildReportQualityActions(summary)}";
    }

    private static Brush ReportStatusBackground(ReportQualitySummary summary) => summary.Status switch
    {
        ReportReadinessStatus.Ready => Brush("#E6F5ED"),
        ReportReadinessStatus.NeedsReview => Brush("#FFF7E6"),
        ReportReadinessStatus.Incomplete => Brush("#FDEBE6"),
        ReportReadinessStatus.NotStarted => Brush("#F1F5F9"),
        _ => Brush("#EEF5FB")
    };

    private static Brush ReportStatusBorder(ReportQualitySummary summary) => summary.Status switch
    {
        ReportReadinessStatus.Ready => Brush("#BFE6CE"),
        ReportReadinessStatus.NeedsReview => Brush("#F6D28C"),
        ReportReadinessStatus.Incomplete => Brush("#D78C72"),
        ReportReadinessStatus.NotStarted => Brush("#CBD5E1"),
        _ => Brush("#BFD7F1")
    };

    private void RenderWorkflowSubsteps(int stepNumber)
    {
        WorkflowSubstepsPanel.Children.Clear();
        foreach (var substep in StepReportCatalog.GetSubsteps(stepNumber))
        {
            var selected = string.Equals(_selectedSubstep?.Number, substep.Number, StringComparison.OrdinalIgnoreCase);
            var quality = _selectedProject is null
                ? ReportQualitySummary.FromIssues([])
                : _reportQuality.EvaluateSubstep(_selectedProject.Id, stepNumber, substep.Number);
            var button = new Button
            {
                Content = DisplayReportSectionTitle(substep),
                Tag = substep.Number,
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 6, 4),
                FontSize = 11,
                FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
                Background = selected ? Brush("#3F4750") : ReportStatusBackground(quality),
                Foreground = selected ? Brushes.White : Brush(quality.StatusColor),
                BorderBrush = selected
                    ? Brush("#3F4750")
                    : ReportStatusBorder(quality),
                BorderThickness = new Thickness(quality.Status != ReportReadinessStatus.Ready && !selected ? 1.5 : 1),
                ToolTip = BuildReportQualityTooltip(quality)
            };
            button.Click += WorkflowSubstepButton_OnClick;
            WorkflowSubstepsPanel.Children.Add(button);
        }
    }

    private void RenderWorkflowParts(StepWorkspace workspace)
    {
        WorkflowPartsPanel.Children.Clear();

        var parts = BuildWorkflowParts(workspace);
        WorkflowPartsHost.Visibility = parts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var activeKey = parts.Any(part => string.Equals(part.Key, _activeWorkflowPartKey, StringComparison.OrdinalIgnoreCase))
            ? _activeWorkflowPartKey
            : parts.Count > 0 ? parts[0].Key : null;
        _activeWorkflowPartKey = activeKey;

        if (parts.Count > 1)
        {
            foreach (var part in parts)
            {
                var visibility = string.Equals(part.Key, activeKey, StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                foreach (var target in part.Targets)
                {
                    target.Visibility = visibility;
                }
            }
        }

        foreach (var part in parts)
        {
            var isActive = string.Equals(part.Key, activeKey, StringComparison.OrdinalIgnoreCase);
            var button = new Button
            {
                Content = part.Label,
                Tag = part.Key,
                Padding = new Thickness(9, 3, 9, 3),
                Margin = new Thickness(0, 0, 6, 4),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Background = isActive ? Brush("#3F4750") : Brushes.White,
                Foreground = isActive ? Brushes.White : Brush("#315B7E"),
                BorderBrush = isActive ? Brush("#3F4750") : Brush("#BFD7F1"),
                BorderThickness = new Thickness(1),
                ToolTip = $"Toon {part.Label}"
            };
            button.Click += WorkflowPartButton_OnClick;
            WorkflowPartsPanel.Children.Add(button);
        }
    }

    private IReadOnlyList<WorkflowPartItem> BuildWorkflowParts(StepWorkspace workspace)
    {
        var parts = new List<WorkflowPartItem>();

        void Add(string key, string label, params FrameworkElement[] targets)
        {
            if (targets.Length == 0) return;
            if (parts.Any(part => string.Equals(part.Key, key, StringComparison.OrdinalIgnoreCase))) return;
            parts.Add(new WorkflowPartItem(key, label, targets));
        }

        void AddIf(bool eligible, string key, string label, params FrameworkElement[] targets)
        {
            if (eligible) Add(key, label, targets);
        }

        if (_selectedReportPreviewStepNumber is not null)
        {
            Add("report-preview", "Rapportpreview", StepTenReportGrid);
            return parts;
        }

        if (IsChapterIntroductionSubstep(_selectedSubstep))
        {
            Add("inleiding", "Inleiding", SubstepIntroductionEditorPanel);
            return parts;
        }

        switch (workspace.StepNumber)
        {
            case 0:
                Add("rapportstart", "Rapportstart", StepZeroPanel);
                break;
            case 1:
                AddStepOneWorkflowParts(Add);
                break;
            case 2:
                Add("imports", "Imports", StepTwoPanel);
                break;
            case 4:
                // Exclusive tabs: GIS kaart / Oppervlakteprofiel. Switching away collapses
                // the map's WebView2, which can drop its WebGL context; NudgeMapRenderIfVisible
                // and RecoverMapRender repaint it when the kaart tab is shown again.
                Add("kaart", "GIS kaart", StepThreeMapFrame, StepThreeToolbarBar);
                AddIf(ShouldShowSurfaceAnalysisPanel(), "oppervlakte", "Oppervlakteprofiel", StepSurfaceAnalysisPanel);
                AddIf(MapWorkspaceGrid.Visibility != Visibility.Collapsed, "rapportblokken", "Rapportblokken", StepCardsPanel);
                break;
            case 7:
                Add("kaart", "GIS kaart", StepThreeToolbarBar, StepThreeMapFrame);
                Add("dwarsprofiel", "Dwarsprofiel", ProfileHeaderDock, ProfileCanvasViewport, ProfilePointsTitle, ProfilePointsScroll);
                Add("profielstaat", "Profielstaat", ProfileEngineeringBlock);
                break;
            case ReportStepNumber:
                Add("eindrapport", "Eindrapport", StepTenReportGrid);
                break;
            case WorkDrawingStepNumber:
                Add("werktekening", "Werktekening", StepElevenWorkDrawingGrid);
                break;
            default:
                if (workspace.StepNumber is >= 3 and <= ThreeDStepNumber)
                {
                    Add("kaart", "GIS kaart", StepThreePanel);
                }
                if (MapWorkspaceGrid.Visibility != Visibility.Collapsed)
                {
                    Add("rapportblokken", "Rapportblokken", StepCardsPanel);
                }
                break;
        }

        return parts;
    }


    private void WorkflowPartButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key } || _selectedStep is null) return;

        _activeWorkflowPartKey = key;
        RenderWorkflowParts(_workspaces[_selectedStep.Number]);
        if (StepThreePanel.Visibility == Visibility.Visible)
        {
            ApplyStepThreeLayoutBounds();
        }
        // Returning to a tab where the GIS map becomes visible again: it was collapsed
        // by the previous tab, so recover its (possibly lost) render surface.
        if (StepThreeMapFrame.Visibility == Visibility.Visible)
        {
            RecoverMapRender();
        }
    }

    private void ActivateWorkflowPart(int stepNumber, string key)
    {
        _activeWorkflowPartKey = key;
        if (_workspaces.TryGetValue(stepNumber, out var workspace))
        {
            RenderWorkflowParts(workspace);
        }
        if (StepThreePanel.Visibility == Visibility.Visible)
        {
            ApplyStepThreeLayoutBounds();
        }
        if (StepThreeMapFrame.Visibility == Visibility.Visible)
        {
            RecoverMapRender();
        }
    }

    private void WorkflowSubstepButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedStep is null || sender is not Button button) return;
        var substepNumber = button.Tag?.ToString();
        var item = FindStepNavigationItem(_selectedStep.Number, false, substepNumber);
        if (item is not null)
        {
            SideStepsList.SelectedItem = item;
            return;
        }

        _selectedSubstep = StepReportCatalog.GetSubsteps(_selectedStep.Number)
            .FirstOrDefault(substep => string.Equals(substep.Number, substepNumber, StringComparison.OrdinalIgnoreCase));
        RenderWorkspace();
    }

    private string BuildStepDataStatusText(int stepNumber)
    {
        var lastSave = ReadStepSavedAt(stepNumber);
        var completeness = GetStepCompletenessText(stepNumber);
        var reportData = _selectedProject is not null && !string.IsNullOrWhiteSpace(_projects.GetStepData(_selectedProject.Id, stepNumber, StepReportDataKey))
            ? "rapportdata gereed"
            : "rapportdata nog bijwerken";
        var substepStatus = BuildStepSubstepStatusText(stepNumber);
        return lastSave is null
            ? $"{completeness} · {substepStatus} · {reportData}"
            : $"{completeness} · {substepStatus} · {reportData} · opgeslagen {lastSave.Value:dd-MM HH:mm}";
    }

    private string BuildStepSubstepStatusText(int stepNumber)
    {
        var total = StepReportCatalog.GetSubsteps(stepNumber).Count;
        if (_selectedProject is null) return $"{total} substap(pen)";
        var json = _projects.GetStepData(_selectedProject.Id, stepNumber, StepReportDataKey);
        if (string.IsNullOrWhiteSpace(json)) return $"0/{total} substappen opgeslagen";
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("substeps", out var substeps) || substeps.ValueKind != JsonValueKind.Array)
            {
                return $"0/{total} substappen opgeslagen";
            }

            var saved = substeps.GetArrayLength();
            var ready = substeps.EnumerateArray().Count(substep =>
                substep.TryGetProperty("ready", out var readyProperty) &&
                readyProperty.ValueKind is JsonValueKind.True);
            return $"{ready}/{Math.Max(total, saved)} substappen gereed";
        }
        catch
        {
            return $"substappen opnieuw opslaan";
        }
    }


    private string BuildReportQualityStatusText()
    {
        if (_selectedProject is null) return "controle onbekend";
        var summary = _selectedStep is null
            ? _reportQuality.EvaluateProject(_selectedProject.Id, ReportContractCatalog.GetAll())
            : _reportQuality.EvaluateStep(_selectedProject.Id, _selectedStep.Number);
        return summary.TotalIssues == 0
            ? $"rapportcontrole {summary.StatusLabel.ToLowerInvariant()}"
            : $"rapportcontrole {summary.StatusLabel.ToLowerInvariant()} ({summary.HighIssues} hoog/{summary.MediumIssues} middel/{summary.LowIssues} laag)";
    }

    private string BuildMapReportStatusText(int stepNumber)
    {
        if (!IsMapWorkspaceStep(stepNumber)) return "Geen kaartstap";
        if (IsMapReportLocked(stepNumber)) return "Opgeslagen voor rapportage";
        var mapStateJson = GetCurrentMapStateJson(stepNumber);
        var runtime = GetCurrentMapWorkspaceRuntime(stepNumber, GetActiveMapReportVariantKey(stepNumber));
        if (_selectedProject is not null)
        {
            var imagePath = _reportPreview.GetLiveMapReportPreviewImagePath(_selectedProject.Id, stepNumber, _ => "", runtime.ScopedReportVariantKey);
            if (!string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
            {
                return "Live kaartcapture beschikbaar";
            }
        }

        return string.IsNullOrWhiteSpace(mapStateJson)
            ? "Nog geen kaartstatus opgeslagen"
            : "Kaartstatus opgeslagen, nog geen rapportcapture";
    }

    private string BuildLastReportGeneratedText()
    {
        if (_selectedProject is null) return "-";
        var snapshotJson = _projects.GetStepData(_selectedProject.Id, ReportStepNumber, ReportSnapshotDataKey);
        if (!string.IsNullOrWhiteSpace(snapshotJson))
        {
            try
            {
                using var document = JsonDocument.Parse(snapshotJson);
                if (document.RootElement.TryGetProperty("generatedAt", out var generatedAt) &&
                    generatedAt.ValueKind == JsonValueKind.String &&
                    DateTimeOffset.TryParse(generatedAt.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var generated))
                {
                    return $"{generated:dd-MM-yyyy HH:mm}";
                }
            }
            catch
            {
                return "Snapshot opnieuw genereren";
            }
        }

        var exportJson = _projects.GetStepData(_selectedProject.Id, ReportStepNumber, "eindrapport_export");
        return TryReadReportExportPath(exportJson, out var path)
            ? $"Export: {System.IO.Path.GetFileName(path)}"
            : "Nog niet gegenereerd";
    }

    private DateTimeOffset? ReadStepSavedAt(int stepNumber)
    {
        if (_selectedProject is null) return null;
        var json = _projects.GetStepData(_selectedProject.Id, stepNumber, "step_save");
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("savedAt", out var savedAt) &&
                savedAt.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(savedAt.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            {
                return parsed;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private string GetStepCompletenessText(int stepNumber)
    {
        if (_selectedProject is null) return "Geen project";
        return stepNumber switch
        {
            0 => "Rapportstart ingericht",
            1 => string.IsNullOrWhiteSpace(_selectedProject.Name) ? "Projectnaam ontbreekt" : "Projectinformatie beschikbaar",
            2 => _projectFiles.Count == 0 ? "Geen importbestanden" : $"{_projectFiles.Count} importbestand(en)",
            3 => GetTraceRowsForProfile().Count >= 2 ? "Boorlijn beschikbaar" : "Boorlijn ontbreekt",
            4 => !string.IsNullOrWhiteSpace(_projects.GetStepData(_selectedProject.Id, 4, "surface_analysis_generated") ??
                                           _projects.GetStepData(_selectedProject.Id, 5, "surface_analysis_generated")) ? "Oppervlakteanalyse beschikbaar" : "Oppervlakteanalyse nog uitvoeren",
            5 => BuildParcelOwnerAnalysis().Segments.Count > 0 ? "Perceelanalyse beschikbaar" : "Perceelanalyse nog uitvoeren",
            6 => BroUndergroundModelTypes.Sum(modelType => GetBroSoundings(modelType).Count) == 0
                ? "BRO/DINOloket datasets laden"
                : $"{BroUndergroundModelTypes.Sum(modelType => GetBroSoundings(modelType).Count)} BRO/DINOloket punt(en)",
            7 => (_projects.GetStepData(_selectedProject.Id, ProfileStepNumber, "diepteprofiel_3d") ??
                  _projects.GetStepData(_selectedProject.Id, LegacyProfileStepNumber, "diepteprofiel_3d")) is null ? "Dwarsprofiel nog opslaan" : "Dwarsprofiel beschikbaar",
            8 => string.IsNullOrWhiteSpace(_selectedMachineId) ? "Machine nog intekenen" : "Machine beschikbaar",
            9 => "Sonderingen nog vullen",
            var n when n == ReportStepNumber => "Rapportgenerator",
            var n when n == ThreeDStepNumber => "3D export buiten rapport",
            var n when n == WorkDrawingStepNumber => "Werktekening buiten rapport",
            _ => "Stap beschikbaar"
        };
    }





    private static string NormalizeVolatileReportJson(string json)
    {
        return Regex.Replace(
            json,
            ",?\"generatedAt\":\"[^\"]*\"",
            "",
            RegexOptions.CultureInvariant);
    }



    private object BuildReportBoringSummary()
    {
        EnsureBoringConfigLoaded(seedDefaultWhenMissing: false);
        if (_boringItems.Count == 0)
        {
            return new
            {
                bundleDiameter = 0,
                boringDiameter = 0,
                processed = Array.Empty<object>(),
                status = "Geen boringconfiguratie vastgelegd"
            };
        }

        return ComputeBoring();
    }

    private void RemoveLegacyDemoBoringConfigForReport()
    {
        if (_boringItems.Count == 0) return;
        if (!LooksLikeLegacyDemoBoringConfig()) return;

        _boringItems.Clear();
        _selectedMachineId = null;
        _selectedDrillingTechnique = "tbd";
    }

    private bool LooksLikeLegacyDemoBoringConfig()
    {
        static bool HasContent(BoringItem item, string text) =>
            item.Contents.Any(content => content.Label.Contains(text, StringComparison.OrdinalIgnoreCase));

        var defaultSeed =
            _boringItems.Count == 2 &&
            _boringItems.Any(item =>
                item.Type == BoringItemType.Mantelbuis &&
                item.Dn == 110 &&
                item.Label.Contains("PE 110", StringComparison.OrdinalIgnoreCase) &&
                HasContent(item, "YMVK 4x25") &&
                HasContent(item, "Microduct 16/12")) &&
            _boringItems.Any(item =>
                item.Type == BoringItemType.Direct &&
                item.Label.Contains("PE40 water", StringComparison.OrdinalIgnoreCase));
        if (defaultSeed) return true;
        return false;
    }
















    private async System.Threading.Tasks.Task<IReadOnlyList<BroSoundingPoint>> FetchDinoModelPointLayerSoundingsAsync(IReadOnlyList<TracePointRow> traceRows, string modelType)
    {
        modelType = NormalizeBroModelType(modelType);
        var layerId = DinoModelPointLayerId(modelType);
        if (layerId is null || traceRows.Count < 2) return [];

        var bounds = BuildDinoModelPointSearchEnvelope(traceRows, DinoModelPointSearchBufferMeters);
        if (bounds is null) return [];

        var values = new Dictionary<string, string>
        {
            ["f"] = "json",
            ["where"] = "1=1",
            ["outFields"] = "*",
            ["returnGeometry"] = "true",
            ["geometry"] = string.Join(",",
                bounds.Value.MinX.ToString("0.###", CultureInfo.InvariantCulture),
                bounds.Value.MinY.ToString("0.###", CultureInfo.InvariantCulture),
                bounds.Value.MaxX.ToString("0.###", CultureInfo.InvariantCulture),
                bounds.Value.MaxY.ToString("0.###", CultureInfo.InvariantCulture)),
            ["geometryType"] = "esriGeometryEnvelope",
            ["inSR"] = "28992",
            ["spatialRel"] = "esriSpatialRelIntersects",
            ["outSR"] = "28992",
            ["orderByFields"] = "DINO_NR ASC",
            ["resultRecordCount"] = MaxDinoModelPointResults.ToString(CultureInfo.InvariantCulture)
        };

        using var content = new FormUrlEncodedContent(values);
        var queryString = await content.ReadAsStringAsync();
        var uri = $"{DinoModelPointLayerEndpoint}/{layerId.Value}/query?{queryString}";
        using var response = await DinoModelHttpClient.GetAsync(uri);
        var text = await response.Content.ReadAsStringAsync();
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        var error = JsonProperty(root, "error");
        if (error is not null)
        {
            var message = FirstNonEmpty(JsonText(error.Value, "message", ""), JsonText(error.Value, "details", ""));
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(message) ? "DINOloket kaartlaag gaf een fout terug." : message);
        }

        var distances = BuildTraceDistances(traceRows);
        var result = new List<BroSoundingPoint>();
        var index = 1;
        foreach (var feature in JsonArray(root, "features"))
        {
            if (!feature.TryGetProperty("geometry", out var geometry) ||
                !feature.TryGetProperty("attributes", out var attributes))
            {
                continue;
            }

            var rd = new RdPoint(
                JsonDouble(geometry, "x", double.NaN),
                JsonDouble(geometry, "y", double.NaN));
            if (!IsValidRdPoint(rd)) continue;

            var reference = ProjectPointOnTraceSigned(rd, traceRows, distances);
            var wgs = RdToWgs84(rd.X, rd.Y);
            var dinoNumber = AttributeText(attributes, "DINO_NR");
            var objectId = FirstNonEmpty(AttributeText(attributes, "OBJECTID"), AttributeText(attributes, "OBJECT_ID"), AttributeText(attributes, "GDW_DBK"));
            var code = FirstNonEmpty(dinoNumber, $"{DinoModelShortLabel(modelType)}-{index}");
            var idKey = FirstNonEmpty(objectId, code, $"{Math.Round(rd.X, 1):0.0}:{Math.Round(rd.Y, 1):0.0}");

            result.Add(new BroSoundingPoint(
                $"{modelType}-DINO-{idKey}",
                code,
                $"{DinoModelLabel(modelType)} bronpunt {code}",
                Math.Round(rd.X, 3),
                Math.Round(rd.Y, 3),
                Math.Round(wgs[0], 8),
                Math.Round(wgs[1], 8),
                Math.Round(reference.Station, 2),
                Math.Round(reference.Offset, 2),
                null,
                null,
                $"{DinoModelLabel(modelType)} booronderzoekpunt uit DINOloket. Selecteer dit bronpunt om het modelprofiel op deze echte locatie op te halen.",
                $"{DinoModelLabel(modelType)} booronderzoeklaag DINOloket",
                DateTime.Today.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
                "Bronpunt beschikbaar",
                modelType,
                DinoModelLabel(modelType),
                []));
            index++;
        }

        return result
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => Math.Abs(item.Offset))
            .ThenBy(item => item.Distance)
            .ThenBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int? DinoModelPointLayerId(string modelType) =>
        NormalizeBroModelType(modelType) switch
        {
            BroDgmModelType => 0,
            BroRegisModelType => 2,
            _ => null
        };

    private static (double MinX, double MinY, double MaxX, double MaxY)? BuildDinoModelPointSearchEnvelope(IReadOnlyList<TracePointRow> traceRows, double bufferMeters)
    {
        var points = traceRows
            .Select(row => new RdPoint(row.X, row.Y))
            .Where(IsValidRdPoint)
            .ToList();
        if (points.Count == 0) return null;

        return (
            points.Min(point => point.X) - bufferMeters,
            points.Min(point => point.Y) - bufferMeters,
            points.Max(point => point.X) + bufferMeters,
            points.Max(point => point.Y) + bufferMeters);
    }

    private static string AttributeText(JsonElement element, string name)
    {
        var property = JsonProperty(element, name);
        return property is null ? "" : JsonText(property.Value, "");
    }



























    private static string JoinNonEmpty(params string[] values) =>
        string.Join(", ", values.Where(value => !string.IsNullOrWhiteSpace(value)));


    private static string DinoModelName(string modelType) =>
        NormalizeBroModelType(modelType) == BroRegisModelType ? "REGIS" : NormalizeBroModelType(modelType);

    private static string DinoModelVersion(string modelType) =>
        NormalizeBroModelType(modelType) == BroRegisModelType ? "v02r2s3" : "v02r2s1";

    private static string DinoModelLabel(string modelType) =>
        NormalizeBroModelType(modelType) switch
        {
            BroRegisModelType => "BRO REGIS II v2.2.3",
            BroGeomorphologyModelType => "BRO Geomorfologie 2025-01",
            BroSoilMapModelType => "BRO Bodemkaart 2025-01",
            BroGroundwaterGhgModelType => "BRO Grondwaterspiegeldiepte GHG 2025-01",
            BroGroundwaterGlgModelType => "BRO Grondwaterspiegeldiepte GLG 2025-01",
            BroGroundwaterGvgModelType => "BRO Grondwaterspiegeldiepte GVG 2025-01",
            BroGroundwaterGtModelType => "BRO Grondwatertrappen Gt 2025-01",
            BroGroundwaterDocumentationModelType => "BRO Grondwaterspiegeldiepte modeldocumentatie 2025-01",
            "CPT" => "BRO CPT / projectbestanden",
            _ => "BRO DGM v2.2.1"
        };

    private static string DinoModelShortLabel(string modelType) =>
        NormalizeBroModelType(modelType) switch
        {
            BroRegisModelType => "REGIS",
            BroGeomorphologyModelType => "Geomorfologie",
            BroSoilMapModelType => "Bodemkaart",
            BroGroundwaterGhgModelType => "GHG",
            BroGroundwaterGlgModelType => "GLG",
            BroGroundwaterGvgModelType => "GVG",
            BroGroundwaterGtModelType => "Grondwatertrappen",
            BroGroundwaterDocumentationModelType => "Modeldocumentatie",
            "CPT" => "CPT",
            _ => "DGM"
        };






    private static bool TryReadGmlPosition(XElement element, string locationLocalName, out double first, out double second)
    {
        first = 0;
        second = 0;
        var location = element.Descendants().FirstOrDefault(item => item.Name.LocalName.Equals(locationLocalName, StringComparison.OrdinalIgnoreCase));
        var pos = location?.Descendants().FirstOrDefault(item => item.Name.LocalName.Equals("pos", StringComparison.OrdinalIgnoreCase))?.Value;
        if (string.IsNullOrWhiteSpace(pos)) return false;

        var parts = Regex.Split(pos.Trim(), @"\s+");
        return parts.Length >= 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out first)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out second);
    }

    private static string DescendantValue(XElement element, string localName) =>
        Regex.Replace(
            element.Descendants().FirstOrDefault(item => item.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))?.Value ?? "",
            @"\s+",
            " ").Trim();

    private static double? DescendantDoubleNullable(XElement element, string localName)
    {
        var value = DescendantValue(element, localName);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }





    private static string FeatureText(GeoJsonFeature feature, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = GetFeatureProperty(feature, key);
            if (!string.IsNullOrWhiteSpace(value)) return Regex.Replace(value, @"\s+", " ").Trim();
        }

        return "";
    }

    private static double? FeatureDoubleNullable(GeoJsonFeature feature, params string[] keys)
    {
        foreach (var key in keys)
        {
            var text = FeatureText(feature, key);
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var invariant)) return invariant;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.GetCultureInfo("nl-NL"), out var dutch)) return dutch;
        }

        return null;
    }

    private static string FirstNonEmptyText(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";











    private static bool TryGetSubstepData(JsonElement root, string number, out JsonElement data)
    {
        data = default;
        if (!root.TryGetProperty("substeps", out var substeps) || substeps.ValueKind != JsonValueKind.Array) return false;
        foreach (var substep in substeps.EnumerateArray())
        {
            if (string.Equals(JsonText(substep, "number", ""), number, StringComparison.OrdinalIgnoreCase) &&
                substep.TryGetProperty("data", out var value))
            {
                data = value.Clone();
                return true;
            }
        }

        return false;
    }



    private static JsonElement? JsonProperty(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (element.TryGetProperty(name, out var property)) return property;

        foreach (var candidate in element.EnumerateObject())
        {
            if (candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return candidate.Value;
            }
        }

        return null;
    }

    private static string JsonText(JsonElement element, string name, string fallback = "-")
    {
        var property = JsonProperty(element, name);
        return property is { ValueKind: JsonValueKind.String } ? property.Value.GetString() ?? fallback : fallback;
    }

    private static string JsonText(JsonElement element, string fallback = "-") =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? fallback,
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "Ja",
            JsonValueKind.False => "Nee",
            _ => fallback
        };

    private static double JsonDouble(JsonElement element, string name, double fallback = 0)
    {
        var property = JsonProperty(element, name);
        if (property is null) return fallback;
        if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDouble(out var number)) return number;
        if (property.Value.ValueKind == JsonValueKind.String && double.TryParse(property.Value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return number;
        return fallback;
    }

    private static double? JsonDoubleNullable(JsonElement element, string name)
    {
        var property = JsonProperty(element, name);
        if (property is null) return null;
        if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDouble(out var number)) return number;
        if (property.Value.ValueKind == JsonValueKind.String && double.TryParse(property.Value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return number;
        return null;
    }

    private static int JsonInt(JsonElement element, string name, int fallback = 0)
    {
        var property = JsonProperty(element, name);
        if (property is null) return fallback;
        if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var number)) return number;
        if (property.Value.ValueKind == JsonValueKind.String && int.TryParse(property.Value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) return number;
        return fallback;
    }

    private static bool JsonBool(JsonElement element, string name, bool fallback = false)
    {
        var property = JsonProperty(element, name);
        if (property is null) return fallback;
        if (property.Value.ValueKind is JsonValueKind.True) return true;
        if (property.Value.ValueKind is JsonValueKind.False) return false;
        if (property.Value.ValueKind == JsonValueKind.String && bool.TryParse(property.Value.GetString(), out var value)) return value;
        return fallback;
    }

    private static IEnumerable<JsonElement> JsonArray(JsonElement element, string name)
    {
        var property = JsonProperty(element, name);
        return property is { ValueKind: JsonValueKind.Array } ? property.Value.EnumerateArray() : [];
    }

    private static string ReportValue(double value, string suffix = "", int decimals = 1)
    {
        var format = decimals <= 0 ? "N0" : $"N{decimals}";
        return $"{value.ToString(format, CultureInfo.CurrentCulture)}{suffix}";
    }






    private ReportStartContext BuildReportStartContext()
    {
        if (_selectedProject is null)
        {
            return new ReportStartContext("", 0, 0, 0, 0, []);
        }

        var settings = ReadReportStartSettings();
        _projectFiles = _projects.GetProjectFiles(_selectedProject.Id);
        var docs = BuildProjectDocumentEntries(_projectFiles).ToList();
        var layers = BuildProjectMapLayers(_projectFiles);
        var parcelAnalysis = BuildParcelOwnerAnalysis();
        var savedTraceRows = GetTraceRowsForProfile();
        var traceRows = NormalizeTraceRowsToRd(savedTraceRows.Count >= 2 ? savedTraceRows : parcelAnalysis.TraceRows);
        var traceDistances = BuildTraceDistances(traceRows);
        var traceLength = traceDistances.Count >= 2 && traceDistances[^1] > 0
            ? traceDistances[^1]
            : parcelAnalysis.TraceLength > 0 ? parcelAnalysis.TraceLength : _selectedProject.BoreLengthMeters;
        var parcelCount = CountCrossedParcels(parcelAnalysis.Segments);
        return new ReportStartContext(
            BuildReportProjectLocation(BuildTraceLocationContext(traceRows)),
            docs.Count,
            layers.Count,
            parcelCount,
            traceLength,
            BuildReportContentsEntries(parcelCount, settings.IncludeAppendices));
    }

    private ReportStartSettings ReadReportStartSettings()
    {
        if (_selectedProject is null) return ReportStartSettings.Default;
        var json = _projects.GetStepData(_selectedProject.Id, 0, ReportStartSettingsDataKey);
        if (string.IsNullOrWhiteSpace(json)) return ReportStartSettings.Default;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return new ReportStartSettings(
                JsonText(root, "coverTitle", ""),
                JsonText(root, "coverSubtitle", ""),
                JsonText(root, "coverRevision", ""),
                JsonText(root, "coverNote", ""),
                JsonText(root, "forewordText", ""),
                JsonText(root, "forewordScope", ""),
                JsonText(root, "contentsTitle", ""),
                JsonText(root, "contentsIntro", ""),
                JsonBool(root, "includeAppendices", true));
        }
        catch
        {
            return ReportStartSettings.Default;
        }
    }




    private string GenerateDefaultForewordText(int documentCount, int parcelCount, double traceLength)
    {
        var projectName = _selectedProject?.Name ?? "het project";
        var client = FirstNonEmpty(_selectedProject?.Client ?? "", "de opdrachtgever");
        var location = BuildReportProjectLocation(BuildTraceLocationContext(GetTraceRowsForProfile()));
        location = FirstNonEmpty(location, _selectedProject?.Location ?? "", "de projectlocatie");

        var foreword = $"Dit rapport beschrijft de prescan voor '{projectName}' in {location}. Het doel van deze rapportage is om de haalbaarheid en aandachtspunten voor een horizontaal gestuurde boring overzichtelijk vast te leggen voor {client}.\n\n"
            + $"De rapportage is opgebouwd uit de actuele projectgegevens, ingeladen bronbestanden, de ingetekende boorlijn, kaartbeelden en de analyses die in Borevexa Prescan zijn vastgelegd. Voor dit concept is uitgegaan van een tracelengte van {traceLength:N1} m, {documentCount} gekoppelde document(en)/bijlage(n) en {parcelCount} gekruiste perceel/percelen.\n\n"
            + "De resultaten vormen een technisch vooronderzoek. Definitieve uitvoering, vergunningen, kabel- en leidinginformatie, grondeigendom en lokale randvoorwaarden blijven afhankelijk van controle op de meest actuele brongegevens en afstemming met betrokken partijen.";
        var knowledgeGuidance = BuildKnowledgeGuidanceForText("voorwoord en uitgangspunten", $"HDD haalbaarheid trace lengte {traceLength:N1} documenten {documentCount} percelen {parcelCount}");
        return string.IsNullOrWhiteSpace(knowledgeGuidance)
            ? foreword
            : $"{foreword}\n\n{knowledgeGuidance}";
    }

    private string GenerateDefaultForewordScope(int documentCount, int parcelCount, double traceLength) =>
        $"Scope: haalbaarheidsonderzoek voor HDD-trace met een indicatieve lengte van {traceLength:N1} m.\n"
        + $"Bronnen: projectgegevens, importbestanden, GIS-kaartlagen, boorlijn, oppervlakteanalyse, ondergrondanalyse en overige opgeslagen rapportstappen.\n"
        + $"Omvang: {documentCount} document(en)/bijlage(n), {parcelCount} gekruiste perceel/percelen en alle beschikbare rapportpreviews uit de processtappen.\n"
        + "Controle: deze prescan is bedoeld als onderbouwde conceptbeoordeling; controleer kritieke uitgangspunten voor definitieve oplevering.";


    private static int CountCrossedParcels(IEnumerable<ParcelOwnerSegment> segments)
    {
        return segments
            .Where(segment => segment.Length >= 0.2)
            .Select(segment =>
            {
                var objectId = NormalizeFeatureKey(segment.CadastralObjectId);
                if (!string.IsNullOrWhiteSpace(objectId) && objectId != "-") return objectId;
                return NormalizeFeatureKey($"{segment.CadastralMunicipality}|{segment.Section}|{segment.ParcelNumber}");
            })
            .Where(key => !string.IsNullOrWhiteSpace(key) && key != "-|-|-")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }


    private static string FormatReportContentsTitle(string title)
    {
        var cleaned = Regex.Replace(title.Trim(), @"^Stap\s+(\d+)\s*-\s*", "$1 ", RegexOptions.IgnoreCase);
        return cleaned
            .Replace(" - kaartbijlage", " kaartbijlage", StringComparison.OrdinalIgnoreCase)
            .Replace(" - detailstaat", " detailstaat", StringComparison.OrdinalIgnoreCase)
            .Replace(" - BGT kaart", " BGT kaart", StringComparison.OrdinalIgnoreCase)
            .Replace(" - oppervlakteprofiel", " oppervlakteprofiel", StringComparison.OrdinalIgnoreCase)
            .Replace(" - AHN4 maaiveldprofiel", " AHN4 maaiveldprofiel", StringComparison.OrdinalIgnoreCase);
    }

    private static (string Number, string Title) SplitReportContentsTitle(string title)
    {
        var match = Regex.Match(title, @"^(?<number>(?:\d+\.)?\d+(?:\.\d+)?)\s+(?<title>.+)$");
        return match.Success
            ? (match.Groups["number"].Value, match.Groups["title"].Value)
            : ("", title);
    }

    private static IReadOnlyList<ReportContentsEntry> BuildReportContentsEntries(int parcelMapPageCount, bool includeAppendices = true)
    {
        var entries = new List<ReportContentsEntry>
        {
            new("1", "Voorblad", "Project, locatie, opdrachtgever, boorlengte en rapportstatus."),
            new("2", "Voorwoord", "Toelichting op de prescan, uitgangspunten en rapportcontext."),
            new("3", "Inhoudsopgave", "Overzicht van de rapportinhoud en hoofdstukvolgorde."),
            new("4", "Stap 1 - Projectinformatie", "Projectinhoud, ligging, kaartbeeld, projectgegevens, boringconfiguratie en productbundel.")
        };

        var page = 5;
        entries.Add(new ReportContentsEntry(page++.ToString(CultureInfo.InvariantCulture), "Stap 2 - Imports", "Ontwerp, KLIC, BAG/Kadaster, BGT en overige projectbestanden."));
        entries.Add(new ReportContentsEntry(page++.ToString(CultureInfo.InvariantCulture), "Stap 3 - Boorlijn", "GIS-lagen, kaartinstellingen, KLIC-context en ligging van de getekende boorlijn."));
        entries.Add(new ReportContentsEntry(page++.ToString(CultureInfo.InvariantCulture), "Stap 4 - Oppervlakteanalyse", "BGT-oppervlakken, segmenten en herstel-/vergunningindicatie."));
        entries.Add(new ReportContentsEntry(page++.ToString(CultureInfo.InvariantCulture), $"Stap {DisplayStepNumber(EnvironmentStepNumber)} - Omgevingsmanagement", "Percelen, bronhouders, ZRO-checklist, acties en restpunten."));
        if (parcelMapPageCount > 0)
        {
            var pageText = parcelMapPageCount == 1
                ? page.ToString(CultureInfo.InvariantCulture)
                : $"{page}-{page + parcelMapPageCount - 1}";
            entries.Add(new ReportContentsEntry(pageText, "Kaartuitsneden per perceel", "Een aparte kaartpagina per gevonden perceelsegment met boorlijn en perceeldata."));
            page += parcelMapPageCount;
        }

        entries.Add(new ReportContentsEntry(page++.ToString(CultureInfo.InvariantCulture), $"Stap {DisplayStepNumber(6)} - Ondergrondanalyse", "BRO/DINOloket kaartdatasets met handmatig gekozen bronpunten."));
        entries.Add(new ReportContentsEntry(page++.ToString(CultureInfo.InvariantCulture), $"Stap {DisplayStepNumber(ProfileStepNumber)} - Dwarsprofiel", "Maaiveld, boorlijn, dieptepunten, KLIC-kruisingen en profielstaat."));
        entries.Add(new ReportContentsEntry(page++.ToString(CultureInfo.InvariantCulture), $"Stap {DisplayStepNumber(MachineStepNumber)} - Machine locatie", "Werkvak, machinepositie, bentoniet en logistieke aandachtspunten."));
        entries.Add(new ReportContentsEntry(page++.ToString(CultureInfo.InvariantCulture), $"Stap {DisplayStepNumber(WorkflowCatalog.SoundingStepNumber)} - Sonderingen", "Sonderingen, geotechnische aandachtspunten en bodemreferenties."));
        entries.Add(new ReportContentsEntry(page++.ToString(CultureInfo.InvariantCulture), $"Stap {DisplayStepNumber(ReportStepNumber)} - Eindrapport & Export", "Eindconclusie, restpunten, AI-analyse en bijlagen."));
        if (includeAppendices)
        {
            entries.Add(new ReportContentsEntry(page.ToString(CultureInfo.InvariantCulture), "Bijlagen en documenten", "Overzicht van gekoppelde projectdocumenten."));
        }
        return entries;
    }








    private static (string Number, string Title, string Chapter) SplitReportSectionHeading(string title, int stepNumber)
    {
        var match = Regex.Match(title.Trim(), @"^(?<number>(?:\d+\.)?\d+(?:\.\d+)?)\s+(?<title>.+)$");
        if (match.Success)
        {
            return (match.Groups["number"].Value, match.Groups["title"].Value, "");
        }

        return (stepNumber > 0 ? DisplayStepNumber(stepNumber) : "", title, stepNumber > 0 ? $"Hoofdstuk {DisplayStepNumber(stepNumber)}" : "");
    }





    private static string FormatReportPageNumber(int pageNumber, int? totalPages = null) =>
        totalPages is > 0
            ? $"Blad {pageNumber.ToString(CultureInfo.InvariantCulture)} van {totalPages.Value.ToString(CultureInfo.InvariantCulture)}"
            : $"Blad {pageNumber.ToString(CultureInfo.InvariantCulture)}";

    private static void RenumberReportPreviewPages(IReadOnlyList<UIElement> pages)
    {
        var total = pages.Count;
        for (var index = 0; index < pages.Count; index++)
        {
            var pageNumber = index + 1;
            if (pages[index] is FrameworkElement element)
            {
                element.Tag = pageNumber;
                foreach (var textBlock in EnumerateVisualDescendants<TextBlock>(element))
                {
                    if (textBlock.Text is null) continue;
                    if (Regex.IsMatch(textBlock.Text, @"^Blad(?:\s+\d+(?:\s+van\s+\d+)?)?$", RegexOptions.IgnoreCase))
                    {
                        textBlock.Text = FormatReportPageNumber(pageNumber, total);
                    }
                    else if (Regex.IsMatch(textBlock.Text, @"^Blad\s+\d+(?:\s+van\s+\d+)?\s+·", RegexOptions.IgnoreCase))
                    {
                        textBlock.Text = Regex.Replace(
                            textBlock.Text,
                            @"^Blad\s+\d+(?:\s+van\s+\d+)?",
                            FormatReportPageNumber(pageNumber, total),
                            RegexOptions.IgnoreCase);
                    }
                }
            }
        }
    }

    private static string ExtractReportPageTitle(UIElement page)
    {
        if (page is not FrameworkElement element) return "";
        var texts = EnumerateVisualDescendants<TextBlock>(element)
            .Select(text => text.Text?.Trim() ?? "")
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Take(40)
            .ToList();

        foreach (var text in texts)
        {
            if (Regex.IsMatch(text, @"^\d+\s+\S+", RegexOptions.IgnoreCase) &&
                !text.StartsWith("Blad ", StringComparison.OrdinalIgnoreCase))
            {
                return text;
            }
            if (Regex.IsMatch(text, @"^(?:\d+\s+)?\d+\.\d+\s+", RegexOptions.IgnoreCase))
            {
                return Regex.Replace(text, @"^\d+\s+(?=\d+\.\d+\s+)", "");
            }
            if (text.StartsWith("Stap ", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("Kaartuitsnede", StringComparison.OrdinalIgnoreCase))
            {
                return text;
            }
        }

        return texts.FirstOrDefault(text => text is "Voorwoord" or "Inhoudsopgave") ?? texts.FirstOrDefault() ?? "";
    }

    private static IEnumerable<T> EnumerateVisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed)
            {
                yield return typed;
            }

            foreach (var descendant in EnumerateVisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }











    private static string NormalizeReportDetailText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var parts = Regex.Split(value.Trim(), @"\s*(?:\r?\n|;)\s+|\s{2,}")
            .Select(part => Regex.Replace(part.Trim(), @"\s+", " "))
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return parts.Count == 0 ? "" : string.Join("\n", parts);
    }

    private static string DetailReportCell(string value, int maxLength)
    {
        value = Regex.Replace(value.Trim(), @"\s+", " ");
        if (string.IsNullOrWhiteSpace(value)) return "-";
        return TruncateText(value, maxLength);
    }







    private static (double MinX, double MinY, double MaxX, double MaxY) ReportBoundsWithBuffer(IReadOnlyList<RdPoint> points, double minBuffer)
    {
        var valid = points.Where(IsValidRdPoint).ToList();
        if (valid.Count == 0) return (0, 0, 1, 1);
        var minX = valid.Min(point => point.X);
        var maxX = valid.Max(point => point.X);
        var minY = valid.Min(point => point.Y);
        var maxY = valid.Max(point => point.Y);
        var span = Math.Max(maxX - minX, maxY - minY);
        var buffer = Math.Max(minBuffer, span * 0.10);
        return (minX - buffer, minY - buffer, maxX + buffer, maxY + buffer);
    }

    private static IEnumerable<RdPoint> ClipRdLineToBounds(IReadOnlyList<RdPoint> points, (double MinX, double MinY, double MaxX, double MaxY) bounds)
    {
        var result = new List<RdPoint>();
        for (var i = 1; i < points.Count; i++)
        {
            if (!TryClipSegmentToBounds(points[i - 1], points[i], bounds, out var a, out var b)) continue;
            if (result.Count == 0 || Distance(result[^1], a) > 0.01) result.Add(a);
            result.Add(b);
        }

        return result;
    }

    private static bool TryClipSegmentToBounds(RdPoint p0, RdPoint p1, (double MinX, double MinY, double MaxX, double MaxY) bounds, out RdPoint a, out RdPoint b)
    {
        var t0 = 0d;
        var t1 = 1d;
        var dx = p1.X - p0.X;
        var dy = p1.Y - p0.Y;

        bool Clip(double p, double q)
        {
            if (Math.Abs(p) < 0.0000001) return q >= 0;
            var r = q / p;
            if (p < 0)
            {
                if (r > t1) return false;
                if (r > t0) t0 = r;
            }
            else
            {
                if (r < t0) return false;
                if (r < t1) t1 = r;
            }

            return true;
        }

        if (Clip(-dx, p0.X - bounds.MinX) &&
            Clip(dx, bounds.MaxX - p0.X) &&
            Clip(-dy, p0.Y - bounds.MinY) &&
            Clip(dy, bounds.MaxY - p0.Y))
        {
            a = new RdPoint(p0.X + t0 * dx, p0.Y + t0 * dy);
            b = new RdPoint(p0.X + t1 * dx, p0.Y + t1 * dy);
            return true;
        }

        a = new RdPoint(0, 0);
        b = new RdPoint(0, 0);
        return false;
    }

    private static double Distance(RdPoint a, RdPoint b) =>
        Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));








    private static bool IsClosedRing(IReadOnlyList<RdPoint> points) =>
        points.Count >= 4 && Distance(points[0], points[^1]) < 0.1;

    private static bool PointInRing(RdPoint point, IReadOnlyList<RdPoint> ring)
    {
        var inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var pi = ring[i];
            var pj = ring[j];
            var intersects = ((pi.Y > point.Y) != (pj.Y > point.Y)) &&
                             (point.X < (pj.X - pi.X) * (point.Y - pi.Y) / Math.Max(0.000001, pj.Y - pi.Y) + pi.X);
            if (intersects) inside = !inside;
        }

        return inside;
    }
















    private static string JoinVisible(IReadOnlyDictionary<string, bool> states)
    {
        var values = states.Where(pair => pair.Value).Select(pair => pair.Key).Take(12).ToList();
        return values.Count == 0 ? "geen" : string.Join(", ", values);
    }







    private static string FormatBoringItemType(BoringItem item) =>
        item.Type == BoringItemType.Mantelbuis ? "Mantelbuis" : "Direct product";





    private void AddLockedStepMapToReportPanel(Panel panel, int stepNumber, string title)
    {
        var path = GetLockedReportMapImagePath(stepNumber);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            panel.Children.Add(CreateReportNote($"Geen opgeslagen kaart gevonden voor stap {stepNumber}. Open stap {stepNumber} en gebruik 'Opslaan voor rapportage' om dit kaartbeeld in het rapport op te nemen."));
            return;
        }

        var card = new Border
        {
            BorderBrush = Brush("#DEE6EA"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Background = Brushes.White,
            Padding = new Thickness(10),
            Margin = new Thickness(0, 10, 0, 0)
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brush("#071422"),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6)
        });
        var image = new Image
        {
            Height = 230,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            SnapsToDevicePixels = true
        };
        ApplyLocalImageSource(image, path);
        stack.Children.Add(image);
        stack.Children.Add(new TextBlock
        {
            Text = $"Kaartbron: stap {stepNumber} in de app, opgeslagen met 'Opslaan voor rapportage'.",
            Foreground = Brush("#64748B"),
            FontSize = 9.5,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        card.Child = stack;
        panel.Children.Add(card);
    }




    private string GetReportStepTitle(int stepNumber)
    {
        if (_workspaces.TryGetValue(stepNumber, out var workspace))
        {
            return workspace.Title;
        }

        var firstSubstep = StepReportCatalog.GetSubsteps(stepNumber).FirstOrDefault();
        return firstSubstep?.ReportSectionTitle ?? "Rapportonderdeel";
    }

    private static string BuildFirstReportQualityAction(ReportQualitySummary summary)
    {
        var issue = summary.Issues.FirstOrDefault();
        return issue is null
            ? "Controleer dit rapportonderdeel."
            : $"{DisplayIssueNumber(issue)}: {issue.Action}";
    }










    private static string ShortReportCell(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return "-";
        value = Regex.Replace(value.Trim(), @"\s+", " ");
        return value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, Math.Max(0, maxLength - 3)), "...");
    }





    private static bool IsHiddenWorkflowStep(int stepNumber) => false;

    private static string DisplayStepNumber(int stepNumber)
    {
        if (stepNumber == 0) return "00";
        return stepNumber.ToString(CultureInfo.InvariantCulture);
    }

    private static string DisplaySubstepNumber(PrescanSubstep substep) =>
        DisplaySubstepNumber(string.IsNullOrWhiteSpace(substep.DisplayNumber) ? substep.Number : substep.DisplayNumber);

    private static string DisplaySubstepNumber(string substepNumber)
    {
        if (string.IsNullOrWhiteSpace(substepNumber)) return substepNumber;
        var parts = substepNumber.Split('.', 2);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var stepNumber))
        {
            return substepNumber;
        }

        return $"{DisplayStepNumber(stepNumber)}.{parts[1]}";
    }

    private static string DisplayIssueNumber(ReportQualityIssue issue) =>
        !string.IsNullOrWhiteSpace(issue.SubstepNumber)
            ? DisplaySubstepNumber(issue.SubstepNumber)
            : DisplayStepNumber(issue.StepNumber);

    private static string DisplayReportSectionTitle(PrescanSubstep substep) =>
        $"{DisplaySubstepNumber(substep)} {substep.Title}";






    private static Image CreateOsmTileImage(int zoom, int x, int y)
    {
        var image = new Image { Width = 256, Height = 256, Stretch = Stretch.Fill };
        var uri = new Uri($"https://tile.openstreetmap.org/{zoom}/{x}/{y}.png");
        ApplyRemoteImageSource(image, uri);
        return image;
    }

    private static Image CreatePdokKadasterTileImage(int zoom, int x, int y)
    {
        var image = new Image { Width = 256, Height = 256, Stretch = Stretch.Fill, Opacity = 0.9 };
        var bbox = WebMercatorTileBbox(x, y, zoom);
        var bboxText = string.Join(",", new[] { bbox.MinX, bbox.MinY, bbox.MaxX, bbox.MaxY }.Select(value => value.ToString("0.###", CultureInfo.InvariantCulture)));
        var uri = new Uri($"https://service.pdok.nl/kadaster/kadastralekaart/wms/v5_0?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=KadastraleGrens&STYLES=&FORMAT=image/png&TRANSPARENT=true&SRS=EPSG:3857&BBOX={bboxText}&WIDTH=256&HEIGHT=256");
        ApplyRemoteImageSource(image, uri);
        return image;
    }

    private static void ApplyRemoteImageSource(Image image, Uri uri)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = uri;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bitmap.EndInit();
        image.Source = bitmap;
    }

    private static (double MinX, double MinY, double MaxX, double MaxY) WebMercatorTileBbox(int x, int y, int zoom)
    {
        const double originShift = 20037508.342789244;
        var tileMeters = originShift * 2d / Math.Pow(2, zoom);
        var minX = -originShift + x * tileMeters;
        var maxX = minX + tileMeters;
        var maxY = originShift - y * tileMeters;
        var minY = maxY - tileMeters;
        return (minX, minY, maxX, maxY);
    }

    private static int ChooseOsmZoom(IReadOnlyList<LonLat> points, double width, double height)
    {
        for (var zoom = 19; zoom >= 10; zoom--)
        {
            var projected = points.Select(point => LonLatToWebMercatorPixel(point.Lon, point.Lat, zoom)).ToList();
            var spanX = projected.Max(point => point.X) - projected.Min(point => point.X);
            var spanY = projected.Max(point => point.Y) - projected.Min(point => point.Y);
            if (spanX <= width * 0.62 && spanY <= height * 0.56)
            {
                return zoom;
            }
        }

        return 10;
    }

    private static Point LonLatToWebMercatorPixel(double lon, double lat, int zoom)
    {
        var sinLat = Math.Sin(Math.Clamp(lat, -85.05112878, 85.05112878) * Math.PI / 180d);
        var scale = 256d * Math.Pow(2, zoom);
        var x = (lon + 180d) / 360d * scale;
        var y = (0.5d - Math.Log((1d + sinLat) / (1d - sinLat)) / (4d * Math.PI)) * scale;
        return new Point(x, y);
    }

    private static LonLat WebMercatorPixelToLonLat(double x, double y, int zoom)
    {
        var scale = 256d * Math.Pow(2, zoom);
        var lon = x / scale * 360d - 180d;
        var n = Math.PI - 2d * Math.PI * y / scale;
        var lat = 180d / Math.PI * Math.Atan(0.5d * (Math.Exp(n) - Math.Exp(-n)));
        return new LonLat(lon, lat);
    }

    private static double WebMercatorMetersPerPixel(double latitude, int zoom) =>
        156543.03392804097 * Math.Cos(latitude * Math.PI / 180d) / Math.Pow(2, zoom);


    private IEnumerable<ReportMapRecipe> BuildDefaultReportMapRecipesForStep(int stepNumber, IReadOnlyList<TracePointRow> traceRows, IReadOnlyList<ProjectMapLayer> layers, ParcelOwnerAnalysis parcelAnalysis)
    {
        return stepNumber switch
        {
            2 => [],
            3 =>
            [
                new ReportMapRecipe("step3-map-background", 3, "Boorlijnkaart - BGT-achtergrond", "Rapportkaart met PDOK BGT-onderlegger, kadastercontext en de actuele boorlijn.", "pdok-bgt-pastel", "trace-cadastral", null, "fit-trace", 760, 250, true, false, false, null, null),
                new ReportMapRecipe("step3-satellite-background", 3, "Boorlijnkaart - satellietachtergrond", "Rapportkaart met PDOK luchtfoto/satellietachtergrond en dezelfde vastgelegde boorlijn.", "pdok-aerial", "trace-cadastral", null, "fit-trace", 760, 250, true, false, false, null, null)
            ],
            4 => BuildFixedScaleTraceRecipes(
                stepNumber,
                "Boorlijn technische kaart 1:200",
                "Schaalvaste kaartbladen met boorlijn, start/einde en relevante ontwerp- en kadastrale context.",
                "grid",
                "trace-detail",
                traceRows,
                200,
                8),
            6 => [],
            7 => [],
            9 => [new ReportMapRecipe("step9-machine-context", 9, "Machine- en werkvakcontext", "Situatiekaart voor machinepositie, werkvak en logistieke ruimte rond de boorlijn.", "grid", "machine-context", null, "fit-trace", 760, 250, true, true, false, null, null)],
            10 => [new ReportMapRecipe("step10-spatial-context", 10, "3D/ontwerp context", "Ruimtelijke referentiekaart bij het 3D-ontwerp en dwarsprofiel.", "grid", "all", null, "fit-trace", 760, 250, true, false, false, null, null)],
            _ => []
        };
    }





    private string BuildReportMapRecipeImageHtml(ReportMapRecipe recipe, IReadOnlyList<TracePointRow> traceRows, IReadOnlyList<ProjectMapLayer> layers, ParcelOwnerAnalysis parcelAnalysis)
    {
        try
        {
            var canvas = CreateReportMapCanvas(recipe, traceRows, layers, parcelAnalysis);
            canvas.Measure(new Size(recipe.Width, recipe.Height));
            canvas.Arrange(new Rect(0, 0, recipe.Width, recipe.Height));
            canvas.UpdateLayout();

            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(recipe.Width), (int)Math.Ceiling(recipe.Height), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(canvas);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            var base64 = Convert.ToBase64String(stream.ToArray());
            return $"""
<div class="mapbox">
<div class="maptitle">{System.Net.WebUtility.HtmlEncode(recipe.Title)}</div>
<img alt="{System.Net.WebUtility.HtmlEncode(recipe.Title)}" src="data:image/png;base64,{base64}" style="width:100%;height:auto;border:1px solid #dbe4ea;display:block" />
</div>
""";
        }
        catch
        {
            return BuildReportMapSvg(recipe.Title, layers, traceRows, recipe.ShowTracePoints, recipe.BaseMap);
        }
    }

    private string BuildReportMapSvg(string label, IReadOnlyList<ProjectMapLayer> layers, IReadOnlyList<TracePointRow> traceRows, bool showTracePoints, string baseMap = "grid")
    {
        static string H(string value) => System.Net.WebUtility.HtmlEncode(value);
        static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        const double width = 760;
        const double height = 250;
        const double margin = 26;
        var layerLines = layers.SelectMany(layer => EnumerateReportFeatureLines(layer).Select(line => (Layer: layer, Points: line))).ToList();
        var allPoints = traceRows.Select(point => new RdPoint(point.X, point.Y))
            .Concat(layerLines.SelectMany(line => line.Points))
            .Where(point => double.IsFinite(point.X) && double.IsFinite(point.Y))
            .ToList();
        if (allPoints.Count == 0)
        {
            allPoints.Add(new RdPoint(0, 0));
            allPoints.Add(new RdPoint(100, 100));
        }

        var minX = allPoints.Min(point => point.X);
        var maxX = allPoints.Max(point => point.X);
        var minY = allPoints.Min(point => point.Y);
        var maxY = allPoints.Max(point => point.Y);
        if (Math.Abs(maxX - minX) < 1) maxX = minX + 1;
        if (Math.Abs(maxY - minY) < 1) maxY = minY + 1;
        var scale = Math.Min((width - margin * 2) / (maxX - minX), (height - margin * 2) / (maxY - minY));
        var offsetX = (width - (maxX - minX) * scale) / 2;
        var offsetY = (height - (maxY - minY) * scale) / 2;
        double X(double rdX) => offsetX + (rdX - minX) * scale;
        double Y(double rdY) => height - offsetY - (rdY - minY) * scale;

        var svg = new StringBuilder();
        svg.AppendLine("<div class=\"mapbox\">");
        svg.AppendLine($"<div class=\"maptitle\">{H(label)}</div>");
        svg.AppendLine($"<svg viewBox=\"0 0 {N(width)} {N(height)}\" width=\"100%\" height=\"250\" xmlns=\"http://www.w3.org/2000/svg\">");
        svg.AppendLine($"<rect x=\"0\" y=\"0\" width=\"{N(width)}\" height=\"{N(height)}\" fill=\"{(baseMap.Contains("aerial", StringComparison.OrdinalIgnoreCase) ? "#DDE7DF" : "#F8FAFC")}\" stroke=\"#CBD5E1\"/>");
        for (var gridX = 0; gridX <= width; gridX += 76) svg.AppendLine($"<line x1=\"{N(gridX)}\" y1=\"0\" x2=\"{N(gridX)}\" y2=\"{N(height)}\" stroke=\"#E2E8F0\" stroke-width=\"0.5\"/>");
        for (var gridY = 0; gridY <= height; gridY += 50) svg.AppendLine($"<line x1=\"0\" y1=\"{N(gridY)}\" x2=\"{N(width)}\" y2=\"{N(gridY)}\" stroke=\"#E2E8F0\" stroke-width=\"0.5\"/>");

        foreach (var line in layerLines.Take(240))
        {
            var points = line.Points.Where(point => double.IsFinite(point.X) && double.IsFinite(point.Y)).ToList();
            if (points.Count < 2) continue;
            var color = string.IsNullOrWhiteSpace(line.Layer.Color) ? "#64748B" : line.Layer.Color;
            var d = string.Join(" ", points.Select((point, index) => $"{(index == 0 ? "M" : "L")} {N(X(point.X))} {N(Y(point.Y))}"));
            svg.AppendLine($"<path d=\"{d}\" fill=\"none\" stroke=\"{H(color)}\" stroke-width=\"1.2\" opacity=\"0.72\"/>");
        }

        if (traceRows.Count >= 2)
        {
            var d = string.Join(" ", traceRows.Select((point, index) => $"{(index == 0 ? "M" : "L")} {N(X(point.X))} {N(Y(point.Y))}"));
            svg.AppendLine($"<path d=\"{d}\" fill=\"none\" stroke=\"#E11D48\" stroke-width=\"3\"/>");
            if (showTracePoints)
            {
                for (var index = 0; index < traceRows.Count; index++)
                {
                    var point = traceRows[index];
                    var code = index == 0 ? "S" : index == traceRows.Count - 1 ? "E" : (index + 1).ToString(CultureInfo.InvariantCulture);
                    svg.AppendLine($"<circle cx=\"{N(X(point.X))}\" cy=\"{N(Y(point.Y))}\" r=\"4.2\" fill=\"#E11D48\" stroke=\"#ffffff\" stroke-width=\"1.2\"/>");
                    svg.AppendLine($"<text x=\"{N(X(point.X) + 7)}\" y=\"{N(Y(point.Y) - 7)}\" font-size=\"10\" font-weight=\"700\" fill=\"#111827\">{H(code)}</text>");
                }
            }
        }

        svg.AppendLine("</svg></div>");
        return svg.ToString();
    }

    private static void AddBaseMapTilesForCamera(Canvas canvas, string baseLayer, double originX, double originY, double width, double height, int zoom, bool showKadasterOverlay)
    {
        var source = CreateBaseMapWmsBitmap(baseLayer, originX, originY, width, height, zoom)
            ?? CreateBaseMapTileBitmap(baseLayer, originX, originY, width, height, zoom, showKadasterOverlay);
        if (source is not null)
        {
            var composite = new Image { Width = width, Height = height, Stretch = Stretch.Fill, Source = source };
            Canvas.SetLeft(composite, 0);
            Canvas.SetTop(composite, 0);
            canvas.Children.Add(composite);
            AddCanvasRect(canvas, 0, 0, width, height, "#22FFFFFF", "Transparent", 0);
            return;
        }

        AddCanvasRect(canvas, 0, 0, width, height, "#F8FAFB", "#E5E7EB", 1);
        for (var x = 0; x <= width; x += 64) AddCanvasLine(canvas, x, 0, x, height, "#EEF2F6", 1, null);
        for (var y = 0; y <= height; y += 64) AddCanvasLine(canvas, 0, y, width, y, "#EEF2F6", 1, null);
    }

    private static ImageSource? CreateBaseMapWmsBitmap(string baseLayer, double originX, double originY, double width, double height, int zoom)
    {
        if (!baseLayer.Equals("pdok-aerial", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var topLeft = WebMercatorPixelToLonLat(originX, originY, zoom);
        var bottomRight = WebMercatorPixelToLonLat(originX + width, originY + height, zoom);
        var rdA = Wgs84ToRd(topLeft.Lon, topLeft.Lat);
        var rdB = Wgs84ToRd(bottomRight.Lon, bottomRight.Lat);
        var minX = Math.Min(rdA.X, rdB.X);
        var maxX = Math.Max(rdA.X, rdB.X);
        var minY = Math.Min(rdA.Y, rdB.Y);
        var maxY = Math.Max(rdA.Y, rdB.Y);
        if (maxX - minX < 1 || maxY - minY < 1)
        {
            return null;
        }

        static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(width));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(height));
        var bbox = string.Join(",", [N(minX), N(minY), N(maxX), N(maxY)]);
        var url = AddMapTileCacheBust($"https://service.pdok.nl/hwh/luchtfotorgb/wms/v1_0?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=Actueel_ortho25&STYLES=&FORMAT=image/jpeg&SRS=EPSG:28992&BBOX={bbox}&WIDTH={pixelWidth.ToString(CultureInfo.InvariantCulture)}&HEIGHT={pixelHeight.ToString(CultureInfo.InvariantCulture)}");
        return LoadRemoteBitmap(url, rejectSolidYellowTiles: true);
    }

    private static ImageSource? CreateBaseMapTileBitmap(string baseLayer, double originX, double originY, double width, double height, int zoom, bool showKadasterOverlay)
    {
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(width));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(height));
        var startTileX = (int)Math.Floor(originX / 256d);
        var endTileX = (int)Math.Floor((originX + width) / 256d);
        var startTileY = (int)Math.Floor(originY / 256d);
        var endTileY = (int)Math.Floor((originY + height) / 256d);
        var maxTile = (1 << zoom) - 1;
        var visual = new DrawingVisual();
        var loadedTileCount = 0;
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brush("#F8FAFB"), null, new Rect(0, 0, pixelWidth, pixelHeight));
            for (var tileX = startTileX; tileX <= endTileX; tileX++)
            {
                for (var tileY = startTileY; tileY <= endTileY; tileY++)
                {
                    if (tileY < 0 || tileY > maxTile) continue;
                    var wrappedX = ((tileX % (maxTile + 1)) + maxTile + 1) % (maxTile + 1);
                    var left = tileX * 256d - originX;
                    var top = tileY * 256d - originY;
                    var bitmap = LoadRemoteBitmap(CreateBaseTileUrl(baseLayer, zoom, wrappedX, tileY), rejectSolidYellowTiles: true);
                    if (bitmap is null && !baseLayer.Equals("pdok-brt", StringComparison.OrdinalIgnoreCase))
                    {
                        bitmap = LoadRemoteBitmap(CreateBaseTileUrl("pdok-brt", zoom, wrappedX, tileY), rejectSolidYellowTiles: true);
                    }
                    if (bitmap is not null)
                    {
                        loadedTileCount++;
                        drawing.DrawImage(bitmap, new Rect(left, top, 256, 256));
                    }

                    if (!showKadasterOverlay) continue;
                    var overlayBitmap = LoadRemoteBitmap(CreatePdokKadasterTileUrl(zoom, wrappedX, tileY), rejectSolidYellowTiles: true);
                    if (overlayBitmap is not null)
                    {
                        drawing.PushOpacity(0.9);
                        drawing.DrawImage(overlayBitmap, new Rect(left, top, 256, 256));
                        drawing.Pop();
                    }
                }
            }
        }

        if (loadedTileCount == 0)
        {
            return null;
        }

        var render = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        render.Render(visual);
        render.Freeze();
        return render;
    }

    private static Image CreateBaseTileImage(string baseLayer, int zoom, int x, int y)
    {
        var image = new Image { Width = 256, Height = 256, Stretch = Stretch.Fill };
        ApplyRemoteImageSource(image, new Uri(CreateBaseTileUrl(baseLayer, zoom, x, y)));
        return image;
    }

    private static string CreateBaseTileUrl(string baseLayer, int zoom, int x, int y)
    {
        var template = baseLayer switch
        {
            "pdok-brt" => "https://service.pdok.nl/brt/achtergrondkaart/wmts/v2_0/standaard/EPSG:3857/{z}/{x}/{y}.png",
            "pdok-gray" => "https://service.pdok.nl/brt/achtergrondkaart/wmts/v2_0/grijs/EPSG:3857/{z}/{x}/{y}.png",
            "pdok-pastel" => "https://service.pdok.nl/brt/achtergrondkaart/wmts/v2_0/pastel/EPSG:3857/{z}/{x}/{y}.png",
            "pdok-bgt-pastel" => "https://service.pdok.nl/lv/bgt/wmts/v1_0/standaardvisualisatie/EPSG:3857/{z}/{x}/{y}.png",
            "pdok-aerial" => "https://service.pdok.nl/hwh/luchtfotorgb/wmts/v1_0?SERVICE=WMTS&REQUEST=GetTile&VERSION=1.0.0&LAYER=Actueel_ortho25&STYLE=default&FORMAT=image/jpeg&tileMatrixSet=EPSG:3857&tileMatrix={z}&tileRow={y}&tileCol={x}",
            "osm" => "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
            _ => "https://service.pdok.nl/brt/achtergrondkaart/wmts/v2_0/standaard/EPSG:3857/{z}/{x}/{y}.png"
        };

        var url = template
            .Replace("{z}", zoom.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{x}", x.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{y}", y.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        return AddMapTileCacheBust(url);
    }

    private static string AddMapTileCacheBust(string url)
    {
        var separator = url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{url}{separator}_bv={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)}";
    }

    private static string CreatePdokKadasterTileUrl(int zoom, int x, int y)
    {
        var bbox = WebMercatorTileBbox(x, y, zoom);
        var bboxText = string.Join(",", new[] { bbox.MinX, bbox.MinY, bbox.MaxX, bbox.MaxY }.Select(value => value.ToString("0.###", CultureInfo.InvariantCulture)));
        return AddMapTileCacheBust($"https://service.pdok.nl/kadaster/kadastralekaart/wms/v5_0?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=KadastraleGrens&STYLES=&FORMAT=image/png&TRANSPARENT=true&SRS=EPSG:3857&BBOX={bboxText}&WIDTH=256&HEIGHT=256");
    }

    private static BitmapSource? LoadRemoteBitmap(string url, bool rejectSolidYellowTiles = false)
    {
        try
        {
            var bytes = ReportTileHttpClient.GetByteArrayAsync(url).GetAwaiter().GetResult();
            if (LooksLikeServiceException(bytes)) return null;
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            if (rejectSolidYellowTiles && LooksLikeInvalidYellowTile(bitmap)) return null;
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static bool LooksLikeServiceException(byte[] bytes)
    {
        if (bytes.Length == 0) return true;
        var probeLength = Math.Min(bytes.Length, 400);
        var text = Encoding.UTF8.GetString(bytes, 0, probeLength);
        return text.Contains("ServiceException", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("ExceptionReport", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("<html", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeInvalidYellowTile(BitmapSource source)
    {
        var converted = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var width = Math.Max(1, converted.PixelWidth);
        var height = Math.Max(1, converted.PixelHeight);
        var stride = width * 4;
        var pixels = new byte[stride * height];
        converted.CopyPixels(pixels, stride, 0);

        var sampled = 0;
        var yellow = 0;
        var stepX = Math.Max(1, width / 24);
        var stepY = Math.Max(1, height / 24);
        for (var y = 0; y < height; y += stepY)
        {
            for (var x = 0; x < width; x += stepX)
            {
                var index = y * stride + x * 4;
                var b = pixels[index];
                var g = pixels[index + 1];
                var r = pixels[index + 2];
                var a = pixels[index + 3];
                if (a < 32) continue;
                sampled++;
                if (r > 235 && g > 235 && b < 80) yellow++;
            }
        }

        return sampled > 20 && yellow / (double)sampled > 0.82;
    }

    private static bool IsReportPresentationLocationMap(ReportMapRecipe recipe) =>
        recipe.Id.Equals("step1-location-cadastral", StringComparison.OrdinalIgnoreCase);

    private static (double MinX, double MinY, double MaxX, double MaxY, double PixelsPerMeter)? CalculateReportRecipeBounds(ReportMapRecipe recipe, IReadOnlyList<TracePointRow> traceRows, IReadOnlyList<RdPoint> allPoints)
    {
        var tracePoints = traceRows.Select(row => new RdPoint(row.X, row.Y)).Where(IsValidRdPoint).ToList();
        var usableWidth = Math.Max(100, recipe.Width - 48);
        var usableHeight = Math.Max(100, recipe.Height - 86);

        if (recipe.ScaleDenominator is int denominator)
        {
            var pixelsPerMeter = ReportPixelsPerMeterForScale(denominator);
            var center = GetReportRecipeCenter(recipe, traceRows);
            if (center is null && allPoints.Count > 0)
            {
                center = new RdPoint((allPoints.Min(point => point.X) + allPoints.Max(point => point.X)) / 2, (allPoints.Min(point => point.Y) + allPoints.Max(point => point.Y)) / 2);
            }
            if (center is null) return null;
            var halfWidthMeters = usableWidth / pixelsPerMeter / 2;
            var halfHeightMeters = usableHeight / pixelsPerMeter / 2;
            return (center.X - halfWidthMeters, center.Y - halfHeightMeters, center.X + halfWidthMeters, center.Y + halfHeightMeters, pixelsPerMeter);
        }

        var focus = recipe.ExtentMode.Equals("fit-trace", StringComparison.OrdinalIgnoreCase) && tracePoints.Count > 0
            ? tracePoints
            : allPoints.Where(IsValidRdPoint).ToList();
        if (focus.Count == 0) return null;
        var minX = focus.Min(point => point.X);
        var maxX = focus.Max(point => point.X);
        var minY = focus.Min(point => point.Y);
        var maxY = focus.Max(point => point.Y);
        var spanX = Math.Max(1, maxX - minX);
        var spanY = Math.Max(1, maxY - minY);
        var padding = Math.Max(8, Math.Max(spanX, spanY) * 0.16);
        minX -= padding;
        maxX += padding;
        minY -= padding;
        maxY += padding;
        if (IsReportPresentationLocationMap(recipe))
        {
            var centerX = (minX + maxX) / 2d;
            var centerY = (minY + maxY) / 2d;
            var minimumWidthMeters = 145d;
            var minimumHeightMeters = 95d;
            if (maxX - minX < minimumWidthMeters)
            {
                minX = centerX - minimumWidthMeters / 2d;
                maxX = centerX + minimumWidthMeters / 2d;
            }
            if (maxY - minY < minimumHeightMeters)
            {
                minY = centerY - minimumHeightMeters / 2d;
                maxY = centerY + minimumHeightMeters / 2d;
            }
        }
        spanX = Math.Max(1, maxX - minX);
        spanY = Math.Max(1, maxY - minY);
        var scale = Math.Min(usableWidth / spanX, usableHeight / spanY);
        return (minX, minY, maxX, maxY, scale);
    }

    private static RdPoint? GetReportRecipeCenter(ReportMapRecipe recipe, IReadOnlyList<TracePointRow> traceRows)
    {
        if (traceRows.Count < 2) return traceRows.Count == 1 ? new RdPoint(traceRows[0].X, traceRows[0].Y) : null;
        var distances = BuildTraceDistances(traceRows);
        if (distances.Count < 2 || distances[^1] <= 0) return new RdPoint(traceRows[0].X, traceRows[0].Y);
        var start = recipe.SegmentStartMeters ?? 0;
        var end = recipe.SegmentEndMeters ?? distances[^1];
        var target = Math.Clamp((start + end) / 2, 0, distances[^1]);
        var point = InterpolateTracePoint(traceRows, distances, target);
        return new RdPoint(point.X, point.Y);
    }

    private static double ReportPixelsPerMeterForScale(int scaleDenominator) =>
        3779.527559055118 / Math.Max(1, scaleDenominator);

    private static bool IsPdokReportBaseMap(string baseMap) =>
        baseMap is "pdok-brt" or "pdok-gray" or "pdok-pastel" or "pdok-bgt-pastel" or "pdok-aerial" or "osm";

    private static int ReportTileZoomForBounds(double centerLat, double metersWide, double metersHigh, double width, double height)
    {
        var targetMetersPerPixel = Math.Max(
            Math.Max(1, metersWide) / Math.Max(1, width - 48),
            Math.Max(1, metersHigh) / Math.Max(1, height - 48));
        var zoom = 19;
        for (var candidate = 19; candidate >= 10; candidate--)
        {
            if (WebMercatorMetersPerPixel(centerLat, candidate) >= targetMetersPerPixel)
            {
                zoom = candidate;
                break;
            }
        }

        return zoom;
    }

    private static bool ReportLineIntersectsBounds(IReadOnlyList<RdPoint> points, double minX, double minY, double maxX, double maxY)
    {
        if (points.Count == 0) return false;
        return points.Max(point => point.X) >= minX && points.Min(point => point.X) <= maxX &&
               points.Max(point => point.Y) >= minY && points.Min(point => point.Y) <= maxY;
    }

    private (Func<RdPoint, Point> Project, double PixelsPerMeter) AddOsmBaseMapForRdBounds(Canvas canvas, double minX, double minY, double maxX, double maxY, double width, double height, bool showKadasterOverlay = false)
    {
        var centerRd = new RdPoint((minX + maxX) / 2, (minY + maxY) / 2);
        var centerWgs = RdToWgs84(centerRd.X, centerRd.Y);
        var centerLat = centerWgs[1];
        var centerLon = centerWgs[0];
        var metersWide = Math.Max(1, maxX - minX);
        var targetMetersPerPixel = metersWide / Math.Max(1, width - 48);
        var zoom = 19;
        for (var candidate = 19; candidate >= 10; candidate--)
        {
            if (WebMercatorMetersPerPixel(centerLat, candidate) >= targetMetersPerPixel)
            {
                zoom = candidate;
                break;
            }
        }

        var centerPixel = LonLatToWebMercatorPixel(centerLon, centerLat, zoom);
        var originX = centerPixel.X - width / 2d;
        var originY = centerPixel.Y - height / 2d;
        Point Project(RdPoint point)
        {
            var wgs = RdToWgs84(point.X, point.Y);
            var pixel = LonLatToWebMercatorPixel(wgs[0], wgs[1], zoom);
            return new Point(pixel.X - originX, pixel.Y - originY);
        }

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
                if (showKadasterOverlay)
                {
                    var overlay = CreatePdokKadasterTileImage(zoom, wrappedX, tileY);
                    Canvas.SetLeft(overlay, left);
                    Canvas.SetTop(overlay, top);
                    canvas.Children.Add(overlay);
                }
            }
        }

        AddCanvasRect(canvas, 0, 0, width, height, "#FFFFFF22", "Transparent", 0);
        return (Project, 1 / WebMercatorMetersPerPixel(centerLat, zoom));
    }

    private static bool ReportRecipeIncludesLayer(ReportMapRecipe recipe, ProjectMapLayer layer)
    {
        return recipe.LayerSet switch
        {
            "cadastral" => IsBagOrKadasterLayer(layer),
            "trace-cadastral" => IsBagOrKadasterLayer(layer),
            "trace-detail" => IsBagOrKadasterLayer(layer) || IsKlicLayer(layer) || layer.Type.Contains("Ontwerp", StringComparison.OrdinalIgnoreCase) || layer.Type.Contains("DXF", StringComparison.OrdinalIgnoreCase),
            "environment-risk" => IsBagOrKadasterLayer(layer) || IsBgtLayer(layer),
            "bgt-context" => IsBgtLayer(layer) || IsBagOrKadasterLayer(layer),
            "machine-context" => IsBgtLayer(layer) || IsBagOrKadasterLayer(layer) || IsKlicLayer(layer),
            _ => true
        };
    }

    private static double ReportRecipeLayerStroke(ReportMapRecipe recipe, ProjectMapLayer layer)
    {
        if (IsBagOrKadasterLayer(layer)) return recipe.ScaleDenominator is not null ? 1.4 : 1.0;
        if (IsKlicLayer(layer)) return 2.4;
        if (IsBgtLayer(layer)) return 1.1;
        return 2.0;
    }

    private static string ReportRecipeLayerFill(ReportMapRecipe recipe, ProjectMapLayer layer, string stroke)
    {
        if (recipe.LayerSet.Equals("trace-cadastral", StringComparison.OrdinalIgnoreCase)) return "#00000000";
        if (recipe.BaseMap.StartsWith("pdok-", StringComparison.OrdinalIgnoreCase) && IsBagOrKadasterLayer(layer)) return "#00000000";
        if (recipe.LayerSet.Equals("environment-risk", StringComparison.OrdinalIgnoreCase) && IsBagOrKadasterLayer(layer)) return "#FEF3C766";
        return ReportLayerFill(layer, stroke);
    }





    private string BuildReportProjectLocation(ReportLocationContext locationContext)
    {
        var declaredLocation = GetDeclaredReportLocation();
        if (!IsPlaceholderReportLocation(_selectedProject?.Location)) return declaredLocation;
        return string.IsNullOrWhiteSpace(locationContext.Summary) ? declaredLocation : locationContext.Summary;
    }

    private string GetReportProjectDisplayName()
    {
        var name = (_selectedProject?.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) return "Project";
        if (!NeedsReportProjectPlaceCompletion(name)) return name;

        var fullName = FirstNonEmpty(
            ExtractFullProjectNameCandidate(TopActiveProjectButton.Content?.ToString(), name),
            FindSavedReportFullProjectName(name));
        if (!string.IsNullOrWhiteSpace(fullName)) return fullName;

        var place = FirstNonEmpty(
            IsUsableReportPlaceName(ExtractReportPlaceName(_selectedProject?.Location ?? ""))
                ? ExtractReportPlaceName(_selectedProject?.Location ?? "")
                : "",
            IsUsableReportPlaceName(GetDeclaredReportPlace()) ? GetDeclaredReportPlace() : "",
            FindSavedReportPlaceName(),
            FindSavedReportProjectNameCompletion(name),
            ExtractPlaceFromProjectTitle(name));

        return string.IsNullOrWhiteSpace(place) ? name : $"{name} {place}";
    }

    private static bool NeedsReportProjectPlaceCompletion(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return Regex.IsMatch(name.Trim(), @"\bte\s*$", RegexOptions.IgnoreCase);
    }

    private string FindSavedReportFullProjectName(string incompleteProjectName)
    {
        if (_selectedProject is null || string.IsNullOrWhiteSpace(incompleteProjectName)) return "";
        var candidates = new (int Step, string Key)[]
        {
            (1, "project_info"),
            (1, StepReportDataKey),
            (0, ReportStartSettingsDataKey),
            (3, StepReportDataKey),
            (ReportStepNumber, ReportSnapshotDataKey)
        };

        foreach (var (step, key) in candidates)
        {
            var json = _projects.GetStepData(_selectedProject.Id, step, key);
            var fullName = ExtractFullProjectNameCandidateFromJson(json, incompleteProjectName);
            if (!string.IsNullOrWhiteSpace(fullName)) return fullName;
        }

        return "";
    }

    private static string ExtractFullProjectNameCandidateFromJson(string? json, string incompleteProjectName)
    {
        if (string.IsNullOrWhiteSpace(json)) return "";
        try
        {
            using var document = JsonDocument.Parse(json);
            foreach (var value in EnumerateAllJsonStrings(document.RootElement))
            {
                var fullName = ExtractFullProjectNameCandidate(value, incompleteProjectName);
                if (!string.IsNullOrWhiteSpace(fullName)) return fullName;
            }
        }
        catch
        {
            return "";
        }

        return "";
    }

    private static string ExtractFullProjectNameCandidate(string? value, string incompleteProjectName)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(incompleteProjectName)) return "";
        var normalizedIncomplete = Regex.Replace(incompleteProjectName.Trim(), @"\s+", " ");
        var splitCandidate = value.Split(" - ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(candidate => candidate.StartsWith(normalizedIncomplete + " ", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(splitCandidate)) return Regex.Replace(splitCandidate.Trim(), @"\s+", " ");

        var escaped = Regex.Escape(normalizedIncomplete).Replace(@"\ ", @"\s+");
        var match = Regex.Match(
            value,
            escaped + @"\s+(?<place>[\p{L}][\p{L}'\-\s]{2,50})",
            RegexOptions.IgnoreCase);
        if (!match.Success) return "";

        var place = Regex.Replace(match.Groups["place"].Value, @"[\s'""-]+$", "").Trim();
        place = Regex.Replace(place, @"\b(in|nabij|circa|voor|met|onder|boven|en)\b.*$", "", RegexOptions.IgnoreCase).Trim();
        place = ExtractReportPlaceName(place);
        return IsUsableReportPlaceName(place) ? $"{normalizedIncomplete} {place}" : "";
    }

    private string FindSavedReportPlaceName()
    {
        if (_selectedProject is null) return "";
        var candidates = new (int Step, string Key)[]
        {
            (1, "project_info"),
            (1, StepReportDataKey),
            (3, StepReportDataKey),
            (ReportStepNumber, ReportSnapshotDataKey)
        };

        foreach (var (step, key) in candidates)
        {
            var json = _projects.GetStepData(_selectedProject.Id, step, key);
            var place = ExtractReportPlaceNameFromJson(json);
            if (!string.IsNullOrWhiteSpace(place)) return place;
        }

        return "";
    }

    private string FindSavedReportProjectNameCompletion(string incompleteProjectName)
    {
        if (_selectedProject is null || string.IsNullOrWhiteSpace(incompleteProjectName)) return "";
        var candidates = new (int Step, string Key)[]
        {
            (1, "project_info"),
            (1, StepReportDataKey),
            (0, ReportStartSettingsDataKey),
            (ReportStepNumber, ReportSnapshotDataKey)
        };

        foreach (var (step, key) in candidates)
        {
            var json = _projects.GetStepData(_selectedProject.Id, step, key);
            var place = ExtractProjectNameCompletionFromJson(json, incompleteProjectName);
            if (!string.IsNullOrWhiteSpace(place)) return place;
        }

        return "";
    }

    private static string ExtractProjectNameCompletionFromJson(string? json, string incompleteProjectName)
    {
        if (string.IsNullOrWhiteSpace(json)) return "";
        try
        {
            using var document = JsonDocument.Parse(json);
            foreach (var value in EnumerateAllJsonStrings(document.RootElement))
            {
                var place = ExtractProjectNameCompletion(value, incompleteProjectName);
                if (IsUsableReportPlaceName(place)) return place;
            }
        }
        catch
        {
            return "";
        }

        return "";
    }

    private static string ExtractProjectNameCompletion(string value, string incompleteProjectName)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(incompleteProjectName)) return "";
        var escapedName = Regex.Escape(incompleteProjectName.Trim());
        var match = Regex.Match(
            value,
            escapedName + @"\s+(?<place>[A-Za-zÀ-ÿ][A-Za-zÀ-ÿ'’\-\s]{2,40})",
            RegexOptions.IgnoreCase);
        if (!match.Success) return "";

        var place = match.Groups["place"].Value;
        place = Regex.Replace(place, @"[\s'’""]+$", "").Trim();
        place = Regex.Replace(place, @"\b(in|nabij|circa|voor|met|onder|boven|en)\b.*$", "", RegexOptions.IgnoreCase).Trim();
        return ExtractReportPlaceName(place);
    }

    private static IEnumerable<string> EnumerateAllJsonStrings(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    foreach (var value in EnumerateAllJsonStrings(property.Value))
                    {
                        yield return value;
                    }
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var value in EnumerateAllJsonStrings(item))
                    {
                        yield return value;
                    }
                }
                break;
            case JsonValueKind.String:
                var text = element.GetString();
                if (!string.IsNullOrWhiteSpace(text)) yield return text;
                break;
        }
    }

    private static string ExtractReportPlaceNameFromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "";
        try
        {
            using var document = JsonDocument.Parse(json);
            foreach (var value in EnumerateReportLocationStrings(document.RootElement))
            {
                var place = ExtractReportPlaceName(value);
                if (IsUsableReportPlaceName(place)) return place;
            }
        }
        catch
        {
            return "";
        }

        return "";
    }

    private static IEnumerable<string> EnumerateReportLocationStrings(JsonElement element, string propertyName = "")
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    foreach (var value in EnumerateReportLocationStrings(property.Value, property.Name))
                    {
                        yield return value;
                    }
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var value in EnumerateReportLocationStrings(item, propertyName))
                    {
                        yield return value;
                    }
                }
                break;
            case JsonValueKind.String when IsReportLocationProperty(propertyName):
                var text = element.GetString();
                if (!string.IsNullOrWhiteSpace(text)) yield return text;
                break;
        }
    }

    private static bool IsReportLocationProperty(string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName)) return false;
        return propertyName.Contains("location", StringComparison.OrdinalIgnoreCase) ||
               propertyName.Contains("locatie", StringComparison.OrdinalIgnoreCase) ||
               propertyName.Contains("plaats", StringComparison.OrdinalIgnoreCase) ||
               propertyName.Contains("place", StringComparison.OrdinalIgnoreCase) ||
               propertyName.Contains("summary", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUsableReportPlaceName(string place)
    {
        if (string.IsNullOrWhiteSpace(place)) return false;
        var normalized = place.Trim().Trim('.', '-').ToLowerInvariant();
        if (normalized.Length < 3) return false;
        if (IsPlaceholderReportLocation(normalized)) return false;
        if (normalized.Contains("niet ingevuld", StringComparison.OrdinalIgnoreCase)) return false;
        if (Regex.IsMatch(normalized, @"^\d+[.,]\d+(\s+\d+[.,]\d+)?$")) return false;
        return true;
    }

    private static string ExtractPlaceFromProjectTitle(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var match = Regex.Match(value, @"\bte\s+(.+?)(?:\s*[-–|].*)?$", RegexOptions.IgnoreCase);
        return match.Success ? ExtractReportPlaceName(match.Groups[1].Value) : "";
    }

    private static string ExtractReportPlaceName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var cleaned = value.Trim();
        var beforeParenthesis = Regex.Replace(cleaned, @"\s*\(.*$", "").Trim();
        if (!string.IsNullOrWhiteSpace(beforeParenthesis) &&
            !beforeParenthesis.Contains("circa", StringComparison.OrdinalIgnoreCase) &&
            !beforeParenthesis.Contains(",", StringComparison.Ordinal))
        {
            return beforeParenthesis;
        }

        var parts = cleaned
            .Split([',', '(', ')'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !part.Contains("circa", StringComparison.OrdinalIgnoreCase) &&
                           !Regex.IsMatch(part, @"\d+[.,]\d+"))
            .ToList();
        return parts.FirstOrDefault() ?? "";
    }

    private double GetReportBoreLengthMeters(IReadOnlyList<TracePointRow> traceRows, double traceLength)
    {
        if (traceRows.Count >= 2 && double.IsFinite(traceLength) && traceLength > 0)
        {
            return Math.Round(traceLength, 1);
        }

        return Math.Max(1, _selectedProject?.BoreLengthMeters ?? 1);
    }

    private string GetDeclaredReportLocation()
    {
        if (!IsPlaceholderReportLocation(_selectedProject?.Location)) return _selectedProject!.Location.Trim();
        var place = GetDeclaredReportPlace();
        return string.IsNullOrWhiteSpace(place) ? "projectlocatie niet ingevuld" : place;
    }

    private string GetDeclaredReportPlace()
    {
        if (!IsPlaceholderReportLocation(_selectedProject?.Location)) return _selectedProject!.Location.Trim();
        var name = _selectedProject?.Name ?? "";
        var match = Regex.Match(name, @"\bte\s+(.+?)(?:\s*[-–|].*)?$", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : "";
    }

    private static string ReconcileReportLocationSummary(string summary, string road, string houseNumber, string reversePlace, string declaredPlace, string fallback)
    {
        if (string.IsNullOrWhiteSpace(declaredPlace)) return summary;
        if (string.IsNullOrWhiteSpace(reversePlace)) return declaredPlace;
        if (reversePlace.Equals(declaredPlace, StringComparison.OrdinalIgnoreCase)) return summary;

        var roadPart = string.IsNullOrWhiteSpace(road)
            ? ""
            : string.IsNullOrWhiteSpace(houseNumber) ? road : $"{road} {houseNumber}";
        return string.IsNullOrWhiteSpace(roadPart)
            ? declaredPlace
            : $"{declaredPlace} (nabij {roadPart})";
    }

    private static bool IsPlaceholderReportLocation(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return true;
        var normalized = location.Trim().Trim('.', '-').ToLowerInvariant();
        return normalized is "x" or "xx" or "xxx" or "nvt" or "n.v.t" or "n.v.t." or "onbekend" or "unknown" or "locatie";
    }

    private static string ReadAddressPart(JsonElement address, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (address.TryGetProperty(key, out var element))
            {
                var value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
        }

        return "";
    }



    private static void AddBoringCanvasDimensions(Canvas canvas, BoringResult boring, double cx, double cy, double boreRadius, double scale)
    {
        AddReportDimensionLine(canvas, cx - boreRadius, 38, cx + boreRadius, 38, $"Vereiste boring Ø{boring.BoringDiameter:N0} mm", labelAbove: true);
        if (boring.BundleDiameter > 0)
        {
            var bundleRadius = Math.Clamp((boring.BundleDiameter / 2d) * scale, 18, boreRadius);
            AddReportDimensionLine(canvas, cx - bundleRadius, 334, cx + bundleRadius, 334, $"Productbundel Ø{boring.BundleDiameter:N0} mm", labelAbove: false);
        }
    }


    private static double AddBoringCanvasLegend(Canvas canvas, BoringResult boring, double left, double top, double width)
    {
        var entries = BuildBoringLegendEntries(boring).Take(10).ToList();
        var height = 38 + entries.Count * 20;
        AddCanvasRect(canvas, left, top, width, height, "#FFFFFFF4", "#CBD5E1", 1);
        AddCanvasText(canvas, "Legenda doorsnede", left + 12, top + 10, "#334155", 10.5, FontWeights.Bold);
        var y = top + 34;
        foreach (var entry in entries)
        {
            AddCanvasCircle(canvas, left + 17, y + 5, 5, entry.Color, entry.Color, 1);
            AddCanvasText(canvas, entry.Label, left + 30, y - 2, "#475569", 8.8, FontWeights.Normal);
            y += 20;
        }
        return top + height;
    }

    private static void AddBoringCanvasDimensionTable(Canvas canvas, BoringResult boring, double left, double top, double width)
    {
        var fillPercentage = boring.BoringDiameter > 0
            ? Math.Pow(boring.BundleDiameter / boring.BoringDiameter, 2) * 100d
            : 0d;
        AddCanvasRect(canvas, left, top, width, 128, "#FFFFFFF4", "#CBD5E1", 1);
        AddCanvasText(canvas, "Afmetingen", left + 12, top + 10, "#334155", 10.5, FontWeights.Bold);
        var rows = new[]
        {
            ("Vereiste boring", $"Ø{boring.BoringDiameter:N0} mm"),
            ("Productbundel", $"Ø{boring.BundleDiameter:N0} mm"),
            ("Vulgraad", $"{fillPercentage:N1}%"),
            ("Aantal elementen", boring.Processed.Count.ToString(CultureInfo.InvariantCulture)),
            ("Maatvoering", "Alle maten buitenwerks")
        };
        var y = top + 34;
        foreach (var (label, value) in rows)
        {
            AddCanvasText(canvas, label, left + 12, y, "#587080", 9.5, FontWeights.Normal);
            AddCanvasText(canvas, value, left + width - 96, y, "#071422", 9.5, FontWeights.SemiBold);
            y += 18;
        }
    }

    private static IEnumerable<(string Color, string Label)> BuildBoringLegendEntries(BoringResult boring)
    {
        yield return ("#C4A45A", "Boorgat / grond");
        yield return ("#C2D6DF", "Bentoniet / boorvloeistof");

        foreach (var group in boring.Processed.GroupBy(item => $"{item.Item.Type}|{item.Item.Dn}|{item.Item.Label}|{item.EffectiveOutsideDiameter:N0}", StringComparer.OrdinalIgnoreCase))
        {
            var item = group.First();
            var prefix = group.Count() > 1 ? $"{group.Count()}x " : "";
            if (item.Item.Type == BoringItemType.Mantelbuis)
            {
                yield return (PeTubeColor(item.Item), $"{prefix}PE {item.Item.Dn} mantelbuis · Ø{item.EffectiveOutsideDiameter:N0} mm");
            }
            else
            {
                yield return (item.Color, $"{prefix}{item.Item.Label} · Ø{item.EffectiveOutsideDiameter:N0} mm");
            }
        }

        foreach (var group in boring.Processed.SelectMany(item => item.Item.Contents).GroupBy(content => $"{content.Label}|{content.OutsideDiameter:N0}", StringComparer.OrdinalIgnoreCase))
        {
            var content = group.First();
            var prefix = group.Count() > 1 ? $"{group.Count()}x " : "";
            yield return (content.Color, $"{prefix}{content.Label} · Ø{content.OutsideDiameter:N0} mm");
        }
    }



    private static double NiceScaleBarMeters(double targetMeters)
    {
        if (!double.IsFinite(targetMeters) || targetMeters <= 0) return 10;
        var exponent = Math.Pow(10, Math.Floor(Math.Log10(targetMeters)));
        foreach (var factor in new[] { 1d, 2d, 5d, 10d })
        {
            var value = factor * exponent;
            if (value >= targetMeters * 0.65) return value;
        }
        return 10 * exponent;
    }

    private static string FormatScaleMeters(double meters) =>
        meters >= 1000 ? $"{meters / 1000:N1} km" : $"{meters:N0} m";

    private IEnumerable<List<RdPoint>> EnumerateReportFeatureLines(ProjectMapLayer layer)
    {
        foreach (var feature in layer.FeatureCollection.Features)
        {
            foreach (var line in EnumerateFeatureGeometryLines(feature))
            {
                yield return line;
            }
        }
    }

    private IEnumerable<List<RdPoint>> EnumerateFeatureGeometryLines(GeoJsonFeature feature)
    {
        if (feature.Geometry.Type.Equals("Point", StringComparison.OrdinalIgnoreCase))
        {
            if (TryGetCoordinate(feature.Geometry.Coordinates, out var coordinate)) yield return [ToRdPoint(coordinate)];
            yield break;
        }

        if (feature.Geometry.Type.Equals("LineString", StringComparison.OrdinalIgnoreCase))
        {
            var line = ExtractCoordinateList(feature.Geometry.Coordinates).Select(ToRdPoint).ToList();
            if (line.Count > 0) yield return line;
            yield break;
        }

        if (feature.Geometry.Type.Equals("Polygon", StringComparison.OrdinalIgnoreCase) ||
            feature.Geometry.Type.Equals("MultiLineString", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var line in ExtractNestedCoordinateLists(feature.Geometry.Coordinates).Select(list => list.Select(ToRdPoint).ToList()))
            {
                if (line.Count > 0) yield return line;
            }
        }
        else if (feature.Geometry.Type.Equals("MultiPolygon", StringComparison.OrdinalIgnoreCase) &&
                 feature.Geometry.Coordinates is System.Collections.IEnumerable polygons &&
                 feature.Geometry.Coordinates is not string)
        {
            foreach (var polygon in polygons)
            {
                foreach (var line in ExtractNestedCoordinateLists(polygon).Select(list => list.Select(ToRdPoint).ToList()))
                {
                    if (line.Count > 0) yield return line;
                }
            }
        }
    }

    private IEnumerable<WorkDrawingLine> EnumerateWorkDrawingLines(ProjectMapLayer layer)
    {
        foreach (var feature in layer.FeatureCollection.Features)
        {
            var theme = NormalizeKlicTheme(GetFeatureProperty(feature, "theme"));
            var color = GetFeatureProperty(feature, "color");
            var label = layer.Name;

            if (IsKlicLayer(layer))
            {
                if (string.IsNullOrWhiteSpace(color)) color = KlicThemeColor(theme);
                label = KlicThemeLabel(theme);
            }
            else if (IsBagOrKadasterLayer(layer))
            {
                theme = "kadaster";
                color = "#0057D8";
                label = "Kadaster/BAG";
            }
            else if (IsBgtLayer(layer))
            {
                theme = "bgt";
                color = "#94A3B8";
                label = "BGT";
            }
            else if (string.IsNullOrWhiteSpace(color))
            {
                color = layer.Color;
            }

            foreach (var line in EnumerateFeatureGeometryLines(feature))
            {
                yield return new WorkDrawingLine(layer, line, theme, color, label);
            }
        }
    }

    private static string GetFeatureProperty(GeoJsonFeature feature, string key)
    {
        if (!feature.Properties.TryGetValue(key, out var value) || value is null) return "";
        return value switch
        {
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString() ?? "",
            JsonElement element when element.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => element.ToString(),
            string text => text,
            _ => value.ToString() ?? ""
        };
    }

    private static IReadOnlyDictionary<string, object>? GetFeatureDetailProperties(GeoJsonFeature feature)
    {
        var detailId = GetFeatureProperty(feature, "detailId");
        return !string.IsNullOrWhiteSpace(detailId) && MapFeatureDetails.TryGetValue(detailId, out var details)
            ? details
            : null;
    }

    private static string GetDetailProperty(IReadOnlyDictionary<string, object>? details, string key)
    {
        if (details is null || !details.TryGetValue(key, out var value) || value is null) return "";
        return Regex.Replace(value.ToString() ?? "", @"\s+", " ").Trim();
    }

    private static int ReportLayerDrawOrder(string type)
    {
        if (type.Contains("BGT", StringComparison.OrdinalIgnoreCase) || type.Contains("BAG", StringComparison.OrdinalIgnoreCase)) return 0;
        if (type.Contains("KLIC", StringComparison.OrdinalIgnoreCase)) return 2;
        return 1;
    }

    private static bool IsBgtLayer(ProjectMapLayer layer) =>
        layer.Type.Contains("BGT", StringComparison.OrdinalIgnoreCase) ||
        layer.Name.Contains("BGT", StringComparison.OrdinalIgnoreCase);

    private static bool IsBagOrKadasterLayer(ProjectMapLayer layer) =>
        layer.Type.Contains("BAG", StringComparison.OrdinalIgnoreCase) ||
        layer.Type.Contains("Kadaster", StringComparison.OrdinalIgnoreCase) ||
        layer.Name.Contains("BAG", StringComparison.OrdinalIgnoreCase) ||
        layer.Name.Contains("Kadaster", StringComparison.OrdinalIgnoreCase);


    private static bool IsDesignOrCustomLayer(ProjectMapLayer layer) =>
        layer.Type.Contains("Ontwerp", StringComparison.OrdinalIgnoreCase) ||
        layer.Type.Contains("Design", StringComparison.OrdinalIgnoreCase) ||
        layer.Type.Contains("DXF", StringComparison.OrdinalIgnoreCase) ||
        layer.Type.Contains("Custom", StringComparison.OrdinalIgnoreCase) ||
        layer.Name.Contains("Ontwerp", StringComparison.OrdinalIgnoreCase) ||
        layer.Name.Contains("Design", StringComparison.OrdinalIgnoreCase) ||
        layer.Name.Contains("DXF", StringComparison.OrdinalIgnoreCase) ||
        layer.Name.Contains("Custom", StringComparison.OrdinalIgnoreCase);

    private static string ReportLayerColor(ProjectMapLayer layer, IReadOnlyList<RdPoint> points)
    {
        if (IsBagOrKadasterLayer(layer)) return "#0057D8";
        if (IsBgtLayer(layer)) return "#CBD5E1";

        var featureColor = layer.FeatureCollection.Features
            .Select(feature => feature.Properties.TryGetValue("color", out var color) ? color?.ToString() : null)
            .FirstOrDefault(color => !string.IsNullOrWhiteSpace(color));
        return string.IsNullOrWhiteSpace(featureColor) ? layer.Color : featureColor!;
    }

    private static string ReportLayerFill(ProjectMapLayer layer, string stroke)
    {
        if (layer.Type.Contains("water", StringComparison.OrdinalIgnoreCase)) return "#BFE7F5";
        if (IsBgtLayer(layer)) return "#FFFFFF";
        if (IsBagOrKadasterLayer(layer)) return "#EAF1FF";
        return "#EEF2F7";
    }

    private static bool ReportLooksClosed(IReadOnlyList<RdPoint> points)
    {
        if (points.Count < 4) return false;
        var first = points[0];
        var last = points[^1];
        return Math.Abs(first.X - last.X) < 0.01 && Math.Abs(first.Y - last.Y) < 0.01;
    }








    private static void ApplyLocalImageSource(Image image, string path) => TryApplyLocalImageSource(image, path);

    private static bool TryApplyLocalImageSource(Image image, string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                image.Source = null;
                return false;
            }

            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var memory = new MemoryStream();
            file.CopyTo(memory);
            memory.Position = 0;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.StreamSource = memory;
            bitmap.EndInit();
            bitmap.Freeze();
            image.Source = bitmap;
            return true;
        }
        catch
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                image.Source = bitmap;
                return true;
            }
            catch
            {
                image.Source = null;
                return false;
            }
        }
    }



    private void ApplyStepThreeLayoutBounds()
    {
        if (StepThreePanel.Visibility != Visibility.Visible) return;
        var compact = IsCompactShell();
        var fallbackAvailable = WorkflowPanel.ActualHeight > 0 ? WorkflowPanel.ActualHeight - 100 : ActualHeight - 132;
        var standardMapHeight = GetStandardGisMapFrameHeight(compact);
        var toolbarAllowance = compact ? 76d : 84d;

        if (_selectedStep?.Number == 3)
        {
            StepThreePanel.Height = standardMapHeight + toolbarAllowance;
            StepThreePanel.MaxHeight = StepThreePanel.Height;
            ApplyStepThreeMapFrameBounds();
            return;
        }

        if (_selectedStep?.Number == ProfileStepNumber)
        {
            var profileTarget = standardMapHeight + (compact ? 520d : 600d);
            StepThreePanel.Height = Math.Clamp(profileTarget, compact ? 720d : 820d, compact ? 900d : 1040d);
            StepThreePanel.MaxHeight = StepThreePanel.Height;
            ApplyStepThreeMapFrameBounds();
            return;
        }
        if (_selectedStep?.Number == 4 && StepSurfaceAnalysisPanel.Visibility == Visibility.Visible)
        {
            if (StepThreeMapFrame.Visibility != Visibility.Visible)
            {
                // Oppervlakteprofiel tab: the GIS map is hidden, so size to the analysis
                // content. Otherwise the empty star-sized map row leaves a big gap above
                // the bar and segments.
                StepThreePanel.Height = double.NaN;
                StepThreePanel.MaxHeight = double.PositiveInfinity;
                return;
            }

            var surfaceTarget = standardMapHeight + (compact ? 360d : 430d);
            StepThreePanel.Height = Math.Clamp(surfaceTarget, compact ? 660d : 780d, compact ? 820d : 940d);
            StepThreePanel.MaxHeight = StepThreePanel.Height;
            ApplyStepThreeMapFrameBounds();
            return;
        }
        if (_selectedStep?.Number == 4)
        {
            StepThreePanel.Height = standardMapHeight + toolbarAllowance;
            StepThreePanel.MaxHeight = StepThreePanel.Height;
            ApplyStepThreeMapFrameBounds();
            return;
        }
        if (ShouldUseBroReportMapAspect())
        {
            StepThreePanel.Height = Math.Clamp(fallbackAvailable, 360d, 470d);
            StepThreePanel.MaxHeight = StepThreePanel.Height;
            ApplyStepThreeMapFrameBounds();
            return;
        }

        StepThreePanel.Height = standardMapHeight + toolbarAllowance;
        StepThreePanel.MaxHeight = StepThreePanel.Height;
        ApplyStepThreeMapFrameBounds();
    }


    private static double GetStandardGisMapFrameHeight(bool compact) =>
        compact ? StandardGisMapCompactHeight : StandardGisMapRegularHeight;

    private void ApplyStepThreeMapFrameBounds()
    {
        if (_applyingStepThreeMapFrameBounds) return;
        _applyingStepThreeMapFrameBounds = true;
        try
        {
            if (ShouldUseBroReportMapAspect())
            {
                var width = StepThreeMapFrame.ActualWidth;
                if (width <= 0 && StepThreePanel.ActualWidth > 0)
                {
                    width = StepThreePanel.ActualWidth;
                }

                if (width <= 0)
                {
                    width = 724d;
                }

                var targetHeight = Math.Clamp(width / BroReportMapAspectRatio, BroReportMapMinAppHeight, BroReportMapMaxAppHeight);
                if (double.IsNaN(StepThreeMapFrame.Height) || Math.Abs(StepThreeMapFrame.Height - targetHeight) > 1)
                {
                    StepThreeMapFrame.Height = targetHeight;
                }

                StepThreeMapFrame.VerticalAlignment = VerticalAlignment.Top;
            }
            else
            {
                var targetHeight = GetStandardGisMapFrameHeight(IsCompactShell());
                if (double.IsNaN(StepThreeMapFrame.Height) || Math.Abs(StepThreeMapFrame.Height - targetHeight) > 1)
                {
                    StepThreeMapFrame.Height = targetHeight;
                }

                StepThreeMapFrame.VerticalAlignment = VerticalAlignment.Top;
            }
        }
        finally
        {
            _applyingStepThreeMapFrameBounds = false;
        }
    }

    private void StepThreeMapFrame_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (StepThreePanel.Visibility != Visibility.Visible) return;
        ApplyStepThreeMapFrameBounds();
        if (_mapLibreLoaded && StepThreeMapView.CoreWebView2 is not null)
        {
            Dispatcher.BeginInvoke(() => SendMapMessage("{\"type\":\"resize\"}"), DispatcherPriority.Background);
        }
    }

    private void LayerToggle_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedStep is null) return;
        RenderGisMap(_workspaces[_selectedStep.Number]);
    }



    private void RenderWorkDrawingStepSidebar()
    {
        var signature = BuildWorkDrawingSidebarSignature();
        if (string.Equals(_lastWorkDrawingSidebarSignature, signature, StringComparison.Ordinal))
        {
            return;
        }

        _lastWorkDrawingSidebarSignature = signature;
        StepSpecificRibbonPanel.Children.Clear();
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

        RenderWorkDrawingSidebarPanel();
        RenderKlicDocumentsPanel();
        RenderAiAnalysisPanel();
        RenderInformationPlaceholders();

        SidebarImportsTab.Visibility = Visibility.Collapsed;
        SidebarBoreTraceTab.Visibility = Visibility.Collapsed;
        SidebarProfileTab.Visibility = Visibility.Collapsed;
        SidebarWorkDrawingTab.Visibility = Visibility.Visible;
        SidebarAnalysisTab.Visibility = Visibility.Collapsed;
        SidebarMapLayersTab.Visibility = Visibility.Collapsed;
        SidebarKlicDocsTab.Visibility = Visibility.Collapsed;
        SidebarAiTab.Visibility = Visibility.Collapsed;
        SidebarKlicInfoTab.Visibility = Visibility.Collapsed;
        SidebarBgtInfoTab.Visibility = Visibility.Collapsed;
        SetSidebarTab("workdrawing");
    }

    private string BuildWorkDrawingSidebarSignature()
    {
        return string.Join(";",
            _selectedProject?.Id.ToString("N") ?? "geen-project",
            _selectedStep?.Number.ToString(CultureInfo.InvariantCulture) ?? "geen-stap",
            _selectedSubstep?.Number ?? "geen-substap",
            _activeSidebarTab,
            _projectFiles.Count.ToString(CultureInfo.InvariantCulture),
            _profilePoints.Count.ToString(CultureInfo.InvariantCulture),
            _workDrawingScale.ToString(CultureInfo.InvariantCulture),
            _selectedMachineId ?? "");
    }

    private void RenderProjectInfoRows()
    {
        ProjectInfoRows.Children.Clear();
        if (_selectedProject is null) return;
        var metadata = ReadProjectHeaderMetadata();

        AddProjectRow("Projectnaam", _selectedProject.Name);
        AddProjectRow("Datum", metadata.ReportDate);
        AddProjectRow("Projectnummer intern", metadata.InternalProjectNumber);
        AddProjectRow("Projectnummer extern", metadata.ExternalProjectNumber);
        AddProjectRow("Opdrachtgever", _selectedProject.Client);
        AddProjectRow("Locatie", _selectedProject.Location);
        AddProjectRow("Boorlengte", $"{_selectedProject.BoreLengthMeters} m");
        AddProjectRow("Diameter", $"Ø{_selectedProject.DiameterMillimeters} mm");
        AddProjectRow("Materiaal", _selectedProject.Material);
        AddProjectRow("Status", _selectedProject.Status);
    }

    private void AddProjectRow(string label, string value)
    {
        var row = new Grid { Margin = new Thickness(10, 0, 10, 0), MinHeight = 27 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Brush("#4B6270"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        var valueBlock = new TextBlock
        {
            Text = value,
            Foreground = Brush("#0D1520"),
            FontSize = 11,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(valueBlock, 1);
        row.Children.Add(valueBlock);

        var editButton = new Button
        {
            Content = "...",
            Tag = label,
            Width = 28,
            Height = 20,
            Padding = new Thickness(0),
            Margin = new Thickness(6, 2, 0, 2),
            FontSize = 11,
            ToolTip = $"{label} aanpassen"
        };
        editButton.Click += EditProjectField_OnClick;
        Grid.SetColumn(editButton, 2);
        row.Children.Add(editButton);

        ProjectInfoRows.Children.Add(row);
        ProjectInfoRows.Children.Add(new Border { BorderBrush = Brush("#F1F4F6"), BorderThickness = new Thickness(0, 1, 0, 0) });
    }

    private void EditProjectField_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedProject is null || sender is not Button { Tag: string field }) return;
        var metadata = ReadProjectHeaderMetadata();

        var current = field switch
        {
            "Projectnaam" => _selectedProject.Name,
            "Datum" => metadata.ReportDate,
            "Projectnummer intern" => metadata.InternalProjectNumber,
            "Projectnummer extern" => metadata.ExternalProjectNumber,
            "Opdrachtgever" => _selectedProject.Client,
            "Locatie" => _selectedProject.Location,
            "Boorlengte" => _selectedProject.BoreLengthMeters.ToString("0.#", CultureInfo.InvariantCulture),
            "Diameter" => _selectedProject.DiameterMillimeters.ToString(CultureInfo.InvariantCulture),
            "Materiaal" => _selectedProject.Material,
            "Status" => _selectedProject.Status,
            _ => ""
        };

        var updated = PromptForProjectValue($"{field} aanpassen", current);
        if (updated is null) return;
        updated = updated.Trim();
        if (updated.Length == 0 && field is not ("Datum" or "Projectnummer intern" or "Projectnummer extern")) return;

        if (field == "Boorlengte")
        {
            if (!TryReadDouble(updated, out var meters))
            {
                MessageBox.Show("Vul een geldige boorlengte in meters in.", "Borevexa", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _selectedProject.BoreLengthMeters = Math.Round(meters, 1);
        }
        else if (field == "Diameter")
        {
            if (!TryReadDouble(updated, out var diameter))
            {
                MessageBox.Show("Vul een geldige diameter in millimeters in.", "Borevexa", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _selectedProject.DiameterMillimeters = Math.Max(1, (int)Math.Round(diameter));
        }
        else
        {
            switch (field)
            {
                case "Projectnaam": _selectedProject.Name = updated; break;
                case "Datum": metadata = metadata with { ReportDate = updated }; break;
                case "Projectnummer intern": metadata = metadata with { InternalProjectNumber = updated }; break;
                case "Projectnummer extern": metadata = metadata with { ExternalProjectNumber = updated }; break;
                case "Opdrachtgever": _selectedProject.Client = updated; break;
                case "Locatie": _selectedProject.Location = updated; break;
                case "Materiaal": _selectedProject.Material = updated; break;
                case "Status": _selectedProject.Status = updated; break;
            }
        }

        SaveProjectSnapshot(metadata);
        RenderStepOne();
        RefreshProjects();
        UpdateTopProjectButton();
        RefreshInlineReportPreviewIfVisible();
        OutputText.Text = $"Projectgegevens opgeslagen\n\n{field}: {updated}";
    }

    private string? PromptForProjectValue(string title, string currentValue)
    {
        var dialog = new Window
        {
            Title = title,
            Owner = this,
            Width = 360,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = Brush("#F5F7F9")
        };
        var root = new DockPanel { Margin = new Thickness(12) };
        var input = new TextBox { Text = currentValue, Margin = new Thickness(0, 0, 0, 10), Height = 28, VerticalContentAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(input, Dock.Top);
        root.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Annuleren", Width = 84, Margin = new Thickness(0, 0, 8, 0) };
        var ok = new Button { Content = "Opslaan", Width = 84, Background = Brush("#3F4750"), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        cancel.Click += (_, _) => { dialog.DialogResult = false; dialog.Close(); };
        ok.Click += (_, _) => { dialog.DialogResult = true; dialog.Close(); };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        dialog.Content = root;
        input.SelectAll();
        return dialog.ShowDialog() == true ? input.Text : null;
    }

    private static bool TryReadDouble(string input, out double value)
    {
        var cleaned = Regex.Replace(input, @"[^0-9,\.\-]", "").Replace(',', '.');
        return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private ProjectHeaderMetadata ReadProjectHeaderMetadata()
    {
        var fallbackDate = DateTime.Now.ToString("dd-MM-yyyy", CultureInfo.CurrentCulture);
        if (_selectedProject is null) return new ProjectHeaderMetadata(fallbackDate, "", "");

        var json = _projects.GetStepData(_selectedProject.Id, 1, "project_info");
        if (string.IsNullOrWhiteSpace(json)) return new ProjectHeaderMetadata(fallbackDate, "", "");

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var reportDate = FirstNonEmpty(
                JsonText(root, "reportDate", ""),
                JsonText(root, "date", ""),
                JsonText(root, "projectDate", ""),
                fallbackDate);
            return new ProjectHeaderMetadata(
                reportDate,
                FirstNonEmpty(JsonText(root, "internalProjectNumber", ""), JsonText(root, "projectNumberInternal", "")),
                FirstNonEmpty(JsonText(root, "externalProjectNumber", ""), JsonText(root, "projectNumberExternal", "")));
        }
        catch
        {
            return new ProjectHeaderMetadata(fallbackDate, "", "");
        }
    }

    private void SaveProjectSnapshot(ProjectHeaderMetadata? metadata = null)
    {
        if (_selectedProject is null) return;
        metadata ??= ReadProjectHeaderMetadata();
        _projects.UpdateProject(_selectedProject);
        SaveSelectedProjectStepData(1, "project_info", JsonSerializer.Serialize(new
        {
            savedAt = DateTimeOffset.Now,
            reportDate = metadata.ReportDate,
            internalProjectNumber = metadata.InternalProjectNumber,
            externalProjectNumber = metadata.ExternalProjectNumber,
            _selectedProject.Name,
            _selectedProject.Client,
            _selectedProject.Location,
            _selectedProject.BoreLengthMeters,
            _selectedProject.DiameterMillimeters,
            _selectedProject.Material,
            _selectedProject.Status
        }, JsonOptions));
    }

    private async void StepQuickSave_OnClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_selectedProject is null || sender is not Button button || !int.TryParse(button.Tag?.ToString(), out var stepNumber)) return;

        SaveStepByNumber(stepNumber);
        if (_selectedStep?.Number == stepNumber && stepNumber is >= 3 and <= ThreeDStepNumber && stepNumber != ReportStepNumber)
        {
            SendMapMessage("{\"type\":\"save\"}");
        }
        var oldContent = button.Content;
        var oldBackground = button.Background;
        var oldForeground = button.Foreground;
        button.Content = "OK";
        button.Background = Brush("#4B5563");
        button.Foreground = Brushes.White;
        await System.Threading.Tasks.Task.Delay(900);
        button.Content = oldContent;
        button.Background = oldBackground;
        button.Foreground = oldForeground;
    }

    private async void HeaderStepSave_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedProject is null || _selectedStep is null)
        {
            OutputText.Text = "Geen project actief. Open of maak eerst een project.";
            return;
        }

        var stepNumber = _selectedStep.Number;
        SaveStepByNumber(stepNumber);
        if (stepNumber is >= 3 and <= ThreeDStepNumber && stepNumber != ReportStepNumber)
        {
            SendMapMessage("{\"type\":\"save\"}");
        }
        RefreshWorkflowReportStatus(stepNumber);

        var oldContent = HeaderStepSaveButton.Content;
        var oldBackground = HeaderStepSaveButton.Background;
        var oldForeground = HeaderStepSaveButton.Foreground;
        HeaderStepSaveButton.Content = "? Opgeslagen";
        HeaderStepSaveButton.Background = Brush("#DCFCE7");
        HeaderStepSaveButton.Foreground = Brush("#15803D");
        await System.Threading.Tasks.Task.Delay(1000);
        HeaderStepSaveButton.Content = oldContent;
        HeaderStepSaveButton.Background = oldBackground;
        HeaderStepSaveButton.Foreground = oldForeground;
    }

    private void SaveStepByNumber(int stepNumber)
    {
        if (_selectedProject is null) return;
        SaveProjectSnapshot();
        if (stepNumber == 1)
        {
            SaveStepOneLayoutState();
            SaveBoringConfigSnapshot();
        }
        if (stepNumber >= 3 && stepNumber <= ThreeDStepNumber && stepNumber != ReportStepNumber) SaveMapStateForStep(stepNumber, false);
        if (IsBorelineStep(stepNumber)) RequestBoreTraceSave();
        if (stepNumber == ProfileStepNumber) SaveDepthProfile();
        if (stepNumber == MachineStepNumber) SaveMachinePlacements();
        SaveStepReportDataForStep(stepNumber);
        SaveSelectedProjectStepData(stepNumber, "step_save", JsonSerializer.Serialize(new
        {
            savedAt = DateTimeOffset.Now,
            stepNumber,
            baseLayer = _selectedMapBaseLayer,
            overlays = _mapOverlayStates,
            klicThemes = _klicThemeStates
        }, JsonOptions));
        RefreshWorkflowReportStatus(stepNumber);
        OutputText.Text = $"Stap {stepNumber} opgeslagen.";
    }

    private void SaveWholeProject()
    {
        if (_selectedProject is null) return;

        SaveProjectSnapshot();
        SaveStepOneLayoutState();
        SaveBoringConfigSnapshot();

        if (_selectedStep is not null && _selectedStep.Number >= 3 && _selectedStep.Number <= ThreeDStepNumber && _selectedStep.Number != ReportStepNumber)
        {
            SaveMapStateForStep(_selectedStep.Number, false);
            SendMapMessage("{\"type\":\"save\"}");
        }

        if (_selectedStep is not null && IsBorelineStep(_selectedStep.Number))
        {
            RequestBoreTraceSave();
        }

        SaveDepthProfile();
        SaveMachinePlacements();
        RefreshAllStepReportData();
        SaveReportSnapshotFromCurrentStepData();

        SaveSelectedProjectStepData(ReportStepNumber, "project_save", JsonSerializer.Serialize(new
        {
            savedAt = DateTimeOffset.Now,
            projectId = _selectedProject.Id,
            activeStep = _selectedStep?.Number,
            activeSubstep = _selectedSubstep?.Number,
            reportSubsteps = _workspaces.Values
                .Where(workspace => !IsHiddenWorkflowStep(workspace.StepNumber))
                .Sum(workspace => CountSavedStepReportSubsteps(workspace.StepNumber))
        }, JsonOptions));

        RefreshWorkflowReportStatus(_selectedStep?.Number ?? ReportStepNumber);
        OutputText.Text = $"Project volledig opgeslagen: {_selectedProject.Name}\n\nProjectgegevens, actieve kaartstatus, boorlijn/profiel/machinegegevens, substap-rapportdata en rapport-snapshot zijn bijgewerkt.";
    }



    private string BuildBoringConfigJson()
    {
        var result = ComputeBoring();
        return JsonSerializer.Serialize(new
        {
            savedAt = DateTimeOffset.Now,
            items = _boringItems.Select(i => new
            {
                type = i.Type.ToString(),
                i.Label,
                i.Dn,
                i.OutsideDiameter,
                i.Color,
                contents = i.Contents.Select(c => new { c.Label, c.OutsideDiameter, c.Color }).ToArray()
            }).ToArray(),
            machine = _selectedMachineId,
            drillingTechnique = _selectedDrillingTechnique,
            boringD = result.BoringDiameter
        }, JsonOptions);
    }

    private void SaveBoringConfigSnapshot()
    {
        if (_selectedProject is null) return;
        if (_loadedBoringConfigProjectId != _selectedProject.Id && _boringItems.Count == 0)
        {
            EnsureBoringConfigLoaded(seedDefaultWhenMissing: false);
        }

        if (_boringItems.Count == 0)
        {
            var existing = GetStoredUsableBoringConfigJson();
            if (!string.IsNullOrWhiteSpace(existing))
            {
                _selectedProject.BoringConfigJson = existing;
                _projects.UpdateProject(_selectedProject);
                SaveSelectedProjectStepData(1, "boring_config", existing);
                BoringSavedText.Text = "Opgeslagen";
            }
            return;
        }

        var payload = BuildBoringConfigJson();
        _selectedProject.BoringConfigJson = payload;
        _projects.UpdateProject(_selectedProject);
        SaveSelectedProjectStepData(1, "boring_config", payload);
        BoringSavedText.Text = "Opgeslagen";
    }

    private void EnsureBoringConfigLoaded(bool seedDefaultWhenMissing = true)
    {
        if (_selectedProject is null) return;
        if (_loadedBoringConfigProjectId == _selectedProject.Id) return;

        _boringItems.Clear();
        _selectedMachineId = null;
        _selectedDrillingTechnique = "walkover";
        if (!TryLoadBoringConfig() && seedDefaultWhenMissing)
        {
            SeedBoringItems();
        }
        _loadedBoringConfigProjectId = _selectedProject.Id;
    }

    private bool TryLoadBoringConfig()
    {
        if (_selectedProject is null) return false;
        var candidates = GetStoredBoringConfigCandidates()
            .Where(json => !string.IsNullOrWhiteSpace(json))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var json in candidates.OrderByDescending(HasUsableBoringConfigJson))
        {
            if (TryReadBoringConfigJson(json, out var items, out var machineId, out var drillingTechnique))
            {
                _boringItems.Clear();
                _boringItems.AddRange(items);
                _selectedMachineId = machineId;
                _selectedDrillingTechnique = drillingTechnique;
                _selectedProject.BoringConfigJson = json;
                _projects.UpdateProject(_selectedProject);
                SaveSelectedProjectStepData(1, "boring_config", json);
                return true;
            }
        }

        return false;
    }

    private IEnumerable<string> GetStoredBoringConfigCandidates()
    {
        if (_selectedProject is null) yield break;
        var stepData = _projects.GetStepData(_selectedProject.Id, 1, "boring_config");
        if (!string.IsNullOrWhiteSpace(stepData)) yield return stepData;
        if (!string.IsNullOrWhiteSpace(_selectedProject.BoringConfigJson)) yield return _selectedProject.BoringConfigJson;
    }

    private string? GetStoredUsableBoringConfigJson()
    {
        return GetStoredBoringConfigCandidates().FirstOrDefault(HasUsableBoringConfigJson);
    }

    private static bool HasUsableBoringConfigJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            return TryGetJsonProperty(document.RootElement, out var itemsElement, "items", "Items")
                && itemsElement.ValueKind == JsonValueKind.Array
                && itemsElement.GetArrayLength() > 0;
        }
        catch
        {
            return false;
        }
    }

    private bool TryReadBoringConfigJson(string json, out List<BoringItem> items, out string? machineId, out string drillingTechnique)
    {
        items = [];
        machineId = null;
        drillingTechnique = "walkover";
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!TryGetJsonProperty(root, out var itemsElement, "items", "Items") || itemsElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var itemElement in itemsElement.EnumerateArray())
            {
                var type = ReadJsonBoringItemType(itemElement);
                var dn = ReadJsonIntProperty(itemElement, "dn", "Dn");
                var outsideDiameter = ReadJsonDoubleProperty(itemElement, "outsideDiameter", "OutsideDiameter", "od", "Od");
                var label = ReadJsonStringProperty(itemElement, "label", "Label");
                var color = ReadJsonStringProperty(itemElement, "color", "Color");
                if (string.Equals(label.Trim(), "HPE", StringComparison.OrdinalIgnoreCase))
                {
                    label = "HDPE";
                }
                if (outsideDiameter <= 0) outsideDiameter = dn > 0 ? dn : 40;
                if (string.IsNullOrWhiteSpace(label)) label = type == BoringItemType.Mantelbuis ? $"PE {dn} mantelbuis" : "Direct product";
                if (string.IsNullOrWhiteSpace(color)) color = type == BoringItemType.Mantelbuis ? "#111827" : "#2563EB";

                var item = new BoringItem
                {
                    Id = Guid.NewGuid(),
                    Type = type,
                    Dn = dn,
                    Label = label,
                    OutsideDiameter = outsideDiameter,
                    Color = color
                };

                if (TryGetJsonProperty(itemElement, out var contentsElement, "contents", "Contents") && contentsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var contentElement in contentsElement.EnumerateArray())
                    {
                        var contentLabel = ReadJsonStringProperty(contentElement, "label", "Label");
                        var contentDiameter = ReadJsonDoubleProperty(contentElement, "outsideDiameter", "OutsideDiameter", "od", "Od");
                        var contentColor = ReadJsonStringProperty(contentElement, "color", "Color");
                        if (string.IsNullOrWhiteSpace(contentLabel)) continue;
                        if (contentDiameter <= 0) contentDiameter = 16;
                        if (string.IsNullOrWhiteSpace(contentColor)) contentColor = "#6B7280";
                        item.Contents.Add(new BoringContent { Id = Guid.NewGuid(), Label = contentLabel, OutsideDiameter = contentDiameter, Color = contentColor });
                    }
                }

                items.Add(item);
            }

            if (TryGetJsonProperty(root, out var machineElement, "machine", "Machine") && machineElement.ValueKind == JsonValueKind.String)
            {
                machineId = machineElement.GetString();
            }
            if (TryGetJsonProperty(root, out var techniqueElement, "drillingTechnique", "DrillingTechnique", "boortechniek", "Boortechniek") && techniqueElement.ValueKind == JsonValueKind.String)
            {
                drillingTechnique = NormalizeDrillingTechniqueKey(techniqueElement.GetString());
            }

            return items.Count > 0;
        }
        catch
        {
            items = [];
            machineId = null;
            drillingTechnique = "walkover";
            return false;
        }
    }

    private static bool TryGetJsonProperty(JsonElement element, out JsonElement value, params string[] names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in names)
            {
                if (element.TryGetProperty(name, out value)) return true;
            }
        }
        value = default;
        return false;
    }

    private static string ReadJsonStringProperty(JsonElement element, params string[] names)
    {
        return TryGetJsonProperty(element, out var value, names) && value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined
            ? value.ToString() ?? string.Empty
            : string.Empty;
    }

    private static double ReadJsonDoubleProperty(JsonElement element, params string[] names)
    {
        if (!TryGetJsonProperty(element, out var value, names)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
        return double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number) ? number : 0;
    }

    private static int ReadJsonIntProperty(JsonElement element, params string[] names)
    {
        if (!TryGetJsonProperty(element, out var value, names)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        return int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number) ? number : 0;
    }

    private static BoringItemType ReadJsonBoringItemType(JsonElement element)
    {
        if (!TryGetJsonProperty(element, out var value, "type", "Type")) return BoringItemType.Direct;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && Enum.IsDefined(typeof(BoringItemType), number))
        {
            return (BoringItemType)number;
        }
        return Enum.TryParse<BoringItemType>(value.ToString(), ignoreCase: true, out var type) ? type : BoringItemType.Direct;
    }
    private void RenderBoringConfigurator()
    {
        if (_boringItems.Count == 0)
        {
            SeedBoringItems();
        }

        var result = ComputeBoring();
        StepOneDoneDot.Background = Brush("#4B5563");
        StepOneDoneText.Text = "2";
        StepOneDoneText.Foreground = Brushes.White;

        BoringItemsPanel.Children.Clear();
        foreach (var item in _boringItems)
        {
            BoringItemsPanel.Children.Add(CreateBoringItemRow(item, result.Processed.First(p => p.Item.Id == item.Id)));
        }

        BundleDiameterText.Text = $"Ø{Math.Round(result.BundleDiameter)} mm";
        RequiredBoringText.Text = $"Ø{result.BoringDiameter} mm";
        MachineIntroText.Text = $"Vereiste boring: Ø{result.BoringDiameter} mm";
        DrawBoringPreview(result);
        RenderMachineCards(result.BoringDiameter);
        ApplyDrillingTechniqueSelection();
    }

    private Border CreateBoringItemRow(BoringItem item, ProcessedBoringItem processed)
    {
        var row = new StackPanel();
        var header = new Grid { Margin = new Thickness(0, 0, 0, item.Contents.Count > 0 ? 4 : 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dot = new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(4.5), Background = Brush(processed.Color), VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(dot);

        var title = item.Type == BoringItemType.Mantelbuis
            ? $"PE {item.Dn} mantelbuis"
            : item.Label;
        var subtitle = item.Type == BoringItemType.Mantelbuis
            ? $"{Math.Round(processed.FillPercentage)}% gevuld"
            : $"Ø{item.OutsideDiameter} mm";
        var text = new TextBlock { Text = $"{title}   {subtitle}", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1B2B35"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(text, 1);
        header.Children.Add(text);

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        if (item.Type == BoringItemType.Mantelbuis)
        {
            var addCable = new Button { Content = "+ kabel", Tag = item.Id, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0), FontSize = 11, Foreground = Brush("#374151"), Background = Brush("#EEF2F5") };
            addCable.Click += AddCableToMantelbuis_OnClick;
            actions.Children.Add(addCable);
        }
        var remove = new Button { Content = "×", Tag = item.Id, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(6, 0, 0, 0), FontSize = 11, Foreground = Brush("#9CA3AF") };
        remove.Click += RemoveBoringItem_OnClick;
        actions.Children.Add(remove);
        Grid.SetColumn(actions, 2);
        header.Children.Add(actions);
        row.Children.Add(header);

        foreach (var content in item.Contents)
        {
            row.Children.Add(new TextBlock
            {
                Text = $"   • {content.Label}   Ø{content.OutsideDiameter} mm",
                FontSize = 11,
                Foreground = Brush("#587080"),
                Margin = new Thickness(18, 0, 0, 0)
            });
        }

        return new Border
        {
            Background = item.Type == BoringItemType.Mantelbuis ? Brush("#F8FAFB") : Brushes.White,
            BorderBrush = processed.Fits ? Brush("#EDF1F4") : Brush("#FCA5A5"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 5),
            Child = row
        };
    }

    private void DrawBoringPreview(BoringResult result)
    {
        BoringPreviewCanvas.Children.Clear();
        const double cx = 380;
        const double cy = 135;
        const double boreRadius = 108;
        var scale = boreRadius / (result.BoringDiameter / 2d);

        AddPreviewCircle(cx, cy, boreRadius + 22, "#C4A45A", "#C4A45A", 1);
        for (var i = 0; i < 28; i++)
        {
            var a = i * 137.5 * Math.PI / 180d;
            var d = boreRadius + 13 + i % 5 * 1.6;
            AddPreviewCircle(cx + d * Math.Cos(a), cy + d * Math.Sin(a), 1.2, "#A0803A", "#A0803A", 1);
        }
        AddPreviewCircle(cx, cy, boreRadius, "#C2D6DF", "#7AAFC4", 1.5);
        AddPreviewLine(cx, cy - boreRadius + 8, cx, cy + boreRadius - 8, "#7AAFC4", 0.5, [3, 4]);
        AddPreviewLine(cx - boreRadius + 8, cy, cx + boreRadius - 8, cy, "#7AAFC4", 0.5, [3, 4]);

        var positions = GravityPack(result.Processed.Select(p => Math.Max((p.EffectiveOutsideDiameter / 2d) * scale, 4)).ToArray(), boreRadius);
        for (var i = 0; i < result.Processed.Count; i++)
        {
            var item = result.Processed[i];
            var pos = positions[i];
            var x = cx + pos.X;
            var y = cy + pos.Y;
            var radius = Math.Max((item.EffectiveOutsideDiameter / 2d) * scale, 4);

            if (item.Item.Type == BoringItemType.Mantelbuis)
            {
                var pe = PeSizes.First(p => p.Dn == item.Item.Dn);
                var tubeColor = PeTubeColor(item.Item);
                AddPreviewCircle(x, y, radius, tubeColor, tubeColor, 1);
                AddPreviewCircle(x, y, Math.Max(radius - pe.Wall * scale, 2), "#EEF6F8", "#EEF6F8", 1);
                var innerRadius = Math.Max(radius - pe.Wall * scale, 2);
                var contentPositions = GravityPack(item.Item.Contents.Select(c => Math.Max((c.OutsideDiameter / 2d) * scale, 2.5)).ToArray(), innerRadius);
                for (var c = 0; c < item.Item.Contents.Count; c++)
                {
                    var content = item.Item.Contents[c];
                    var contentPos = contentPositions[c];
                    AddPreviewCircle(x + contentPos.X, y + contentPos.Y, Math.Max((content.OutsideDiameter / 2d) * scale, 2.5), content.Color, content.Color, 1);
                }
            }
            else
            {
                AddPreviewCircle(x, y, radius, item.Color, item.Color, 1);
            }
        }

        BoringLegendPanel.Children.Clear();
        AddLegendChip("#C2D6DF", "Bentoniet");
        AddLegendChip("#C4A45A", "Boorgat / grond");
        var legendKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in result.Processed)
        {
            if (item.Item.Type == BoringItemType.Mantelbuis)
            {
                var tubeLabel = $"PE{item.Item.Dn} mantelbuis (zwart)";
                if (legendKeys.Add(tubeLabel)) AddLegendChip(PeTubeColor(item.Item), tubeLabel);
                foreach (var content in item.Item.Contents)
                {
                    if (legendKeys.Add(content.Label)) AddLegendChip(content.Color, content.Label);
                }
            }
            else if (legendKeys.Add(item.Item.Label))
            {
                AddLegendChip(item.Color, item.Item.Label);
            }
        }
    }

    private static string PeTubeColor(BoringItem item)
    {
        if (item.Type != BoringItemType.Mantelbuis) return item.Color;
        return "#111827";
    }







    private void ApplyDrillingTechniqueSelection()
    {
        if (DrillingTechniqueCombo is null) return;
        var key = NormalizeDrillingTechniqueKey(_selectedDrillingTechnique);
        foreach (var item in DrillingTechniqueCombo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), key, StringComparison.OrdinalIgnoreCase))
            {
                DrillingTechniqueCombo.SelectionChanged -= DrillingTechniqueCombo_OnSelectionChanged;
                DrillingTechniqueCombo.SelectedItem = item;
                DrillingTechniqueCombo.SelectionChanged += DrillingTechniqueCombo_OnSelectionChanged;
                break;
            }
        }

        DrillingTechniqueDescriptionText.Text = GetDrillingTechniqueDescription(key);
    }

    private void DrillingTechniqueCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DrillingTechniqueCombo.SelectedItem is not ComboBoxItem item) return;
        _selectedDrillingTechnique = NormalizeDrillingTechniqueKey(item.Tag?.ToString());
        DrillingTechniqueDescriptionText.Text = GetDrillingTechniqueDescription(_selectedDrillingTechnique);
        SaveBoringConfigSnapshot();
        RefreshInlineReportPreviewIfVisible();
    }

    private static string NormalizeDrillingTechniqueKey(string? key) =>
        key?.Trim().ToLowerInvariant() switch
        {
            "gyro" => "gyro",
            "gyro_walkover" or "gyro-walkover" or "gyro + walk-over controle" => "gyro_walkover",
            "tbd" or "nader te bepalen" => "tbd",
            _ => "walkover"
        };

    private static string GetDrillingTechniqueLabel(string key) =>
        NormalizeDrillingTechniqueKey(key) switch
        {
            "gyro" => "Gyro gestuurd",
            "gyro_walkover" => "Gyro + walk-over controle",
            "tbd" => "Nader te bepalen",
            _ => "Walk-over tracking"
        };

    private static string GetDrillingTechniqueDescription(string key) =>
        NormalizeDrillingTechniqueKey(key) switch
        {
            "gyro" => "Toepassing bij hogere nauwkeurigheid, beperkte bovengrondse detectie of tracés waar walk-over niet betrouwbaar gevolgd kan worden.",
            "gyro_walkover" => "Gyro als primaire meting met bovengrondse controle waar mogelijk; geschikt voor extra kwaliteitsborging.",
            "tbd" => "Boortechniek nog vast te leggen op basis van uitvoeringsplan, bereikbaarheid en nauwkeurigheidseis.",
            _ => "Zender/sonde wordt bovengronds gevolgd; passend bij kortere of goed bereikbare HDD-tracés."
        };

    private void AddMantelbuis_OnClick(object sender, RoutedEventArgs e)
    {
        _boringItems.Add(new BoringItem { Id = Guid.NewGuid(), Type = BoringItemType.Mantelbuis, Dn = 110, Label = "PE 110 mantelbuis", OutsideDiameter = 110, Color = TubeColors[_boringItems.Count % TubeColors.Length] });
        RenderBoringConfigurator();
        RefreshInlineReportPreviewIfVisible();
    }

    private void AddDirectProduct_OnClick(object sender, RoutedEventArgs e)
    {
        _boringItems.Add(CreateDefaultDirectHdpeProduct());
        RenderBoringConfigurator();
        RefreshInlineReportPreviewIfVisible();
    }

    private void AddCableToMantelbuis_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not Guid id) return;
        var product = CableCatalog[1].Products[0];
        var item = _boringItems.FirstOrDefault(i => i.Id == id);
        item?.Contents.Add(new BoringContent { Id = Guid.NewGuid(), Label = product.Label, OutsideDiameter = product.OutsideDiameter, Color = CableCatalog[1].Color });
        RenderBoringConfigurator();
        RefreshInlineReportPreviewIfVisible();
    }

    private void RemoveBoringItem_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not Guid id) return;
        _boringItems.RemoveAll(i => i.Id == id);
        RenderBoringConfigurator();
        RefreshInlineReportPreviewIfVisible();
    }

    private void MachineCard_OnClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as Border)?.Tag is not string id) return;
        var result = ComputeBoring();
        var machine = Machines.First(m => m.Id == id);
        if (machine.MaxBoring < result.BoringDiameter) return;
        _selectedMachineId = _selectedMachineId == id ? null : id;
        RenderMachineCards(result.BoringDiameter);
        SaveBoringConfigSnapshot();
        RefreshInlineReportPreviewIfVisible();
    }

    private void SaveBoringConfig_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedProject is null) return;
        SaveStepOneLayoutState();
        SaveBoringConfigSnapshot();
        var result = ComputeBoring();
        OutputText.Text = $"Boring configuratie lokaal opgeslagen.\n\nVereiste boring: Ø{result.BoringDiameter} mm\nMachine: {_selectedMachineId ?? "nog niet gekozen"}";
    }

    private void EditProject_OnClick(object sender, RoutedEventArgs e)
    {
        OutputText.Text = "Project bewerken opent straks het native bewerkscherm met dezelfde projectvelden en lokale opslag.";
    }

    private void SeedBoringItems()
    {
        _boringItems.Add(CreateDefaultDirectHdpeProduct());
    }

    private static BoringItem CreateDefaultDirectHdpeProduct() =>
        new()
        {
            Id = Guid.NewGuid(),
            Type = BoringItemType.Direct,
            Label = "HDPE",
            OutsideDiameter = 63,
            Color = "#2563EB"
        };



    private Border CreateLayerUploadRow(LayerUploadDefinition layer, ProjectFileRecord? file)
    {
        var row = new Grid { MinHeight = 50 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dot = new Border
        {
            Width = 10,
            Height = 10,
            CornerRadius = new CornerRadius(5),
            Background = Brush(layer.Color),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        row.Children.Add(dot);

        var textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (layer.IsCustom)
        {
            textPanel.Children.Add(new TextBox
            {
                Text = file is null ? "" : layer.Label,
                BorderBrush = Brush("#DEE6EA"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Background = Brushes.Transparent,
                Foreground = Brush("#071422"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                MinWidth = 170
            });
        }
        else
        {
            textPanel.Children.Add(new TextBlock
            {
                Text = layer.Label,
                Foreground = Brush("#071422"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            });
        }

        textPanel.Children.Add(new TextBlock
        {
            Text = file is null
                ? layer.AcceptText
                : $"Geimporteerd  {file.DisplayName} · {Math.Round(file.SizeBytes / 1024d):N0} KB",
            Foreground = file is null ? Brush("#8FA6B2") : Brush("#4B5563"),
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0)
        });
        Grid.SetColumn(textPanel, 1);
        row.Children.Add(textPanel);

        if (file is not null)
        {
            var success = new TextBlock
            {
                Text = "OK",
                Foreground = Brush("#15803D"),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(10, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(success, 2);
            row.Children.Add(success);

            var remove = new Button
            {
                Content = "×",
                Tag = file.Id,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Brush("#94A3B8"),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(4, 0, 12, 0),
                FontSize = 14,
                ToolTip = "Bestand verwijderen",
                VerticalAlignment = VerticalAlignment.Center
            };
            remove.Content = "x";
            remove.Click += RemoveLayerFile_OnClick;
            Grid.SetColumn(remove, 3);
            row.Children.Add(remove);
        }

        var choose = new Button
        {
            Content = "Importeren",
            Tag = layer.Key,
            Background = Brushes.White,
            BorderBrush = Brush("#DEE6EA"),
            Foreground = Brush("#1B2B35"),
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        choose.Click += ChooseLayerFile_OnClick;
        Grid.SetColumn(choose, 4);
        row.Children.Add(choose);

        return new Border
        {
            BorderBrush = Brush("#E6EDF1"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 0, 10, 0),
            Child = row
        };
    }


    private static IEnumerable<LayerUploadDefinition> GetAllImportLayerDefinitions()
    {
        foreach (var layer in StepTwoLayers) yield return layer;
        foreach (var layer in StepThreeImportLayers) yield return layer;
    }
    private void ChooseLayerFile_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedProject is null || (sender as Button)?.Tag is not string layerKey) return;
        var layer = GetAllImportLayerDefinitions().First(l => l.Key == layerKey);
        var dialog = new OpenFileDialog
        {
            Title = $"Kies bestand voor {layer.Label}",
            Filter = layer.Filter,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true) return;

        var record = _projects.AddProjectFileRecord(_selectedProject.Id, layer.Key, dialog.FileName);
        _projectFiles = _projects.GetProjectFiles(_selectedProject.Id);
        ClearEnvironmentAnalysisCache();
        MarkReportUiDataChanged();
        OutputText.Text = $"Import gelukt\n\n? {layer.Label}\n{record.DisplayName}\n{Math.Round(record.SizeBytes / 1024d):N0} KB";
        RenderStepTwo();
    }

    private void RemoveLayerFile_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedProject is null || (sender as Button)?.Tag is not Guid fileId) return;

        var file = _projectFiles.FirstOrDefault(record => record.Id == fileId);
        if (file is null) return;
        if (!ConfirmDeleteImportFile(file)) return;

        try
        {
            var deletedCount = _projects.DeleteProjectFilesByType(_selectedProject.Id, file.FileType);
            _projectFiles = _projects.GetProjectFiles(_selectedProject.Id);
            _mapLayerBuilder.ClearCache();
            ClearEnvironmentAnalysisCache();
            MarkReportUiDataChanged();
            foreach (var record in _projectFiles.Where(record => record.FileType.Equals(file.FileType, StringComparison.OrdinalIgnoreCase)))
            {
                _projectLayerStates.Remove(record.Id.ToString("N"));
            }

            OutputText.Text = deletedCount > 0
                ? $"Import verwijderd\n\n{file.FileType} is volledig uit dit project gehaald.\nVerwijderd: {deletedCount} bestand(en)."
                : $"Import niet gevonden\n\n{file.DisplayName} stond niet meer in dit project.";
            RenderStepTwo();
        }
        catch (Exception exception)
        {
            OutputText.Text = $"Import verwijderen mislukt\n\n{exception.Message}";
        }
    }

    private bool ConfirmDeleteImportFile(ProjectFileRecord file)
    {
        var dialog = new Window
        {
            Title = "Import verwijderen",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            Background = Brushes.White
        };

        var panel = new StackPanel { Margin = new Thickness(22), Width = 380 };
        panel.Children.Add(new TextBlock
        {
            Text = "Wil je deze import verwijderen?",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#071422"),
            Margin = new Thickness(0, 0, 0, 8)
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"{file.DisplayName}\n\nAlle imports van type {file.FileType} worden uit dit project verwijderd, inclusief lokale kopieen. Je kunt de laag later opnieuw importeren.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#4B5563"),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 18)
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var cancel = new Button
        {
            Content = "Annuleren",
            Padding = new Thickness(14, 7, 14, 7),
            Margin = new Thickness(0, 0, 8, 0),
            Background = Brushes.White,
            BorderBrush = Brush("#DEE6EA"),
            Foreground = Brush("#1B2B35"),
            IsCancel = true
        };
        cancel.Click += (_, _) =>
        {
            dialog.DialogResult = false;
            dialog.Close();
        };

        var delete = new Button
        {
            Content = "Verwijderen",
            Padding = new Thickness(14, 7, 14, 7),
            Background = Brush("#DC2626"),
            BorderBrush = Brush("#DC2626"),
            Foreground = Brushes.White,
            IsDefault = true
        };
        delete.Click += (_, _) =>
        {
            dialog.DialogResult = true;
            dialog.Close();
        };

        buttons.Children.Add(cancel);
        buttons.Children.Add(delete);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        return dialog.ShowDialog() == true;
    }

    private void RenderStepThree(bool force = false)
    {
        var workspace = _selectedStep is null ? null : _workspaces[_selectedStep.Number];
        var initialSignature = BuildStepThreeRenderSignature();
        if (!force &&
            StepThreePanel.Visibility == Visibility.Visible &&
            string.Equals(_lastStepThreeRenderSignature, initialSignature, StringComparison.Ordinal))
        {
            return;
        }

        StepSpecificRibbonPanel.Children.Clear();
        StepThreeBaseLayersPanel.Children.Clear();
        StepThreeEsriLayersPanel.Children.Clear();
        StepThreeImportsPanel.Children.Clear();
        SurfaceAnalysisSidebarPanel.Children.Clear();
        EnvironmentAnalysisPanel.Children.Clear();
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
        StepProfilePanel.Visibility = _selectedStep?.Number == ProfileStepNumber ? Visibility.Visible : Visibility.Collapsed;
        StepSurfaceAnalysisPanel.Visibility = _selectedStep?.Number == 4 && ShouldShowSurfaceAnalysisPanel()
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateSurfaceAnalysisSummaryBar();
        UpdateMapLockButton();

        if (_showingStepThreeDocs)
        {
            _showingStepThreeDocs = false;
        }

        var stepThreeKlicSubstep = IsSelectedStepThreeKlicSubstep();

        if (IsBorelineStep(_selectedStep?.Number) && !stepThreeKlicSubstep)
        {
            LoadTraceVisualSettings();
            RenderBoreTraceSidebarPanel();
        }
        else if (stepThreeKlicSubstep)
        {
            EnsureStoredBoreTraceLoaded();
        }
        else if (_selectedStep?.Number == 4)
        {
            EnsureStoredBoreTraceLoaded();
            AddStepFiveBgtSidebarTools();
            RenderSurfaceAnalysisPanel();
        }
        else if (_selectedStep?.Number == EnvironmentStepNumber)
        {
            RenderEnvironmentAnalysisSidebarPanel(showResults: true);
        }
        else if (_selectedStep?.Number == 6)
        {
            RenderUndergroundAnalysisSidebarPanel();
            QueueAutoLoadDgmForCurrentSubstep();
        }
        else if (_selectedStep?.Number == ProfileStepNumber)
        {
            RenderProfileToolsSidebarPanel();
            EnsureProfilePoints();
            RenderProfilePanel();
            RequestProfileMapAlignmentIfNeeded();
        }
        else if (_selectedStep?.Number == WorkDrawingStepNumber)
        {
            RenderWorkDrawingSidebarPanel();
            EnsureProfilePoints();
        }
        else if (_selectedStep?.Number == MachineStepNumber)
        {
            AddStepEightMachineRibbon();
        }
        else if (workspace is not null && _selectedStep?.Number != 3)
        {
            AddGenericStepRibbonActions(workspace);
        }

        var stepNumber = _selectedStep?.Number ?? 3;
        var selectedSubstepNumber = _selectedSubstep?.Number ?? "";
        var showBgtInfoTab = IsSurfaceSegmentsReportSubstep(stepNumber, selectedSubstepNumber);
        if (stepNumber == 3 && !IsStepThreeLiveBaseLayer(_selectedMapBaseLayer))
        {
            _selectedMapBaseLayer = "pdok-brt";
            _gisLayerState.BaseLayer = _selectedMapBaseLayer;
        }

        AddStepThreeRadio(StepThreeBaseLayersPanel, "BRT Standaard", "pdok-brt");
        AddStepThreeRadio(StepThreeBaseLayersPanel, "BRT Grijs", "pdok-gray");
        AddStepThreeRadio(StepThreeBaseLayersPanel, "BRT Pastel", "pdok-pastel");
        AddStepThreeRadio(StepThreeBaseLayersPanel, "Luchtfoto (PDOK)", "pdok-aerial");
        if (stepNumber != 3)
        {
            AddStepThreeRadio(StepThreeBaseLayersPanel, "BGT standaardvisualisatie", "pdok-bgt-pastel");
            AddStepThreeRadio(StepThreeEsriLayersPanel, "Esri Topo RD", "esri-topo-rd");
            AddStepThreeRadio(StepThreeEsriLayersPanel, "Esri Open Topo", "esri-open-topo");
            AddStepThreeRadio(StepThreeEsriLayersPanel, "Esri Luchtfoto (HR)", "esri-aerial");
        }

        AddMapOverlayControls();
        RenderKlicDocumentsPanel();
        RenderAiAnalysisPanel();
        RenderInformationPlaceholders();
        SidebarImportsTab.Content = stepNumber == 4
            ? "Oppervlakteanalyse"
            : stepNumber == 6
                ? "Ondergrondanalyse"
                : stepThreeKlicSubstep ? "KLIC" : "Importbestanden";
        SidebarImportsTab.Visibility = Visibility.Collapsed;
        SidebarEnvironmentTab.Visibility = stepNumber == EnvironmentStepNumber ? Visibility.Visible : Visibility.Collapsed;
        SidebarAnalysisTab.Visibility = stepNumber == 4 ? Visibility.Visible : Visibility.Collapsed;
        SidebarBoreTraceTab.Visibility = IsBorelineStep(stepNumber) ? Visibility.Visible : Visibility.Collapsed;
        SidebarProfileTab.Visibility = stepNumber == ProfileStepNumber ? Visibility.Visible : Visibility.Collapsed;
        SidebarWorkDrawingTab.Visibility = stepNumber == WorkDrawingStepNumber ? Visibility.Visible : Visibility.Collapsed;
        SidebarMapLayersTab.Visibility = stepNumber == WorkDrawingStepNumber ? Visibility.Collapsed : Visibility.Visible;
        SidebarKlicDocsTab.Visibility = Visibility.Collapsed;
        SidebarAiTab.Visibility = Visibility.Collapsed;
        SidebarKlicInfoTab.Visibility = Visibility.Collapsed;
        SidebarBgtInfoTab.Visibility = showBgtInfoTab ? Visibility.Visible : Visibility.Collapsed;
        if (stepNumber == 3)
        {
            SidebarImportsTab.Visibility = Visibility.Collapsed;
            SidebarAnalysisTab.Visibility = Visibility.Collapsed;
            SidebarKlicDocsTab.Visibility = Visibility.Collapsed;
            SidebarAiTab.Visibility = Visibility.Collapsed;
            SidebarKlicInfoTab.Visibility = Visibility.Collapsed;
            SidebarBgtInfoTab.Visibility = Visibility.Collapsed;
            SidebarBoreTraceTab.Visibility = stepThreeKlicSubstep ? Visibility.Collapsed : Visibility.Visible;
            SidebarMapLayersTab.Visibility = Visibility.Visible;
            if (stepThreeKlicSubstep)
            {
                if (_activeSidebarTab is "trace" or "docs" or "ai" or "klicInfo" or "bgtInfo")
                {
                    _activeSidebarTab = "layers";
                }
            }
            else if (_activeSidebarTab is "imports" or "docs" or "ai" or "klicInfo" or "bgtInfo")
            {
                _activeSidebarTab = "trace";
            }
        }
        if (stepNumber == 6)
        {
            SidebarImportsTab.Visibility = Visibility.Collapsed;
            SidebarEnvironmentTab.Visibility = Visibility.Collapsed;
            SidebarAnalysisTab.Visibility = Visibility.Collapsed;
            SidebarBoreTraceTab.Visibility = Visibility.Collapsed;
            SidebarProfileTab.Visibility = Visibility.Collapsed;
            SidebarWorkDrawingTab.Visibility = Visibility.Collapsed;
            SidebarMapLayersTab.Visibility = Visibility.Visible;
            SidebarKlicDocsTab.Visibility = Visibility.Collapsed;
            SidebarAiTab.Visibility = Visibility.Collapsed;
            SidebarKlicInfoTab.Visibility = Visibility.Collapsed;
            SidebarBgtInfoTab.Visibility = Visibility.Collapsed;
            if (_activeSidebarTab is not "reportInfo" and not "knowledge" and not "projectInfo" and not "layers")
            {
                _activeSidebarTab = "layers";
            }
        }
        if (stepNumber == ProfileStepNumber)
        {
            SidebarImportsTab.Visibility = Visibility.Collapsed;
            SidebarEnvironmentTab.Visibility = Visibility.Collapsed;
            SidebarAnalysisTab.Visibility = Visibility.Collapsed;
            SidebarBoreTraceTab.Visibility = Visibility.Collapsed;
            SidebarProfileTab.Visibility = Visibility.Visible;
            SidebarWorkDrawingTab.Visibility = Visibility.Collapsed;
            SidebarMapLayersTab.Visibility = Visibility.Visible;
            SidebarKlicDocsTab.Visibility = Visibility.Collapsed;
            SidebarAiTab.Visibility = Visibility.Collapsed;
            SidebarKlicInfoTab.Visibility = Visibility.Collapsed;
            SidebarBgtInfoTab.Visibility = Visibility.Collapsed;
            if (_activeSidebarTab is not "reportInfo" and not "knowledge" and not "projectInfo" and not "profile" and not "layers")
            {
                _activeSidebarTab = "profile";
            }
        }
        if (_selectedStep?.Number == WorkDrawingStepNumber && _activeSidebarTab is "imports" or "profile")
        {
            _activeSidebarTab = "workdrawing";
        }
        else if (_selectedStep?.Number != WorkDrawingStepNumber && _activeSidebarTab == "workdrawing")
        {
            _activeSidebarTab = "layers";
        }

        if (_selectedStep?.Number == ProfileStepNumber && _activeSidebarTab == "imports")
        {
            _activeSidebarTab = "profile";
        }
        else if (_selectedStep?.Number != ProfileStepNumber && _activeSidebarTab == "profile")
        {
            _activeSidebarTab = "layers";
        }

        if (_selectedStep?.Number == EnvironmentStepNumber && _activeSidebarTab is "imports" or "layers" or "docs" or "ai" or "klicInfo" or "bgtInfo")
        {
            _activeSidebarTab = "environment";
        }
        else if (_selectedStep?.Number != EnvironmentStepNumber && _activeSidebarTab == "environment")
        {
            _activeSidebarTab = "layers";
        }

        if (_selectedStep?.Number != 4 && _activeSidebarTab == "analysis")
        {
            _activeSidebarTab = "layers";
        }

        if (IsBorelineStep(_selectedStep?.Number) && !stepThreeKlicSubstep && _activeSidebarTab == "imports")
        {
            _activeSidebarTab = "trace";
        }
        else if (stepThreeKlicSubstep && _activeSidebarTab == "trace")
        {
            _activeSidebarTab = "layers";
        }
        else if (_selectedStep?.Number == 4 && _activeSidebarTab is "trace" or "docs" or "klicInfo" or "environment")
        {
            _activeSidebarTab = "layers";
        }
        if (!showBgtInfoTab && _activeSidebarTab == "bgtInfo")
        {
            _activeSidebarTab = "layers";
        }
        else if (!IsBorelineStep(_selectedStep?.Number) && _activeSidebarTab == "trace")
        {
            _activeSidebarTab = "layers";
        }
        SetSidebarTab(_activeSidebarTab);
        StepSpecificRibbonBorder.Visibility = StepSpecificRibbonPanel.Children.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (_mapLibreLoaded && !_suppressProjectLayerSend)
        {
            SendProjectLayersToMap();
            SendProfileModeToMap();
            SendMachineStateToMap();
            RequestProfileMapAlignmentIfNeeded();
        }
        else
        {
            _ = EnsureMapLibreLoadedAsync();
        }

        ApplyStepThreeLayoutBounds();
        _lastStepThreeRenderSignature = BuildStepThreeRenderSignature();
    }


    private string BuildStepThreeRenderSignature()
    {
        var fileSignature = string.Join("|", _projectFiles
            .OrderBy(file => file.Id)
            .Select(file => $"{file.Id:N}:{file.FileType}:{file.SizeBytes}:{file.CreatedAt:O}"));
        var overlaySignature = string.Join("|", _mapOverlayStates.OrderBy(item => item.Key).Select(item => $"{item.Key}:{item.Value}"));
        var projectLayerSignature = string.Join("|", _projectLayerStates.OrderBy(item => item.Key).Select(item => $"{item.Key}:{item.Value}"));
        var bgtSignature = string.Join("|", _bgtSurfaceStates.OrderBy(item => item.Key).Select(item => $"{item.Key}:{item.Value}"));
        var klicSignature = string.Join("|", _klicThemeStates.OrderBy(item => item.Key).Select(item => $"{item.Key}:{item.Value}"));
        var broSelectionSignature = string.Join("|", _selectedBroModelSoundingIdLists
            .OrderBy(item => item.Key)
            .Select(item => $"{item.Key}:{string.Join(",", item.Value.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))}"));

        return string.Join(";",
            _selectedProject?.Id.ToString("N") ?? "geen-project",
            _selectedStep?.Number.ToString(CultureInfo.InvariantCulture) ?? "geen-stap",
            _selectedSubstep?.Number ?? "geen-substap",
            _activeSidebarTab,
            _showingStepThreeDocs,
            _selectedMapBaseLayer,
            _selectedBroModelType,
            _selectedBroSoundingId ?? "",
            _currentBoreTraceJson?.GetHashCode(StringComparison.Ordinal).ToString(CultureInfo.InvariantCulture) ?? "",
            _currentMachinePlacementsJson?.GetHashCode(StringComparison.Ordinal).ToString(CultureInfo.InvariantCulture) ?? "",
            fileSignature,
            overlaySignature,
            projectLayerSignature,
            bgtSignature,
            klicSignature,
            broSelectionSignature);
    }




    private void RenderEnvironmentAnalysisSidebarPanel(bool showResults)
    {
        EnvironmentAnalysisPanel.Children.Clear();
        var card = new Border
        {
            Background = Brush("#F8FAFB"),
            BorderBrush = Brush("#DEE6EA"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7),
            Margin = new Thickness(5, 0, 5, 10)
        };

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Omgevingsmanagement",
            Foreground = Brush("#3F4750"),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var buttons = new UniformGrid { Columns = 1, Rows = 3 };
        AddBgtRibbonButton(buttons, "Analyse uitvoeren", "Analyse uitvoeren", true);
        AddBgtRibbonButton(buttons, "BAG/Kadaster aan/uit", "BAG/Kadaster aan/uit", true);
        AddBgtRibbonButton(buttons, "BGT aan/uit", "BGT aan/uit", true);
        panel.Children.Add(buttons);

        panel.Children.Add(new TextBlock
        {
            Text = "Perceel- en bronhouderanalyse gebruikt de geimporteerde kadastrale kaart en BGT-bronhouder. ZRO blijft een handmatige controlekolom.",
            Foreground = Brush("#7F99AC"),
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        });

        card.Child = panel;
        EnvironmentAnalysisPanel.Children.Add(card);

        if (showResults)
        {
            AddEnvironmentAnalysisResults(EnvironmentAnalysisPanel);
            return;
        }

        EnvironmentAnalysisPanel.Children.Add(new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("#D7E8FA"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 10, 0, 0),
            Child = new TextBlock
            {
                Text = "Klik op 'Analyse uitvoeren' om de boorlijn te kruisen met Kadaster/BAG-percelen en BGT-bronhouders. De uitkomst verschijnt hier.",
                Foreground = Brush("#587080"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            }
        });
    }






    private static UIElement CreateRemoteLegendImage(string url)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(url, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnDemand;
            bitmap.EndInit();

            return new Image
            {
                Source = bitmap,
                Stretch = Stretch.None,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
        }
        catch
        {
            return new TextBlock
            {
                Text = "Legenda kon niet geladen worden.",
                Foreground = Brush("#8FA6B2"),
                FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap
            };
        }
    }










    private void RenderWorkDrawingSidebarPanel()
    {
        WorkDrawingSidebarPanel.Children.Clear();
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
            Text = "Werktekening",
            Foreground = Brush("#315B7E"),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var buttons = new UniformGrid { Columns = 1, Rows = 3 };
        AddBgtRibbonButton(buttons, "Genereer werktekening", "Genereer werktekening", true);
        AddBgtRibbonButton(buttons, "Exporteer werktekening", "Exporteer werktekening", false);
        AddBgtRibbonButton(buttons, "Schaal 1:200", "Werktekening schaal 200", _workDrawingScale == 200);
        panel.Children.Add(buttons);

        panel.Children.Add(new TextBlock
        {
            Text = "Infra schaal",
            Foreground = Brush("#315B7E"),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 10, 0, 5)
        });
        var scaleButtons = new UniformGrid { Columns = 5, Rows = 1 };
        foreach (var scale in new[] { 100, 200, 250, 500, 1000 })
        {
            AddBgtRibbonButton(scaleButtons, $"1:{scale}", $"Werktekening schaal {scale}", _workDrawingScale == scale);
        }
        panel.Children.Add(scaleButtons);

        panel.Children.Add(new TextBlock
        {
            Text = $"Stap {WorkDrawingStepNumber} is alleen de print-preview. Controleer en teken de boorlijn in stap 3/4 en het profiel in stap {ProfileStepNumber}; deze stap maakt daar een vaste A3 werktekening van.",
            Foreground = Brush("#7F99AC"),
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        });

        card.Child = panel;
        WorkDrawingSidebarPanel.Children.Add(card);
    }





    private void AddCompactRibbonButton(Panel parent, string label, string action, bool primary)
    {
        var button = new Button
        {
            Content = label,
            Tag = action,
            Height = 30,
            Margin = new Thickness(0, 0, 6, 6),
            Padding = new Thickness(9, 0, 9, 0),
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


    private void AddBgtRibbonButton(Panel parent, string label, string action, bool primary)
    {
        var button = new Button
        {
            Content = label,
            Tag = action,
            Height = 30,
            Margin = new Thickness(0, 0, 0, 5),
            Background = primary ? Brush("#3F4750") : Brushes.White,
            Foreground = primary ? Brushes.White : Brush("#071422"),
            BorderBrush = primary ? Brush("#3F4750") : Brush("#DEE6EA"),
            BorderThickness = new Thickness(1),
            FontSize = 11,
            FontWeight = primary ? FontWeights.Bold : FontWeights.SemiBold
        };
        button.Click += StepAction_OnClick;
        parent.Children.Add(button);
    }





    private void AddOverlayBulkButtons()
    {
        _gisSidebar.AddBulkButtons(StepThreeOverlaysPanel, "Alles aan", "Alles uit", OverlayBulk_OnClick);
    }
    private void AddStepThreeRadio(Panel parent, string label, string layerId)
    {
        _gisSidebar.AddBaseLayerRadio(parent, label, layerId, _selectedMapBaseLayer, BaseMapLayer_OnClick);
    }

    private static bool IsStepThreeLiveBaseLayer(string layerId) =>
        layerId is "pdok-brt" or "pdok-gray" or "pdok-pastel" or "pdok-aerial" or "osm";

    private void AddStepThreeCheckbox(string label, string overlayId)
    {
        var visible = _mapOverlayStates.TryGetValue(overlayId, out var isVisible) && isVisible;
        _gisSidebar.AddLayerToggle(
            StepThreeOverlaysPanel,
            label,
            overlayId,
            OverlayTypeLabel(overlayId),
            visible,
            overlayId is "klicBuffer" ? new Thickness(22, 0, 0, 2) : new Thickness(5, 0, 0, 0),
            MapOverlay_OnClick);
    }


    private static string OverlayTypeLabel(string overlayId) => GisSidebarBuilder.OverlayTypeLabel(overlayId);



    private async Task EnsureMapLibreLoadedAsync()
    {
        if (_mapLibreLoaded || _mapInitializationStarted) return;

        try
        {
            _mapInitializationStarted = true;
            _gisMap.QueueSync();
            BeginMapLoading("GIS kaart laden...");
            MapDebugBadge.Visibility = Visibility.Visible;
            MapDebugText.Text = "WebView2 initialiseren...";
            AppendMapDiagnostic("Start WebView2 initialisatie.");

            var mapPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "MapLibre", "step3-map.html");
            if (!System.IO.File.Exists(mapPath))
            {
                MapDebugText.Text = "MapLibre HTML ontbreekt.";
                OutputText.Text = $"MapLibre bestand niet gevonden:\n{mapPath}";
                AppendMapDiagnostic($"MapLibre HTML ontbreekt: {mapPath}");
                _mapInitializationStarted = false;
                EndMapLoading();
                return;
            }

            var userDataFolder = GetWebView2UserDataFolder();
            Directory.CreateDirectory(userDataFolder);
            var writeTestPath = System.IO.Path.Combine(userDataFolder, "write-test.txt");
            System.IO.File.WriteAllText(writeTestPath, DateTimeOffset.Now.ToString("O"), Encoding.UTF8);
            AppendMapDiagnostic($"WebView2 user-data map OK: {userDataFolder}");

            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await StepThreeMapView.EnsureCoreWebView2Async(environment);
            MapDebugText.Text = "WebView2 klaar, HTML laden...";
            AppendMapDiagnostic("EnsureCoreWebView2Async voltooid.");

            StepThreeMapView.CoreWebView2.WebMessageReceived -= MapView_WebMessageReceived;
            StepThreeMapView.CoreWebView2.WebMessageReceived += MapView_WebMessageReceived;
            StepThreeMapView.NavigationCompleted -= StepThreeMapView_NavigationCompleted;
            StepThreeMapView.NavigationCompleted += StepThreeMapView_NavigationCompleted;

            StepThreeMapView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            StepThreeMapView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            StepThreeMapView.CoreWebView2.NavigateToString(BuildInlineMapHtml(mapPath));
            _mapLibreLoaded = false;
            MapDebugText.Text = "HTML navigatie gestart...";
            AppendMapDiagnostic("NavigateToString gestart.");
        }
        catch (Exception exception)
        {
            MapDebugBadge.Visibility = Visibility.Visible;
            MapDebugText.Text = $"WebView2 fout: {exception.Message}";
            OutputText.Text = $"WebView2 kaart kon niet starten.\n\n{exception}";
            AppendMapDiagnostic($"WebView2 fout: {exception}");
            _mapInitializationStarted = false;
            _mapLibreLoaded = false;
            EndMapLoading();
        }
    }

    private static string GetWebView2UserDataFolder() =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Borevexa",
            "WebView2UserData");

    private static string GetMapDiagnosticLogPath() =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Borevexa",
            "map-debug.log");

    private static void AppendMapDiagnostic(string message)
    {
        try
        {
            var path = GetMapDiagnosticLogPath();
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.AppendAllText(path, $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}", Encoding.UTF8);
        }
        catch (System.Exception swallowedException)
        {
            // Logging must never block the app.
            AppLog.Swallowed(swallowedException);
        }
    }

    private static string BuildInlineMapHtml(string mapPath)
    {
        var mapDir = System.IO.Path.GetDirectoryName(mapPath) ?? AppContext.BaseDirectory;
        var html = System.IO.File.ReadAllText(mapPath, Encoding.UTF8);
        var cssPath = System.IO.Path.Combine(mapDir, "maplibre-gl.css");
        var jsPath = System.IO.Path.Combine(mapDir, "maplibre-gl.js");

        if (System.IO.File.Exists(cssPath))
        {
            var css = System.IO.File.ReadAllText(cssPath, Encoding.UTF8);
            html = html.Replace("<link href=\"./maplibre-gl.css\" rel=\"stylesheet\" />", $"<style>{css}</style>");
        }

        if (System.IO.File.Exists(jsPath))
        {
            var js = System.IO.File.ReadAllText(jsPath, Encoding.UTF8).Replace("</script>", "<\\/script>");
            html = html.Replace("<script src=\"./maplibre-gl.js\"></script>", $"<script>{js}</script>");
        }

        return html;
    }

    private void StepThreeMapView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            MapDebugBadge.Visibility = Visibility.Visible;
            MapDebugText.Text = $"HTML laadfout: {e.WebErrorStatus}";
            OutputText.Text = $"MapLibre HTML kon niet laden.\n\nWebView2 status: {e.WebErrorStatus}";
            AppendMapDiagnostic($"NavigationCompleted fout: {e.WebErrorStatus}");
            _mapInitializationStarted = false;
            _mapLibreLoaded = false;
            EndMapLoading();
            return;
        }

        MapDebugText.Text = "HTML geladen, MapLibre starten...";
        AppendMapDiagnostic("NavigationCompleted succesvol.");
        _ = StepThreeMapView.ExecuteScriptAsync("window.borevexaMap && window.borevexaMap.handleMessage({ type: 'resize' })");
    }

    private void MapView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!GisMapMessageParser.TryParse(e, out var mapMessage))
        {
            AppendMapDiagnostic($"Onbekend MapLibre bericht genegeerd: {e.WebMessageAsJson}");
            return;
        }

        var message = mapMessage.RawJson;
        switch (mapMessage.Type)
        {
            case "ready":
                _mapLibreLoaded = true;
                _mapInitializationStarted = false;
                MapDebugText.Text = "Kaart actief.";
                MapDebugBadge.Visibility = Visibility.Collapsed;
                AppendMapDiagnostic("MapLibre ready ontvangen.");
                OutputText.Text = "MapLibre GL JS kaart geladen. Lagen, overlays, zoom en kaartacties zijn gekoppeld.";
                EndMapLoading();
                SyncFullMapStateToMap();
                RequestProfileMapAlignmentIfNeeded();
                break;
            case "error":
                MapDebugBadge.Visibility = Visibility.Visible;
                MapDebugText.Text = "Kaartfout ontvangen.";
                AppendMapDiagnostic($"MapLibre error bericht: {message}");
                OutputText.Text = $"Kaartfout ontvangen\n\n{message}";
                EndMapLoading();
                break;
            case "coord":
                OutputText.Text = $"Kaartklik ontvangen\n\n{message}";
                break;
            case "featureDetailRequest":
                SendFeatureDetail(message);
                break;
            case "broSoundingSelected":
                HandleBroSoundingSelected(message);
                break;
            case "traceSave":
                SaveBoreTraceFromMap(message);
                break;
            case "traceChanged":
                CaptureCurrentBoreTrace(message);
                OutputText.Text = FormatTraceStatusMessage(message);
                break;
            case "machineChanged":
                CaptureMachinePlacements(message);
                OutputText.Text = FormatMachineStatusMessage(message);
                break;
            case "traceProfileMetrics":
                CaptureProfileScreenMetrics(message);
                break;
            case "traceRefreshRequest":
                SendTraceStateToMap();
                break;
            case "mapLockChanged":
                CaptureMapLockState(message);
                break;
            case "mapStateChanged":
                CaptureLiveMapState(message);
                break;
            case "feature":
                OutputText.Text = HandleSelectedFeatureMessage(message);
                break;
            case "saved":
                CaptureMapCamera(message);
                SaveStepThreeState();
                break;
            case "openDocument":
                OpenMapDocument(message);
                break;
            default:
                AppendMapDiagnostic($"Niet-afgehandeld MapLibre berichttype '{mapMessage.Type}': {message}");
                break;
        }
    }









    private void BaseMapLayer_OnClick(object sender, RoutedEventArgs e)
    {
        if (BlockIfCurrentMapReportLocked()) return;
        if ((sender as Button)?.Tag is not string layerId) return;
        if (_selectedStep?.Number == 3 && !IsStepThreeLiveBaseLayer(layerId))
        {
            layerId = "pdok-brt";
        }

        _selectedMapBaseLayer = layerId;
        if (_selectedStep?.Number == 4)
        {
            _mapBgtSurfaceSamples = [];
        }
        _showingStepThreeDocs = false;
        _suppressProjectLayerSend = true;
        try
        {
            RenderStepThree(force: true);
        }
        finally
        {
            _suppressProjectLayerSend = false;
        }
        SendMapMessage($"{{\"type\":\"base\",\"id\":\"{layerId}\"}}");
        SendTraceStateToMap();
        SendProfileModeToMap();
        SendMachineStateToMap();
        SaveCurrentMapStateAfterLayerChange();
    }

    private string HandleSelectedFeatureMessage(string message)
    {
        var formatted = FormatSelectedFeatureMessage(message);
        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            var properties = root.GetProperty("properties");
            string Get(string key) => properties.TryGetProperty(key, out var value) ? value.ToString() : "";
            string RootGet(string key) => root.TryGetProperty(key, out var value) ? value.ToString() : "";

            var source = Get("source");
            var layerType = RootGet("layerType");
            if (source.Equals("KLIC", StringComparison.OrdinalIgnoreCase) ||
                layerType.Equals("KLIC", StringComparison.OrdinalIgnoreCase))
            {
                SetSidebarInfo(KlicInfoPanel, "KLIC Informatie", formatted.Replace("KLIC leiding geselecteerd\n\n", ""));
            }
            else if (source.Equals("BGT", StringComparison.OrdinalIgnoreCase) ||
                     layerType.Equals("BGT", StringComparison.OrdinalIgnoreCase))
            {
                SetSidebarInfo(BgtInfoPanel, "BGT Informatie", formatted.Replace("BGT object geselecteerd\n\n", ""));
            }
        }
        catch (System.Exception swallowedException)
        {
            // The output panel still receives the raw formatted message.
            AppLog.Swallowed(swallowedException);
        }

        return formatted;
    }

    private static string FormatSelectedFeatureMessage(string message)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            var properties = root.GetProperty("properties");
            string Get(string key) => properties.TryGetProperty(key, out var value) ? value.ToString() : "-";
            string RootGet(string key) => root.TryGetProperty(key, out var value) ? value.ToString() : "-";

            var theme = Get("theme");
            var themeLabel = theme == "-" ? "-" : KlicThemeLabel(theme);
            var source = Get("source");
            var title = source.Equals("BGT", StringComparison.OrdinalIgnoreCase)
                ? "BGT object geselecteerd"
                : "KLIC leiding geselecteerd";

            return
                $"{title}\n\n" +
                $"Laag: {RootGet("layerName")}\n" +
                $"Type: {RootGet("layerType")}\n" +
                $"Geometrie: {RootGet("geometryType")}\n" +
                $"Thema: {themeLabel}\n" +
                $"Bron: {Get("source")}\n" +
                $"UtilityLink: {Get("utilityLinkId")}\n" +
                $"Netwerk: {Get("networkId")}\n" +
                $"Kleur: {Get("color")}";
        }
        catch
        {
            return $"KLIC object geselecteerd\n\n{message}";
        }
    }




    private object BuildBoringMapInfo()
    {
        EnsureBoringConfigLoaded();
        var boring = ComputeBoring();
        var contents = _boringItems
            .Take(8)
            .Select(item =>
            {
                var nested = item.Contents.Count == 0
                    ? ""
                    : $" ({string.Join(", ", item.Contents.Select(content => $"{content.Label} Ø{content.OutsideDiameter:N0}"))})";
                return $"{item.Label} Ø{item.OutsideDiameter:N0}{nested}";
            })
            .ToArray();
        return new
        {
            title = $"Boring Ø{boring.BoringDiameter:N0} mm",
            subtitle = $"Bundel Ø{boring.BundleDiameter:N0} mm · {_profilePoints.Count} profielpunt(en)",
            contents,
            text = contents.Length == 0
                ? $"Boring Ø{boring.BoringDiameter:N0} mm\nBundel Ø{boring.BundleDiameter:N0} mm"
                : $"Boring Ø{boring.BoringDiameter:N0} mm\nBundel Ø{boring.BundleDiameter:N0} mm\n{string.Join("\n", contents)}"
        };
    }

    private int GetBoringDiameterMillimeters()
    {
        if (_selectedProject is not null)
        {
            var saved = _projects.GetStepData(_selectedProject.Id, 1, "boring_config");
            if (!string.IsNullOrWhiteSpace(saved))
            {
                try
                {
                    using var document = JsonDocument.Parse(saved);
                    if (document.RootElement.TryGetProperty("boringD", out var boringD) &&
                        boringD.TryGetInt32(out var savedDiameter) &&
                        savedDiameter > 0)
                    {
                        return savedDiameter;
                    }
                }
                catch (System.Exception swallowedException)
                {
                    // Fall back to the live configuration below.
                    AppLog.Swallowed(swallowedException);
                }
            }
        }

        return ComputeBoring().BoringDiameter;
    }


















    private static RdPoint CoordinateToRdPoint(double x, double y) =>
        LooksLikeRd(x, y) ? new RdPoint(x, y) : Wgs84ToRd(x, y);

    private static bool LooksLikeLonLat(double x, double y) =>
        x is >= -180 and <= 180 && y is >= -90 and <= 90;










    private static int GetJsonInt(JsonElement element, string property, int fallback) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt32(out var result) ? result : fallback;

    private static double GetJsonDouble(JsonElement element, string property, double fallback) =>
        element.TryGetProperty(property, out var value) && value.TryGetDouble(out var result) ? result : fallback;





    private static double DemoSurfaceNap(double distance, double total, int index)
    {
        var ratio = distance / Math.Max(1, total);
        return Math.Round(1.0 - 3.5 * ratio + 0.45 * Math.Sin(ratio * Math.PI * 2) + 0.06 * index, 2);
    }


    private bool TryFetchAhn4DtmSurfaceNap(double rdX, double rdY, out double surfaceNap)
    {
        surfaceNap = 0;
        if (!LooksLikeRd(rdX, rdY)) return false;
        if (DateTime.UtcNow < _ahnSurfaceSamplingSuspendedUntilUtc) return false;

        var cacheKey = $"{Math.Round(rdX, 1):0.0}:{Math.Round(rdY, 1):0.0}";
        if (_ahnSurfaceNapCache.TryGetValue(cacheKey, out surfaceNap)) return true;

        try
        {
            const double halfPixelWindow = 1.0;
            var minX = (rdX - halfPixelWindow).ToString("0.###", CultureInfo.InvariantCulture);
            var minY = (rdY - halfPixelWindow).ToString("0.###", CultureInfo.InvariantCulture);
            var maxX = (rdX + halfPixelWindow).ToString("0.###", CultureInfo.InvariantCulture);
            var maxY = (rdY + halfPixelWindow).ToString("0.###", CultureInfo.InvariantCulture);
            var url =
                "https://service.pdok.nl/rws/ahn/wms/v1_0?" +
                "SERVICE=WMS&VERSION=1.1.1&REQUEST=GetFeatureInfo" +
                "&LAYERS=dtm_05m&QUERY_LAYERS=dtm_05m&STYLES=" +
                "&SRS=EPSG:28992" +
                $"&BBOX={minX},{minY},{maxX},{maxY}" +
                "&WIDTH=101&HEIGHT=101&X=50&Y=50&INFO_FORMAT=text/html";

            var content = AhnHeightHttpClient.GetStringAsync(url).GetAwaiter().GetResult();
            if (!TryParseAhnHeight(content, out surfaceNap)) return false;

            _ahnSurfaceNapCache[cacheKey] = surfaceNap;
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AHN4 DTM sampling failed: {ex.Message}");
            _ahnSurfaceSamplingSuspendedUntilUtc = DateTime.UtcNow.AddSeconds(20);
            return false;
        }
    }

    private static bool TryParseAhnHeight(string content, out double surfaceNap)
    {
        surfaceNap = 0;
        if (string.IsNullOrWhiteSpace(content)) return false;

        var match = Regex.Match(content, @"<td>\s*(-?\d+(?:[\.,]\d+)?)\s*</td>", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            match = Regex.Match(content, @"value(?:_list)?[^-\d]*(-?\d+(?:[\.,]\d+)?)", RegexOptions.IgnoreCase);
        }

        if (!match.Success) return false;
        var raw = match.Groups[1].Value.Replace(',', '.');
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return false;
        if (double.IsNaN(value) || double.IsInfinity(value) || value is <= -100 or >= 400) return false;

        surfaceNap = value;
        return true;
    }



    // KLIC-kruisingen (kabels/leidingen) plaatsen hun diepte t.o.v. het maaiveld op
    // deze functie. Die las voorheen het maaiveld af uit de 4 sparse dieptepunten
    // (rechtgetrokken tussen intrede/dieptepunt(en)/uittrede) — dezelfde te grove bron
    // die we voor de zichtbare maaiveldlijn al vervangen hebben door dichte AHN4-
    // bemonstering. Het gevolg: een kruising kon boven het (nu wél accurate) maaiveld
    // uitsteken zodra het echte terrein afweek van die rechte lijn. Gebruik daarom
    // dezelfde dichte, gecachte AHN4-samples als de maaiveldlijn zelf.
    private double InterpolateSurfaceNap(double distance, double traceLength)
    {
        var rows = GetAhnSurfaceProfileRows(traceLength);
        if (rows.Count == 0) return _profilePoints.Count > 0 ? _profilePoints[0].Surface : 0;
        if (distance <= rows[0].Distance) return rows[0].Surface;
        if (distance >= rows[^1].Distance) return rows[^1].Surface;

        for (var i = 1; i < rows.Count; i++)
        {
            var previous = rows[i - 1];
            var next = rows[i];
            if (distance > next.Distance) continue;

            var length = Math.Max(0.001, next.Distance - previous.Distance);
            var ratio = (distance - previous.Distance) / length;
            return previous.Surface + (next.Surface - previous.Surface) * ratio;
        }

        return rows[^1].Surface;
    }


    private static IEnumerable<IReadOnlyList<double[]>> EnumerateGeometryLines(GeoJsonGeometry geometry)
    {
        if (geometry.Type.Equals("LineString", StringComparison.OrdinalIgnoreCase))
        {
            var line = ExtractCoordinateList(geometry.Coordinates);
            if (line.Count >= 2) yield return line;
        }
        else if (geometry.Type.Equals("MultiLineString", StringComparison.OrdinalIgnoreCase) ||
                 geometry.Type.Equals("Polygon", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var child in ExtractNestedCoordinateLists(geometry.Coordinates))
            {
                if (child.Count >= 2) yield return child;
            }
        }
        else if (geometry.Type.Equals("MultiPolygon", StringComparison.OrdinalIgnoreCase) &&
                 geometry.Coordinates is System.Collections.IEnumerable polygons &&
                 geometry.Coordinates is not string)
        {
            foreach (var polygon in polygons)
            {
                foreach (var child in ExtractNestedCoordinateLists(polygon))
                {
                    if (child.Count >= 2) yield return child;
                }
            }
        }
    }

    private static List<double[]> ExtractCoordinateList(object? coordinates)
    {
        var result = new List<double[]>();
        if (coordinates is IEnumerable<double[]> direct)
        {
            result.AddRange(direct);
            return result;
        }

        if (coordinates is not System.Collections.IEnumerable enumerable || coordinates is string) return result;
        foreach (var item in enumerable)
        {
            if (TryGetCoordinate(item, out var coordinate))
            {
                result.Add(coordinate);
            }
        }

        return result;
    }

    private static IEnumerable<List<double[]>> ExtractNestedCoordinateLists(object? coordinates)
    {
        if (coordinates is not System.Collections.IEnumerable enumerable || coordinates is string) yield break;
        foreach (var item in enumerable)
        {
            var line = ExtractCoordinateList(item);
            if (line.Count >= 2)
            {
                yield return line;
            }
        }
    }

    private static bool TryGetCoordinate(object? value, out double[] coordinate)
    {
        switch (value)
        {
            case double[] array when array.Length >= 2:
                coordinate = [array[0], array[1]];
                return true;
            case IReadOnlyList<double> list when list.Count >= 2:
                coordinate = [list[0], list[1]];
                return true;
            case System.Collections.IEnumerable enumerable when value is not string:
            {
                var values = enumerable.OfType<double>().Take(2).ToArray();
                if (values.Length >= 2)
                {
                    coordinate = [values[0], values[1]];
                    return true;
                }
                break;
            }
        }

        coordinate = [];
        return false;
    }

    private RdPoint ToRdPoint(double[] coordinate)
    {
        if (coordinate.Length < 2) return new RdPoint(0, 0);
        return CoordinateToRdPoint(coordinate[0], coordinate[1]);
    }

    private static RdPoint Wgs84ToRd(double longitude, double latitude)
    {
        var dLat = 0.36 * (latitude - 52.15517440);
        var dLon = 0.36 * (longitude - 5.38720621);
        var x = 155000.0
            + 190094.945 * Math.Pow(dLon, 1)
            - 11832.228 * Math.Pow(dLat, 1) * Math.Pow(dLon, 1)
            - 114.221 * Math.Pow(dLat, 2) * Math.Pow(dLon, 1)
            - 32.391 * Math.Pow(dLon, 3)
            - 0.705 * Math.Pow(dLat, 1)
            - 2.340 * Math.Pow(dLat, 3) * Math.Pow(dLon, 1)
            - 0.608 * Math.Pow(dLat, 1) * Math.Pow(dLon, 3)
            - 0.008 * Math.Pow(dLon, 2)
            + 0.148 * Math.Pow(dLat, 2) * Math.Pow(dLon, 3);

        var y = 463000.0
            + 309056.544 * Math.Pow(dLat, 1)
            + 3638.893 * Math.Pow(dLon, 2)
            + 73.077 * Math.Pow(dLat, 2)
            - 157.984 * Math.Pow(dLat, 1) * Math.Pow(dLon, 2)
            + 59.788 * Math.Pow(dLat, 3)
            + 0.433 * Math.Pow(dLon, 1)
            - 6.439 * Math.Pow(dLat, 2) * Math.Pow(dLon, 2)
            - 0.032 * Math.Pow(dLat, 1) * Math.Pow(dLon, 1)
            + 0.092 * Math.Pow(dLon, 4)
            - 0.054 * Math.Pow(dLat, 1) * Math.Pow(dLon, 4);

        return new RdPoint(x, y);
    }


    private static bool TrySegmentIntersection(
        double ax,
        double ay,
        double bx,
        double by,
        double cx,
        double cy,
        double dx,
        double dy,
        out double traceRatio)
    {
        traceRatio = 0;
        var rx = bx - ax;
        var ry = by - ay;
        var sx = dx - cx;
        var sy = dy - cy;
        var denominator = rx * sy - ry * sx;
        if (Math.Abs(denominator) < 0.000001) return false;

        var qpx = cx - ax;
        var qpy = cy - ay;
        var t = (qpx * sy - qpy * sx) / denominator;
        var u = (qpx * ry - qpy * rx) / denominator;
        if (t is < 0 or > 1 || u is < 0 or > 1) return false;

        traceRatio = t;
        return true;
    }

    private static SegmentClosest ClosestPointBetweenSegments(TracePointRow traceA, TracePointRow traceB, RdPoint klicA, RdPoint klicB)
    {
        var bestRatio = 0d;
        var bestOffset = double.MaxValue;
        for (var step = 0; step <= 20; step++)
        {
            var ratio = step / 20d;
            var x = traceA.X + (traceB.X - traceA.X) * ratio;
            var y = traceA.Y + (traceB.Y - traceA.Y) * ratio;
            var offset = DistancePointToSegment(x, y, klicA.X, klicA.Y, klicB.X, klicB.Y);
            if (offset >= bestOffset) continue;

            bestOffset = offset;
            bestRatio = ratio;
        }

        return new SegmentClosest(bestRatio, bestOffset);
    }

    private static double DistancePointToSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        var dx = bx - ax;
        var dy = by - ay;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= 0.0001) return Math.Sqrt(Math.Pow(px - ax, 2) + Math.Pow(py - ay, 2));

        var t = Math.Max(0, Math.Min(1, ((px - ax) * dx + (py - ay) * dy) / lengthSquared));
        var x = ax + dx * t;
        var y = ay + dy * t;
        return Math.Sqrt(Math.Pow(px - x, 2) + Math.Pow(py - y, 2));
    }


    private void UpdateWorkDrawingTitleBlock()
    {
        if (_selectedStep?.Number != WorkDrawingStepNumber) return;
        var project = _selectedProject?.Name ?? "Werktekening HDD";
        var location = _selectedProject?.Location ?? string.Empty;
        WorkDrawingTitleProject.Text = string.IsNullOrWhiteSpace(location) ? project : $"{project} - {location}";
        WorkDrawingTitleScale.Text = $"1:{_workDrawingScale}";
        WorkDrawingTitleBoring.Text = $"Ø{GetBoringDiameterMillimeters()} mm";
        var trace = _profilePoints.Count > 0 ? _profilePoints[^1].Distance : 0;
        WorkDrawingTitleTrace.Text = $"{trace:N1} m";
    }








    private static Point CubicBezier(Point p0, Point c1, Point c2, Point p3, double t)
    {
        var u = 1 - t;
        var tt = t * t;
        var uu = u * u;
        var uuu = uu * u;
        var ttt = tt * t;
        return new Point(
            uuu * p0.X + 3 * uu * t * c1.X + 3 * u * tt * c2.X + ttt * p3.X,
            uuu * p0.Y + 3 * uu * t * c1.Y + 3 * u * tt * c2.Y + ttt * p3.Y);
    }


    private static string SmoothSvgPathData(IReadOnlyList<Point> points, Func<double, string> format)
    {
        var builder = new StringBuilder();
        builder.Append($"M {format(points[0].X)} {format(points[0].Y)}");
        for (var i = 0; i < points.Count - 1; i++)
        {
            var p0 = i == 0 ? points[i] : points[i - 1];
            var p1 = points[i];
            var p2 = points[i + 1];
            var p3 = i + 2 < points.Count ? points[i + 2] : p2;
            var c1 = new Point(p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6);
            var c2 = new Point(p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6);
            builder.Append(CultureInfo.InvariantCulture, $" C {format(c1.X)} {format(c1.Y)}, {format(c2.X)} {format(c2.Y)}, {format(p2.X)} {format(p2.Y)}");
        }

        return builder.ToString();
    }

    private void DrawBgtSurfaceStrip(Func<double, double> xDistance, double top, double height, double total, double left, double plotWidth)
    {
        var segments = GetBgtSurfaceSegments(total);
        if (segments.Count == 0) return;

        var title = new TextBlock
        {
            Text = "Oppervlak",
            Foreground = Brush("#5F7785"),
            FontSize = 9,
            FontWeight = FontWeights.Bold
        };
        Canvas.SetLeft(title, 5);
        Canvas.SetTop(title, top + 2);
        ProfileCanvas.Children.Add(title);

        foreach (var segment in segments)
        {
            var startX = xDistance(segment.Start);
            var endX = xDistance(segment.End);
            var width = Math.Max(3, endX - startX);
            var rect = new Rectangle
            {
                Width = width,
                Height = height,
                Fill = Brush(segment.Color),
                Stroke = Brush("#FFFFFF"),
                StrokeThickness = 1,
                Opacity = 0.78
            };
            Canvas.SetLeft(rect, startX);
            Canvas.SetTop(rect, top);
            ProfileCanvas.Children.Add(rect);

            if (segment.Start > 0.1)
            {
                ProfileCanvas.Children.Add(new Line
                {
                    X1 = startX,
                    X2 = startX,
                    Y1 = top,
                    Y2 = top + height + 12,
                    Stroke = Brush("#64748B"),
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection([2, 2]),
                    Opacity = 0.75
                });
            }

            if (width > 44)
            {
                var label = new TextBlock
                {
                    Text = $"{segment.Length:N0} m {segment.Label}",
                    Foreground = Brush("#071422"),
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Width = Math.Max(20, width - 6)
                };
                Canvas.SetLeft(label, startX + 3);
                Canvas.SetTop(label, top + 3);
                ProfileCanvas.Children.Add(label);
            }
        }

        ProfileCanvas.Children.Add(new Line
        {
            X1 = left,
            X2 = left + plotWidth,
            Y1 = top + height,
            Y2 = top + height,
            Stroke = Brush("#CBD5E1"),
            StrokeThickness = 1
        });
    }






    private double InterpolateBoreNapAtDistance(double distance)
    {
        if (!_profileSmoothBore || _profilePoints.Count < 3)
        {
            return InterpolateProfileValue(distance, point => point.Nap);
        }

        if (distance <= _profilePoints[0].Distance) return _profilePoints[0].Nap;
        if (distance >= _profilePoints[^1].Distance) return _profilePoints[^1].Nap;

        return EvaluateMonotonicHermite(_profilePoints, distance);
    }

    // "Boorlijn vloeiend" gebruikte een uniforme Catmull-Rom-spline door de
    // dieptepunten. Bij een steil segment (bv. een grote intrede-/uittredehoek) naast
    // een vlak middenstuk kan zo'n spline flink overshoten of zelfs terug omhoog
    // lussen — een fysiek onmogelijke boorlijn die niet overeenkomt met de simpele
    // rechte-lijn segmentafstanden/-hoeken die ernaast getoond worden. Een monotone
    // kubieke Hermite-spline (Fritsch-Carlson) blijft per definitie tussen de NAP-
    // waarden van de twee omliggende dieptepunten — geen overshoot, geen lus — en
    // levert nog steeds een vloeiende (geen geknikte) curve.
    private static double EvaluateMonotonicHermite(IReadOnlyList<ProfilePointRow> points, double distance)
    {
        var n = points.Count;
        var xs = new double[n];
        var ys = new double[n];
        for (var i = 0; i < n; i++)
        {
            xs[i] = points[i].Distance;
            ys[i] = points[i].Nap;
        }

        var deltas = new double[n - 1];
        for (var i = 0; i < n - 1; i++)
        {
            var dx = Math.Max(0.0001, xs[i + 1] - xs[i]);
            deltas[i] = (ys[i + 1] - ys[i]) / dx;
        }

        var tangents = new double[n];
        tangents[0] = deltas[0];
        tangents[n - 1] = deltas[n - 2];
        for (var i = 1; i < n - 1; i++)
        {
            tangents[i] = (deltas[i - 1] + deltas[i]) / 2.0;
        }

        for (var i = 0; i < n - 1; i++)
        {
            if (deltas[i] == 0)
            {
                tangents[i] = 0;
                tangents[i + 1] = 0;
                continue;
            }

            var alpha = tangents[i] / deltas[i];
            var beta = tangents[i + 1] / deltas[i];
            var sumSq = alpha * alpha + beta * beta;
            if (sumSq > 9)
            {
                var tau = 3.0 / Math.Sqrt(sumSq);
                tangents[i] = tau * alpha * deltas[i];
                tangents[i + 1] = tau * beta * deltas[i];
            }
        }

        var segIndex = 0;
        for (var i = 0; i < n - 1; i++)
        {
            segIndex = i;
            if (distance <= xs[i + 1]) break;
        }

        var x0 = xs[segIndex];
        var x1 = xs[segIndex + 1];
        var h = Math.Max(0.0001, x1 - x0);
        var t = Math.Clamp((distance - x0) / h, 0, 1);
        var t2 = t * t;
        var t3 = t2 * t;

        var h00 = 2 * t3 - 3 * t2 + 1;
        var h10 = t3 - 2 * t2 + t;
        var h01 = -2 * t3 + 3 * t2;
        var h11 = t3 - t2;

        return h00 * ys[segIndex] + h10 * h * tangents[segIndex] + h01 * ys[segIndex + 1] + h11 * h * tangents[segIndex + 1];
    }




    private double BoreLengthAtDistance(double distance)
    {
        var length = 0d;
        for (var i = 0; i < _profilePoints.Count - 1; i++)
        {
            var from = _profilePoints[i];
            var to = _profilePoints[i + 1];
            if (distance <= from.Distance) break;
            var segmentEnd = Math.Min(distance, to.Distance);
            var horizontal = Math.Max(0, segmentEnd - from.Distance);
            var segmentHorizontal = Math.Max(0.001, to.Distance - from.Distance);
            var vertical = (to.Nap - from.Nap) * horizontal / segmentHorizontal;
            length += Math.Sqrt(horizontal * horizontal + vertical * vertical);
            if (distance <= to.Distance) break;
        }
        return length;
    }

    private double? EstimateVerticalRadiusAt(double distance)
    {
        if (_profilePoints.Count < 3) return null;
        var nearest = _profilePoints
            .Select((point, index) => new { point.Distance, Index = index, Delta = Math.Abs(point.Distance - distance) })
            .Where(item => item.Index > 0 && item.Index < _profilePoints.Count - 1)
            .OrderBy(item => item.Delta)
            .FirstOrDefault();
        if (nearest is null || nearest.Delta > 0.25) return null;

        var a = _profilePoints[nearest.Index - 1];
        var b = _profilePoints[nearest.Index];
        var c = _profilePoints[nearest.Index + 1];
        var ab = Distance2D(a.Distance, a.Nap, b.Distance, b.Nap);
        var bc = Distance2D(b.Distance, b.Nap, c.Distance, c.Nap);
        var ca = Distance2D(c.Distance, c.Nap, a.Distance, a.Nap);
        var area = Math.Abs((a.Distance * (b.Nap - c.Nap) + b.Distance * (c.Nap - a.Nap) + c.Distance * (a.Nap - b.Nap)) / 2);
        if (area < 0.001) return null;
        return ab * bc * ca / (4 * area);
    }

    private static double Distance2D(double x1, double y1, double x2, double y2) =>
        Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));





    private void ProfilePointAction_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not string tag) return;
        var parts = tag.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var index) || index < 0 || index >= _profilePoints.Count) return;

        var point = _profilePoints[index];
        switch (parts[0])
        {
            case "up":
                point = point with { Depth = Math.Max(0, point.Depth - 0.1) };
                _profilePoints[index] = point with { Nap = Math.Round(point.Surface - point.Depth, 2) };
                _profileHasUnsavedChanges = true;
                RenderProfilePanel();
                return;
            case "down":
                point = point with { Depth = point.Depth + 0.1 };
                _profilePoints[index] = point with { Nap = Math.Round(point.Surface - point.Depth, 2) };
                _profileHasUnsavedChanges = true;
                RenderProfilePanel();
                return;
            case "left" when index > 0:
                point = point with { Distance = Math.Max(_profilePoints[index - 1].Distance + 0.1, point.Distance - 1) };
                break;
            case "right" when index < _profilePoints.Count - 1:
                point = point with { Distance = Math.Min(_profilePoints[index + 1].Distance - 0.1, point.Distance + 1) };
                break;
            case "delete" when index > 0 && index < _profilePoints.Count - 1:
                _profilePoints.RemoveAt(index);
                _profileGeometryDirty = true;
                _profileHasUnsavedChanges = true;
                RenderProfilePanel();
                return;
        }

        _profilePoints[index] = point;
        _profileGeometryDirty = true;
        _profileHasUnsavedChanges = true;
        RenderProfilePanel();
    }

















    private static IEnumerable<IReadOnlyList<List<double[]>>> EnumeratePolygonCoordinateRings(GeoJsonGeometry geometry)
    {
        if (geometry.Type.Equals("Polygon", StringComparison.OrdinalIgnoreCase))
        {
            var rings = ExtractNestedCoordinateLists(geometry.Coordinates).ToList();
            if (rings.Count > 0) yield return rings;
        }
        else if (geometry.Type.Equals("MultiPolygon", StringComparison.OrdinalIgnoreCase) &&
                 geometry.Coordinates is System.Collections.IEnumerable polygons &&
                 geometry.Coordinates is not string)
        {
            foreach (var polygon in polygons)
            {
                var rings = ExtractNestedCoordinateLists(polygon).ToList();
                if (rings.Count > 0) yield return rings;
            }
        }
    }










    private static double PolygonArea(IReadOnlyList<RdPoint> ring)
    {
        if (ring.Count < 3) return double.MaxValue;
        var area = 0d;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            area += (ring[j].X + ring[i].X) * (ring[j].Y - ring[i].Y);
        }

        return Math.Abs(area / 2);
    }

    private static string GetFeatureString(GeoJsonFeature feature, string key, string fallback)
    {
        return feature.Properties.TryGetValue(key, out var value) && value is not null && !string.IsNullOrWhiteSpace(value.ToString())
            ? value.ToString()!
            : fallback;
    }

    private static bool PointInPolygon(double x, double y, IReadOnlyList<RdPoint> ring)
    {
        var inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var pi = ring[i];
            var pj = ring[j];
            if ((pi.Y > y) != (pj.Y > y))
            {
                // De deling is hier veilig: de voorwaarde hierboven garandeert dat
                // pi.Y en pj.Y aan weerszijden van y liggen, dus pj.Y != pi.Y.
                // (De oude variant verving negatieve noemers via Math.Max door een
                // epsilon, waardoor de helft van de randen een onzinnig snijpunt
                // kreeg en grote polygonen massaal fout werden geclassificeerd —
                // de bron van het "phantom water" in de oppervlakteanalyse.)
                var intersectX = pi.X + (pj.X - pi.X) * (y - pi.Y) / (pj.Y - pi.Y);
                if (x < intersectX)
                {
                    inside = !inside;
                }
            }
        }

        return inside;
    }

    private double SegmentDistance(int index) => index >= 0 && index < _profilePoints.Count - 1
        ? Math.Max(0, _profilePoints[index + 1].Distance - _profilePoints[index].Distance)
        : 0;

    private double SegmentAngle(int index)
    {
        var distance = SegmentDistance(index);
        if (distance <= 0 || index < 0 || index >= _profilePoints.Count - 1) return 0;
        var deltaHeight = _profilePoints[index + 1].Nap - _profilePoints[index].Nap;
        return Math.Atan2(deltaHeight, distance) * 180 / Math.PI;
    }

    private void ProfileSave_OnClick(object sender, RoutedEventArgs e)
    {
        OutputText.Text = SaveDepthProfile();
    }

    private void ProfileZoomIn_OnClick(object sender, RoutedEventArgs e)
    {
        _profileViewZoom = Math.Min(3.0, _profileViewZoom * 1.15);
        _profileLayoutLocked = true;
        SaveProfileVisualSettings();
        RenderProfilePanel();
    }

    private void ProfileZoomOut_OnClick(object sender, RoutedEventArgs e)
    {
        _profileViewZoom = Math.Max(0.55, _profileViewZoom / 1.15);
        _profileLayoutLocked = true;
        SaveProfileVisualSettings();
        RenderProfilePanel();
    }

    private void ProfileExpand_OnClick(object sender, RoutedEventArgs e)
    {
        _profileExpanded = !_profileExpanded;
        _profileLayoutLocked = true;
        if (_profileExpanded && _profileViewZoom < 1.05) _profileViewZoom = 1.15;
        if (!_profileExpanded) _profileViewZoom = 1.0;
        SaveProfileVisualSettings();
        RenderProfilePanel();
        OutputText.Text = _profileExpanded
            ? "Dwarsprofiel vergroot\n\nHet profiel is vastgezet en zoomt nu als een vaste tekening, zonder herberekening van de lijnhoogtes."
            : "Dwarsprofiel ingeklapt\n\nDe profielweergave staat terug op 100%.";
    }

    private void ProfileSmooth_OnClick(object sender, RoutedEventArgs e)
    {
        OutputText.Text = ToggleSmoothProfile();
    }

    private void ProfileLock_OnClick(object sender, RoutedEventArgs e)
    {
        OutputText.Text = ToggleProfileLayoutLock();
    }

    private void ProfileAlign_OnClick(object sender, RoutedEventArgs e)
    {
        OutputText.Text = AlignProfileToMap();
    }










    private string AddDepthControlPoint()
    {
        EnsureProfilePoints();
        if (_profilePoints.Count < 2) return "Geen profiel beschikbaar. Genereer eerst een profiel.";

        var longestIndex = 0;
        var longestDistance = 0d;
        for (var i = 0; i < _profilePoints.Count - 1; i++)
        {
            var distance = SegmentDistance(i);
            if (distance <= longestDistance) continue;
            longestDistance = distance;
            longestIndex = i;
        }

        var a = _profilePoints[longestIndex];
        var b = _profilePoints[longestIndex + 1];
        var distanceAt = (a.Distance + b.Distance) / 2;
        var depth = Math.Max(0, (a.Depth + b.Depth) / 2);
        var traceRows = GetTraceRowsForProfile();
        var xy = InterpolateTracePoint(traceRows, BuildTraceDistances(traceRows), distanceAt);
        var surface = GetProfileSurfaceNap(xy.X, xy.Y, distanceAt, Math.Max(1, _profilePoints[^1].Distance), _profilePoints.Count, (a.Surface + b.Surface) / 2);
        _profilePoints.Add(new ProfilePointRow(0, "Dieptepunt", xy.X, xy.Y, Math.Round(distanceAt, 2), Math.Round(depth, 2), Math.Round(surface - depth, 2), Math.Round(surface, 2)));
        RecalculateProfileRolesAndNap();
        _profileHasUnsavedChanges = true;
        _profileGeometryDirty = false;
        RenderProfilePanel();
        return $"Dieptepunt toegevoegd\n\nPositie: {distanceAt:N1} m\nDiepte: {depth:N2} m\nSegment: {longestDistance:N1} m opgesplitst.";
    }


    private void SendFeatureDetail(string message)
    {
        if (StepThreeMapView.CoreWebView2 is null) return;

        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            var detailId = root.TryGetProperty("detailId", out var detailIdElement)
                ? detailIdElement.ToString()
                : "";

            MapFeatureDetails.TryGetValue(detailId, out var details);
            var relatedDocuments = FindRelatedDocuments(details).ToList();
            var payload = JsonSerializer.Serialize(new
            {
                type = "featureDetail",
                detailId,
            properties = details ?? new Dictionary<string, object>(),
                documents = relatedDocuments.Select(doc => new
                {
                    doc.Id,
                    doc.Name,
                    doc.Type,
                    doc.SizeKb
                })
            }, JsonOptions);

            _gisMap.TryPostJson(
                StepThreeMapView.CoreWebView2,
                payload,
                exception => AppendMapDiagnostic($"Feature detail naar kaart sturen mislukt: {exception.Message}"));
        }
        catch (Exception exception)
        {
            AppendMapDiagnostic($"Feature detail fout: {exception.Message}");
        }
    }
    private IEnumerable<ProjectDocumentEntry> FindRelatedDocuments(IReadOnlyDictionary<string, object>? details)
    {
        if (_selectedProject is null) yield break;

        _projectFiles = _projects.GetProjectFiles(_selectedProject.Id);
        var docs = BuildProjectDocumentEntries(_projectFiles).ToList();
        if (docs.Count == 0) yield break;

        var haystack = details is null
            ? ""
            : string.Join(" ", details.Select(pair => $"{pair.Key} {pair.Value}"));
        var objectType = details is not null && details.TryGetValue("objectType", out var objectValue)
                ? objectValue?.ToString() ?? ""
                : "";
        var wantsProfile = ContainsAny(haystack, "profiel", "schets", "boring", "boorstaat", "sondering", "mantel", "persing", "boogzinker", "bescherm") ||
                           ContainsAny(objectType, "profiel", "schets", "boring", "mantel", "persing", "boogzinker", "bescherm");

        var ranked = docs
            .Select(doc => new { Document = doc, Score = ScoreRelatedDocument(doc, haystack, wantsProfile) })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Document.Name)
            .Take(8)
            .Select(item => item.Document)
            .ToList();

        if (ranked.Count == 0 && wantsProfile)
        {
            ranked = docs
                .Where(doc => ContainsAny(doc.Name, "profiel", "schets", "boring", "mantel", "persing", "boogzinker", "ligging", "algemeen"))
                .OrderBy(doc => doc.Name)
                .Take(8)
                .ToList();
        }

        foreach (var doc in ranked)
        {
            _mapDocumentEntries[doc.Id] = doc;
            yield return doc;
        }
    }

    private static int ScoreRelatedDocument(ProjectDocumentEntry doc, string haystack, bool wantsProfile)
    {
        var name = doc.Name;
        var score = 0;
        if (wantsProfile && ContainsAny(name, "profiel", "schets", "boring", "mantel", "persing", "boogzinker")) score += 100;
        if (ContainsAny(name, "ligging", "algemeen", "liander", "klic")) score += 15;
        if (doc.Type.Equals("PDF", StringComparison.OrdinalIgnoreCase)) score += 10;

        foreach (Match token in Regex.Matches(name, @"[A-Za-z0-9]{5,}"))
        {
            if (haystack.Contains(token.Value, StringComparison.OrdinalIgnoreCase)) score += 30;
        }

        return score;
    }

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private void OpenMapDocument(string message)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            var id = document.RootElement.TryGetProperty("id", out var idElement) ? idElement.ToString() : "";
            if (string.IsNullOrWhiteSpace(id) || !_mapDocumentEntries.TryGetValue(id, out var doc))
            {
                OutputText.Text = "Bijlage kon niet worden gevonden. Open eerst de KLIC-popup opnieuw.";
                return;
            }

            OutputText.Text = $"Bijlage openen\n\n{doc.Name}\n{doc.Type} · {doc.SizeKb:N0} KB";
            TryOpenDocument(doc);
        }
        catch (Exception exception)
        {
            OutputText.Text = $"Bijlage openen lukte niet\n\n{exception.Message}";
        }
    }

    private void MapOverlay_OnClick(object sender, RoutedEventArgs e)
    {
        if (BlockIfCurrentMapReportLocked()) return;
        if ((sender as Button)?.Tag is not string overlayId) return;

        var visible = !_mapOverlayStates.TryGetValue(overlayId, out var current) || !current;
        _mapOverlayStates[overlayId] = visible;
        if (IsSurfaceAnalysisOverlay(overlayId))
        {
            _mapBgtSurfaceSamples = [];
        }
        _suppressProjectLayerSend = true;
        try
        {
            RenderStepThree(force: true);
        }
        finally
        {
            _suppressProjectLayerSend = false;
        }
        if (overlayId.Equals("profileTracePoints", StringComparison.OrdinalIgnoreCase))
        {
            RenderProfilePanel();
            SaveCurrentMapStateAfterLayerChange();
            return;
        }
        if (overlayId.StartsWith("boreTrace", StringComparison.OrdinalIgnoreCase))
        {
            SendTraceStateToMap();
            SaveCurrentMapStateAfterLayerChange();
            return;
        }
        if (overlayId.Equals("machines", StringComparison.OrdinalIgnoreCase))
        {
            SendMachineStateToMap();
            SaveCurrentMapStateAfterLayerChange();
            return;
        }
        SendMapMessage($"{{\"type\":\"overlay\",\"id\":\"{overlayId}\",\"visible\":{visible.ToString().ToLowerInvariant()}}}");
        SendTraceStateToMap();
        SaveCurrentMapStateAfterLayerChange();
    }

    private void OverlayBulk_OnClick(object sender, RoutedEventArgs e)
    {
        if (BlockIfCurrentMapReportLocked()) return;
        if ((sender as Button)?.Tag is not bool visible) return;

        if (_selectedStep?.Number == 4)
        {
            _gisLayerState.NormalizeSurfaceAnalysisMapState();
            foreach (var key in _mapOverlayStates.Keys.Where(key => !IsImportOverlayKey(key)).ToList())
            {
                _mapOverlayStates[key] = false;
            }

            _mapOverlayStates["baseMap"] = visible;
            _mapOverlayStates["parcels"] = visible;
            _mapOverlayStates["boreTrace"] = visible;
            _mapOverlayStates["boreTraceInfo"] = visible;
            _mapOverlayStates["boreTraceNumbers"] = false;
            _mapOverlayStates["boreTraceLengths"] = false;
            _mapBgtSurfaceSamples = [];
        }
        else
        {
            _gisLayerState.SetNonImportOverlays(visible);
        }

        _suppressProjectLayerSend = true;
        try
        {
            RenderStepThree(force: true);
        }
        finally
        {
            _suppressProjectLayerSend = false;
        }

        SendAllFilterStatesToMap();
        OutputText.Text = visible ? "Alle filters ingeschakeld." : "Alle filters uitgeschakeld.";
        SaveCurrentMapStateAfterLayerChange();
    }

    private void ImportBulk_OnClick(object sender, RoutedEventArgs e)
    {
        if (BlockIfCurrentMapReportLocked()) return;
        if ((sender as Button)?.Tag is not bool visible) return;

        if (_selectedStep?.Number == 4)
        {
            _gisLayerState.NormalizeSurfaceAnalysisMapState();
            _mapOverlayStates["bgt"] = visible;
            _mapOverlayStates["bagImport"] = visible;
            _mapOverlayStates["klic"] = visible;
            _mapOverlayStates["klicBuffer"] = visible;
            _mapOverlayStates["designImport"] = false;
            _mapOverlayStates["customImport"] = false;
            foreach (var key in _klicThemeStates.Keys.ToList())
            {
                _klicThemeStates[key] = visible;
            }
            foreach (var key in _bgtSurfaceStates.Keys.ToList())
            {
                _bgtSurfaceStates[key] = visible;
            }
            foreach (var key in _projectLayerStates.Keys.ToList())
            {
                _projectLayerStates[key] = visible;
            }
            _mapBgtSurfaceSamples = [];
        }
        else
        {
            _gisLayerState.SetImportFilters(visible);
        }

        _suppressProjectLayerSend = true;
        try
        {
            RenderStepThree(force: true);
        }
        finally
        {
            _suppressProjectLayerSend = false;
        }

        SendAllFilterStatesToMap();
        OutputText.Text = visible ? "Alle importbestanden ingeschakeld." : "Alle importbestanden uitgeschakeld.";
        SaveCurrentMapStateAfterLayerChange();
    }

    private static bool IsImportOverlayKey(string key) => GisLayerStateService.IsImportOverlayKey(key);

    private void SendAllFilterStatesToMap()
    {
        foreach (var overlay in _mapOverlayStates.Where(overlay =>
                     !overlay.Key.StartsWith("boreTrace", StringComparison.OrdinalIgnoreCase) &&
                     !overlay.Key.Equals("profileTracePoints", StringComparison.OrdinalIgnoreCase)))
        {
            SendMapMessage($"{{\"type\":\"overlay\",\"id\":\"{overlay.Key}\",\"visible\":{overlay.Value.ToString().ToLowerInvariant()}}}");
        }

        foreach (var theme in _klicThemeStates)
        {
            var payload = JsonSerializer.Serialize(new { type = "klicTheme", theme = theme.Key, visible = theme.Value }, JsonOptions);
            SendMapMessage(payload);
        }

        foreach (var layer in _projectLayerStates)
        {
            var payload = JsonSerializer.Serialize(new { type = "projectLayerVisibility", layerId = layer.Key, visible = layer.Value }, JsonOptions);
            SendMapMessage(payload);
        }

        SendBgtSurfaceFiltersToMap();

        SendTraceStateToMap();
        SendMachineStateToMap();
    }

    private void ProjectLayer_OnClick(object sender, RoutedEventArgs e)
    {
        if (BlockIfCurrentMapReportLocked()) return;
        if ((sender as Button)?.Tag is not string layerId) return;

        var visible = _gisLayerState.ToggleProjectLayer(layerId);
        if (_selectedStep?.Number == 4)
        {
            _mapBgtSurfaceSamples = [];
        }
        _suppressProjectLayerSend = true;
        try
        {
            RenderStepThree(force: true);
        }
        finally
        {
            _suppressProjectLayerSend = false;
        }

        var payload = JsonSerializer.Serialize(new
        {
            type = "projectLayerVisibility",
            layerId,
            visible
        }, JsonOptions);
        SendMapMessage(payload);
        SaveCurrentMapStateAfterLayerChange();
    }

    private void KlicTheme_OnClick(object sender, RoutedEventArgs e)
    {
        if (BlockIfCurrentMapReportLocked()) return;
        if ((sender as Button)?.Tag is not string theme) return;

        var visible = _gisLayerState.ToggleKlicTheme(theme);
        if (_selectedStep?.Number == 4)
        {
            _mapBgtSurfaceSamples = [];
        }
        _suppressProjectLayerSend = true;
        try
        {
            RenderStepThree(force: true);
        }
        finally
        {
            _suppressProjectLayerSend = false;
        }
        var payload = JsonSerializer.Serialize(new { type = "klicTheme", theme, visible }, JsonOptions);
        SendMapMessage(payload);
        SendTraceStateToMap();
        SaveCurrentMapStateAfterLayerChange();
    }




    private async void AiAnalysis_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedProject is null)
        {
            SetAiAnalysisOutput("Geen project actief.");
            return;
        }

        _projectFiles = _projects.GetProjectFiles(_selectedProject.Id);
        var layers = BuildProjectMapLayers(_projectFiles);
        var docs = BuildProjectDocumentEntries(_projectFiles).ToList();
        var themes = layers
            .SelectMany(layer => layer.FeatureCollection.Features)
            .Select(feature => feature.Properties.TryGetValue("theme", out var theme) ? theme?.ToString() : null)
            .Where(theme => !string.IsNullOrWhiteSpace(theme))
            .GroupBy(theme => theme!, StringComparer.OrdinalIgnoreCase)
            .Select(group => $"{KlicThemeLabel(group.Key)}: {group.Count()}")
            .ToArray();

        var localContext = BuildAiAnalysisContext(layers, docs, themes);
        var apiKey = GetGroqApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            SetAiAnalysisOutput("AI is niet gekoppeld.\n\nZet een geldige API key als Windows gebruikersvariabele en start de app opnieuw.");
            return;
        }

        try
        {
            SetAiAnalysisOutput("AI-analyse wordt uitgevoerd...");
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var request = new
            {
                model = "llama-3.1-8b-instant",
                temperature = 0.25,
                max_tokens = 900,
                messages = new[]
                {
                    new
                    {
                        role = "system",
                        content = "Je bent Borevexa AI, een technische HDD-prescan assistent. Antwoord kort, praktisch en in het Nederlands. Benoem risico's, ontbrekende data en concrete vervolgstappen. Gebruik alleen de meegegeven projectcontext."
                    },
                    new
                    {
                        role = "user",
                        content = "Analyseer deze prescan en geef aandachtspunten voor ontwerp, KLIC/BGT, boorlijn, machinekeuze en export.\n\n" + localContext
                    }
                }
            };

            using var content = new StringContent(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await http.PostAsync("https://api.groq.com/openai/v1/chat/completions", content);
            var responseText = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                var authHint = (int)response.StatusCode is 401 or 403
                    ? "De API key is geweigerd. Controleer of de key actief is en stel hem opnieuw in."
                    : $"AI is tijdelijk niet beschikbaar: {(int)response.StatusCode} {response.ReasonPhrase}.";
                SetAiAnalysisOutput("AI-analyse niet uitgevoerd.\n\n" + authHint);
                return;
            }

            using var json = JsonDocument.Parse(responseText);
            var answer = json.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            SetAiAnalysisOutput(string.IsNullOrWhiteSpace(answer) ? localContext : answer.Trim());
        }
        catch (Exception ex)
        {
            SetAiAnalysisOutput($"AI-analyse kon niet worden uitgevoerd.\n\n{ex.Message}");
        }
    }

    private async void AskGroq_OnClick(object sender, RoutedEventArgs e)
    {
        var question = _aiQuestionInput?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(question) && StepOneAiQuestionInput is not null)
        {
            question = StepOneAiQuestionInput.Text?.Trim();
        }
        if (string.IsNullOrWhiteSpace(question))
        {
            SetAiAnalysisOutput("Typ eerst een vraag voor AI.");
            return;
        }

        var apiKey = GetGroqApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            SetAiAnalysisOutput("AI is niet gekoppeld.\n\nZet een geldige API key als Windows gebruikersvariabele en start de app opnieuw.");
            return;
        }

        try
        {
            SetAiAnalysisOutput("Vraag wordt naar AI gestuurd...");
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var request = new
            {
                model = "llama-3.1-8b-instant",
                temperature = 0.2,
                max_tokens = 700,
                messages = new[]
                {
                    new
                    {
                        role = "system",
                        content = "Je bent Borevexa AI, een technische HDD-prescan assistent. Antwoord in het Nederlands, concreet en praktisch. Je krijgt alleen de vraag van de gebruiker, geen automatisch meegestuurde projectdata."
                    },
                    new
                    {
                        role = "user",
                        content = $"Stap {_selectedStep?.Number ?? 0}: {_selectedStep?.Title ?? "-"}\n\nVraag:\n{question}"
                    }
                }
            };

            using var content = new StringContent(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await http.PostAsync("https://api.groq.com/openai/v1/chat/completions", content);
            var responseText = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                var authHint = (int)response.StatusCode is 401 or 403
                    ? "De API key is geweigerd. Controleer of de key actief is en stel hem opnieuw in."
                    : $"AI is tijdelijk niet beschikbaar: {(int)response.StatusCode} {response.ReasonPhrase}.";
                SetAiAnalysisOutput("AI-vraag niet uitgevoerd.\n\n" + authHint);
                return;
            }

            using var json = JsonDocument.Parse(responseText);
            var answer = json.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            SetAiAnalysisOutput(string.IsNullOrWhiteSpace(answer) ? "AI gaf geen antwoord." : answer.Trim());
        }
        catch (Exception ex)
        {
            SetAiAnalysisOutput($"AI-vraag kon niet worden uitgevoerd.\n\n{ex.Message}");
        }
    }





    private static string? GetGroqApiKey()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("GROQ_API_KEY", EnvironmentVariableTarget.User),
            Environment.GetEnvironmentVariable("GROQ_API_KEY", EnvironmentVariableTarget.Machine),
            Environment.GetEnvironmentVariable("GROQ_API_KEY")
        };

        return candidates.FirstOrDefault(IsGroqApiKeyCandidate);
    }

    private static bool IsGroqApiKeyCandidate(string? key)
    {
        return !string.IsNullOrWhiteSpace(key)
               && key.StartsWith("gsk_", StringComparison.Ordinal)
               && key.Length > 30;
    }
    private void MapZoomIn_OnClick(object sender, RoutedEventArgs e)
    {
        if (_mapLocked) return;
        SendMapMessage("{\"type\":\"zoomIn\"}");
    }

    private void MapZoomOut_OnClick(object sender, RoutedEventArgs e)
    {
        if (_mapLocked) return;
        SendMapMessage("{\"type\":\"zoomOut\"}");
    }



    private void MapScaleComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiInitialized) return;
        if (MapScaleComboBox?.SelectedItem is not ComboBoxItem item) return;
        var raw = item.Tag?.ToString() ?? "";
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var scale)) return;

        _workDrawingScale = scale;
        SendMapMessage("{\"type\":\"mapScale\",\"scale\":" + scale.ToString(CultureInfo.InvariantCulture) + "}");
        if (_selectedProject is not null && _selectedStep is not null && IsMapWorkspaceStep(_selectedStep.Number))
        {
            SaveMapStateForStep(_selectedStep.Number, false);
            RefreshInlineReportPreviewIfVisible();
        }
    }

    private void MapSearchTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        e.Handled = true;
        _ = SearchMapLocationAsync();
    }

    private void MapSearch_OnClick(object sender, RoutedEventArgs e) => _ = SearchMapLocationAsync();

    private void MapRestore_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedStep?.Number == 4)
        {
            _gisLayerState.ApplySurfaceAnalysisMapDefaults();
            _mapBgtSurfaceSamples = [];
        }
        else
        {
            _gisLayerState.RestoreVisibleDefaults();
        }

        _suppressProjectLayerSend = true;
        try
        {
            RenderStepThree(force: true);
        }
        finally
        {
            _suppressProjectLayerSend = false;
        }

        SendMapMessage("{\"type\":\"base\",\"id\":\"pdok-brt\"}");
        SendProjectLayersToMap();
        SendAllFilterStatesToMap();
        SendStoredMapStateToMap();
        SaveMapStateForStep(_selectedStep?.Number ?? 3, false);
        RefreshInlineReportPreviewIfVisible();

        if (MapSearchStatusText is not null) MapSearchStatusText.Text = "Kaart hersteld.";
        if (OutputText is not null)
        {
            OutputText.Text = _selectedStep?.Number == 4
                ? "Kaart hersteld\n\nStap 4 toont BGT, BAG/Kadaster, KLIC en de boorlijn. AHN, BRO, ontwerp en overige imports staan uit."
                : "Kaart hersteld\n\nBRT ondergrond, projectlagen, KLIC/BAG/BGT en boorlijnlagen staan weer zichtbaar.";
        }
    }

    private async Task SearchMapLocationAsync()
    {
        if (!_uiInitialized || MapSearchTextBox is null) return;

        var query = MapSearchTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            if (MapSearchStatusText is not null) MapSearchStatusText.Text = "Vul een plaats of adres in.";
            return;
        }

        try
        {
            if (MapSearchStatusText is not null) MapSearchStatusText.Text = "Zoeken...";

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Borevexa/1.0 (map search)");

            var url = "https://nominatim.openstreetmap.org/search?format=jsonv2&limit=1&countrycodes=nl&q=" + Uri.EscapeDataString(query);
            var json = await client.GetStringAsync(url);
            using var document = JsonDocument.Parse(json);

            var result = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray().FirstOrDefault()
                : default;

            if (result.ValueKind != JsonValueKind.Object ||
                !double.TryParse(JsonText(result, "lon", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon) ||
                !double.TryParse(JsonText(result, "lat", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat))
            {
                if (MapSearchStatusText is not null) MapSearchStatusText.Text = "Niet gevonden.";
                return;
            }

            const double searchZoom = 14.2;
            var camera = new { center = new[] { lon, lat }, zoom = searchZoom, bearing = 0, pitch = 0 };
            SendMapMessage(JsonSerializer.Serialize(new { type = "restoreCamera", camera }, JsonOptions));

            if (_selectedStep is not null && IsMapWorkspaceStep(_selectedStep.Number))
            {
                _lastMapCamera = JsonSerializer.SerializeToElement(camera, JsonOptions);
                SaveMapStateForStep(_selectedStep.Number, false);
                RefreshInlineReportPreviewIfVisible();
            }

            if (MapSearchStatusText is not null)
            {
                MapSearchStatusText.Text = ShortReportCell(JsonText(result, "display_name", query), 56);
            }
        }
        catch (Exception ex)
        {
            if (MapSearchStatusText is not null) MapSearchStatusText.Text = "Zoeken mislukt.";
            if (OutputText is not null) OutputText.Text = $"Zoeken mislukt\n\n{ex.Message}";
        }
    }


    private void UpdateMapLockButton()
    {
        UpdateMapReportLockButton();
    }

    private bool IsMapWorkspaceStep(int stepNumber) => _gisMapWorkspaces.IsMapWorkspaceStep(stepNumber);


    private GisMapWorkspaceRuntime GetCurrentMapWorkspaceRuntime(int stepNumber, string? activeReportVariantKey = null)
    {
        var selectedSubstep = _selectedStep?.Number == stepNumber ? _selectedSubstep : null;
        return _gisMapWorkspaces.CreateRuntime(stepNumber, selectedSubstep, activeReportVariantKey);
    }

    private string? GetCurrentMapStateContextKey(int stepNumber) => GetCurrentMapWorkspaceRuntime(stepNumber).ContextKey;

    private string? GetCurrentMapStateJson(int stepNumber)
    {
        if (_selectedProject is null) return null;

        var runtime = GetCurrentMapWorkspaceRuntime(stepNumber);
        if (runtime.HasScopedContext)
        {
            var scoped = _mapState.GetStepMapStateJson(_selectedProject.Id, stepNumber, runtime.ContextKey, includeLegacyFallback: false);
            if (!string.IsNullOrWhiteSpace(scoped)) return scoped;

            // Map steps share the same chapter number. Falling back to the legacy chapter-wide
            // state here can restore the camera of another substep after an update.
            if (runtime.SuppressLegacyFallback) return null;
        }

        return _mapState.GetStepMapStateJson(_selectedProject.Id, stepNumber);
    }


























    private void MapReset_OnClick(object sender, RoutedEventArgs e)
    {
        SendMapMessage("{\"type\":\"reset\"}");
        OutputText.Text = "Kaart opnieuw gecentreerd.";
    }

    private async void MapSave_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedProject is null || _selectedStep is null) return;
        if (!IsMapWorkspaceStep(_selectedStep.Number)) return;
        if (IsMapReportLocked(_selectedStep.Number))
        {
            UnlockMapForReport(_selectedStep.Number);
            return;
        }

        await SaveAndLockMapForReportAsync(_selectedStep.Number);
    }
    private void LayerTab_OnClick(object sender, RoutedEventArgs e)
    {
        _showingStepThreeDocs = false;
        RenderStepThree(force: true);
        OutputText.Text = $"Lagenpaneel actief. Kies een onderlegger of overlay voor stap {_selectedStep?.Number ?? 3}.";
    }

    private void DocsTab_OnClick(object sender, RoutedEventArgs e)
    {
        _showingStepThreeDocs = true;
        RenderStepThree(force: true);
    }

    private void CloseLayerPanel_OnClick(object sender, RoutedEventArgs e)
    {
        OutputText.Text = "Compacte kaartmodus staat klaar. Ik kan de lagenkolom hierna inklapbaar maken.";
    }


    private void RenderStepThreeDocs()
    {
        KlicDocumentsPanel.Children.Clear();
        RenderKlicDocumentsPanel();
        OutputText.Text = $"KLIC Documenten geopend\n\n{KlicDocumentsPanel.Children.Count} item(s) in de sidebar.";
    }


    private void RenderInformationPlaceholders()
    {
        AddSidebarInfoText(KlicInfoPanel, "Klik op een KLIC lijn in de kaart om de leidinginformatie hier te tonen.");
        AddSidebarInfoText(BgtInfoPanel, "Klik op een BGT object in de kaart om de BGT informatie hier te tonen.");
    }

    private static void AddSidebarInfoText(Panel parent, string text)
    {
        parent.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = Brush("#587080"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap
        });
    }

    private void SetSidebarInfo(Panel parent, string title, string body)
    {
        parent.Children.Clear();
        parent.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brush("#071422"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });
        parent.Children.Add(new TextBlock
        {
            Text = body,
            Foreground = Brush("#334155"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap
        });
    }

    private void DocumentEntry_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not ProjectDocumentEntry doc) return;

        var content = ReadDocumentContent(doc, 18000);
        OutputText.Text = $"Document geselecteerd\n\n{doc.Name}\n{doc.Type} · {doc.SizeKb:N0} KB\n\n{content}";

        if (!IsTextDocumentType(doc.Type))
        {
            TryOpenDocument(doc);
        }
    }



    private void QueueMapSync()
    {
        _gisMap.QueueSync();
    }






    private void BeginMapLoading(string message)
    {
        _mapLoadingDepth++;
        MapLoadingBadge.Visibility = Visibility.Visible;
        MapLoadingBar.Visibility = Visibility.Visible;
        MapLoadingBar.IsIndeterminate = true;
        MapLoadingText.Text = message;
    }

    private void EndMapLoading()
    {
        _mapLoadingDepth = Math.Max(0, _mapLoadingDepth - 1);
        if (_mapLoadingDepth > 0) return;

        MapLoadingBar.IsIndeterminate = false;
        MapLoadingBar.Visibility = Visibility.Collapsed;
        MapLoadingBadge.Visibility = Visibility.Collapsed;
    }




    private void RenderGenericStepRibbon(StepWorkspace workspace)
    {
        StepSpecificRibbonPanel.Children.Clear();
        StepThreeBaseLayersPanel.Children.Clear();
        StepThreeEsriLayersPanel.Children.Clear();
        StepThreeImportsPanel.Children.Clear();
        StepThreeOverlaysPanel.Children.Clear();
        MachineSidebarPanel.Children.Clear();
        MachineSidebarHost.Visibility = Visibility.Collapsed;
        StepProfilePanel.Visibility = Visibility.Collapsed;

        AddGenericStepRibbonActions(workspace);
        AddStepThreeRadio(StepThreeBaseLayersPanel, "BRT Standaard", "pdok-brt");
        AddStepThreeRadio(StepThreeBaseLayersPanel, "BRT Grijs", "pdok-gray");
        AddStepThreeRadio(StepThreeBaseLayersPanel, "BRT Pastel", "pdok-pastel");
        AddStepThreeRadio(StepThreeBaseLayersPanel, "BGT standaardvisualisatie", "pdok-bgt-pastel");
        AddStepThreeRadio(StepThreeBaseLayersPanel, "Luchtfoto (PDOK)", "pdok-aerial");

        AddStepThreeRadio(StepThreeEsriLayersPanel, "Esri Topo RD", "esri-topo-rd");
        AddStepThreeRadio(StepThreeEsriLayersPanel, "Esri Open Topo", "esri-open-topo");
        AddStepThreeRadio(StepThreeEsriLayersPanel, "Esri Luchtfoto (HR)", "esri-aerial");

        AddMapOverlayControls();
        StepSpecificRibbonBorder.Visibility = StepSpecificRibbonPanel.Children.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void AddMapOverlayControls()
    {
        if (_selectedStep?.Number == 3)
        {
            if (IsSelectedStepThreeKlicSubstep())
            {
                EnsureStepThreeKlicMapDefaults();
                AddStepThreeCheckbox("Kadaster percelen (PDOK)", "parcels");
                AddStepThreeKlicLayerControls();
                return;
            }

            _gisLayerState.ApplyStepThreeCleanMapDefaults();
            AddStepThreeCheckbox("Kadaster percelen (PDOK)", "parcels");
            AddStepThreeCheckbox("Boorlijn", "boreTrace");
            // KLIC-lagen zijn hier ook zichtbaar en filterbaar (naast de tekentools),
            // zodat je ondergrondse kabels/leidingen kan zien terwijl je de boorlijn
            // intekent — niet alleen op de aparte KLIC-substap.
            AddStepThreeCheckbox("KLIC lagen", "klic");
            AddStepThreeCheckbox("KLIC bufferzone 1 m links/rechts", "klicBuffer");
            AddKlicThemeToggles(StepThreeOverlaysPanel);
            return;
        }

        if (_selectedStep?.Number == 4)
        {
            AddSurfaceAnalysisMapLayerControls();
            AddSurfaceAnalysisImportControls();
            return;
        }

        AddOverlayBulkButtons();
        AddStepThreeCheckbox("Ondergrond", "baseMap");
        AddStepThreeCheckbox("Kadaster percelen (PDOK)", "parcels");
        AddStepThreeCheckbox("BAG Panden", "buildings");
        AddStepThreeCheckbox("BAG Adressen", "addresses");
        AddStepThreeCheckbox("AHN4 maaiveld (DTM)", "ahn4Dtm");
        AddStepThreeCheckbox("AHN4 ruw hoogtebeeld (DSM)", "ahn4Dsm");
        if (_selectedStep?.Number == ProfileStepNumber)
        {
            // Uit standaard voor het dwarsprofiel (zie NormalizeProfileMapState), maar hier
            // wel expliciet aan/uit te zetten: de grondwaterspiegeldiepte-laag is een bijna
            // dekkende blauwe kleurvlakkaart en werd eerder abusievelijk aangezien voor een
            // kapotte kaartondergrond.
            AddStepThreeCheckbox("BRO Grondwaterspiegeldiepte GHG", "broGroundwaterGhg");
        }
        if (_selectedStep?.Number == 6 && string.Equals(_selectedSubstep?.Number, "6.3", StringComparison.OrdinalIgnoreCase))
        {
            AddStepThreeCheckbox("BRO Geomorfologie 2025-01", "broGeomorphology");
        }
        if (_selectedStep?.Number == 6 && string.Equals(_selectedSubstep?.Number, "6.4", StringComparison.OrdinalIgnoreCase))
        {
            AddStepThreeCheckbox("BRO Bodemkaart 2025-01", "broSoilMap");
        }
        if (_selectedStep?.Number == 6)
        {
            switch (_selectedSubstep?.Number)
            {
                case "6.5":
                case "6.5.1":
                    AddStepThreeCheckbox("BRO Grondwaterspiegeldiepte GHG", "broGroundwaterGhg");
                    break;
                case "6.5.2":
                    AddStepThreeCheckbox("BRO Grondwaterspiegeldiepte GLG", "broGroundwaterGlg");
                    break;
                case "6.5.3":
                    AddStepThreeCheckbox("BRO Grondwaterspiegeldiepte GVG", "broGroundwaterGvg");
                    break;
                case "6.5.4":
                    AddStepThreeCheckbox("BRO Grondwatertrappen Gt", "broGroundwaterGt");
                    break;
                case "6.5.5":
                    AddStepThreeCheckbox("BRO Grondwaterspiegeldiepte modeldocumentatie", "broGroundwaterDocumentation");
                    break;
            }
        }
        AddStepThreeCheckbox("Boorlijn", "boreTrace");
        AddStepThreeCheckbox("Boorlijn nummers", "boreTraceNumbers");
        AddStepThreeCheckbox("Boorlijn lengtes", "boreTraceLengths");
        AddStepThreeCheckbox("Boring label", "boreTraceInfo");
        AddStepThreeCheckbox("Tracepunten in dwarsprofiel", "profileTracePoints");
        AddStepThreeCheckbox("Machines", "machines");
        AddImportFileControls();
    }

    private void AddSurfaceAnalysisMapLayerControls()
    {
        AddOverlayBulkButtons();
        AddStepThreeCheckbox("Ondergrond", "baseMap");
        AddStepThreeCheckbox("Kadaster percelen (PDOK)", "parcels");
        AddStepThreeCheckbox("Boorlijn", "boreTrace");
        AddStepThreeCheckbox("Boring label", "boreTraceInfo");
        AddStepThreeCheckbox("AHN4 maaiveld (DTM)", "ahn4Dtm");
    }



    private void AddImportFileControls()
    {
        if (_selectedProject is null) return;

        _projectFiles = _projects.GetProjectFiles(_selectedProject.Id);
        var uniqueFiles = UniqueProjectFiles(_projectFiles).ToArray();
        SyncProjectLayerStates(uniqueFiles);

        AddImportBulkButtons();
        AddStepThreeCheckboxToPanel(StepThreeImportsPanel, "Ontwerp import", "designImport", new Thickness(5, 0, 0, 0));
        AddStepThreeCheckboxToPanel(StepThreeImportsPanel, "BGT import", "bgt", new Thickness(5, 4, 0, 0));
        AddBgtSurfaceToggles(StepThreeImportsPanel);
        AddStepThreeCheckboxToPanel(StepThreeImportsPanel, "BAG import", "bagImport", new Thickness(5, 4, 0, 0));
        AddStepThreeCheckboxToPanel(StepThreeImportsPanel, "KLIC import", "klic", new Thickness(5, 4, 0, 0));
        AddStepThreeCheckboxToPanel(StepThreeImportsPanel, "KLIC bufferzone 1 m links/rechts", "klicBuffer", new Thickness(22, 0, 0, 2));
        AddKlicThemeToggles(StepThreeImportsPanel);
        AddStepThreeCheckboxToPanel(StepThreeImportsPanel, "Overige geimporteerde bestanden", "customImport", new Thickness(5, 4, 0, 0));

        var filterableFiles = uniqueFiles
            .Where(IsFilterableProjectFile)
            .OrderBy(ProjectFileSortKey)
            .ThenBy(file => file.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        if (filterableFiles.Length == 0)
        {
            StepThreeImportsPanel.Children.Add(new TextBlock
            {
                Text = "Geen geimporteerde ZIP/GML/DXF/GeoJSON bestanden gevonden.",
                Foreground = Brush("#8FA6B2"),
                FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(5, 4, 5, 0)
            });
            return;
        }

        foreach (var file in filterableFiles)
        {
            var layerId = ProjectFileLayerId(file);
            _gisSidebar.AddLayerToggle(
                StepThreeImportsPanel,
                ProjectFileLayerLabel(file),
                layerId,
                ProjectFileTypeLabel(file),
                IsProjectLayerVisible(layerId),
                new Thickness(22, 0, 0, 0),
                ProjectLayer_OnClick,
            10.5);
        }
    }

    private void AddSurfaceAnalysisImportControls()
    {
        if (_selectedProject is null) return;

        _projectFiles = _projects.GetProjectFiles(_selectedProject.Id);
        var uniqueFiles = UniqueProjectFiles(_projectFiles).ToArray();
        SyncProjectLayerStates(uniqueFiles);

        _gisSidebar.AddBulkButtons(StepThreeImportsPanel, "Filters aan", "Filters uit", ImportBulk_OnClick);
        AddStepThreeCheckboxToPanel(StepThreeImportsPanel, "BGT import", "bgt", new Thickness(5, 0, 0, 0));
        AddBgtSurfaceToggles(StepThreeImportsPanel);
        AddStepThreeCheckboxToPanel(StepThreeImportsPanel, "BAG/Kadaster import", "bagImport", new Thickness(5, 4, 0, 0));
        AddStepThreeCheckboxToPanel(StepThreeImportsPanel, "KLIC import", "klic", new Thickness(5, 4, 0, 0));
        AddStepThreeCheckboxToPanel(StepThreeImportsPanel, "KLIC bufferzone 1 m links/rechts", "klicBuffer", new Thickness(22, 0, 0, 2));
        AddKlicThemeToggles(StepThreeImportsPanel);

        var filterableFiles = uniqueFiles
            .Where(IsSurfaceAnalysisRelevantProjectFile)
            .OrderBy(ProjectFileSortKey)
            .ThenBy(file => file.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        if (filterableFiles.Length == 0)
        {
            StepThreeImportsPanel.Children.Add(new TextBlock
            {
                Text = "Geen BGT-, BAG/Kadaster- of KLIC-bestanden gevonden. Importeer deze bronnen in stap 2.1.",
                Foreground = Brush("#8FA6B2"),
                FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(5, 4, 5, 0)
            });
            return;
        }

        foreach (var file in filterableFiles)
        {
            var layerId = ProjectFileLayerId(file);
            _gisSidebar.AddLayerToggle(
                StepThreeImportsPanel,
                ProjectFileLayerLabel(file),
                layerId,
                ProjectFileTypeLabel(file),
                IsProjectLayerVisible(layerId),
                new Thickness(22, 0, 0, 0),
                ProjectLayer_OnClick,
                10.5);
        }
    }

    private void AddStepThreeCheckboxToPanel(Panel parent, string label, string overlayId, Thickness margin)
    {
        var visible = _mapOverlayStates.TryGetValue(overlayId, out var isVisible) && isVisible;
        _gisSidebar.AddLayerToggle(
            parent,
            label,
            overlayId,
            OverlayTypeLabel(overlayId),
            visible,
            margin,
            MapOverlay_OnClick,
            overlayId is "klicBuffer" ? 10.5 : 11);
    }

    private void AddImportBulkButtons()
    {
        _gisSidebar.AddBulkButtons(StepThreeImportsPanel, "Imports aan", "Imports uit", ImportBulk_OnClick);
    }

    private void SyncProjectLayerStates(IEnumerable<ProjectFileRecord> files)
    {
        _gisLayerState.SyncProjectLayerStates(files, IsFilterableProjectFile, ProjectFileLayerId);
    }

    private static IEnumerable<ProjectFileRecord> UniqueProjectFiles(IEnumerable<ProjectFileRecord> files)
    {
        return files
            .GroupBy(ProjectFileDedupKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(file => file.CreatedAt).First());
    }

    private static string ProjectFileDedupKey(ProjectFileRecord file)
    {
        var sourceName = string.IsNullOrWhiteSpace(file.DisplayName)
            ? System.IO.Path.GetFileName(file.SourcePath)
            : file.DisplayName;
        return $"{file.FileType}|{sourceName}|{file.SizeBytes}";
    }

    private bool IsProjectLayerVisible(string layerId) => _gisLayerState.IsProjectLayerVisible(layerId);

    private static string ProjectFileLayerId(ProjectFileRecord file) => file.Id.ToString("N");

    private static bool IsFilterableProjectFile(ProjectFileRecord file)
    {
        var extension = System.IO.Path.GetExtension(file.LocalPath).ToLowerInvariant();
        return extension is ".zip" or ".gml" or ".xml" or ".geojson" or ".json" or ".dxf" or ".kml";
    }


    private static bool IsSurfaceAnalysisRelevantProjectFile(ProjectFileRecord file)
    {
        if (!IsFilterableProjectFile(file)) return false;
        if (file.FileType.Equals("BGT", StringComparison.OrdinalIgnoreCase) ||
            file.FileType.Equals("BAG", StringComparison.OrdinalIgnoreCase) ||
            file.FileType.Equals("KLIC", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var name = string.IsNullOrWhiteSpace(file.DisplayName)
            ? System.IO.Path.GetFileName(file.LocalPath)
            : file.DisplayName;
        return name.Contains("BGT", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("BAG", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Kadaster", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("KLIC", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSurfaceAnalysisRelevantMapLayer(ProjectMapLayer layer) =>
        IsBgtLayer(layer) || IsBagOrKadasterLayer(layer) || IsKlicLayer(layer);

    private static IReadOnlyList<ProjectMapLayer> GetRelevantMapLayersForStep(IReadOnlyList<ProjectMapLayer> layers, int stepNumber, bool broPointOnlyMap)
    {
        if (broPointOnlyMap || stepNumber == 6)
        {
            return [];
        }

        static bool CoreReferenceLayer(ProjectMapLayer layer) =>
            IsBgtLayer(layer) || IsBagOrKadasterLayer(layer) || IsKlicLayer(layer);

        return stepNumber switch
        {
            3 => layers.Where(layer => CoreReferenceLayer(layer) || IsDesignOrCustomLayer(layer)).ToList(),
            4 => layers.Where(IsSurfaceAnalysisRelevantMapLayer).ToList(),
            5 => layers.Where(CoreReferenceLayer).ToList(),
            var n when n == ProfileStepNumber => layers.Where(CoreReferenceLayer).ToList(),
            var n when n == MachineStepNumber => layers.Where(CoreReferenceLayer).ToList(),
            var n when n == WorkDrawingStepNumber => layers.Where(CoreReferenceLayer).ToList(),
            _ => layers.Where(CoreReferenceLayer).ToList()
        };
    }

    private static int ProjectFileSortKey(ProjectFileRecord file) =>
        file.FileType.ToUpperInvariant() switch
        {
            "BGT" => 0,
            "BAG" => 1,
            "KLIC" => 2,
            "LS" => 3,
            "MS" => 4,
            "GAS" => 5,
            "WATER" => 6,
            "DATA" => 7,
            _ => 8
        };

    private static string ProjectFileLayerLabel(ProjectFileRecord file)
    {
        var name = string.IsNullOrWhiteSpace(file.DisplayName) ? file.FileType : file.DisplayName;
        return $"{ImportGroupLabel(file.FileType)}: {name}";
    }

    private static string ProjectFileTypeLabel(ProjectFileRecord file) =>
        file.FileType.ToUpperInvariant() switch
        {
            "BGT" => "BGT bestand",
            "BAG" => "BAG bestand",
            "KLIC" => "KLIC bestand",
            "LS" or "MS" or "GAS" or "WATER" or "DATA" => "ontwerp DXF/GML",
            "ONTWERP" => "ontwerpbestand",
            _ => "importlaag"
        };

    private static string ImportGroupLabel(string fileType) =>
        fileType.ToUpperInvariant() switch
        {
            "BGT" => "BGT",
            "BAG" => "BAG",
            "KLIC" => "KLIC",
            "LS" => "Laagspanning",
            "MS" => "Middenspanning",
            "GAS" => "Gas",
            "WATER" => "Water",
            "DATA" => "Data/Telecom",
            "ONTWERP" => "Ontwerp",
            _ => fileType
        };

    private void AddGenericStepRibbonActions(StepWorkspace workspace)
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
            Text = workspace.Title,
            Foreground = Brush("#3F4750"),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var buttons = new WrapPanel();
        foreach (var action in workspace.Actions)
        {
            AddCompactRibbonButton(buttons, action, action, action == workspace.Actions.First());
        }

        panel.Children.Add(buttons);
        card.Child = panel;
        StepSpecificRibbonPanel.Children.Add(card);
    }
    private void SendMapMessage(string json)
    {
        if (!_mapLibreLoaded || StepThreeMapView is null || StepThreeMapView.CoreWebView2 is null)
        {
            QueueMapSync();
            if (OutputText is not null) OutputText.Text = "MapLibre kaart is nog aan het laden.";
            return;
        }

        _gisMap.TrySendJson(
            StepThreeMapView.CoreWebView2,
            json,
            exception => AppendMapDiagnostic($"MapLibre bericht verzenden mislukt: {exception.Message}"));
    }



    private bool _mapRecoveryRunning;


    private void SendProjectLayersToMap()
    {
        if (_selectedProject is null) return;
        if (!_mapLibreLoaded || StepThreeMapView.CoreWebView2 is null)
        {
            QueueMapSync();
            return;
        }

        BeginMapLoading("Filters laden...");
        try
        {

        _projectFiles = _projects.GetProjectFiles(_selectedProject.Id);
        var layers = BuildProjectMapLayers(_projectFiles);
        SyncProjectLayerStates(_projectFiles);
        SyncKlicThemeStates(layers);
        var selectedStepNumber = _selectedStep?.Number ?? 3;
        var isStepThreeMap = selectedStepNumber == 3;
        var isStepFourMap = selectedStepNumber == 4;
        var isStepSixMap = selectedStepNumber == 6;
        var broPointOnlyMap = isStepSixMap && IsBroPointDatasetModel(GetActiveUndergroundModelType());
        IReadOnlyList<ProjectMapLayer> layersForMap = GetRelevantMapLayersForStep(layers, selectedStepNumber, broPointOnlyMap);
        if (isStepSixMap)
        {
            _mapOverlayStates["boreTrace"] = true;
            _mapOverlayStates["boreTraceNumbers"] = !broPointOnlyMap;
            _mapOverlayStates["boreTraceLengths"] = !broPointOnlyMap;
            _mapOverlayStates["boreTraceInfo"] = !broPointOnlyMap;
            if (string.Equals(_selectedSubstep?.Number, "6.3", StringComparison.OrdinalIgnoreCase) &&
                !_mapOverlayStates.ContainsKey("broGeomorphology"))
            {
                _mapOverlayStates["broGeomorphology"] = true;
            }
            if (string.Equals(_selectedSubstep?.Number, "6.4", StringComparison.OrdinalIgnoreCase) &&
                !_mapOverlayStates.ContainsKey("broSoilMap"))
            {
                _mapOverlayStates["broSoilMap"] = true;
            }

            var activeBroWmsOverlayKey = BroWmsOverlayKey(GetActiveUndergroundModelType());
            if (!string.IsNullOrWhiteSpace(activeBroWmsOverlayKey) &&
                !_mapOverlayStates.ContainsKey(activeBroWmsOverlayKey))
            {
                _mapOverlayStates[activeBroWmsOverlayKey] = true;
            }
        }
        var activeBroOverlay = isStepSixMap ? BroWmsOverlayKey(GetActiveUndergroundModelType()) : "";
        bool IsActiveBroOverlayVisible(string overlayKey) =>
            isStepSixMap &&
            activeBroOverlay.Equals(overlayKey, StringComparison.OrdinalIgnoreCase) &&
            _mapOverlayStates.TryGetValue(overlayKey, out var visible) &&
            visible;
        var showBroGeomorphology = IsActiveBroOverlayVisible("broGeomorphology");
        var showBroSoilMap = IsActiveBroOverlayVisible("broSoilMap");
        var showBroGroundwaterGhg = IsActiveBroOverlayVisible("broGroundwaterGhg");
        var showBroGroundwaterGlg = IsActiveBroOverlayVisible("broGroundwaterGlg");
        var showBroGroundwaterGvg = IsActiveBroOverlayVisible("broGroundwaterGvg");
        var showBroGroundwaterGt = IsActiveBroOverlayVisible("broGroundwaterGt");
        var showBroGroundwaterDocumentation = IsActiveBroOverlayVisible("broGroundwaterDocumentation");
        var isStepThreeKlicMap = IsSelectedStepThreeKlicSubstep() || _forceStepThreeKlicMapForReportCapture;
        var payload = JsonSerializer.Serialize(new
        {
            type = "projectLayers",
            layers = layersForMap,
            projectLayerVisibility = _projectLayerStates,
            baseMapVisible = selectedStepNumber == WorkDrawingStepNumber ? false : _mapOverlayStates.TryGetValue("baseMap", out var baseMapVisible) && baseMapVisible,
            parcelsVisible = !broPointOnlyMap && (isStepSixMap ? false : isStepThreeMap ? _mapOverlayStates.TryGetValue("parcels", out var step3ParcelsVisible) && step3ParcelsVisible : selectedStepNumber == WorkDrawingStepNumber ? false : _mapOverlayStates.TryGetValue("parcels", out var parcelsVisible) && parcelsVisible),
            buildingsVisible = !broPointOnlyMap && !isStepFourMap && !isStepThreeMap && !isStepSixMap && selectedStepNumber != WorkDrawingStepNumber && _mapOverlayStates.TryGetValue("buildings", out var buildingsVisible) && buildingsVisible,
            addressesVisible = !broPointOnlyMap && !isStepFourMap && !isStepThreeMap && !isStepSixMap && selectedStepNumber != WorkDrawingStepNumber && _mapOverlayStates.TryGetValue("addresses", out var addressesVisible) && addressesVisible,
            bgtVisible = !broPointOnlyMap && !isStepThreeMap && !isStepSixMap && _mapOverlayStates.TryGetValue("bgt", out var bgtVisible) && bgtVisible,
            bagImportVisible = !broPointOnlyMap && !isStepThreeMap && !isStepSixMap && _mapOverlayStates.TryGetValue("bagImport", out var bagImportVisible) && bagImportVisible,
            ahn4DtmVisible = !broPointOnlyMap && !isStepThreeMap && !isStepSixMap && selectedStepNumber != WorkDrawingStepNumber && _mapOverlayStates.TryGetValue("ahn4Dtm", out var ahn4DtmVisible) && ahn4DtmVisible,
            ahn4DsmVisible = !broPointOnlyMap && !isStepThreeMap && !isStepSixMap && selectedStepNumber != WorkDrawingStepNumber && _mapOverlayStates.TryGetValue("ahn4Dsm", out var ahn4DsmVisible) && ahn4DsmVisible,
            broGeomorphologyVisible = !broPointOnlyMap && showBroGeomorphology,
            broSoilMapVisible = !broPointOnlyMap && showBroSoilMap,
            broGroundwaterGhgVisible = !broPointOnlyMap && showBroGroundwaterGhg,
            broGroundwaterGlgVisible = !broPointOnlyMap && showBroGroundwaterGlg,
            broGroundwaterGvgVisible = !broPointOnlyMap && showBroGroundwaterGvg,
            broGroundwaterGtVisible = !broPointOnlyMap && showBroGroundwaterGt,
            broGroundwaterDocumentationVisible = !broPointOnlyMap && showBroGroundwaterDocumentation,
            klicVisible = broPointOnlyMap ? false : isStepSixMap
                ? false
                : isStepThreeKlicMap
                    ? _mapOverlayStates.TryGetValue("klic", out var step3KlicVisible) && step3KlicVisible
                    : !isStepThreeMap && _mapOverlayStates.TryGetValue("klic", out var klicVisible) && klicVisible,
            klicBufferVisible = broPointOnlyMap ? false : isStepSixMap
                ? false
                : isStepThreeKlicMap
                    ? _mapOverlayStates.TryGetValue("klicBuffer", out var step3KlicBufferVisible) && step3KlicBufferVisible
                    : !isStepThreeMap && _mapOverlayStates.TryGetValue("klicBuffer", out var klicBufferVisible) && klicBufferVisible,
            designImportVisible = !broPointOnlyMap && !isStepFourMap && !isStepThreeMap && !isStepSixMap && _mapOverlayStates.TryGetValue("designImport", out var designImportVisible) && designImportVisible,
            customImportVisible = !broPointOnlyMap && !isStepFourMap && !isStepThreeMap && !isStepSixMap && _mapOverlayStates.TryGetValue("customImport", out var customImportVisible) && customImportVisible,
            borelineMapMode = isStepThreeMap,
            workDrawingMode = selectedStepNumber == WorkDrawingStepNumber,
            klicThemes = _klicThemeStates,
            bgtSurfaces = _bgtSurfaceStates
        }, JsonOptions);
        _gisMap.TryPostJson(
            StepThreeMapView.CoreWebView2,
            payload,
            exception => AppendMapDiagnostic($"Projectlagen naar kaart sturen mislukt: {exception.Message}"));
        SendTraceStateToMap();
        if (isStepSixMap) SendBroSoundingsToMap();
        _ = RefreshTraceStateAfterMapLayerSyncAsync();
        SendMachineStateToMap();

        var featureCount = layersForMap.Sum(layer => layer.FeatureCollection.Features.Count);
        var stepLabel = selectedStepNumber switch
        {
            3 => "stap 3 Boorlijn",
            4 => "stap 4 Oppervlakteanalyse",
            5 => "stap 5 Omgevingsmanagement",
            6 => "stap 6 Ondergrondanalyse",
            7 => "stap 7 Dwarsprofiel",
            8 => "stap 8 Machine locatie",
            9 => "stap 9 Sonderingen",
            _ => $"stap {selectedStepNumber}"
        };
        OutputText.Text = broPointOnlyMap
            ? $"DINOloket kaartmodus\n\nAlle project-, KLIC-, BGT- en importlagen zijn verborgen. Alleen de actieve {DinoModelShortLabel(GetActiveUndergroundModelType())}-bronpunten en de boorlijnreferentie blijven zichtbaar."
            : featureCount > 0
            ? $"GIS-lagen geladen\n\n{layersForMap.Count} bestand(en)\n{featureCount} geometrieen zichtbaar in {stepLabel}."
            : $"{CultureInfo.CurrentCulture.TextInfo.ToTitleCase(stepLabel)} kaart geladen.\n\nEr zijn nog geen leesbare KLIC/BGT/GML/GeoJSON geometrieen gevonden in de gekoppelde bestanden.";
        }
        finally
        {
            EndMapLoading();
        }
    }


    private void RenderGisMap(StepWorkspace workspace)
    {
        var signature = BuildGisMapRenderSignature(workspace);
        if (string.Equals(_lastGisMapRenderSignature, signature, StringComparison.Ordinal))
        {
            return;
        }

        _lastGisMapRenderSignature = signature;
        MapCanvas.Children.Clear();

        if (BaseLayerToggle.IsChecked == true)
        {
            DrawBaseMap();
        }

        if (AhnLayerToggle.IsChecked == true)
        {
            DrawAhnGrid();
        }

        if (BgtLayerToggle.IsChecked == true)
        {
            DrawBgtOverlay();
        }

        if (KlicLayerToggle.IsChecked == true)
        {
            DrawKlicOverlay();
        }

        DrawBoreLine(workspace);
        DrawMapLabels(workspace);
        DrawLegend();
    }

    private string BuildGisMapRenderSignature(StepWorkspace workspace)
    {
        return string.Join(";",
            _selectedProject?.Id.ToString("N") ?? "geen-project",
            workspace.StepNumber.ToString(CultureInfo.InvariantCulture),
            workspace.MapTitle,
            workspace.MapSubtitle,
            BaseLayerToggle.IsChecked == true,
            AhnLayerToggle.IsChecked == true,
            BgtLayerToggle.IsChecked == true,
            KlicLayerToggle.IsChecked == true,
            _selectedMachineId ?? "",
            _profilePoints.Count.ToString(CultureInfo.InvariantCulture),
            _profilePoints.Count == 0 ? "" : _profilePoints[^1].Distance.ToString(CultureInfo.InvariantCulture));
    }

    private void DrawBaseMap()
    {
        AddRect(0, 0, 760, 430, "#EDF2F1", "#EDF2F1", 1);
        AddPolygon("#D7E9DF", "#C0D9CD", 1, [20, 350, 155, 305, 260, 326, 388, 285, 500, 315, 724, 250, 760, 430, 20, 430]);
        AddPolyline("#78B7D5", 18, [0, 142, 126, 156, 236, 145, 372, 164, 525, 152, 760, 178]);
        AddPolyline("#A7D2E6", 10, [0, 142, 126, 156, 236, 145, 372, 164, 525, 152, 760, 178]);
        AddPolyline("#EFE7D8", 28, [90, 0, 138, 70, 165, 150, 204, 228, 232, 430]);
        AddPolyline("#CDBD9D", 2, [90, 0, 138, 70, 165, 150, 204, 228, 232, 430]);
        AddPolyline("#FFFFFF", 16, [90, 0, 138, 70, 165, 150, 204, 228, 232, 430]);
        AddText("Watergang", 520, 126, "#25759A", 11, FontWeights.SemiBold);
        AddText("Provinciale weg", 176, 205, "#76664A", 11, FontWeights.SemiBold);
    }

    private void DrawAhnGrid()
    {
        for (var x = 0; x <= 760; x += 48)
        {
            AddLine(x, 0, x, 430, "#D9E2E5", 1, null);
        }

        for (var y = 0; y <= 430; y += 48)
        {
            AddLine(0, y, 760, y, "#D9E2E5", 1, null);
        }

        AddText("AHN +1.4 m", 624, 34, "#8FA6B2", 11, FontWeights.Normal);
        AddText("RD grid", 26, 400, "#8FA6B2", 11, FontWeights.Normal);
    }

    private void DrawBgtOverlay()
    {
        AddRect(286, 42, 190, 86, "#E8F4EC", "#8BC7A1", 1);
        AddText("BGT groenvoorziening", 304, 72, "#286947", 11, FontWeights.SemiBold);
        AddRect(505, 258, 164, 74, "#F6F2E8", "#D6C388", 1);
        AddText("BGT erf/verharding", 522, 288, "#756529", 11, FontWeights.SemiBold);
        AddRect(42, 222, 136, 78, "#EFEFEF", "#C8CDD0", 1);
        AddText("BGT wegdeel", 62, 252, "#52606A", 11, FontWeights.SemiBold);
        AddRect(620, 82, 92, 62, "#EEF5FF", "#A8C8F0", 1);
        AddText("BAG", 650, 106, "#3C6FA8", 11, FontWeights.SemiBold);
    }



    private void DrawMapLabels(StepWorkspace workspace)
    {
        AddCallout(214, 28, 184, 68, GetPrimaryMapLabel(workspace.StepNumber), workspace.MapTitle);
        AddCallout(408, 272, 210, 82, "Controlepunt", workspace.MapSubtitle);
    }

    private static string GetPrimaryMapLabel(int stepNumber) => stepNumber switch
    {
        1 => "Boring configuratie",
        2 => "KLIC / ontwerp",
        3 => "Laagcontrole",
        4 => "Boorlijn",
        5 => "BGT profiel",
        6 => "Omgeving",
        7 => "BRO / AHN",
        8 => "Diepteprofiel",
        9 => "Machinepositie",
        10 => "3D context",
        _ => "Rapport"
    };

    private void DrawLegend()
    {
        AddRect(12, 12, 186, 102, "#FDFEFE", "#DEE6EA", 1);
        AddText("Legenda", 28, 24, "#0D1520", 11, FontWeights.Bold);
        AddLegendLine(28, 48, "#007A5A", "Boorlijn");
        AddLegendLine(28, 66, "#7B00AA", "KLIC laagspanning");
        AddLegendLine(28, 84, "#000080", "KLIC water");
        AddText("BGT vlakken zichtbaar", 78, 96, "#587080", 10, FontWeights.Normal);
    }

    private void AddLegendLine(double x, double y, string color, string label)
    {
        AddLine(x, y + 5, x + 42, y + 5, color, 3, null);
        AddText(label, x + 50, y, "#587080", 10, FontWeights.Normal);
    }

    private void AddCallout(double left, double top, double width, double height, string title, string body)
    {
        AddRect(left, top, width, height, "#FFFFFF", "#DEE6EA", 1);
        AddText(title, left + 18, top + 20, "#1B2B35", 11, FontWeights.SemiBold);
        AddText(body, left + 18, top + 44, "#587080", 11, FontWeights.Normal, width - 30);
    }

    private void AddRect(double left, double top, double width, double height, string fill, string stroke, double strokeThickness)
    {
        var shape = new Rectangle
        {
            Width = width,
            Height = height,
            RadiusX = 8,
            RadiusY = 8,
            Fill = Brush(fill),
            Stroke = Brush(stroke),
            StrokeThickness = strokeThickness
        };
        Canvas.SetLeft(shape, left);
        Canvas.SetTop(shape, top);
        MapCanvas.Children.Add(shape);
    }

    private void AddCircle(double centerX, double centerY, double radius, string fill, string stroke, double strokeThickness = 1)
    {
        var shape = new Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            Fill = Brush(fill),
            Stroke = Brush(stroke),
            StrokeThickness = strokeThickness
        };
        Canvas.SetLeft(shape, centerX - radius);
        Canvas.SetTop(shape, centerY - radius);
        MapCanvas.Children.Add(shape);
    }

    private void AddLine(double x1, double y1, double x2, double y2, string color, double thickness, double[]? dash)
    {
        var shape = new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = Brush(color),
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        if (dash is not null) shape.StrokeDashArray = new DoubleCollection(dash);
        MapCanvas.Children.Add(shape);
    }

    private void AddPolyline(string color, double thickness, double[] coordinates)
    {
        var shape = new Polyline
        {
            Stroke = Brush(color),
            StrokeThickness = thickness,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };

        for (var i = 0; i < coordinates.Length - 1; i += 2)
        {
            shape.Points.Add(new Point(coordinates[i], coordinates[i + 1]));
        }

        MapCanvas.Children.Add(shape);
    }

    private void AddPolygon(string fill, string stroke, double thickness, double[] coordinates)
    {
        var shape = new Polygon
        {
            Fill = Brush(fill),
            Stroke = Brush(stroke),
            StrokeThickness = thickness
        };

        for (var i = 0; i < coordinates.Length - 1; i += 2)
        {
            shape.Points.Add(new Point(coordinates[i], coordinates[i + 1]));
        }

        MapCanvas.Children.Add(shape);
    }

    private void AddText(string text, double left, double top, string color, double size, FontWeight weight, double? width = null)
    {
        var block = new TextBlock
        {
            Text = text,
            Foreground = Brush(color),
            FontSize = size,
            FontWeight = weight,
            TextWrapping = TextWrapping.Wrap
        };
        if (width is not null) block.Width = width.Value;
        Canvas.SetLeft(block, left);
        Canvas.SetTop(block, top);
        MapCanvas.Children.Add(block);
    }

    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));

    private static void AddCanvasRect(Canvas canvas, double left, double top, double width, double height, string fill, string stroke, double strokeThickness)
    {
        var shape = new Rectangle
        {
            Width = width,
            Height = height,
            RadiusX = fill == "Transparent" ? 0 : 6,
            RadiusY = fill == "Transparent" ? 0 : 6,
            Fill = fill == "Transparent" ? Brushes.Transparent : Brush(fill),
            Stroke = Brush(stroke),
            StrokeThickness = strokeThickness
        };
        Canvas.SetLeft(shape, left);
        Canvas.SetTop(shape, top);
        canvas.Children.Add(shape);
    }

    private static void AddCanvasCircle(Canvas canvas, double centerX, double centerY, double radius, string fill, string stroke, double strokeThickness)
    {
        var shape = new Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            Fill = Brush(fill),
            Stroke = Brush(stroke),
            StrokeThickness = strokeThickness
        };
        Canvas.SetLeft(shape, centerX - radius);
        Canvas.SetTop(shape, centerY - radius);
        canvas.Children.Add(shape);
    }

    private static void AddCanvasLine(Canvas canvas, double x1, double y1, double x2, double y2, string color, double thickness, double[]? dash)
    {
        var shape = new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = Brush(color),
            StrokeThickness = thickness
        };
        if (dash is not null) shape.StrokeDashArray = new DoubleCollection(dash);
        canvas.Children.Add(shape);
    }

    private static void AddCanvasPolyline(Canvas canvas, string color, double thickness, double[] coordinates)
    {
        var shape = new Polyline
        {
            Stroke = Brush(color),
            StrokeThickness = thickness,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        for (var i = 0; i < coordinates.Length - 1; i += 2)
        {
            shape.Points.Add(new Point(coordinates[i], coordinates[i + 1]));
        }
        canvas.Children.Add(shape);
    }

    private static void AddCanvasSmoothPath(Canvas canvas, string color, double thickness, IReadOnlyList<Point> points, double opacity)
    {
        if (points.Count < 2) return;

        var figure = new PathFigure
        {
            StartPoint = points[0],
            IsClosed = false,
            IsFilled = false
        };

        for (var i = 0; i < points.Count - 1; i++)
        {
            var p0 = i == 0 ? points[i] : points[i - 1];
            var p1 = points[i];
            var p2 = points[i + 1];
            var p3 = i + 2 < points.Count ? points[i + 2] : p2;

            var c1 = new Point(
                p1.X + (p2.X - p0.X) / 6.0,
                p1.Y + (p2.Y - p0.Y) / 6.0);
            var c2 = new Point(
                p2.X - (p3.X - p1.X) / 6.0,
                p2.Y - (p3.Y - p1.Y) / 6.0);

            figure.Segments.Add(new BezierSegment(c1, c2, p2, true));
        }

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        canvas.Children.Add(new System.Windows.Shapes.Path
        {
            Data = geometry,
            Stroke = Brush(color),
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent,
            Opacity = opacity
        });
    }

    private static void AddCanvasPolygon(Canvas canvas, string fill, string stroke, double thickness, double[] coordinates)
    {
        var shape = new Polygon
        {
            Fill = Brush(fill),
            Stroke = Brush(stroke),
            StrokeThickness = thickness
        };
        for (var i = 0; i < coordinates.Length - 1; i += 2)
        {
            shape.Points.Add(new Point(coordinates[i], coordinates[i + 1]));
        }
        canvas.Children.Add(shape);
    }
    private static void AddCanvasText(Canvas canvas, string text, double left, double top, string color, double size, FontWeight weight, double rotation = 0)
    {
        var block = new TextBlock
        {
            Text = text,
            Foreground = Brush(color),
            FontSize = size,
            FontWeight = weight
        };
        if (Math.Abs(rotation) > 0.01)
        {
            block.RenderTransform = new RotateTransform(rotation);
        }
        Canvas.SetLeft(block, left);
        Canvas.SetTop(block, top);
        canvas.Children.Add(block);
    }




    private static bool IsReportHexColor(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && Regex.IsMatch(value, "^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$");
    }

    private static string CompactSoundingLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "-";
        value = Regex.Replace(value.Trim(), "\\s+", " ");
        return value.Length <= 13 ? value : value[..12] + "...";
    }

    private static string CompactSoundingLegend(string value, int maxLength = 30)
    {
        if (string.IsNullOrWhiteSpace(value)) return "-";
        value = Regex.Replace(value.Trim(), "\\s+", " ");
        maxLength = Math.Max(4, maxLength);
        return value.Length <= maxLength ? value : value[..(maxLength - 1)] + "...";
    }

    private static Border CreateCard(WorkspaceCard card)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = card.Label,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(143, 166, 178))
        });
        panel.Children.Add(new TextBlock
        {
            Text = card.Title,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(13, 21, 32))
        });
        panel.Children.Add(new TextBlock
        {
            Text = card.Body,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 5, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(88, 112, 128))
        });

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(222, 230, 234)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 10),
            Child = panel
        };
    }

    private static Border CreateSubstepReportCard(PrescanSubstep substep)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = DisplaySubstepNumber(substep),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#315B7E")
        });
        panel.Children.Add(new TextBlock
        {
            Text = substep.ReportCardTitle,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = Brush("#071422")
        });
        panel.Children.Add(new TextBlock
        {
            Text = substep.Description,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 5, 0, 0),
            Foreground = Brush("#587080")
        });

        return new Border
        {
            Background = Brush("#F8FAFB"),
            BorderBrush = Brush("#BFD7F1"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 8),
            Child = panel
        };
    }



    private async void StepAction_OnClick(object sender, RoutedEventArgs e)
    {
        var action = (sender as Button)?.Tag as string ?? "";
        if (_selectedProject is null || _selectedStep is null) return;

        var stopwatch = Stopwatch.StartNew();
        try
        {
        if (_selectedStep.Number == 6 && action.StartsWith("BRO select:", StringComparison.OrdinalIgnoreCase))
        {
            var rawSelection = action["BRO select:".Length..].Trim();
            var modelType = GetActiveUndergroundModelType();
            var selectedId = rawSelection;
            var separatorIndex = rawSelection.IndexOf(':');
            if (separatorIndex > 0)
            {
                modelType = NormalizeBroModelType(rawSelection[..separatorIndex]);
                selectedId = rawSelection[(separatorIndex + 1)..].Trim();
            }

            _selectedBroModelType = modelType;
            var selectedIds = ToggleSelectedBroSoundingId(modelType, selectedId);
            RefreshUndergroundAnalysisSidebarPanel();
            SendBroSoundingsToMap();
            if (selectedIds.Any(id => id.Equals(selectedId, StringComparison.OrdinalIgnoreCase)))
            {
                await EnsureBroModelProfileForSelectionAsync(modelType, selectedId);
            }

            SaveStepReportDataForStep(6);
            RefreshWorkflowReportStatus(6);
            QueueLiveMapReportCapture(6);
            RefreshInlineReportPreviewIfVisible();
            OutputText.Text = $"{DinoModelLabel(modelType)} bronpuntselectie\n\n{selectedIds.Count}/{GetMaxBroSelectedSoundings(modelType)} echte DINOloket-bronpunt(en) staan nu in de selectie voor de rapportage.";
            return;
        }

        if (_selectedStep.Number == 6 && action == "BRO profiel PDF importeren")
        {
            ImportBroProfilePdfsForActiveModel();
            return;
        }

        if (_selectedStep.Number == 6 && action == "BRO profiel PDF wissen")
        {
            var modelType = GetActiveUndergroundModelType();
            if (!SupportsImportedBroProfiles(modelType)) return;

            SaveBroImportedProfiles(modelType, []);
            SetSelectedBroSoundingId(modelType, null);
            SetBroLoadStatus(modelType, $"{DinoModelShortLabel(modelType)} PDF-profielen gewist.");
            RefreshUndergroundAnalysisSidebarPanel();
            SendBroSoundingsToMap();
            SaveStepReportDataForStep(6);
            RefreshInlineReportPreviewIfVisible();
            OutputText.Text = $"{DinoModelLabel(modelType)} PDF-profielen gewist\n\nImporteer opnieuw de officiele DINOloket/BRO PDF als deze in de rapportage moet staan.";
            return;
        }

        if (_selectedStep.Number == 6 && action == "BRO selectie rapport")
        {
            var modelType = GetActiveUndergroundModelType();
            if (IsBroWmsMapLayer(modelType))
            {
                var overlayKey = BroWmsOverlayKey(modelType);
                if (!string.IsNullOrWhiteSpace(overlayKey))
                {
                    _mapOverlayStates[overlayKey] = true;
                }

                SetBroLoadStatus(modelType, $"{DinoModelLabel(modelType)} kaartlaag is opgeslagen als rapportbron.");
                RefreshUndergroundAnalysisSidebarPanel();
                SendProjectLayersToMap();
                SaveStepReportDataForStep(6);
                RefreshWorkflowReportStatus(6);
                var imagePath = await CaptureStepSixSubsurfaceWmsReportMapAsync(modelType);
                RefreshInlineReportPreviewIfVisible();
                OutputText.Text = string.IsNullOrWhiteSpace(imagePath)
                    ? $"{DinoModelShortLabel(modelType)} opgeslagen\n\nDe {DinoModelLabel(modelType)} kaartlaag is opgeslagen, maar de kaartcapture is nog niet gelukt. Wacht tot de kaart volledig geladen is en klik opnieuw op 'Opslaan in rapport'."
                    : $"{DinoModelShortLabel(modelType)} opgeslagen\n\nDe actieve GIS-kaart met {DinoModelLabel(modelType)} en legenda wordt nu in de rapportage meegenomen.";
                return;
            }

            if (SupportsImportedBroProfiles(modelType) && ReadBroImportedProfiles(modelType).Count > 0)
            {
                MergeImportedBroProfilesIntoSoundings(modelType, selectImported: true);
                SaveStepReportDataForStep(6);
                RefreshWorkflowReportStatus(6);
                QueueLiveMapReportCapture(6);
                RefreshInlineReportPreviewIfVisible();
                OutputText.Text = $"{DinoModelLabel(modelType)} PDF-profielen opgeslagen\n\nDe geimporteerde BRO/DINOloket PDF-profielen worden als leidende bron meegenomen in de rapportage van {_selectedSubstep?.Number ?? "6.x"}.";
                return;
            }

            var selectedCount = GetSelectedBroSoundingIds(modelType).Count;
            foreach (var selectedId in GetSelectedBroSoundingIds(modelType))
            {
                await EnsureBroModelProfileForSelectionAsync(modelType, selectedId);
            }

            SaveStepReportDataForStep(6);
            RefreshWorkflowReportStatus(6);
            QueueLiveMapReportCapture(6);
            RefreshInlineReportPreviewIfVisible();
            OutputText.Text = $"{DinoModelLabel(modelType)} selectie opgeslagen\n\n{selectedCount}/{GetMaxBroSelectedSoundings(modelType)} kaartpunt(en) worden meegenomen in de rapportage van {_selectedSubstep?.Number ?? "6.x"}.";
            return;
        }

        if (_selectedStep.Number == 6 && action == "BRO CPT/bestanden laden")
        {
            OutputText.Text = "BRO CPT/sonderingen horen bij stap 9\n\nStap 6 gebruikt losse BRO/DINOloket kaartdatasets: 6.1 DGM, 6.2 REGIS II, 6.3 geomorfologie, 6.4 bodemkaart en 6.5 grondwaterspiegeldiepte.";
            return;
        }

        if (_selectedStep.Number == 6 && TryGetBroModelTypeFromLoadAction(action, out var loadModelType))
        {
            _selectedBroModelType = loadModelType;
            if (IsBroWmsMapLayer(loadModelType))
            {
                var overlayKey = BroWmsOverlayKey(loadModelType);
                if (!string.IsNullOrWhiteSpace(overlayKey))
                {
                    _mapOverlayStates[overlayKey] = true;
                }

                SetBroLoadStatus(loadModelType, $"{DinoModelLabel(loadModelType)} kaartlaag is zichtbaar via PDOK WMS.");
                RefreshUndergroundAnalysisSidebarPanel();
                SendTraceStateToMap();
                SendProjectLayersToMap();
                SendMapMessage("{\"type\":\"fitTrace\",\"scale\":" + _workDrawingScale.ToString(CultureInfo.InvariantCulture) + "}");
                SaveStepReportDataForStep(6);
                RefreshWorkflowReportStatus(6);
                RefreshInlineReportPreviewIfVisible();
                OutputText.Text = $"{DinoModelShortLabel(loadModelType)} kaartlaag\n\nDe DINOloket/PDOK {DinoModelLabel(loadModelType)} kaartlaag is ingeschakeld op de GIS-kaart. De boorlijn blijft als referentie zichtbaar.";
                return;
            }

            await LoadBroModelForStepSixAsync(loadModelType, fit: true, initiatedByUser: true);
            return;
        }

        if (_selectedStep.Number == 6 && action == "Zoom naar BRO-bronnen")
        {
            var modelType = GetActiveUndergroundModelType();
            SendTraceStateToMap();
            if (IsBroWmsMapLayer(modelType))
            {
                var overlayKey = BroWmsOverlayKey(modelType);
                if (!string.IsNullOrWhiteSpace(overlayKey))
                {
                    _mapOverlayStates[overlayKey] = true;
                }

                SendProjectLayersToMap();
                SendMapMessage("{\"type\":\"fitTrace\",\"scale\":" + _workDrawingScale.ToString(CultureInfo.InvariantCulture) + "}");
                OutputText.Text = $"Zoom naar {DinoModelShortLabel(modelType)} kaart\n\nDe kaart zoomt naar de boorlijn met de {DinoModelLabel(modelType)} kaartlaag als ondergrond.";
            }
            else
            {
                SendBroSoundingsToMap(fit: true);
                OutputText.Text = $"Zoom naar BRO-bronnen\n\nDe kaart zoomt naar de geladen {DinoModelShortLabel(modelType)}-kaartpunten en de boorlijn als referentie.";
            }
            return;
        }

        if (_selectedStep.Number == 6 && action == "BRO selectie wissen")
        {
            var modelType = GetActiveUndergroundModelType();
            SetSelectedBroSoundingId(modelType, null);
            RefreshUndergroundAnalysisSidebarPanel();
            SendBroSoundingsToMap();
            SaveStepReportDataForStep(6);
            RefreshInlineReportPreviewIfVisible();
            OutputText.Text = $"{DinoModelLabel(modelType)} selectie gewist\n\nKlik op een kaartpunt om opnieuw data te tonen.";
            return;
        }

        if (IsSelectedStepThreeKlicSubstep() && IsBoreTraceEditAction(action))
        {
            SendTraceStateToMap(drawing: false);
            OutputText.Text = "KLIC-controlesubstap is alleen-lezen\n\nDe boorlijn is zichtbaar voor KLIC-controle, maar aanpassen kan alleen in 3.1 Boorlijn ingetekend.";
            return;
        }

        if (action == "Naar boorlijn")
        {
            EnsureProfilePoints();
            SendTraceStateToMap();
            SendProfileModeToMap();
            SendMapMessage("{\"type\":\"fitTrace\",\"scale\":" + _workDrawingScale.ToString(CultureInfo.InvariantCulture) + "}");
            RenderProfilePanel();
            OutputText.Text = $"Werktekening naar boorlijn\n\nKaart en dwarsprofiel zijn uitgelijnd op schaal 1:{_workDrawingScale}.";
            return;
        }
        if (action.StartsWith("Werktekening schaal ", StringComparison.OrdinalIgnoreCase))
        {
            var value = action.Replace("Werktekening schaal ", "", StringComparison.OrdinalIgnoreCase);
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var scale))
            {
                _workDrawingScale = scale;
                RenderWorkDrawingSidebarPanel();
                RenderWorkDrawingPreview();
                OutputText.Text = $"Werktekeningschaal ingesteld\n\nSituatie boring en dwarsprofiel staan op 1:{_workDrawingScale}.";
            }
            return;
        }
        if (action == "Nieuwe boorlijn")
        {
            if (_currentBoreTracePoints.Count >= 1 || !string.IsNullOrWhiteSpace(_currentBoreTraceJson))
            {
                var result = MessageBox.Show(
                    "Er kan maar 1 boorlijn in dit project staan.\n\nAls je een nieuwe boorlijn start, wordt de bestaande boorlijn automatisch verwijderd en vervangen zodra je opslaat.\n\nWil je doorgaan?",
                    "Bestaande boorlijn vervangen",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                {
                    OutputText.Text = "Nieuwe boorlijn geannuleerd\n\nDe bestaande boorlijn is behouden.";
                    return;
                }
            }

            _currentBoreTraceJson = null;
            _currentBoreTracePoints = [];
            SaveBoreTraceGeoJson("null");
            RenderTracePointsTable();
            RefreshWorkflowReportStatus(_selectedStep.Number);
            SaveStepReportDataForStep(3);
            SendMapMessage("{\"type\":\"traceClear\"}");
            OutputText.Text = "Nieuwe boorlijn gestart\n\nDe vorige boorlijn is verwijderd. Klik op de kaart voor het intredepunt. Klik daarna voor tussenpunten en uittrede.";
            return;
        }
        if (action == "Start tekenmodus")
        {
            OutputText.Text = StartBoreTraceMode();
            return;
        }
        if (action == "Boorlijn handmodus")
        {
            SendTraceStateToMap(drawing: false);
            OutputText.Text = "Kaart verplaatsen actief\n\nDe boorlijn blijft zichtbaar, maar klikken voegt geen punten toe. Sleep de kaart om de uitsnede te verplaatsen.";
            return;
        }
        if (action == "Zoom naar boorlijn")
        {
            EnsureStoredBoreTraceLoaded();
            SendTraceStateToMap(drawing: false);
            SendMapMessage("{\"type\":\"zoomToBoreline\"}");
            OutputText.Text = _currentBoreTracePoints.Count >= 2
                ? "Zoom naar boorlijn\n\nDe GIS-kaart zoomt naar de volledige opgeslagen boorlijn."
                : "Zoom naar boorlijn\n\nEr is nog geen volledige boorlijn opgeslagen. Teken minimaal een intrede- en uittredepunt.";
            return;
        }
        if (action == "Boorlijn opslaan")
        {
            OutputText.Text = RequestBoreTraceSave();
            return;
        }
        if (action == "Verschuif lijn")
        {
            SendMapMessage("{\"type\":\"traceMoveMode\"}");
            OutputText.Text = "Verschuifmodus geactiveerd\n\nSleep de boorlijn op de kaart om alle punten tegelijk te verplaatsen.";
            return;
        }
        if (action == "Boorlijn trace vloeiend")
        {
            OutputText.Text = ToggleSmoothTrace();
            return;
        }
        if (action == "Wissel richting")
        {
            SendMapMessage("{\"type\":\"traceReverse\"}");
            OutputText.Text = "Richting wisselen aangevraagd\n\nIntrede en uittrede worden omgedraaid; de puntnummers en tabel worden opnieuw opgebouwd.";
            return;
        }
        if (action == "Verwijder trace")
        {
            OutputText.Text = ClearBoreTrace();
            return;
        }
        if (action == "Plaats boormachine")
        {
            OutputText.Text = StartMachinePlacement("rig", "Boormachine");
            return;
        }
        if (action == "Plaats bentonietwagen")
        {
            OutputText.Text = StartMachinePlacement("bentonite", "Bentonietwagen");
            return;
        }
        if (action == "Lijn machine uit op boorlijn")
        {
            SendMapMessage("{\"type\":\"machineAlignTrace\",\"machineType\":\"rig\"}");
            OutputText.Text = "Machine uitlijnen\n\nDe boormachine wordt evenwijdig aan de boorlijn gezet.";
            return;
        }
        if (action == "Zet machine op intrede")
        {
            SendMapMessage("{\"type\":\"machineSnapToTraceStart\",\"machineType\":\"rig\"}");
            OutputText.Text = "Machine op intrede zetten\n\nHet aansluitpunt van de boormachine wordt op het eerste boorpunt geplaatst.";
            return;
        }
        if (action == "Roteer bentonietwagen")
        {
            SendMapMessage("{\"type\":\"machineRotate\",\"machineType\":\"bentonite\",\"mode\":\"drag\"}");
            OutputText.Text = "Bentonietwagen roteren\n\nSleep de bentonietwagen op de kaart om hem vrij rond het aansluitpunt te draaien.";
            return;
        }
        if (action == "Machine info modus")
        {
            SendMapMessage("{\"type\":\"featureInfoMode\",\"enabled\":true}");
            OutputText.Text = "Infomodus actief\n\nKlik op BGT/KLIC/lagen om de gegevens te bekijken. Machine plaatsen is tijdelijk gestopt.";
            return;
        }
        if (action == "Machine handmodus")
        {
            SendMapMessage("{\"type\":\"featureInfoMode\",\"enabled\":false}");
            SendMapMessage("{\"type\":\"machineStop\"}");
            OutputText.Text = "Handmodus actief\n\nJe kunt de kaart verplaatsen en klikken zonder dat BGT/KLIC informatie opent.";
            return;
        }
        if (action == "Pas machinemaat toe")
        {
            OutputText.Text = ApplySelectedMachineSize();
            return;
        }
        if (action == "Verwijder machines")
        {
            OutputText.Text = ClearMachines();
            return;
        }
        if (action == "Sla machines op")
        {
            OutputText.Text = SaveMachinePlacements();
            return;
        }
        if (action == "Stop machine plaatsen")
        {
            SendMapMessage("{\"type\":\"machineStop\"}");
            OutputText.Text = "Machine plaatsen gestopt.";
            return;
        }
        if (action == "Boorlijn horizontaal" && _selectedStep.Number == 4)
        {
            OutputText.Text = AlignMapToHorizontalBorelineForStepFour(runAnalysis: false);
            return;
        }
        if (action == "Uitlijnen BGT analyse" && _selectedStep.Number == 4)
        {
            OutputText.Text = AlignMapToHorizontalBorelineForStepFour(runAnalysis: true);
            return;
        }
        if (action == "Analyse uitvoeren" && _selectedStep.Number == EnvironmentStepNumber)
        {
            var analysis = BuildParcelOwnerAnalysis(refresh: true);
            var total = analysis.TraceLength > 0 ? analysis.TraceLength : Math.Max(1, _selectedProject.BoreLengthMeters);
            var segments = analysis.Segments;
            RenderEnvironmentAnalysisSidebarPanel(showResults: true);
            SetSidebarTab("environment");
            if (_selectedReportPreviewStepNumber == EnvironmentStepNumber)
            {
                RenderStepReportPreview(EnvironmentStepNumber);
            }
            OutputText.Text = segments.Count == 0
                ? "Omgevingsanalyse uitgevoerd\n\nGeen perceelsegmenten gevonden. Controleer of de Kadaster/BAG-zip met kadastralekaart_perceel.gml en een opgeslagen boorlijn aanwezig zijn."
                : $"Omgevingsanalyse uitgevoerd\n\n{segments.Count} perceel-/bronhoudersegment(en) langs {total:N1} m boorlijn. Rapportpreview stap {DisplayStepNumber(EnvironmentStepNumber)} en het eindrapport gebruiken nu deze analyse.";
            return;
        }
        if (action == "Analyse uitvoeren" && _selectedStep.Number == 4)
        {
            OutputText.Text = ExecuteSurfaceAnalysis();
            return;
        }
        if (action == "BGT oppervlakteanalyse")
        {
            OutputText.Text = ExecuteSurfaceAnalysis();
            if (_selectedStep.Number == ProfileStepNumber)
            {
                StepSurfaceAnalysisPanel.Visibility = Visibility.Collapsed;
                RequestProfileMapAlignmentIfNeeded();
                RenderProfilePanel();
            }
            return;
        }
        if (action is "Genereer werktekening" or "Werktekening preview")
        {
            await RunUiBackgroundOperationAsync(
                "Werktekening-preview opbouwen...",
                async () =>
                {
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    return GenerateWorkDrawingPreview();
                });
            return;
        }
        if (action == "Exporteer werktekening")
        {
            await RunUiBackgroundOperationAsync(
                "Werktekening exporteren...",
                () => ExportWorkDrawingHtmlAsync(openAfterExport: true));
            return;
        }
        if (action == "Genereer rapport")
        {
            await RunUiBackgroundOperationAsync(
                "Rapportdata verversen...",
                async () =>
                {
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    return RegenerateFinalReportPreview();
                });
            return;
        }

        OutputText.Text = action switch
        {
            "Controleer lokale database" => _projects.GetDatabaseStatus(),
            "Kies KLIC bestand" => ChooseProjectFile("KLIC", "KLIC bestanden (*.zip;*.gml)|*.zip;*.gml|Alle bestanden (*.*)|*.*"),
            "Importeer DXF/GML" => ChooseProjectFile("Ontwerp", "Ontwerpbestanden (*.dxf;*.gml;*.geojson;*.kml;*.zip)|*.dxf;*.gml;*.geojson;*.kml;*.zip|Alle bestanden (*.*)|*.*"),
            "Open PDOK BGT downloader" => OpenPdokBgtDownloader(),
            "Importeer BGT download" => ImportBgtDownload(),
            "Controleer BGT service" => _geo.GetBgtStatus(),
            "Controleer BRO service" => _geo.GetBroStatus(),
            "Controleer AHN service" => $"{_geo.GetAhnStatus()}\nDemo RD: X={_geo.ToApproximateRd(52.0907, 5.1214).X}, Y={_geo.ToApproximateRd(52.0907, 5.1214).Y}",
            "AHN4 maaiveld aan/uit" => ToggleOverlayFromAction("ahn4Dtm", "AHN4 maaiveld (DTM)"),
            "AHN4 ruw aan/uit" => ToggleOverlayFromAction("ahn4Dsm", "AHN4 ruw hoogtebeeld (DSM)"),
            "BAG/Kadaster aan/uit" => ToggleOverlayFromAction("bagImport", "BAG/Kadaster import"),
            "BGT aan/uit" => ToggleOverlayFromAction("bgt", "BGT"),
            "Sla analyse op" => SaveStepData("analyse_punten", "{\"bron\":\"native\",\"status\":\"concept\",\"risico\":\"laag\"}"),
            "Genereer profiel" => GenerateDepthProfile(),
            "Boorlijn horizontaal" => AlignBorelineHorizontalForProfile(),
            "Kaart uitlijnen met profiel" => AlignProfileToMap(),
            "Boorlijn vloeiend" => ToggleSmoothProfile(),
            "Profiel vastzetten" => ToggleProfileLayoutLock(),
            "Voeg dieptepunt toe" => AddDepthControlPoint(),
            "Sla dieptepunten op" => SaveDepthProfile(),
            "Download GeoJSON" => ExportDepthProfileGeoJson(),
            "Sla werkvak op" => SaveMachinePlacements(),
            "Maak CAD export preview" => CreateCadOutput(),
            _ => $"{action}\n\nDeze actie is nog niet ingericht voor stap {_selectedStep.Number}. Gebruik de beschikbare invoervelden en rapportpreview voor dit onderdeel."
        };
        }
        finally
        {
            stopwatch.Stop();
            LogPerformanceTiming($"Stapactie {_selectedStep.Number}: {action}", stopwatch.Elapsed);
        }
    }













    private static List<Point> ReadDxfPolylinePoints(IReadOnlyList<(string Code, string Value)> entity)
    {
        var points = new List<Point>();
        double? x = null;
        foreach (var (code, value) in entity)
        {
            if (code == "10" && TryParseDxfDouble(value, out var parsedX))
            {
                x = parsedX;
            }
            else if (code == "20" && x.HasValue && TryParseDxfDouble(value, out var parsedY))
            {
                points.Add(new Point(x.Value, parsedY));
                x = null;
            }
        }
        return points;
    }



    private static bool TryReadDxfValue(IReadOnlyList<(string Code, string Value)> entity, string code, out double value)
    {
        foreach (var pair in entity)
        {
            if (pair.Code == code && TryParseDxfDouble(pair.Value, out value)) return true;
        }
        value = 0;
        return false;
    }

    private static bool TryReadDxfInt(IReadOnlyList<(string Code, string Value)> entity, string code, out int value)
    {
        foreach (var pair in entity)
        {
            if (pair.Code == code && int.TryParse(pair.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return true;
        }
        value = 0;
        return false;
    }



    private string ToggleOverlayFromAction(string overlayId, string label)
    {
        if (BlockIfCurrentMapReportLocked()) return "Kaart opgeslagen voor rapportage. Deze kaart wordt gebruikt in het rapport.";
        var visible = !_mapOverlayStates.TryGetValue(overlayId, out var current) || !current;
        _mapOverlayStates[overlayId] = visible;
        SendMapMessage($"{{\"type\":\"overlay\",\"id\":\"{overlayId}\",\"visible\":{visible.ToString().ToLowerInvariant()}}}");
        _suppressProjectLayerSend = true;
        try
        {
            RenderStepThree(force: true);
        }
        finally
        {
            _suppressProjectLayerSend = false;
        }
        SendProjectLayersToMap();
        return $"{label} {(visible ? "ingeschakeld" : "uitgeschakeld")}.";
    }

    private string ChooseProjectFile(string fileType, string filter)
    {
        if (_selectedProject is null) return "";

        var dialog = new OpenFileDialog
        {
            Title = $"Kies {fileType} bestand",
            Filter = filter,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return "Geen bestand gekozen.";
        }

        ClearEnvironmentAnalysisCache();
        var result = _projects.AddProjectFile(_selectedProject.Id, fileType, dialog.FileName);
        _projectFiles = _projects.GetProjectFiles(_selectedProject.Id);
        MarkReportUiDataChanged();
        return result;
    }

    private string OpenPdokBgtDownloader()
    {
        Process.Start(new ProcessStartInfo("https://app.pdok.nl/lv/bgt/download-viewer/") { UseShellExecute = true });
        return "PDOK BGT downloader geopend.\n\nDownload daar de BGT ZIP en importeer die daarna met 'Importeer BGT download'.";
    }

    private string ImportBgtDownload()
    {
        if (_selectedProject is null) return "";

        var result = ChooseProjectFile("BGT", "BGT download (*.zip;*.gml;*.geojson)|*.zip;*.gml;*.geojson|Alle bestanden (*.*)|*.*");
        _mapLayerBuilder.ClearCache();
        _projectFiles = _projects.GetProjectFiles(_selectedProject.Id);
        _gisLayerState.SetOverlay("bgt", true);

        if (_mapLibreLoaded && StepThreeMapView.CoreWebView2 is not null)
        {
            SendProjectLayersToMap();
        }

        return $"{result}\n\nBGT overlay staat aan. De BGT geometrieen worden zichtbaar in stap 4 zodra de kaart geladen is.";
    }

    private string SaveStepData(string key, string json)
    {
        if (_selectedProject is null || _selectedStep is null) return "";
        SaveSelectedProjectStepData(_selectedStep.Number, key, json);
        return $"Opgeslagen in lokale SQLite database\n\nProject: {_selectedProject.Name}\nStap: {_selectedStep.Number} - {_selectedStep.Title}\nSleutel: {key}\n\n{json}";
    }

    private string CreateCadOutput()
    {
        if (_selectedProject is null) return "";
        var preview = _cad.CreatePreview(_selectedProject);
        return $"{preview.Title}\n\nLagen:\n- {string.Join("\n- ", preview.Layers)}\n\n{preview.Note}";
    }

    private static bool TryReadReportExportPath(string? json, out string path)
    {
        path = "";
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("path", out var pathElement)) return false;
            path = pathElement.GetString() ?? "";
            return !string.IsNullOrWhiteSpace(path);
        }
        catch
        {
            return false;
        }
    }

    private static int ImportedFileTypeOrder(string fileType)
    {
        var normalized = NormalizeImportedFileType(fileType);
        if (normalized.Contains("Ontwerp", StringComparison.OrdinalIgnoreCase)) return 0;
        if (normalized.Equals("KLIC", StringComparison.OrdinalIgnoreCase)) return 1;
        if (normalized.Contains("BAG", StringComparison.OrdinalIgnoreCase) || normalized.Contains("Kadaster", StringComparison.OrdinalIgnoreCase)) return 2;
        if (normalized.Equals("BGT", StringComparison.OrdinalIgnoreCase)) return 3;
        return 9;
    }

    private static string NormalizeImportedFileType(string fileType)
    {
        if (IsDesignFileType(fileType)) return $"Ontwerplaag ({fileType.ToUpperInvariant()})";
        if (fileType.Contains("KLIC", StringComparison.OrdinalIgnoreCase)) return "KLIC";
        if (fileType.Contains("BAG", StringComparison.OrdinalIgnoreCase) || fileType.Contains("Kadaster", StringComparison.OrdinalIgnoreCase)) return "BAG/Kadaster";
        if (fileType.Contains("BGT", StringComparison.OrdinalIgnoreCase)) return "BGT";
        return string.IsNullOrWhiteSpace(fileType) ? "Onbekend" : fileType;
    }

    private static string DescribeImportedFileType(string fileType)
    {
        if (IsDesignFileType(fileType)) return "Ontwerp-/netlaag voor technische ligging en kruisingen.";
        if (fileType.Contains("KLIC", StringComparison.OrdinalIgnoreCase)) return "KLIC-melding met kabels/leidingen en documentbijlagen.";
        if (fileType.Contains("BAG", StringComparison.OrdinalIgnoreCase) || fileType.Contains("Kadaster", StringComparison.OrdinalIgnoreCase)) return "BAG/Kadaster-context voor percelen, adressen of objectreferentie.";
        if (fileType.Contains("BGT", StringComparison.OrdinalIgnoreCase)) return "BGT-context voor topografie, oppervlaktes en bronhouderinformatie.";
        return "Gekoppeld projectbestand.";
    }

    private static bool IsDesignFileType(string fileType) =>
        fileType.Equals("LS", StringComparison.OrdinalIgnoreCase) ||
        fileType.Equals("MS", StringComparison.OrdinalIgnoreCase) ||
        fileType.Equals("Ontwerp", StringComparison.OrdinalIgnoreCase) ||
        fileType.Contains("spanning", StringComparison.OrdinalIgnoreCase) ||
        fileType.Contains("gas", StringComparison.OrdinalIgnoreCase) ||
        fileType.Contains("water", StringComparison.OrdinalIgnoreCase) ||
        fileType.Contains("data", StringComparison.OrdinalIgnoreCase) ||
        fileType.Contains("telecom", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeReportText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "- Geen AI analyse uitgevoerd.";
        var normalized = text.Replace("\r\n", "\n").Trim();
        return normalized.Contains('\n') ? normalized : $"- {normalized}";
    }

    private void TryAddDefaultBgtDownload(Guid projectId)
    {
        const string defaultBgtPath = @"C:\Users\ThierryPapenhuijzen\Downloads\extract.zip";
        if (!System.IO.File.Exists(defaultBgtPath)) return;

        var fileName = System.IO.Path.GetFileName(defaultBgtPath);
        var existing = _projects.GetProjectFiles(projectId).Any(file =>
            file.FileType.Equals("BGT", StringComparison.OrdinalIgnoreCase) &&
            file.DisplayName.Equals(fileName, StringComparison.OrdinalIgnoreCase));

        if (existing) return;

        _projects.AddProjectFileRecord(projectId, "BGT", defaultBgtPath);
        ClearEnvironmentAnalysisCache();
        MarkReportUiDataChanged();
    }

    private void NewProject_OnClick(object sender, RoutedEventArgs e)
    {
        var project = _projects.CreateProject(
            $"Nieuw prescan project {DateTime.Now:HHmm}",
            "Lokale opdrachtgever",
            "Nieuwe locatie",
            120,
            200,
            "PE100 SDR11");

        TryAddDefaultBgtDownload(project.Id);

        var stayOnProjectsPage = ProjectsPagePanel.Visibility == Visibility.Visible;
        RefreshProjects();
        ProjectsList.SelectedItem = ProjectsList.Items.Cast<PrescanProject>().FirstOrDefault(p => p.Id == project.Id);
        ProjectsPageList.SelectedItem = ProjectsList.SelectedItem;
        if (stayOnProjectsPage)
        {
            ShowProjectsPage();
        }
        else
        {
            ShowWorkflowPage();
        }
        OutputText.Text = "Nieuw lokaal project aangemaakt in SQLite. De standaard BGT download is gekoppeld als extract.zip beschikbaar is.";
    }

    private void Sync_OnClick(object sender, RoutedEventArgs e)
    {
        OutputText.Text = _projects.GetDatabaseStatus();
    }

    private static string GetNativeNote(int stepNumber) => stepNumber switch
    {
        1 => "Projectgegevens en boringconfiguratie worden lokaal opgeslagen in SQLite. Geen online database nodig.",
        2 => "Bestandsupload wordt lokaal: bronbestanden, bestandspaden, validatie en later GDAL/OGR parsing.",
        3 => "Leaflet wordt vervangen door een native kaartviewer. Lagen, styling en selectie blijven als modules bestaan.",
        4 => "Boorlijn tekenen wordt een native tekenmodus met snapping, RD-coordinaten en lokale opslag.",
        5 => "BGT analyse gaat naar een Geo module met geometrie-intersecties en risico-classificatie.",
        6 => "Omgevingsmanagement gebruikt kadastrale percelen, BGT-bronhouders en handmatige ZRO-controle.",
        7 => "BRO/AHN services worden native HttpClient-koppelingen met caching en foutstatussen.",
        8 => "Dwarsprofiel wordt een eigen tekencomponent met NAP, dieptepunten en kruisinglabels.",
        9 => "Machineplaatsing wordt een aparte werkruimte met werkvak, bentoniet en BLVC-notities.",
        10 => "3D-context en export blijven beschikbaar als aparte module buiten het eindrapport.",
        _ => "Rapportage en export worden lokale modules, met DXF/DWG/rapport als AutoCAD-ready output."
    };

    private BoringResult ComputeBoring()
    {
        if (_boringItems.Count == 0)
        {
            return new BoringResult([], 0, 75);
        }

        var processed = _boringItems.Select((item, index) =>
        {
            if (item.Type == BoringItemType.Mantelbuis)
            {
                var pe = PeSizes.First(p => p.Dn == item.Dn);
                var contentArea = item.Contents.Sum(c => Math.PI * Math.Pow(c.OutsideDiameter / 2d, 2));
                var requiredId = item.Contents.Count > 0 ? 2 * Math.Sqrt(contentArea / (Math.PI * FillFactor)) : 0;
                var idArea = Math.PI * Math.Pow(pe.InnerDiameter / 2d, 2);
                var fill = idArea > 0 ? Math.Min(contentArea / idArea * 100, 100) : 0;
                return new ProcessedBoringItem(item, pe.OutsideDiameter, TubeColors[index % TubeColors.Length], pe.InnerDiameter >= requiredId, fill);
            }

            return new ProcessedBoringItem(item, item.OutsideDiameter, item.Color, true, 0);
        }).ToArray();

        var totalArea = processed.Sum(p => Math.PI * Math.Pow(p.EffectiveOutsideDiameter / 2d, 2));
        var bundleDiameter = 2 * Math.Sqrt(totalArea / (Math.PI * 0.64));
        var boringDiameter = Math.Max((int)Math.Ceiling(bundleDiameter * BoringFactor / 25d) * 25, 75);
        return new BoringResult(processed, bundleDiameter, boringDiameter);
    }

    private static Point[] GravityPack(IReadOnlyList<double> radii, double containerRadius)
    {
        if (radii.Count == 0) return [];
        if (radii.Count == 1) return [new Point(0, containerRadius - radii[0])];

        var sorted = radii.Select((radius, index) => new PackedCircle(radius, index)).OrderByDescending(c => c.Radius).ToList();
        var placed = new List<PackedCircle>();

        foreach (var circle in sorted)
        {
            var maxRadius = containerRadius - circle.Radius;
            if (maxRadius <= 0)
            {
                placed.Add(circle with { X = 0, Y = 0 });
                continue;
            }

            var bestX = 0d;
            var bestY = -maxRadius;
            for (var step = -120; step <= 120; step++)
            {
                var x = step / 120d * maxRadius;
                if (x * x > maxRadius * maxRadius + 0.01) continue;
                var y = Math.Sqrt(Math.Max(0, maxRadius * maxRadius - x * x));
                foreach (var existing in placed)
                {
                    var dx = x - existing.X;
                    var minDistance = circle.Radius + existing.Radius;
                    if (Math.Abs(dx) < minDistance)
                    {
                        y = Math.Min(y, existing.Y - Math.Sqrt(Math.Max(0, minDistance * minDistance - dx * dx)));
                    }
                }

                if (x * x + y * y <= maxRadius * maxRadius + 0.5 && y > bestY)
                {
                    bestY = y;
                    bestX = x;
                }
            }

            placed.Add(circle with { X = bestX, Y = bestY });
        }

        var result = new Point[radii.Count];
        foreach (var circle in placed)
        {
            result[circle.OriginalIndex] = new Point(circle.X, circle.Y);
        }

        return result;
    }

    private void AddPreviewCircle(double centerX, double centerY, double radius, string fill, string stroke, double strokeThickness)
    {
        var shape = new Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            Fill = Brush(fill),
            Stroke = Brush(stroke),
            StrokeThickness = strokeThickness
        };
        Canvas.SetLeft(shape, centerX - radius);
        Canvas.SetTop(shape, centerY - radius);
        BoringPreviewCanvas.Children.Add(shape);
    }

    private void AddPreviewLine(double x1, double y1, double x2, double y2, string color, double thickness, double[]? dash)
    {
        var line = new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = Brush(color),
            StrokeThickness = thickness
        };
        if (dash is not null) line.StrokeDashArray = new DoubleCollection(dash);
        BoringPreviewCanvas.Children.Add(line);
    }

    private void AddPreviewText(string text, double left, double top, string color, double size, FontWeight weight)
    {
        var block = new TextBlock
        {
            Text = text,
            Foreground = Brush(color),
            FontSize = size,
            FontWeight = weight
        };
        Canvas.SetLeft(block, left);
        Canvas.SetTop(block, top);
        BoringPreviewCanvas.Children.Add(block);
    }

    private void AddLegendChip(string color, string label)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(7, 0, 7, 4) };
        panel.Children.Add(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = Brush(color), Margin = new Thickness(0, 1, 5, 0) });
        panel.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = Brush("#587080") });
        BoringLegendPanel.Children.Add(panel);
    }

    private static readonly PeSize[] PeSizes =
    [
        new(32, 32, 3.0, 26.0),
        new(40, 40, 3.7, 32.6),
        new(50, 50, 4.6, 40.8),
        new(63, 63, 5.8, 51.4),
        new(75, 75, 6.8, 61.4),
        new(90, 90, 8.2, 73.6),
        new(110, 110, 10.0, 90.0),
        new(125, 125, 11.4, 102.2),
        new(160, 160, 14.6, 130.8),
        new(200, 200, 18.2, 163.6),
        new(250, 250, 22.7, 204.6)
    ];

    private static readonly string[] TubeColors = ["#111827", "#1F2937", "#0F172A", "#374151", "#111827", "#1F2937"];

    private static readonly CableCategory[] CableCatalog =
    [
        new("ls", "Kabel LS", "#DC2626", [new("YMVK 4x10 mm2", 19), new("YMVK 4x16 mm2", 22), new("YMVK 4x25 mm2", 24), new("YMVK 4x35 mm2", 26), new("YMVK 4x50 mm2", 29), new("YMVK 4x95 mm2", 35), new("YMVK 4x150 mm2", 40)]),
        new("ms", "Kabel MS", "#7C3AED", [new("MS kabel 630AL 1x630AL", 42)]),
        new("gf", "Glasvezel", "#D97706", [new("Microduct 10/8 mm", 10), new("Microduct 16/12 mm", 16), new("GF kabel 12F", 14), new("GF kabel 24F", 16), new("GF kabel 96F", 22)]),
        new("water", "Water PE", "#2563EB", [new("PE32 water", 32), new("PE40 water", 40), new("PE50 water", 50), new("PE63 water", 63), new("PE90 water", 90)]),
        new("gas", "Gas PE", "#F59E0B", [new("PE32 gas", 32), new("PE40 gas", 40), new("PE50 gas", 50), new("PE63 gas", 63)]),
        new("hdpe", "HDPE", "#2563EB", [new("HDPE", 63)])
    ];

    private static readonly DrillMachine[] Machines =
    [
        new("d10x15", "Vermeer", "D10x15 S3", 180, 44.5, 1085, 91, "Kubota D1105, 23 pk"),
        new("d20x22", "Vermeer", "D20x22 S3", 250, 86.7, 2983, 122, "Deutz TD2.9, 74 pk"),
        new("d23x30", "Vermeer", "D23x30 S3", 300, 102, 4067, 122, "Deutz TCD2.9, 90 pk"),
        new("jt24", "Ditch Witch", "JT24", 300, 107, 4076, 122, "Cummins F3.8, 101 hp; mudpomp 151 L/min", PullbackKn: 107, SourceNote: "Bron: JT24 datasheet/literature; max. boringdiameter is als app-machineklasse opgenomen omdat de datasheet geen maximale ruimerdiameter vermeldt."),
        new("jt30", "Ditch Witch", "JT30", 350, 110, 5420, 144, "Cummins QSB4.5, 160 hp; mudpomp 189 L/min", PullbackKn: 133, SourceNote: "Bron: JT30 brochure/specsheet; max. boringdiameter is als app-machineklasse opgenomen omdat de datasheet geen maximale ruimerdiameter vermeldt."),
        new("d36x50", "Vermeer", "D36x50 S3", 400, 160, 6779, 152, "Deutz TCD3.6, 130 pk")
    ];

    private static readonly LayerUploadDefinition[] StepTwoLayers =
    [
        new("LS", "Laagspanning (LS)", "#7B00AA", ".dxf,.gml,.kml,.geojson,.zip", "Ontwerpbestanden (*.dxf;*.gml;*.kml;*.geojson;*.zip)|*.dxf;*.gml;*.kml;*.geojson;*.zip|Alle bestanden (*.*)|*.*", false),
        new("MS", "Middenspanning (MS)", "#00CCFF", ".dxf,.gml,.kml,.geojson,.zip", "Ontwerpbestanden (*.dxf;*.gml;*.kml;*.geojson;*.zip)|*.dxf;*.gml;*.kml;*.geojson;*.zip|Alle bestanden (*.*)|*.*", false),
        new("Gas", "Gas (lage druk)", "#FFFF00", ".dxf,.gml,.kml,.geojson,.zip", "Ontwerpbestanden (*.dxf;*.gml;*.kml;*.geojson;*.zip)|*.dxf;*.gml;*.kml;*.geojson;*.zip|Alle bestanden (*.*)|*.*", false),
        new("Water", "Water", "#000080", ".dxf,.gml,.kml,.geojson,.zip", "Ontwerpbestanden (*.dxf;*.gml;*.kml;*.geojson;*.zip)|*.dxf;*.gml;*.kml;*.geojson;*.zip|Alle bestanden (*.*)|*.*", false),
        new("Data", "Data / Telecom", "#00CC00", ".dxf,.gml,.kml,.geojson,.zip", "Ontwerpbestanden (*.dxf;*.gml;*.kml;*.geojson;*.zip)|*.dxf;*.gml;*.kml;*.geojson;*.zip|Alle bestanden (*.*)|*.*", false),
        new("KLIC", "KLIC-melding (ZIP / GML)", "#FF0000", ".zip,.gml", "KLIC bestanden (*.zip;*.gml)|*.zip;*.gml|Alle bestanden (*.*)|*.*", false),
        new("custom1", "", "#888888", ".dxf,.gml,.kml,.geojson,.zip", "Ontwerpbestanden (*.dxf;*.gml;*.kml;*.geojson;*.zip)|*.dxf;*.gml;*.kml;*.geojson;*.zip|Alle bestanden (*.*)|*.*", true),
        new("custom2", "", "#555555", ".dxf,.gml,.kml,.geojson,.zip", "Ontwerpbestanden (*.dxf;*.gml;*.kml;*.geojson;*.zip)|*.dxf;*.gml;*.kml;*.geojson;*.zip|Alle bestanden (*.*)|*.*", true),
        new("custom3", "", "#333333", ".dxf,.gml,.kml,.geojson,.zip", "Ontwerpbestanden (*.dxf;*.gml;*.kml;*.geojson;*.zip)|*.dxf;*.gml;*.kml;*.geojson;*.zip|Alle bestanden (*.*)|*.*", true)
    ];
    private static readonly LayerUploadDefinition[] StepThreeImportLayers =
    [
        new("BGT", "BGT download", "#2563EB", ".zip,.gml,.geojson", "BGT bestanden (*.zip;*.gml;*.geojson)|*.zip;*.gml;*.geojson|Alle bestanden (*.*)|*.*", false),
        new("BAG", "Kadaster/BAG panden/adressen", "#15803D", ".zip,.gml,.geojson", "Kadaster/BAG bestanden (*.zip;*.gml;*.geojson)|*.zip;*.gml;*.geojson|Alle bestanden (*.*)|*.*", false),
        new("bagbgt-custom1", "", "#888888", ".dxf,.gml,.kml,.geojson,.zip", "GIS bestanden (*.dxf;*.gml;*.kml;*.geojson;*.zip)|*.dxf;*.gml;*.kml;*.geojson;*.zip|Alle bestanden (*.*)|*.*", true),
        new("bagbgt-custom2", "", "#555555", ".dxf,.gml,.kml,.geojson,.zip", "GIS bestanden (*.dxf;*.gml;*.kml;*.geojson;*.zip)|*.dxf;*.gml;*.kml;*.geojson;*.zip|Alle bestanden (*.*)|*.*", true),
        new("bagbgt-custom3", "", "#333333", ".dxf,.gml,.kml,.geojson,.zip", "GIS bestanden (*.dxf;*.gml;*.kml;*.geojson;*.zip)|*.dxf;*.gml;*.kml;*.geojson;*.zip|Alle bestanden (*.*)|*.*", true)
    ];
    private static IReadOnlyDictionary<int, StepWorkspace> CreateWorkspaces()
    {
        return WorkflowCatalog.CreateWorkspaces();
    }
    private static StepWorkspace Step(int number, string title, string subtitle, string mapTitle, string mapSubtitle, IReadOnlyList<string> actions, params WorkspaceCard[] cards)
    {
        return new StepWorkspace
        {
            StepNumber = number,
            Title = title,
            Subtitle = subtitle,
            MapTitle = mapTitle,
            MapSubtitle = mapSubtitle,
            Actions = actions,
            Cards = cards
        };
    }

    private static WorkspaceCard Card(string label, string title, string body) => new() { Label = label, Title = title, Body = body };

    private sealed record PeSize(int Dn, double OutsideDiameter, double Wall, double InnerDiameter);
    private sealed record CableProduct(string Label, double OutsideDiameter);
    private sealed record CableCategory(string Key, string Label, string Color, CableProduct[] Products);
    private sealed record KnowledgeDocumentRecord
    {
        public Guid Id { get; init; }
        public string DisplayName { get; init; } = "";
        public string SourcePath { get; init; } = "";
        public string LocalPath { get; init; } = "";
        public long SizeBytes { get; init; }
        public DateTimeOffset ImportedAt { get; init; }
        public DateTimeOffset IndexedAt { get; init; }
        public string ExtractedText { get; init; } = "";
        public string ImportStatus { get; init; } = "";
    }
    private sealed record LayerUploadDefinition(string Key, string Label, string Color, string AcceptText, string Filter, bool IsCustom);
    private sealed record ReportMapRecipe(
        string Id,
        int StepNumber,
        string Title,
        string Purpose,
        string BaseMap,
        string LayerSet,
        int? ScaleDenominator,
        string ExtentMode,
        double Width,
        double Height,
        bool ShowTracePoints,
        bool ShowMachine,
        bool ShowRisk,
        double? SegmentStartMeters,
        double? SegmentEndMeters);
    private sealed record ReportStartContext(
        string ReportLocation,
        int DocumentCount,
        int LayerCount,
        int ParcelCount,
        double TraceLength,
        IReadOnlyList<ReportContentsEntry> ContentsEntries);

    private sealed record ReportStartSettings(
        string CoverTitle,
        string CoverSubtitle,
        string CoverRevision,
        string CoverNote,
        string ForewordText,
        string ForewordScope,
        string ContentsTitle,
        string ContentsIntro,
        bool IncludeAppendices)
    {
        public static ReportStartSettings Default { get; } = new("", "", "", "", "", "", "", "", true);
    }

    private sealed record ProjectHeaderMetadata(string ReportDate, string InternalProjectNumber, string ExternalProjectNumber);
    private sealed record ReportContentsEntry(string Page, string Title, string Description);
    private sealed record LonLat(double Lon, double Lat);
    private sealed record ReportMapState(
        string BaseLayer,
        IReadOnlyDictionary<string, bool> Overlays,
        IReadOnlyDictionary<string, bool> KlicThemes,
        IReadOnlyDictionary<string, bool> ProjectLayerVisibility,
        double? CenterLon,
        double? CenterLat,
        double? Zoom,
        int? MapScale);
    private sealed record ReportLocationContext(string Summary, string Road, string Place, string HouseNumber, string DisplayName);
    private sealed record DxfPair(string Code, string Value);
    private sealed record BroImportedProfileRecord
    {
        public Guid Id { get; init; }
        public string ModelType { get; init; } = "";
        public string ModelName { get; init; } = "";
        public string Identification { get; init; } = "";
        public double? X { get; init; }
        public double? Y { get; init; }
        public double? SurfaceNap { get; init; }
        public double? DepthTop { get; init; }
        public double? DepthBottom { get; init; }
        public string ExtractedSummary { get; init; } = "";
        public string SourcePath { get; init; } = "";
        public string LocalPath { get; init; } = "";
        public string ProfileImagePath { get; init; } = "";
        public string FileName { get; init; } = "";
        public DateTimeOffset ImportedAt { get; init; }
    }
    private sealed record BgtSurfacePolygon(IReadOnlyList<RdPoint> Ring, IReadOnlyList<IReadOnlyList<RdPoint>> Holes, string Label, string Color, double Area);
    private sealed record BgtSurfaceSample(double Distance, string Label, string Color);
    private sealed record BgtSurfaceSegment(double Start, double End, string Label, string Color)
    {
        public double Length => Math.Max(0, End - Start);
    }
    private sealed record SegmentClosest(double TraceRatio, double Offset);
    private sealed record StepNavigationItem(PrescanStep Step, PrescanSubstep? Substep, bool IsReportPreview)
    {
        public int Number => Step.Number;
        public bool IsSubstep => Substep is not null;
        public bool IsWorkflowStep => !IsReportPreview && !IsSubstep;
        public bool IsSelectable => IsReportPreview || IsSubstep;
        public string DisplayText => IsReportPreview
            ? "   Rapport preview"
            : Substep is not null
                ? $"{DisplaySubstepNumber(Substep)} - {Substep.Title}"
                : $"{DisplayStepNumber(Step.Number)} - {Step.Title}";
        public string CompactDisplayText => Substep is not null ? DisplaySubstepNumber(Substep) : DisplayStepNumber(Step.Number);
        public Thickness ItemMargin => IsReportPreview
            ? new Thickness(14, 0, 0, 0)
            : Substep is not null
                ? new Thickness(18, 0, 0, 0)
                : new Thickness(0);
        public Thickness CompactItemMargin => Substep is not null ? new Thickness(0, 2, 0, 2) : new Thickness(0, 7, 0, 2);
        public double CompactFontSize => Substep is not null ? 11 : 12;
        public FontWeight TitleWeight => IsReportPreview || Substep is not null ? FontWeights.Normal : FontWeights.SemiBold;
        public Brush TitleBrush => Substep is not null ? Brush("#315B7E") : Brush("#071422");
        public Visibility QuickSaveVisibility => IsReportPreview ? Visibility.Collapsed : Visibility.Visible;
    }
    private sealed record WorkflowPartItem(string Key, string Label, IReadOnlyList<FrameworkElement> Targets);
    private sealed record PackedCircle(double Radius, int OriginalIndex, double X = 0, double Y = 0);
    private sealed record ProcessedBoringItem(BoringItem Item, double EffectiveOutsideDiameter, string Color, bool Fits, double FillPercentage);
    private sealed record BoringResult(IReadOnlyList<ProcessedBoringItem> Processed, double BundleDiameter, int BoringDiameter);

    private enum ReportPreviewWindowScope
    {
        Substep,
        Chapter
    }

    private sealed class BoringItem
    {
        public Guid Id { get; init; }
        public BoringItemType Type { get; init; }
        public int Dn { get; init; }
        public string Label { get; init; } = "";
        public double OutsideDiameter { get; init; }
        public string Color { get; init; } = "#6B7280";
        public List<BoringContent> Contents { get; init; } = [];
    }

    private sealed class BoringContent
    {
        public Guid Id { get; init; }
        public string Label { get; init; } = "";
        public double OutsideDiameter { get; init; }
        public string Color { get; init; } = "#6B7280";
    }

    private enum BoringItemType
    {
        Mantelbuis,
        Direct
    }
}











































































































