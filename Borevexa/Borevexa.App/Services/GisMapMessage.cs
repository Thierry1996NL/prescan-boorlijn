using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Borevexa.App.Services;

public sealed record GisMapMessage(string Type, string RawJson);

public static class GisMapMessageParser
{
    public static bool TryParse(CoreWebView2WebMessageReceivedEventArgs args, out GisMapMessage message)
    {
        var raw = args.WebMessageAsJson;
        if (TryParse(raw, out message))
        {
            return true;
        }

        try
        {
            return TryParse(args.TryGetWebMessageAsString(), out message);
        }
        catch
        {
            message = new GisMapMessage("", raw);
            return false;
        }
    }

    public static bool TryParse(string raw, out GisMapMessage message)
    {
        message = new GisMapMessage("", raw);
        if (string.IsNullOrWhiteSpace(raw)) return false;

        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.String)
            {
                var nested = root.GetString() ?? "";
                if (!string.IsNullOrWhiteSpace(nested) && !nested.Equals(raw, StringComparison.Ordinal))
                {
                    return TryParse(nested, out message);
                }

                return false;
            }

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var typeElement) ||
                typeElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var type = typeElement.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(type)) return false;

            message = new GisMapMessage(type, root.GetRawText());
            return true;
        }
        catch
        {
            return false;
        }
    }
}
