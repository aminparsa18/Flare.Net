using System.Text.Json;
using System.Text.RegularExpressions;
using Flare.Ingest.Auth;
using Flare.Ingest.Pipeline;

namespace Flare.Ingest.Otlp;

/// <summary>
/// <c>POST /v1/feedback</c> - a "report a problem" message from a client app (ADR-0193). Not OTLP, but under <c>/v1</c> so the
/// ingest-key middleware, its service allow-list and the browser CORS policy cover it like the signal endpoints. The body is
/// JSON: <c>service</c>, <c>session_id</c> and <c>message</c> are required; <c>user_id</c>, <c>name</c>, <c>email</c> and
/// <c>app_version</c> are optional.
/// </summary>
public static partial class FeedbackEndpoints
{
    public const int MaxBodyBytes = 16 * 1024;
    public const int MaxMessageChars = 4000;
    private const int MaxFieldChars = 256;

    public static IEndpointRouteBuilder MapFeedbackEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/v1/feedback", HandleAsync);
        return endpoints;
    }

    public static async Task<IResult> HandleAsync(
        HttpContext http,
        IClickHouseFeedbackWriter writer,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var contentType = (http.Request.ContentType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();
        if (contentType != "application/json")
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        if (http.Request.ContentLength is > MaxBodyBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        using var buffer = new MemoryStream();
        var chunk = new byte[4 * 1024];
        int read;
        while ((read = await http.Request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            buffer.Write(chunk, 0, read);
        }

        string? service, sessionId, message;
        string userId, name, email, appVersion;
        try
        {
            using var doc = JsonDocument.Parse(buffer.ToArray());
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return Results.Problem("The body must be a JSON object.", statusCode: StatusCodes.Status400BadRequest);
            var root = doc.RootElement;
            service = Text(root, "service");
            sessionId = Text(root, "session_id");
            message = Text(root, "message");
            userId = Text(root, "user_id") ?? string.Empty;
            name = Text(root, "name") ?? string.Empty;
            email = Text(root, "email") ?? string.Empty;
            appVersion = Text(root, "app_version") ?? string.Empty;
        }
        catch (JsonException)
        {
            return Results.Problem("The body is not valid JSON.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(service) || sessionId is null || !SessionIdPattern().IsMatch(sessionId) || string.IsNullOrWhiteSpace(message))
            return Results.Problem("service, session_id and message are required.", statusCode: StatusCodes.Status400BadRequest);
        if (IngestKeyScope.RejectsService(http, service))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        message = message.Trim();
        if (message.Length > MaxMessageChars) message = message[..MaxMessageChars];

        var now = timeProvider.GetUtcNow();
        try
        {
            await writer.WriteAsync(
                new FeedbackRecord(now, Guid.NewGuid(), service, sessionId, Clip(userId), Clip(name), Clip(email), Clip(appVersion), message),
                now, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            loggerFactory.CreateLogger("Flare.Ingest.Otlp.FeedbackEndpoints").LogWarning(ex, "Failed to store feedback for session {SessionId}", sessionId);
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        return Results.NoContent();
    }

    private static string? Text(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Clip(string value) => value.Length > MaxFieldChars ? value[..MaxFieldChars] : value;

    [GeneratedRegex("^[0-9a-zA-Z-]{8,64}$")]
    private static partial Regex SessionIdPattern();
}
