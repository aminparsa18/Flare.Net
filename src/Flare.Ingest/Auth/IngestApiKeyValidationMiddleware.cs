using Flare.Ingest.Stats;
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
/// <para>
/// Also the enforcement point for per-key ingestion limits (ADR-0051): once a SQLite-backed
/// key is resolved, a key with enforced limits gets its current Redis usage checked
/// (rejected via <see cref="IngestKeyLimitRejection"/> if a cap is reached), and every such
/// key gets an <see cref="IngestKeyUsageFeature"/> whose totals are written back to Redis
/// after the receiver runs. The static key has neither. A Redis failure on the check fails
/// open - Redis being down already fails the export at the sink, so there's nothing gained
/// by also rejecting here, and a limit is a fairness guard, not a security boundary.
/// </para>
/// </remarks>
public sealed class IngestApiKeyValidationMiddleware(
    RequestDelegate next,
    IOptions<IngestAuthOptions> options,
    IngestApiKeyCache cache,
    IIngestKeyUsageStore usageStore,
    IIngestionStatsTracker stats,
    TimeProvider timeProvider,
    ILogger<IngestApiKeyValidationMiddleware> logger)
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
        if (!header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase) || !cache.TryGetKey(header[BearerPrefix.Length..], out var key))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (key.KeyId is not { } keyId)
        {
            await next(context);
            return;
        }

        if (key.Limits.IsEnforced && await CheckLimitAsync(keyId, key, context) is { } retryAfter)
        {
            await IngestKeyLimitRejection.WriteAsync(context, retryAfter);
            if (IngestKeyLimitRejection.SignalOf(context.Request) is { } signal)
            {
                var protocol = IngestKeyLimitRejection.IsGrpc(context.Request) ? IngestionProtocol.Grpc : IngestionProtocol.Http;
                await stats.RecordRejectedAsync(signal, protocol, $"ingest-key-limit:{key.Name}", context.RequestAborted);
            }
            return;
        }

        var usage = new IngestKeyUsageFeature(keyId);
        context.Features.Set(usage);

        await next(context);

        if (usage.Events > 0 || usage.Bytes > 0)
        {
            try
            {
                await usageStore.RecordAsync(keyId, usage.Events, usage.Bytes, timeProvider.GetUtcNow(), context.RequestAborted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The export itself already succeeded and its response is written - losing
                // one request's usage count is better than turning that into a failure the
                // exporter would retry (and so double-ingest).
                logger.LogWarning(ex, "Failed to record usage for ingest key {KeyName}", key.Name);
            }
        }
    }

    private async Task<TimeSpan?> CheckLimitAsync(Guid keyId, IngestKeyCacheEntry key, HttpContext context)
    {
        var now = timeProvider.GetUtcNow();
        try
        {
            var usage = await usageStore.GetAsync(keyId, now, context.RequestAborted);
            return IngestKeyLimitEvaluator.Evaluate(key.Limits, usage, now);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to read usage for ingest key {KeyName}; admitting the request without a limit check", key.Name);
            return null;
        }
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
