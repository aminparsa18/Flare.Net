using System.Diagnostics;
using System.Globalization;
using System.Net.Http;

namespace Flare.Maui;

/// <summary>
/// Enriches <c>HttpClient</c> spans with request and response sizes, marks failures that happened with no
/// connectivity, and records each call as an <c>http</c> breadcrumb (ADR-0190). Sizes come from
/// <c>Content-Length</c>, so a chunked or streamed body has none rather than a guess.
/// </summary>
internal static class NetworkTracing
{
    internal const string RequestSizeTag = "http.request.body.size";
    internal const string ResponseSizeTag = "http.response.body.size";
    internal const string OfflineTag = "network.offline";

    internal static void OnRequest(Activity activity, HttpRequestMessage request)
    {
        if (request.Content?.Headers.ContentLength is { } size) activity.SetTag(RequestSizeTag, size);
    }

    internal static void OnResponse(Activity activity, HttpResponseMessage response, ActivitySource? crumbs)
    {
        var size = response.Content?.Headers.ContentLength;
        if (size is { } s) activity.SetTag(ResponseSizeTag, s);
        if (crumbs is null) return;

        var status = (int)response.StatusCode;
        var sizeText = size is { } n ? $", {FormatSize(n)}" : "";
        Breadcrumbs.Add(crumbs, "http", $"{Describe(activity)} {status}{sizeText}, {Elapsed(activity)} ms", detached: true);
    }

    internal static void OnException(Activity activity, Exception exception, ActivitySource? crumbs, Func<bool>? isOnline)
    {
        // A failed call with no connectivity is a different problem from a server that answered badly.
        var offline = isOnline is not null && !SafeIsOnline(isOnline);
        if (offline) activity.SetTag(OfflineTag, true);
        if (crumbs is null) return;

        var reason = offline ? "offline" : exception.GetType().Name;
        Breadcrumbs.Add(crumbs, "http", $"{Describe(activity)} failed: {reason}, {Elapsed(activity)} ms", detached: true);
    }

    /// <summary><c>GET host/path</c> without scheme or query string, which can carry tokens and PII.</summary>
    internal static string Describe(Activity activity)
    {
        var method = activity.GetTagItem("http.request.method") as string ?? "HTTP";
        return activity.GetTagItem("url.full") is string url && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? $"{method} {uri.Authority}{uri.AbsolutePath}"
            : method;
    }

    internal static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.#} KB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0):0.#} MB")
    };

    private static long Elapsed(Activity activity) => (long)(DateTime.UtcNow - activity.StartTimeUtc).TotalMilliseconds;

    private static bool SafeIsOnline(Func<bool> isOnline)
    {
        try { return isOnline(); }
        catch { return true; }
    }
}
