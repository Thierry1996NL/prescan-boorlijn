using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Borevexa.App.Services;

public sealed class GisMapBridgeService
{
    public bool TrySendJson(CoreWebView2? webView, string json, Action<Exception>? onError = null)
    {
        if (webView is null || string.IsNullOrWhiteSpace(json)) return false;

        try
        {
            using var validation = JsonDocument.Parse(json);
            webView.PostWebMessageAsJson(json);
            var scriptJson = JsonSerializer.Serialize(json);
            _ = webView.ExecuteScriptAsync($"window.borevexaMap && window.borevexaMap.handleMessage(JSON.parse({scriptJson}))");
            return true;
        }
        catch (Exception exception)
        {
            onError?.Invoke(exception);
            return false;
        }
    }

    public bool TryPostJson(CoreWebView2? webView, string json, Action<Exception>? onError = null)
    {
        if (webView is null || string.IsNullOrWhiteSpace(json)) return false;

        try
        {
            using var validation = JsonDocument.Parse(json);
            webView.PostWebMessageAsJson(json);
            return true;
        }
        catch (Exception exception)
        {
            onError?.Invoke(exception);
            return false;
        }
    }
}
