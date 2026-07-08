using System.IO;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace Borevexa.App.Services;

public sealed class ReportMapCaptureCoordinator
{
    private readonly ReportPreviewService _reportPreview;
    private readonly Func<Guid?> _projectIdProvider;
    private readonly Func<int, object> _metadataProvider;
    private readonly Action<int, string> _captureCompleted;
    private readonly Action<string> _diagnostic;
    private DispatcherTimer? _liveMapReportCaptureTimer;

    public ReportMapCaptureCoordinator(
        ReportPreviewService reportPreview,
        Func<Guid?> projectIdProvider,
        Func<int, object> metadataProvider,
        Action<int, string> captureCompleted,
        Action<string> diagnostic)
    {
        _reportPreview = reportPreview;
        _projectIdProvider = projectIdProvider;
        _metadataProvider = metadataProvider;
        _captureCompleted = captureCompleted;
        _diagnostic = diagnostic;
    }

    public void QueueLiveMapCapture(int stepNumber, CoreWebView2? webView, bool refreshPreview, string? variantKey = null)
    {
        if (_projectIdProvider() is null || stepNumber <= 0 || webView is null) return;

        _liveMapReportCaptureTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(550)
        };

        _liveMapReportCaptureTimer.Stop();
        _liveMapReportCaptureTimer.Tick -= LiveMapReportCaptureTimer_OnTick;
        _liveMapReportCaptureTimer.Tick += LiveMapReportCaptureTimer_OnTick;
        _liveMapReportCaptureTimer.Tag = new PendingLiveMapCapture(stepNumber, webView, refreshPreview, variantKey);
        _liveMapReportCaptureTimer.Start();
    }

    public async Task<string> CaptureLiveMapAsync(int stepNumber, CoreWebView2? webView, bool refreshPreview, bool force = false, string? variantKey = null)
    {
        var projectId = _projectIdProvider();
        if (projectId is null || stepNumber <= 0 || webView is null) return "";

        var path = await _reportPreview.CaptureLiveMapForReportPreviewAsync(
            projectId.Value,
            stepNumber,
            webView,
            _metadataProvider(stepNumber),
            exception => _diagnostic($"Live kaartcapture voor rapportpreview mislukt: {exception}"),
            force,
            variantKey);

        if (!string.IsNullOrWhiteSpace(path) && refreshPreview)
        {
            _captureCompleted(stepNumber, path);
        }

        return path;
    }

    private async void LiveMapReportCaptureTimer_OnTick(object? sender, EventArgs e)
    {
        if (_liveMapReportCaptureTimer is null) return;

        var pending = _liveMapReportCaptureTimer.Tag as PendingLiveMapCapture;
        _liveMapReportCaptureTimer.Stop();
        if (pending is null) return;

        await CaptureLiveMapAsync(pending.StepNumber, pending.WebView, pending.RefreshPreview, variantKey: pending.VariantKey);
    }

    private sealed record PendingLiveMapCapture(int StepNumber, CoreWebView2 WebView, bool RefreshPreview, string? VariantKey);
}
