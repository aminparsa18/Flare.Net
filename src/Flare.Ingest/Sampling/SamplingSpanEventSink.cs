using Flare.Ingest.Auth;
using Flare.Ingest.Model;
using Flare.Ingest.Sinks;

namespace Flare.Ingest.Sampling;

/// <summary>
/// <see cref="ISpanEventSink"/> decorator that runs every span through <see cref="TraceSampler"/>
/// before the Redis buffer (ADR-0122), so dropped spans cost no stream memory or ClickHouse
/// storage. Only registered when <c>Sampling:Enabled</c> is true.
/// </summary>
/// <remarks>
/// The receiving ingest key (for per-key rules) is read from the request's
/// <see cref="IngestKeyUsageFeature"/>; background flushes of expired holds carry no request
/// and use the weight each span captured when it arrived.
/// </remarks>
public sealed class SamplingSpanEventSink(
    ISpanEventSink inner,
    TraceSampler sampler,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider timeProvider) : ISpanEventSink
{
    public async ValueTask WriteAsync(SpanRecord span, CancellationToken cancellationToken = default)
    {
        var keyId = httpContextAccessor.HttpContext?.Features.Get<IngestKeyUsageFeature>()?.KeyId;
        var emit = new List<SpanRecord>(1);
        sampler.Add(span, keyId, timeProvider.GetUtcNow(), emit);
        foreach (var kept in emit)
        {
            await inner.WriteAsync(kept, cancellationToken);
        }
    }
}
