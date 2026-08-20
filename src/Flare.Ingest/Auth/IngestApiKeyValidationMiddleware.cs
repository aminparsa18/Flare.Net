using Microsoft.Extensions.Options;

namespace Flare.Ingest.Auth;

/// <summary>
/// Validates the <c>Authorization: Bearer &lt;key&gt;</c> header on every OTLP request -
/// HTTP (<c>POST /v1/logs|traces|metrics</c>) <em>and</em> gRPC alike. No separate
/// <c>Grpc.Core.Interceptors.Interceptor</c> is needed: gRPC-on-ASP.NET-Core requests
/// flow through this exact same middleware pipeline (gRPC metadata entries like
/// <c>authorization</c> are implemented as plain HTTP/2 headers on the wire, with no
/// special-casing needed to read them here), so one plain ASP.NET Core middleware
/// covers both transports.
/// </summary>
/// <remarks>
/// A failed check returns a bare HTTP 401 - not a proper gRPC trailers-only status.
/// Building a real <c>grpc-status</c> trailer from raw middleware, before the gRPC
/// pipeline itself would normally run, is more machinery than this warrants: every OTLP
/// exporter treats a failed export as failed/retry-or-drop regardless of the exact error
/// shape, so the distinction isn't operationally meaningful here.
/// <para>
/// No-ops entirely (every request passes through unauthenticated) unless
/// <see cref="IngestAuthOptions.IngestKeyRequired"/> is true, and even then only for OTLP
/// paths - <c>/health</c>/<c>/alive</c> (Docker/Aspire health checks) must stay reachable
/// unconditionally.
/// </para>
/// </remarks>
public sealed class IngestApiKeyValidationMiddleware(RequestDelegate next, IOptions<IngestAuthOptions> options, IngestApiKeyCache cache)
{
    private const string BearerPrefix = "Bearer ";

    public async Task InvokeAsync(HttpContext context)
    {
        if (!options.Value.IngestKeyRequired || !IsOtlpRequest(context.Request))
        {
            await next(context);
            return;
        }

        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase) || !cache.IsValid(header[BearerPrefix.Length..]))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }

    /// <summary>Positive allow-list (OTLP HTTP paths, or gRPC by content-type) rather
    /// than excluding known non-OTLP paths like <c>/health</c> - stays correct even if
    /// Flare.ServiceDefaults' <c>MapDefaultEndpoints</c> adds more diagnostic endpoints
    /// later, since anything not explicitly OTLP is left unauthenticated by default here
    /// rather than accidentally gated.</summary>
    private static bool IsOtlpRequest(HttpRequest request) =>
        request.Path.StartsWithSegments("/v1") ||
        (request.ContentType?.StartsWith("application/grpc", StringComparison.OrdinalIgnoreCase) ?? false);
}
