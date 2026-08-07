using System.Net.WebSockets;
using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.LiveTail;
using Flare.Api.Model;

namespace Flare.Api.Endpoints;

/// <summary>
/// The live-tail streaming endpoint: <c>GET /api/logs/tail</c>, upgraded to a WebSocket.
/// A connection sends no events until it sends a <see cref="LogTailClientMessageType.Subscribe"/>
/// message (mirrors <c>/api/logs/search</c>'s "send your query, then get results" shape
/// rather than an implicit firehose-on-connect) and can re-subscribe with a new filter or
/// <see cref="LogTailClientMessageType.Pause"/>/<see cref="LogTailClientMessageType.Resume"/>
/// at any point without reconnecting.
/// </summary>
public static class LogTailEndpoints
{
    public static IEndpointRouteBuilder MapLogTailEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/logs/tail", HandleTailAsync);
        return endpoints;
    }

    private static async Task HandleTailAsync(HttpContext http, LogTailBroadcaster broadcaster, ILogger<LogTailSubscription> logger)
    {
        if (!http.WebSockets.IsWebSocketRequest)
        {
            http.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await http.WebSockets.AcceptWebSocketAsync();
        var subscription = broadcaster.Subscribe();

        // Linked so either loop ending (client closed the socket, or the send loop hit a
        // fatal WebSocket error) tears down the other rather than leaking a half-open
        // connection.
        using var lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);

        try
        {
            var receiveTask = ReceiveControlMessagesAsync(socket, subscription, lifetimeCts.Token, logger);
            var sendTask = SendServerMessagesAsync(socket, subscription, lifetimeCts.Token);

            await Task.WhenAny(receiveTask, sendTask);
            await lifetimeCts.CancelAsync();
            await Task.WhenAll(receiveTask, sendTask);
        }
        finally
        {
            broadcaster.Unsubscribe(subscription);
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

    /// <summary>Parses inbound <see cref="LogTailClientMessage"/> frames and applies them to <paramref name="subscription"/>.</summary>
    private static async Task ReceiveControlMessagesAsync(
        WebSocket socket, LogTailSubscription subscription, CancellationToken cancellationToken, ILogger logger)
    {
        var buffer = new byte[8 * 1024];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                using var messageStream = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }
                    messageStream.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                messageStream.Position = 0;
                ApplyClientMessage(messageStream, subscription, logger);
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

    private static void ApplyClientMessage(Stream json, LogTailSubscription subscription, ILogger logger)
    {
        LogTailClientMessage? message;
        try
        {
            message = JsonSerializer.Deserialize(json, LogTailJsonContext.Default.LogTailClientMessage);
        }
        catch (JsonException ex)
        {
            logger.LogDebug(ex, "Malformed live-tail client message.");
            subscription.TryPublishError("Malformed message: expected {\"type\":\"subscribe\"|\"pause\"|\"resume\", ...}.");
            return;
        }

        if (message is null)
        {
            return;
        }

        switch (message.Type)
        {
            case LogTailClientMessageType.Subscribe:
                subscription.Filter = message.Filter ?? new LogFilter();
                subscription.Paused = false;
                break;
            case LogTailClientMessageType.Pause:
                subscription.Paused = true;
                break;
            case LogTailClientMessageType.Resume:
                subscription.Paused = false;
                break;
            default:
                subscription.TryPublishError($"Unsupported message type: {message.Type}.");
                break;
        }
    }

    /// <summary>Drains <paramref name="subscription"/>'s channel to the socket, interleaving <see cref="LogTailServerMessageType.Dropped"/> reports as they occur.</summary>
    private static async Task SendServerMessagesAsync(WebSocket socket, LogTailSubscription subscription, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in subscription.Reader.ReadAllAsync(cancellationToken))
            {
                await SendAsync(socket, message, cancellationToken);

                var dropped = subscription.TakeDroppedCount();
                if (dropped > 0)
                {
                    await SendAsync(
                        socket,
                        new LogTailServerMessage { Type = LogTailServerMessageType.Dropped, DroppedCount = dropped },
                        cancellationToken);
                }
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

    private static async Task SendAsync(WebSocket socket, LogTailServerMessage message, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, LogTailJsonContext.Default.LogTailServerMessage);
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
    }
}
