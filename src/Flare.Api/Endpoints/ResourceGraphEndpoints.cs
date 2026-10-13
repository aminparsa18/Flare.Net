using System.Net.WebSockets;
using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.LiveTail;
using Flare.Api.ResourceGraph;
using Microsoft.Extensions.Options;

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
/// protocol to receive here, so the watch connection is push-only. The producer overlay is
/// narrowed to the caller's projects (<see cref="ResourceGraphScopeFilter"/>, ADR-0123): per
/// request for the snapshot, and at subscribe time for the watch stream.
/// </summary>
public static class ResourceGraphEndpoints
{
    public static IEndpointRouteBuilder MapResourceGraphEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/resources/snapshot", HandleSnapshot);
        endpoints.MapGet("/api/resources/watch", HandleWatchAsync);
        return endpoints;
    }

    internal static IResult HandleSnapshot(HttpContext http, ResourceGraphSourceRegistry registry) =>
        ApiSerialization.Write(http, ResourceGraphScopeFilter.Apply(registry.CurrentSnapshot, Query.ServiceScope.Current), ResourceGraphJsonContext.Default.ResourceGraphSnapshot);

    private static async Task HandleWatchAsync(HttpContext http, ResourceGraphSourceRegistry registry, IOptions<LiveTailOptions> options, PushSocketLimiter limiter)
    {
        if (!http.WebSockets.IsWebSocketRequest)
        {
            http.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (!WebSocketOriginGuard.IsAllowed(http))
        {
            return;
        }

        // Claimed before the upgrade so a refused caller gets a plain 429 and never holds a socket.
        using var lease = limiter.TryAcquire("resource-graph", PushSocketLimiter.UserKey(http), options.Value);
        if (lease is null)
        {
            http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return;
        }

        using var socket = await http.WebSockets.AcceptWebSocketAsync();
        var subscription = registry.Subscribe();

        using var lifetimeCts = WebSocketLifetime.Link(http, options.Value);

        try
        {
            // Push-only: no receive loop needed since there's no client->server message
            // this connection ever expects (contrast LogTailEndpoints, which needs one for
            // Subscribe/Pause/Resume). A minimal "wait for the client to close" task still
            // has to run alongside the send loop, though - otherwise this handler would
            // never observe the client disconnecting and would leak the WebSocket/subscription
            // until the whole HTTP request pipeline eventually tears it down.
            var closeTask = PushSocketReceive.WaitForCloseAsync(socket, options.Value, lifetimeCts.Token);
            var sendTask = SendSnapshotsAsync(socket, subscription, lifetimeCts.Token);

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
