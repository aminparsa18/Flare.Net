using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Flare.Cli.Internal;

internal abstract record TailMessage
{
    public sealed record Event(
        DateTimeOffset Timestamp, string ServiceName, byte SeverityNumber, string Body, string TraceId, string EventName) : TailMessage;

    public sealed record Dropped(int Count) : TailMessage;

    public sealed record Error(string Message) : TailMessage;
}

internal sealed class TailFilter
{
    public IReadOnlyList<string> Services { get; init; } = [];

    public IReadOnlyList<byte> SeverityNumbers { get; init; } = [];

    public string? TraceId { get; init; }

    public string? Search { get; init; }
}

/// <summary>
/// Client for Flare.Api's <c>GET /api/logs/tail</c> live-tail WebSocket protocol (see
/// <c>src/Flare.Api/README.md</c>'s "Live-tail streaming" section and
/// <c>src/Flare.Api/Endpoints/LogTailEndpoints.cs</c> for the server side this talks
/// to). Deliberately hand-rolled against the documented wire contract rather than taking
/// a project/package reference on Flare.Api - that project pulls in the whole ASP.NET
/// Core/ClickHouse/Redis/auth dependency graph, which would balloon this tool's install
/// size for a client that only ever needs a handful of JSON fields over a socket. Same
/// "these are opaque processes talking over a documented protocol, not shared
/// assemblies" boundary <see cref="ComposeRunner"/> already draws around Flare's Docker
/// images.
/// </summary>
internal sealed class LogTailClient : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();

    public Task ConnectAsync(Uri uri, CancellationToken ct) => _socket.ConnectAsync(uri, ct);

    public Task SubscribeAsync(TailFilter filter, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            type = "subscribe",
            filter = new
            {
                services = filter.Services.Count > 0 ? filter.Services : null,
                severityNumbers = filter.SeverityNumbers.Count > 0 ? filter.SeverityNumbers : null,
                traceId = filter.TraceId,
                search = filter.Search,
            },
        });

        var bytes = Encoding.UTF8.GetBytes(payload);
        return _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    public async IAsyncEnumerable<TailMessage> ReadAllAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];

        while (_socket.State == WebSocketState.Open)
        {
            using var stream = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    yield break;
                }

                stream.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            stream.Position = 0;
            var parsed = TryParse(stream);
            if (parsed is not null)
            {
                yield return parsed;
            }
        }
    }

    // Defensive: a malformed/unexpected server message shouldn't kill the whole tail
    // session, just get silently skipped - the protocol is meant to be forward-
    // compatible (see LogTailServerMessageType's own "connection stays open" contract
    // for its `error` case).
    private static TailMessage? TryParse(Stream json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();

            // Case-insensitive: Flare.Api's LogTailJsonContext sets a camelCase
            // PropertyNamingPolicy, but that only rewrites property *names* - enum
            // *values* (this "type" discriminator) go through UseStringEnumConverter
            // alone, which serializes the raw C# member name verbatim ("Event", not
            // "event"). Confirmed against a live server - Flare.Api/README.md's own
            // live-tail example ({"type":"event",...}) doesn't match the actual wire
            // casing, which is what silently broke this parser on the first pass.
            return type?.ToLowerInvariant() switch
            {
                "event" => ParseEvent(root.GetProperty("event")),
                "dropped" => new TailMessage.Dropped(root.GetProperty("droppedCount").GetInt32()),
                "error" => new TailMessage.Error(root.GetProperty("error").GetString() ?? "unknown error"),
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static TailMessage.Event ParseEvent(JsonElement evt) => new(
        Timestamp: evt.GetProperty("timestamp").GetDateTimeOffset(),
        ServiceName: evt.GetProperty("serviceName").GetString() ?? string.Empty,
        SeverityNumber: evt.GetProperty("severityNumber").GetByte(),
        Body: evt.GetProperty("body").GetString() ?? string.Empty,
        TraceId: evt.TryGetProperty("traceId", out var traceId) ? traceId.GetString() ?? string.Empty : string.Empty,
        EventName: evt.TryGetProperty("eventName", out var eventName) ? eventName.GetString() ?? string.Empty : string.Empty);

    public async Task CloseAsync()
    {
        if (_socket.State == WebSocketState.Open)
        {
            try
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, statusDescription: null, CancellationToken.None).ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
                // Server may already be gone - best-effort close, not worth surfacing.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        _socket.Dispose();
    }
}
