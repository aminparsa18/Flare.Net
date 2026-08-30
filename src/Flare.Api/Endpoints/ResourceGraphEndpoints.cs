using System.Net.WebSockets;
using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.ResourceGraph;

namespace Flare.Api.Endpoints;

/// <summary>
/// The Resources page's endpoint pair: <c>GET /api/resources/snapshot</c> (REST, instant
/// read of <see cref="ResourceGraphSourceRegistry.CurrentSnapshot"/>) and
/// <c>GET /api/resources/watch</c> (WebSocket, pushes a fresh
/// <see cref="Model.ResourceGraphSnapshot"/> every time either topology provider
/// - <c>DockerResources.DockerContainerPoller</c> or
/// <c>KubernetesResources.KubernetesResourcePoller</c> - publishes one; see
/// <see cref="ResourceGraphSourceRegistry"/> for how the two are reconciled into this one
/// stream). Same REST-snapshot/WebSocket-stream pairing as
/// <c>LogsEndpoints</c>/<c>LogTailEndpoints</c>, simplified: there's no client-sent control
/// protocol to receive here (the graph isn't filtered), so the watch connection is
/// push-only.
/// </summary>
public static class ResourceGraphEndpoints
{
    public static IEndpointRouteBuilder MapResourceGraphEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/resources/snapshot", HandleSnapshot);
        endpoints.MapGet("/api/resources/watch", HandleWatchAsync);
        return endpoints;
    }

    private static IResult HandleSnapshot(ResourceGraphSourceRegistry registry) =>
        Results.Json(registry.CurrentSnapshot, ResourceGraphJsonContext.Default.ResourceGraphSnapshot);

    private static async Task HandleWatchAsync(HttpContext http, ResourceGraphSourceRegistry registry)
    {
        if (!http.WebSockets.IsWebSocketRequest)
        {
            http.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await http.WebSockets.AcceptWebSocketAsync();
        var subscription = registry.Subscribe();

        try
        {
            // Push-only: no receive loop needed since there's no client->server message
            // this connection ever expects (contrast LogTailEndpoints, which needs one for
            // Subscribe/Pause/Resume). A minimal "wait for the client to close" task still
            // has to run alongside the send loop, though - otherwise this handler would
            // never observe the client disconnecting and would leak the WebSocket/subscription
            // until the whole HTTP request pipeline eventually tears it down.
            var closeTask = WaitForCloseAsync(socket, http.RequestAborted);
            var sendTask = SendSnapshotsAsync(socket, subscription, http.RequestAborted);

            await Task.WhenAny(closeTask, sendTask);
        }
        finally
        {
            registry.Unsubscribe(subscription);
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

    private static async Task SendSnapshotsAsync(WebSocket socket, ResourceGraphSubscription subscription, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var snapshot in subscription.Reader.ReadAllAsync(cancellationToken))
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, ResourceGraphJsonContext.Default.ResourceGraphSnapshot);
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
