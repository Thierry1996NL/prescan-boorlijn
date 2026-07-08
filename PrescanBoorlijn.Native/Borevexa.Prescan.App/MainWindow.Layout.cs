using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Borevexa.Prescan.App;

public partial class MainWindow
{
    private void SidebarToggle_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_sidebarCollapsed && SidebarColumn.ActualWidth > 80)
        {
            _expandedSidebarWidth = SidebarColumn.ActualWidth;
        }

        _sidebarCollapsed = !_sidebarCollapsed;
        ApplySidebarState();
    }

    private bool IsCompactShell()
    {
        var width = ActualWidth > 0 ? ActualWidth : 1280d;
        var height = ActualHeight > 0 ? ActualHeight : 730d;
        return width < 1500d || height < 820d;
    }

    private double GetSidebarMinExpandedWidth() => IsCompactShell() ? 316d : 334d;

    private double GetSidebarMaxWidth() => IsCompactShell() ? 346d : 380d;

    private double GetResponsiveStepFilterWidth()
    {
        var width = ActualWidth > 0 ? ActualWidth : 1280d;
        if (width < 1280d) return 276d;
        if (width < 1500d) return 300d;
        if (width < 1750d) return 336d;
        return 370d;
    }

    private double GetResponsiveReportPreviewWidth()
    {
        var shellWidth = WorkflowPanel.ActualWidth > 0
            ? WorkflowPanel.ActualWidth
            : Math.Max(900d, (ActualWidth > 0 ? ActualWidth : 1280d) - SidebarColumn.ActualWidth - SidebarSplitterColumn.ActualWidth);
        var preferred = _inlineReportPreviewWidth;
        var compact = IsCompactShell();
        var minimum = compact ? 292d : 340d;
        var hardMaximum = compact ? 410d : 820d;
        var keepMainWorkspace = compact ? 520d : 620d;
        var dynamicMaximum = Math.Max(minimum, shellWidth - keepMainWorkspace - 5d);
        return Math.Clamp(preferred, minimum, Math.Min(hardMaximum, dynamicMaximum));
    }

    private bool IsSidebarProcessInfoAvailable() =>
        StepFilterSidebar.Visibility == Visibility.Visible && _selectedStep is not null && _selectedReportPreviewStepNumber is null;

    private bool IsSidebarProcessInfoModeActive() =>
        string.Equals(_activeSidebarMainMode, "info", StringComparison.OrdinalIgnoreCase) && IsSidebarProcessInfoAvailable();

    private void MoveStepFilterSidebarToUnifiedHost()
    {
        if (ReferenceEquals(UnifiedStepContextHost.Content, StepFilterSidebar))
        {
            return;
        }

        if (StepFilterSidebar.Parent is Panel parentPanel)
        {
            parentPanel.Children.Remove(StepFilterSidebar);
        }
        else if (StepFilterSidebar.Parent is ContentControl parentContent)
        {
            parentContent.Content = null;
        }

        Grid.SetRow(StepFilterSidebar, 0);
        Grid.SetRowSpan(StepFilterSidebar, 1);
        Grid.SetColumn(StepFilterSidebar, 0);
        Grid.SetColumnSpan(StepFilterSidebar, 1);
        StepFilterSidebar.Margin = new Thickness(0);
        StepFilterSidebar.Padding = new Thickness(0);
        StepFilterSidebar.BorderThickness = new Thickness(0);
        StepFilterSidebar.Background = Brush("#F8FAFC");
        UnifiedStepContextHost.Content = StepFilterSidebar;
        StepFilterColumn.Width = new GridLength(0);
    }

    private void ApplyResponsiveChromeLayout()
    {
        if (!_sidebarCollapsed)
        {
            var maxSidebarWidth = GetSidebarMaxWidth();
            if (_expandedSidebarWidth > maxSidebarWidth)
            {
                _expandedSidebarWidth = maxSidebarWidth;
            }

            SidebarColumn.Width = new GridLength(Math.Clamp(_expandedSidebarWidth, GetSidebarMinExpandedWidth(), maxSidebarWidth));
        }

        StepFilterColumn.Width = new GridLength(0);

        if (InlineReportPreviewSidebar.Visibility == Visibility.Visible)
        {
            ReportPreviewColumn.Width = new GridLength(GetResponsiveReportPreviewWidth());
        }
    }

    private void ApplySidebarState()
    {
        var showProcessInfo = !_sidebarCollapsed && IsSidebarProcessInfoModeActive();
        var showProcessSteps = !_sidebarCollapsed && !showProcessInfo;

        SidebarColumn.Width = new GridLength(_sidebarCollapsed ? 54 : Math.Clamp(_expandedSidebarWidth, GetSidebarMinExpandedWidth(), GetSidebarMaxWidth()));
        SidebarLogoImage.Width = _sidebarCollapsed ? 34 : 138;
        SidebarLogoImage.Height = _sidebarCollapsed ? 34 : 28;
        SidebarPanelTitle.Visibility = Visibility.Collapsed;
        SidebarMainTabsPanel.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        SidebarMainContent.Visibility = showProcessSteps ? Visibility.Visible : Visibility.Collapsed;
        UnifiedStepContextHost.Visibility = showProcessInfo ? Visibility.Visible : Visibility.Collapsed;
        SidebarCompactStepsList.Visibility = _sidebarCollapsed ? Visibility.Visible : Visibility.Collapsed;
        SidebarSplitter.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        SidebarSplitterColumn.Width = new GridLength(_sidebarCollapsed ? 0 : 5);
        SidebarToggleButton.Content = _sidebarCollapsed ? ">" : "<";
        Grid.SetColumn(SidebarToggleButton, _sidebarCollapsed ? 0 : 1);
        Grid.SetColumnSpan(SidebarToggleButton, _sidebarCollapsed ? 2 : 1);
        SidebarToggleButton.HorizontalAlignment = _sidebarCollapsed ? HorizontalAlignment.Center : HorizontalAlignment.Right;
        SidebarToggleButton.Margin = _sidebarCollapsed ? new Thickness(0) : new Thickness(8, 0, 0, 0);
        RefreshSidebarMainModeButtons();
        UpdateProcessStepsScrollHeight();
    }

    private void UpdateProcessStepsScrollHeight()
    {
        if (SidebarBorder.ActualHeight <= 0) return;

        var reservedHeight =
            46 + // sidebar header
            SidebarMainContent.Margin.Top +
            SidebarMainContent.Margin.Bottom;

        SideStepsList.MaxHeight = Math.Max(180, SidebarBorder.ActualHeight - reservedHeight - 18);
        SidebarCompactStepsList.MaxHeight = Math.Max(180, SidebarBorder.ActualHeight - 46 - 22);
    }

    private void ToggleReportPreview_OnClick(object sender, RoutedEventArgs e)
    {
        ShowReportPreviewWindow(ReportPreviewWindowScope.Substep);
    }

    private void SubstepReportPreview_OnClick(object sender, RoutedEventArgs e)
    {
        ShowReportPreviewWindow(ReportPreviewWindowScope.Substep);
    }

    private void ChapterReportPreview_OnClick(object sender, RoutedEventArgs e)
    {
        ShowReportPreviewWindow(ReportPreviewWindowScope.Chapter);
    }

    private void ApplyInlineReportPreviewState()
    {
        var available = _selectedProject is not null && _selectedStep is not null && _selectedReportPreviewStepNumber is null;

        _inlineReportPreviewCollapsed = true;
        InlineReportPreviewSidebar.Visibility = Visibility.Collapsed;
        ReportPreviewSplitter.Visibility = Visibility.Collapsed;
        ReportPreviewSplitterColumn.Width = new GridLength(0);
        ReportPreviewColumn.Width = new GridLength(0);
        SubstepReportPreviewButton.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        ChapterReportPreviewButton.Visibility = available ? Visibility.Visible : Visibility.Collapsed;

        if (!available && _reportPreviewWindow is not null)
        {
            _reportPreviewWindow.Close();
        }
        else if (available && IsReportPreviewWindowOpen())
        {
            RefreshReportPreviewWindow();
        }
    }

    private async void ShowReportPreviewWindow(ReportPreviewWindowScope scope)
    {
        if (_selectedProject is null || _selectedStep is null || _selectedReportPreviewStepNumber is not null)
        {
            return;
        }

        // Neem eerst een vers kaartbeeld op zodat de preview altijd de actuele
        // kaartstand toont (1-op-1 met de app).
        await EnsureFreshMapReportCaptureAsync();
        if (_selectedProject is null || _selectedStep is null || _selectedReportPreviewStepNumber is not null)
        {
            return;
        }

        _reportPreviewWindowScope = scope;
        if (_reportPreviewWindow is { IsVisible: true })
        {
            RefreshReportPreviewWindow();
            _reportPreviewWindow.Activate();
            return;
        }

        _reportPreviewWindowTitle = new TextBlock
        {
            Text = "Rapportpreview",
            Foreground = Brush("#071422"),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        _reportPreviewWindowSubtitle = new TextBlock
        {
            Text = "Onderdeel eindrapportage",
            Foreground = Brush("#587080"),
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0)
        };
        _reportPreviewWindowContent = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(14)
        };

        var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titlePanel = new StackPanel();
        titlePanel.Children.Add(_reportPreviewWindowTitle);
        titlePanel.Children.Add(_reportPreviewWindowSubtitle);
        header.Children.Add(titlePanel);

        _reportPreviewWindowZoomButton = CreateReportPreviewWindowButton($"{_inlineReportPreviewZoom * 100:N0}%", 52, ReportPreviewZoomReset_OnClick);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0) };
        actions.Children.Add(CreateReportPreviewWindowButton("Export PDF", 96, InlineReportPreviewPdfExport_OnClick, primary: true));
        actions.Children.Add(CreateReportPreviewWindowButton("-", 30, ReportPreviewZoomOut_OnClick));
        actions.Children.Add(_reportPreviewWindowZoomButton);
        actions.Children.Add(CreateReportPreviewWindowButton("+", 30, ReportPreviewZoomIn_OnClick));
        actions.Children.Add(CreateReportPreviewWindowButton("Sluiten", 72, (_, _) => _reportPreviewWindow?.Close()));
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);

        var root = new DockPanel { LastChildFill = true, Background = Brush("#F3F6FA") };
        var headerBorder = new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("#D8E6F3"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(14, 10, 14, 8),
            Child = header
        };
        DockPanel.SetDock(headerBorder, Dock.Top);
        root.Children.Add(headerBorder);

        var previewHost = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        previewHost.Children.Add(_reportPreviewWindowContent);

        var previewScrollViewer = new ScrollViewer
        {
            Content = previewHost,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            PanningMode = PanningMode.Both,
            Background = Brush("#F3F6FA")
        };
        previewScrollViewer.SizeChanged += (_, args) =>
        {
            previewHost.MinWidth = Math.Max(0, args.NewSize.Width - SystemParameters.VerticalScrollBarWidth);
        };
        root.Children.Add(previewScrollViewer);

        var workArea = SystemParameters.WorkArea;
        _reportPreviewWindow = new Window
        {
            Owner = this,
            Title = "Rapportpreview",
            Content = root,
            Width = Math.Min(1040, Math.Max(760, workArea.Width * 0.72)),
            Height = Math.Min(820, Math.Max(560, workArea.Height * 0.86)),
            MinWidth = 720,
            MinHeight = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = Brush("#F3F6FA"),
            FontFamily = FontFamily
        };
        _reportPreviewWindow.Closed += (_, _) =>
        {
            _reportPreviewWindow = null;
            _reportPreviewWindowContent = null;
            _reportPreviewWindowTitle = null;
            _reportPreviewWindowSubtitle = null;
            _reportPreviewWindowZoomButton = null;
        };

        RefreshReportPreviewWindow();
        _reportPreviewWindow.Show();
    }

    private static Button CreateReportPreviewWindowButton(string text, double width, RoutedEventHandler click, bool primary = false)
    {
        var button = new Button
        {
            Content = text,
            Width = width,
            Height = 30,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 6, 0),
            Background = primary ? Brush("#334155") : Brushes.White,
            Foreground = primary ? Brushes.White : Brush("#315B7E"),
            BorderBrush = primary ? Brush("#334155") : Brush("#CBD5E1"),
            FontSize = 11,
            FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal
        };
        button.Click += click;
        return button;
    }

    private void ApplyStepFilterSidebarState(bool available)
    {
        ToggleStepFilterButton.Visibility = Visibility.Collapsed;
        KnowledgeLibraryButton.Visibility = Visibility.Collapsed;
        if (!available)
        {
            StepFilterSidebar.Visibility = Visibility.Collapsed;
            UnifiedStepContextHost.Visibility = Visibility.Collapsed;
            StepFilterColumn.Width = new GridLength(0);
            if (string.Equals(_activeSidebarMainMode, "info", StringComparison.OrdinalIgnoreCase))
            {
                _activeSidebarMainMode = "steps";
            }

            ApplySidebarState();
            return;
        }

        StepFilterSidebar.Visibility = Visibility.Visible;
        StepFilterColumn.Width = new GridLength(0);
        ApplySidebarState();
        UpdateProcessStepsScrollHeight();
    }

    private void SidebarMainMode_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not string mode)
        {
            return;
        }

        SetSidebarMainMode(mode);
    }

    private void SetSidebarMainMode(string mode)
    {
        if (string.Equals(mode, "info", StringComparison.OrdinalIgnoreCase) && !IsSidebarProcessInfoAvailable())
        {
            mode = "steps";
            OutputText.Text = "Procesinfo is beschikbaar zodra er een processtap actief is.";
        }

        _activeSidebarMainMode = string.Equals(mode, "info", StringComparison.OrdinalIgnoreCase) ? "info" : "steps";
        ApplySidebarState();
    }

    private void RefreshSidebarMainModeButtons()
    {
        var infoAvailable = IsSidebarProcessInfoAvailable();
        SidebarInfoModeButton.IsEnabled = infoAvailable;

        var activeMode = IsSidebarProcessInfoModeActive() ? "info" : "steps";
        foreach (var button in new[] { SidebarStepsModeButton, SidebarInfoModeButton })
        {
            var active = string.Equals(button.Tag?.ToString(), activeMode, StringComparison.OrdinalIgnoreCase);
            button.Background = active ? Brush("#DBEAFE") : Brushes.White;
            button.Foreground = active ? Brush("#174A7C") : Brush("#315B7E");
            button.BorderBrush = active ? Brush("#BFDBFE") : Brush("#D8E6F3");
            button.Opacity = button.IsEnabled ? 1.0 : 0.55;
        }
    }

    private void ApplySidebarToolTabAvailability(bool toolsAvailable)
    {
        SidebarReportInfoTab.Visibility = Visibility.Visible;
        SidebarIntroductionTab.Visibility = Visibility.Collapsed;
        SidebarKnowledgeTab.Visibility = Visibility.Collapsed;
        SidebarProjectInfoTab.Visibility = ShouldShowSidebarProjectInformation() ? Visibility.Visible : Visibility.Collapsed;
        SidebarAiTab.Visibility = Visibility.Collapsed;

        if (_activeSidebarTab == "introduction" && SidebarIntroductionTab.Visibility != Visibility.Visible)
        {
            _activeSidebarTab = "reportInfo";
        }
        if (_activeSidebarTab == "knowledge" && SidebarKnowledgeTab.Visibility != Visibility.Visible)
        {
            _activeSidebarTab = "reportInfo";
        }
        if (_activeSidebarTab == "projectInfo" && SidebarProjectInfoTab.Visibility != Visibility.Visible)
        {
            _activeSidebarTab = "reportInfo";
        }
        if (_activeSidebarTab == "ai")
        {
            _activeSidebarTab = "reportInfo";
        }

        if (toolsAvailable) return;

        foreach (var button in new[] { SidebarImportsTab, SidebarBoreTraceTab, SidebarProfileTab, SidebarWorkDrawingTab, SidebarEnvironmentTab, SidebarAnalysisTab, SidebarMapLayersTab, SidebarKlicDocsTab, SidebarAiTab, SidebarKlicInfoTab, SidebarBgtInfoTab })
        {
            button.Visibility = Visibility.Collapsed;
        }
    }

    private void ToggleStepFilterSidebar_OnClick(object sender, RoutedEventArgs e)
    {
        var available = _selectedStep is not null && _selectedReportPreviewStepNumber is null;
        ApplyStepFilterSidebarState(available);
    }

    private void SidebarTab_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is string tab)
        {
            if (tab == "environment" && _selectedStep?.Number == EnvironmentStepNumber)
            {
                RenderEnvironmentAnalysisSidebarPanel(showResults: true);
            }

            SetSidebarTab(tab);
        }
    }

    private void SetSidebarTab(string tab)
    {
        if (tab is "imports" or "docs" or "klicInfo")
        {
            tab = "layers";
        }
        if (tab == "ai")
        {
            tab = "reportInfo";
        }
        if (tab == "introduction")
        {
            tab = "reportInfo";
        }
        if (tab == "knowledge" && SidebarKnowledgeTab.Visibility != Visibility.Visible)
        {
            tab = "reportInfo";
        }
        if (tab == "projectInfo" && SidebarProjectInfoTab.Visibility != Visibility.Visible)
        {
            tab = "reportInfo";
        }
        if (tab == "analysis" && SidebarAnalysisTab.Visibility != Visibility.Visible)
        {
            tab = "layers";
        }
        if (tab == "bgtInfo" && SidebarBgtInfoTab.Visibility != Visibility.Visible)
        {
            tab = "layers";
        }
        if ((tab is not "reportInfo" and not "knowledge" and not "projectInfo") &&
            (SidebarToolsTabsPanel.Visibility != Visibility.Visible || SidebarToolContentHost.Visibility != Visibility.Visible))
        {
            tab = "reportInfo";
        }

        _activeSidebarTab = tab;
        SidebarReportInfoCard.Visibility = tab == "reportInfo" ? Visibility.Visible : Visibility.Collapsed;
        SidebarIntroductionContent.Visibility = Visibility.Collapsed;
        SidebarKnowledgeContent.Visibility = tab == "knowledge" ? Visibility.Visible : Visibility.Collapsed;
        SidebarProjectInfoContent.Visibility = tab == "projectInfo" ? Visibility.Visible : Visibility.Collapsed;
        SidebarImportsContent.Visibility = Visibility.Collapsed;
        SidebarBoreTraceContent.Visibility = tab == "trace" ? Visibility.Visible : Visibility.Collapsed;
        SidebarProfileContent.Visibility = tab == "profile" ? Visibility.Visible : Visibility.Collapsed;
        SidebarWorkDrawingContent.Visibility = tab == "workdrawing" ? Visibility.Visible : Visibility.Collapsed;
        SidebarEnvironmentContent.Visibility = tab == "environment" ? Visibility.Visible : Visibility.Collapsed;
        SidebarAnalysisContent.Visibility = tab == "analysis" ? Visibility.Visible : Visibility.Collapsed;
        SidebarMapLayersContent.Visibility = tab == "layers" ? Visibility.Visible : Visibility.Collapsed;
        SidebarKlicDocsContent.Visibility = Visibility.Collapsed;
        SidebarAiContent.Visibility = Visibility.Collapsed;
        SidebarKlicInfoContent.Visibility = Visibility.Collapsed;
        SidebarBgtInfoContent.Visibility = tab == "bgtInfo" ? Visibility.Visible : Visibility.Collapsed;
        MachineSidebarHost.Visibility = tab == "workdrawing" && MachineSidebarPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (tab == "knowledge")
        {
            RenderKnowledgeLibraryPanel();
        }

        foreach (var button in new[] { SidebarReportInfoTab, SidebarIntroductionTab, SidebarKnowledgeTab, SidebarProjectInfoTab, SidebarImportsTab, SidebarBoreTraceTab, SidebarProfileTab, SidebarWorkDrawingTab, SidebarEnvironmentTab, SidebarAnalysisTab, SidebarMapLayersTab, SidebarKlicDocsTab, SidebarAiTab, SidebarKlicInfoTab, SidebarBgtInfoTab })
        {
            var active = button.Tag?.ToString() == tab;
            button.Background = active ? Brush("#334155") : Brush("#F8FAFC");
            button.Foreground = active ? Brushes.White : Brush("#315B7E");
            button.BorderBrush = active ? Brush("#334155") : Brush("#D8E6F3");
        }
    }
}
