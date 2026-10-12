namespace Flare.Api.Endpoints;

/// <summary>
/// Origin check for the WebSocket upgrades (live tail, host stats, resource graph). CORS does not apply
/// to WebSockets, so without this any web page could open a socket with the visitor's cookies and read the stream.
/// </summary>
public static class WebSocketOriginGuard
{
    /// <summary>
    /// True when the request has no <c>Origin</c> (non-browser clients such as the CLI) or its origin is listed in
    /// <c>Cors:AllowedOrigins</c>. Otherwise sets 403 and returns false; the caller must return without accepting.
    /// </summary>
    public static bool IsAllowed(HttpContext http)
    {
        var allowed = http.RequestServices.GetRequiredService<IConfiguration>()
            .GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (IsAllowed(http.Request.Headers.Origin.ToString(), allowed))
        {
            return true;
        }

        http.Response.StatusCode = StatusCodes.Status403Forbidden;
        return false;
    }

    internal static bool IsAllowed(string origin, IEnumerable<string> allowedOrigins) =>
        origin.Length == 0
        || allowedOrigins.Any(a => string.Equals(a.TrimEnd('/'), origin.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
}
