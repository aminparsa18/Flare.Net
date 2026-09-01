using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MemoryPack;

namespace Flare.Api.Json;

/// <summary>
/// Content negotiation between JSON (the unchanged default) and MemoryPack (opt-in, via
/// <c>Accept</c>/<c>Content-Type: application/x-memorypack</c>) for Flare.Api's
/// request/response Minimal API endpoints - Phase 1 of
/// docs-internal/investigations/memorypack-serialization-migration-scope.md.
/// </summary>
/// <remarks>
/// Additive, not a replacement: a caller that never sends the MemoryPack content type
/// gets exactly the JSON wire format Flare.Api always returned, via the same
/// feature-scoped <see cref="JsonTypeInfo{T}"/> source-gen contexts every endpoint
/// already used (AOT-safe, unchanged). Minimal APIs have no MVC-style
/// <c>IInputFormatter</c>/<c>IOutputFormatter</c> pipeline to hook globally (see the
/// investigation's Finding 1), so every endpoint calls <see cref="ReadAsync{T}"/> /
/// <see cref="Write{T}"/> explicitly, the same way each already explicitly called
/// <see cref="JsonSerializer"/> / <see cref="Results.Json{TValue}"/>.
/// <para/>
/// Deliberately not used by the three WebSocket-upgrade endpoints
/// (<c>LogTailEndpoints</c>, <c>HostStatsEndpoints</c>'s <c>/watch</c>,
/// <c>ResourceGraphEndpoints</c>'s <c>/watch</c>) - a persistent connection has no
/// per-message <c>Accept</c>-header renegotiation the way a request/response call does;
/// see the investigation's Finding 5.
/// </remarks>
public static class ApiSerialization
{
    public const string MemoryPackContentType = "application/x-memorypack";

    /// <summary>True if the caller's <c>Accept</c> header asks for MemoryPack over JSON.</summary>
    public static bool WantsMemoryPack(HttpRequest request)
    {
        var accept = request.Headers.Accept;
        for (var i = 0; i < accept.Count; i++)
        {
            if (accept[i] is { } value && value.Contains(MemoryPackContentType, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reads a request body as MemoryPack (if <c>Content-Type</c> says so) or JSON
    /// (default - the same <paramref name="jsonTypeInfo"/>-driven path every endpoint
    /// already used).
    /// </summary>
    public static async ValueTask<T?> ReadAsync<T>(HttpContext http, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken)
    {
        var contentType = http.Request.ContentType ?? string.Empty;
        if (!contentType.Contains(MemoryPackContentType, StringComparison.OrdinalIgnoreCase))
        {
            return await JsonSerializer.DeserializeAsync(http.Request.Body, jsonTypeInfo, cancellationToken);
        }

        using var buffer = new MemoryStream();
        await http.Request.Body.CopyToAsync(buffer, cancellationToken);
        return MemoryPackSerializer.Deserialize<T>(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
    }

    /// <summary>
    /// Writes a response as MemoryPack (if the caller's <c>Accept</c> header asked for
    /// it) or JSON (default - the same <paramref name="jsonTypeInfo"/>-driven
    /// <see cref="Results.Json{TValue}(TValue,JsonTypeInfo{TValue},string?,int?)"/> path
    /// every endpoint already used).
    /// </summary>
    public static IResult Write<T>(HttpContext http, T value, JsonTypeInfo<T> jsonTypeInfo, int? statusCode = null)
    {
        if (!WantsMemoryPack(http.Request))
        {
            return Results.Json(value, jsonTypeInfo, statusCode: statusCode);
        }

        if (statusCode.HasValue)
        {
            http.Response.StatusCode = statusCode.Value;
        }

        return Results.Bytes(MemoryPackSerializer.Serialize(value), MemoryPackContentType);
    }
}
