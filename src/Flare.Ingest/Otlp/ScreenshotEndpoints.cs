using System.Text.RegularExpressions;
using Flare.Ingest.Auth;
using Flare.Ingest.Pipeline;

namespace Flare.Ingest.Otlp;

/// <summary>
/// <c>POST /v1/screenshots</c> - an error screenshot from a client app (ADR-0174). Not OTLP, but under
/// <c>/v1</c> so the ingest-key middleware, its service allow-list and the browser CORS policy cover it like
/// the signal endpoints. The body is the image; the rest is in the query string.
/// </summary>
public static partial class ScreenshotEndpoints
{
    /// <summary>Largest accepted image. The client caps itself lower; this is the server's backstop.</summary>
    public const int MaxImageBytes = 512 * 1024;

    private static readonly HashSet<string> ContentTypes = ["image/jpeg", "image/png", "image/webp"];

    public static IEndpointRouteBuilder MapScreenshotEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/v1/screenshots", HandleAsync);
        return endpoints;
    }

    public static async Task<IResult> HandleAsync(
        HttpContext http,
        IClickHouseScreenshotWriter writer,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var query = http.Request.Query;
        string sessionId = query["session_id"].ToString(), traceId = query["trace_id"].ToString(),
            spanId = query["span_id"].ToString(), service = query["service"].ToString();
        var contentType = (http.Request.ContentType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();

        if (!ContentTypes.Contains(contentType))
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        if (string.IsNullOrWhiteSpace(service) || !SessionIdPattern().IsMatch(sessionId)
            || !TraceIdPattern().IsMatch(traceId) || !SpanIdPattern().IsMatch(spanId))
            return Results.Problem("service, session_id, trace_id (32 hex) and span_id (16 hex) are required.", statusCode: StatusCodes.Status400BadRequest);
        if (IngestKeyScope.RejectsService(http, service))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (http.Request.ContentLength is > MaxImageBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await http.Request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxImageBytes)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            buffer.Write(chunk, 0, read);
        }
        if (buffer.Length == 0)
            return Results.Problem("The body is empty.", statusCode: StatusCodes.Status400BadRequest);

        var now = timeProvider.GetUtcNow();
        try
        {
            await writer.WriteAsync(
                new ScreenshotRecord(now, service, sessionId, traceId, spanId, contentType, buffer.ToArray()),
                now, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            loggerFactory.CreateLogger("Flare.Ingest.Otlp.ScreenshotEndpoints").LogWarning(ex, "Failed to store a screenshot for session {SessionId}", sessionId);
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        return Results.NoContent();
    }

    [GeneratedRegex("^[0-9a-fA-F]{32}$")]
    private static partial Regex TraceIdPattern();

    [GeneratedRegex("^[0-9a-fA-F]{16}$")]
    private static partial Regex SpanIdPattern();

    [GeneratedRegex("^[0-9a-zA-Z-]{8,64}$")]
    private static partial Regex SessionIdPattern();
}
