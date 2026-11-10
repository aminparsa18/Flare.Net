using Flare.Api.Auth;
using Flare.Identity;

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
        var allowed = http.RequestServices.GetRequiredService<CorsAllowedOrigins>().Origins;
        if (IsAllowed(http.Request.Headers.Origin.ToString(), allowed))
        {
            return true;
        }

        http.Response.StatusCode = StatusCodes.Status403Forbidden;
        return false;
    }

    /// <summary><paramref name="allowedOrigins"/> is already normalized (<see cref="CorsAllowedOrigins"/>); the
    /// presented origin is normalized the same way, so a non-canonical header still compares correctly.</summary>
    internal static bool IsAllowed(string origin, IEnumerable<string> allowedOrigins) =>
        origin.Length == 0
        || BrowserOrigins.TryNormalize(origin, allowPath: true, out var normalized)
            && allowedOrigins.Contains(normalized, StringComparer.OrdinalIgnoreCase);
}
