namespace Flare.Ingest.Auth;

/// <summary>
/// Per-request accumulator for what an identified ingest key actually got accepted
/// (ADR-0051). <see cref="IngestApiKeyValidationMiddleware"/> sets one on
/// <c>HttpContext.Features</c> after resolving a SQLite-backed key, the OTLP receivers
/// report their accepted record/byte counts into it via <see cref="Add"/>, and the
/// middleware writes the total to Redis once the handler returns - so the receivers stay
/// free of any Redis or ingest-key awareness beyond this one call.
/// </summary>
/// <remarks>
/// Only the record count is unknown before the body is parsed, which is why usage is
/// recorded after the handler rather than up front in the middleware alongside the limit
/// check. gRPC services reach the same <c>HttpContext</c> via
/// <c>ServerCallContext.GetHttpContext()</c>.
/// </remarks>
public sealed class IngestKeyUsageFeature(Guid keyId)
{
    public Guid KeyId { get; } = keyId;

    public long Events { get; private set; }

    public long Bytes { get; private set; }

    /// <summary>No-op when the request carries no feature - key auth off, the static key,
    /// or a test calling a receiver directly.</summary>
    public static void Add(HttpContext? context, long events, long bytes)
    {
        var feature = context?.Features.Get<IngestKeyUsageFeature>();
        if (feature is null)
        {
            return;
        }

        feature.Events += events;
        feature.Bytes += bytes;
    }
}
