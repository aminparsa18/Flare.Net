using System.Net.WebSockets;
using System.Text.Json;
using Flare.Api.HostStats;
using Flare.Api.Json;

namespace Flare.Api.Endpoints;

/// <summary>
/// The Resources page's Host overview panel endpoint trio:
/// <c>GET /api/resources/host/snapshot</c> (REST, instant read of
/// <see cref="HostStatsPoller.CurrentSnapshot"/>), <c>GET /api/resources/host/watch</c>
/// (WebSocket, pushes a fresh <see cref="Model.HostStatsSnapshot"/> every time the poller
/// publishes one), and <c>GET /api/resources/host/history</c> (REST, the Resource trends
/// chart's backfill - see <see cref="HostStatsPoller.GetHistory"/>). The snapshot/watch
/// pair is the same REST-snapshot/WebSocket-stream pairing as <c>ResourceGraphEndpoints</c>,
/// copied near-verbatim - push-only, no client-sent control protocol to receive. History
/// has no WebSocket counterpart - the frontend fetches it once on connect, then derives
/// each subsequent history point itself from the watch stream it's already receiving (see
/// <c>$lib/resources/host-stats.svelte.ts</c>), so there's no need for the server to push
/// the whole rolling window on every tick.
/// </summary>
public static class HostStatsEndpoints
{
    public static IEndpointRouteBuilder MapHostStatsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/resources/host/snapshot", HandleSnapshot);
        endpoints.MapGet("/api/resources/host/watch", HandleWatchAsync);
        endpoints.MapGet("/api/resources/host/history", HandleHistory);
        return endpoints;
    }

    private static IResult HandleSnapshot(HttpContext http, HostStatsPoller poller) =>
        ApiSerialization.Write(http, poller.CurrentSnapshot, HostStatsJsonContext.Default.HostStatsSnapshot);

    private static IResult HandleHistory(HttpContext http, HostStatsPoller poller) =>
        ApiSerialization.Write(http, poller.GetHistory(), HostStatsJsonContext.Default.IReadOnlyListHostStatsHistoryPoint);

    private static async Task HandleWatchAsync(HttpContext http, HostStatsPoller poller)
    {
        if (!http.WebSockets.IsWebSocketRequest)
        {
            http.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await http.WebSockets.AcceptWebSocketAsync();
        var subscription = poller.Subscribe();

        try
        {
            var closeTask = WaitForCloseAsync(socket, http.RequestAborted);
            var sendTask = SendSnapshotsAsync(socket, subscription, http.RequestAborted);

            await Task.WhenAny(closeTask, sendTask);
        }
        finally
        {
            poller.Unsubscribe(subscription);
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, statusDescription: null, CancellationToken.None);
                }
                catch (WebSocketException)
                {
                    // Client already gone - nothing to clean up.
                }
            }
        }
    }

    /// <summary>Drains (and discards) inbound frames purely to detect a client-initiated close - this endpoint has no control protocol to act on.</summary>
    private static async Task WaitForCloseAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Connection ending - driven by the send loop or client disconnect.
        }
        catch (WebSocketException)
        {
            // Client disconnected uncleanly.
        }
    }

    private static async Task SendSnapshotsAsync(WebSocket socket, HostStatsSubscription subscription, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var snapshot in subscription.Reader.ReadAllAsync(cancellationToken))
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, HostStatsJsonContext.Default.HostStatsSnapshot);
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Connection ending.
        }
        catch (WebSocketException)
        {
            // Client disconnected uncleanly.
        }
    }
}
