using System.IO;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace Borevexa.Prescan.App.Services;

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

    public async Task<string> CaptureStaticMapAsync(int stepNumber, CoreWebView2? webView)
    {
        var projectId = _projectIdProvider();
        if (projectId is null || webView is null) return "";

        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Borevexa",
                "PrescanNative",
                "ReportLocks");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"project-{projectId.Value}-step-{stepNumber}-kaart.png");
            await using (var stream = File.Create(path))
            {
                await webView.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
            }

            var validation = ReportPreviewService.ValidateMapCapture(path);
            if (!validation.IsUsable)
            {
                TryDeleteFile(path);
                _diagnostic($"Rapportagekaart screenshot afgekeurd: {validation.Reason}");
                return "";
            }

            return path;
        }
        catch (Exception exception)
        {
            _diagnostic($"Rapportagekaart screenshot mislukt: {exception}");
            return "";
        }
    }

    private async void LiveMapReportCaptureTimer_OnTick(object? sender, EventArgs e)
    {
        if (_liveMapReportCaptureTimer is null) return;

        var pending = _liveMapReportCaptureTimer.Tag as PendingLiveMapCapture;
        _liveMapReportCaptureTimer.Stop();
        if (pending is null) return;

        await CaptureLiveMapAsync(pending.StepNumber, pending.WebView, pending.RefreshPreview, variantKey: pending.VariantKey);
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); } catch { }
    }

    private sealed record PendingLiveMapCapture(int StepNumber, CoreWebView2 WebView, bool RefreshPreview, string? VariantKey);
}
