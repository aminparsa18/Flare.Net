using System.Net.WebSockets;
using System.Security.Claims;
using Flare.Api.LiveTail;

namespace Flare.Api.Endpoints;

/// <summary>
/// Connection caps and inbound-frame bound for the push-only WebSockets (host stats, resource graph),
/// which send no control messages. Each endpoint gets its own bucket, capped by
/// <see cref="LiveTailOptions.MaxConnectionsPerUser"/> and <see cref="LiveTailOptions.MaxConnections"/>,
/// so a flood on one cannot starve live tail or the other endpoint. Anonymous callers share one bucket.
/// </summary>
public sealed class PushSocketLimiter
{
    private readonly object _lock = new();
    private readonly Dictionary<string, Dictionary<string, int>> _perKind = new(StringComparer.Ordinal);

    /// <summary>A lease to dispose when the socket ends, or null when the per-user or total cap for <paramref name="kind"/> is reached.</summary>
    public IDisposable? TryAcquire(string kind, string userKey, LiveTailOptions options)
    {
        lock (_lock)
        {
            if (!_perKind.TryGetValue(kind, out var users))
            {
                users = new Dictionary<string, int>(StringComparer.Ordinal);
                _perKind[kind] = users;
            }

            users.TryGetValue(userKey, out var forUser);
            if (users.Values.Sum() >= options.MaxConnections || forUser >= options.MaxConnectionsPerUser)
            {
                return null;
            }

            users[userKey] = forUser + 1;
            return new Lease(() => Release(kind, userKey));
        }
    }

    internal static string UserKey(HttpContext http) => http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";

    private void Release(string kind, string userKey)
    {
        lock (_lock)
        {
            var users = _perKind[kind];
            if (users[userKey] <= 1)
            {
                users.Remove(userKey);
            }
            else
            {
                users[userKey]--;
            }
        }
    }

    private sealed class Lease(Action release) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                release();
            }
        }
    }
}

internal static class PushSocketReceive
{
    /// <summary>
    /// Drains (and discards) inbound frames purely to detect a client-initiated close. A message larger than
    /// <see cref="LiveTailOptions.MaxClientMessageBytes"/> (summed across fragments) closes the socket with 1009.
    /// </summary>
    public static async Task WaitForCloseAsync(WebSocket socket, LiveTailOptions options, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                long messageBytes = 0;
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    messageBytes += result.Count;
                    if (messageBytes > options.MaxClientMessageBytes)
                    {
                        await socket.CloseOutputAsync(
                            WebSocketCloseStatus.MessageTooBig, "Message exceeds the size limit.", CancellationToken.None);
                        return;
                    }
                } while (!result.EndOfMessage);
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
}
