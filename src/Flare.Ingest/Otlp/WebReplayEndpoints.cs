using System.Text.Json;
using System.Text.RegularExpressions;
using Flare.Ingest.Auth;
using Flare.Ingest.Pipeline;

namespace Flare.Ingest.Otlp;

/// <summary>
/// <c>POST /v1/replay</c> - a batch of rrweb events from a browser tab (ADR-0264). Not OTLP, but under <c>/v1</c> so the
/// ingest-key middleware, its service allow-list and the browser CORS policy cover it like the signal endpoints. The body
/// is a JSON array of rrweb events (gzip is undone by the request decompression middleware); <c>service</c> and
/// <c>session_id</c> are in the query string. Every element must be an object with a numeric <c>type</c> and
/// <c>timestamp</c>; the content is otherwise opaque and is never interpreted on the server.
/// </summary>
public static partial class WebReplayEndpoints
{
    /// <summary>Largest accepted batch, after decompression. A full page snapshot is the biggest event.</summary>
    public const int MaxPayloadBytes = 10 * 1024 * 1024;

    /// <summary>How far a browser's event times may sit from the server clock before the stored times are clamped.</summary>
    public static readonly TimeSpan MaxPast = TimeSpan.FromHours(24);
    public static readonly TimeSpan MaxFuture = TimeSpan.FromMinutes(5);

    public static IEndpointRouteBuilder MapWebReplayEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/v1/replay", HandleAsync);
        return endpoints;
    }

    public static async Task<IResult> HandleAsync(
        HttpContext http,
        IClickHouseWebReplayWriter writer,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var query = http.Request.Query;
        string service = query["service"].ToString(), sessionId = query["session_id"].ToString();
        var contentType = (http.Request.ContentType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();

        if (contentType != "application/json")
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        if (string.IsNullOrWhiteSpace(service) || !SessionIdPattern().IsMatch(sessionId))
            return Results.Problem("service and session_id are required.", statusCode: StatusCodes.Status400BadRequest);
        if (IngestKeyScope.RejectsService(http, service))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (http.Request.ContentLength is > MaxPayloadBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await http.Request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxPayloadBytes)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            buffer.Write(chunk, 0, read);
        }

        var payload = buffer.ToArray();
        var summary = Inspect(payload, out var error);
        if (summary is null)
            return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);

        // The key's byte caps (ADR-0051) count a chunk's bytes like an OTLP body's, and so do its replay caps (ADR-0268). Its event caps do
        // not: an rrweb event is not a telemetry record, and one page snapshot is a single event hundreds of kilobytes long.
        IngestKeyUsageFeature.AddReplay(http, payload.Length);

        var now = timeProvider.GetUtcNow();
        var start = Clamp(summary.Value.FirstUnixMs, now);
        var end = Clamp(summary.Value.LastUnixMs, now);
        try
        {
            await writer.WriteAsync(
                new WebReplayRecord(start, end, service, sessionId, Guid.NewGuid(), summary.Value.EventCount, payload), now, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            loggerFactory.CreateLogger("Flare.Ingest.Otlp.WebReplayEndpoints").LogWarning(ex, "Failed to store a replay chunk for session {SessionId}", sessionId);
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        return Results.NoContent();
    }

    /// <summary>The event count and the earliest and latest event time (Unix ms) in a batch.</summary>
    public readonly record struct ChunkSummary(int EventCount, long FirstUnixMs, long LastUnixMs);

    /// <summary>
    /// Walks the JSON once, without building a document: the root must be a non-empty array whose every element is an object with
    /// a numeric <c>type</c> and a numeric <c>timestamp</c> at its own level (the nested <c>data</c> is not looked at). Returns null
    /// and an explanation when it is not. The depth limit is raised because a serialised DOM nests two levels per element.
    /// </summary>
    public static ChunkSummary? Inspect(ReadOnlySpan<byte> json, out string error)
    {
        error = "The body must be a JSON array of rrweb events.";
        var reader = new Utf8JsonReader(json, new JsonReaderOptions { MaxDepth = 4096 });
        int count = 0;
        long first = long.MaxValue, last = long.MinValue;
        bool sawType = false, sawTimestamp = false;
        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray) return null;
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.EndArray when reader.CurrentDepth == 0:
                        if (reader.Read()) return null; // trailing content
                        if (count == 0)
                        {
                            error = "The batch has no events.";
                            return null;
                        }
                        return new ChunkSummary(count, first, last);
                    case JsonTokenType.StartObject when reader.CurrentDepth == 1:
                        sawType = sawTimestamp = false;
                        break;
                    case JsonTokenType.EndObject when reader.CurrentDepth == 1:
                        if (!sawType || !sawTimestamp)
                        {
                            error = "Every event needs a numeric type and timestamp.";
                            return null;
                        }
                        count++;
                        break;
                    case JsonTokenType.PropertyName when reader.CurrentDepth == 2:
                        var isType = reader.ValueTextEquals("type"u8);
                        var isTimestamp = !isType && reader.ValueTextEquals("timestamp"u8);
                        if (!isType && !isTimestamp) break;
                        reader.Read();
                        if (reader.TokenType != JsonTokenType.Number || !reader.TryGetDouble(out var number) || !double.IsFinite(number))
                        {
                            error = "Every event needs a numeric type and timestamp.";
                            return null;
                        }
                        var n = (long)number;
                        if (isType) sawType = true;
                        else
                        {
                            sawTimestamp = true;
                            if (n < first) first = n;
                            if (n > last) last = n;
                        }
                        break;
                    case JsonTokenType.StartObject or JsonTokenType.StartArray or JsonTokenType.String or JsonTokenType.Number
                        or JsonTokenType.True or JsonTokenType.False or JsonTokenType.Null when reader.CurrentDepth == 1:
                        // An element that is not an object.
                        error = "Every element must be an event object.";
                        return null;
                }
            }
        }
        catch (JsonException)
        {
            error = "The body is not valid JSON.";
            return null;
        }
        return null; // truncated: the array never closed
    }

    private static DateTimeOffset Clamp(long unixMs, DateTimeOffset now)
    {
        var floor = now - MaxPast;
        var ceiling = now + MaxFuture;
        var at = unixMs is > 0 and < 32_503_680_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(unixMs) : now;
        return at < floor ? floor : at > ceiling ? ceiling : at;
    }

    [GeneratedRegex("^[0-9a-zA-Z-]{8,64}$")]
    private static partial Regex SessionIdPattern();
}
