using System.Text;
using System.Text.RegularExpressions;
using Flare.Ingest.Pipeline;

namespace Flare.Ingest.Otlp;

/// <summary>
/// <c>GET|POST /v1/checkins/{slug}[/{start|ok|fail}]</c> - a scheduled job telling Flare it started, finished or failed
/// (ADR-0260). Not OTLP, but under <c>/v1</c> so the ingest-key middleware covers it like the signal endpoints. The status
/// is the last path segment and defaults to <c>ok</c>; a POST body, if any, is kept as a short message. A ping for a slug
/// with no monitor is stored and monitors nothing. Both verbs are accepted so a plain <c>curl</c> works.
/// </summary>
public static partial class CheckInEndpoints
{
    public const int MaxBodyBytes = 4 * 1024;
    public const int MaxMessageChars = 1000;

    public static IEndpointRouteBuilder MapCheckInEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMethods("/v1/checkins/{slug}/{status?}", ["GET", "POST"], HandleAsync);
        return endpoints;
    }

    /// <summary>The status a path segment names (<c>ok</c> when absent), or null when it is not one of <c>start</c>, <c>ok</c>, <c>fail</c>. Case-insensitive.</summary>
    public static string? ParseStatus(string? segment) => segment?.ToLowerInvariant() switch
    {
        null or "" => "ok",
        "start" => "start",
        "ok" => "ok",
        "fail" => "fail",
        _ => null,
    };

    public static bool IsValidSlug(string slug) => SlugPattern().IsMatch(slug);

    /// <summary>Decodes a ping body as UTF-8 text, trimmed and clipped to <see cref="MaxMessageChars"/>. Invalid bytes become U+FFFD.</summary>
    public static string ToMessage(ReadOnlySpan<byte> body)
    {
        var text = Encoding.UTF8.GetString(body).Trim();
        return text.Length > MaxMessageChars ? text[..MaxMessageChars] : text;
    }

    public static async Task<IResult> HandleAsync(
        string slug,
        string? status,
        HttpContext http,
        IClickHouseCheckInWriter writer,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!IsValidSlug(slug))
            return Results.Problem("The slug must be 1-64 lower-case letters, digits, '-' or '_'.", statusCode: StatusCodes.Status400BadRequest);
        if (ParseStatus(status) is not { } parsed)
            return Results.Problem("The status must be start, ok or fail.", statusCode: StatusCodes.Status400BadRequest);
        if (http.Request.ContentLength is > MaxBodyBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        using var buffer = new MemoryStream();
        var chunk = new byte[1024];
        int read;
        while ((read = await http.Request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            buffer.Write(chunk, 0, read);
        }

        try
        {
            await writer.WriteAsync(new CheckInRecord(timeProvider.GetUtcNow(), slug, parsed, ToMessage(buffer.ToArray())), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            loggerFactory.CreateLogger("Flare.Ingest.Otlp.CheckInEndpoints").LogWarning(ex, "Failed to store a {Status} check-in for {Slug}", parsed, slug);
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        return Results.NoContent();
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,63}$")]
    private static partial Regex SlugPattern();
}
