using Flare.Api.LiveTail;

namespace Flare.Api.Endpoints;

/// <summary>
/// Caps how long a WebSocket stays open. Authorization (session, PAT, project scope) is only
/// evaluated on the upgrade request, so a bounded lifetime forces the client to reconnect and
/// be re-authorized, which is what makes revocation, user disable, idle timeout, PAT expiry and
/// project-membership changes take effect on a socket that is already open.
/// </summary>
internal static class WebSocketLifetime
{
    /// <summary>A token cancelled when the request aborts or <see cref="LiveTailOptions.MaxSocketLifetime"/> elapses. A non-positive lifetime disables the cap.</summary>
    public static CancellationTokenSource Link(HttpContext http, LiveTailOptions options)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
        if (options.MaxSocketLifetime > TimeSpan.Zero)
        {
            cts.CancelAfter(options.MaxSocketLifetime);
        }
        return cts;
    }
}
