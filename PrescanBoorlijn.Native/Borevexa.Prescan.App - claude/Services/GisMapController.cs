using Microsoft.Web.WebView2.Core;

namespace Borevexa.Prescan.App.Services;

public sealed class GisMapController
{
    private readonly GisMapBridgeService _bridge = new();

    public bool PendingSync { get; private set; }

    public void QueueSync() => PendingSync = true;

    public void ClearPendingSync() => PendingSync = false;

    public bool TrySendJson(CoreWebView2? webView, string json, Action<Exception>? onError = null) =>
        _bridge.TrySendJson(webView, json, onError);

    public bool TryPostJson(CoreWebView2? webView, string json, Action<Exception>? onError = null) =>
        _bridge.TryPostJson(webView, json, onError);
}
