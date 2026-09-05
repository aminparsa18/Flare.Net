using Flare.Ingest.Model;
using Flare.Ingest.Pipeline;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Flare.Ingest.Sinks;

/// <summary>
/// Durable buffer for the batched ClickHouse insert pipeline for metrics: writes each
/// <see cref="MetricPointRecord"/> into a single Redis Stream (<c>XADD</c>) instead of
/// persisting it directly, same rationale and shape as <see cref="RedisStreamSpanEventSink"/>.
/// MemoryPack-encoded via <see cref="Pipeline.RedisEventPayload"/> (ADR-0017) - engages
/// <see cref="MetricPointRecord"/>'s <see cref="MemoryPack.MemoryPackUnionAttribute"/>
/// declarations, so one stream entry round-trips as whichever concrete point type it
/// actually is, same as the JSON contract it replaced.
/// </summary>
public sealed class RedisStreamMetricEventSink(
    IConnectionMultiplexer connectionMultiplexer,
    IOptions<MetricEventPipelineOptions> options) : IMetricEventSink
{
    private static readonly RedisValue DataField = "data";

    public async ValueTask WriteAsync(MetricPointRecord point, CancellationToken cancellationToken = default)
    {
        var opts = options.Value;
        var payload = RedisEventPayload.Encode(point);

        var db = connectionMultiplexer.GetDatabase();
        await db.StreamAddAsync(
            opts.StreamKey,
            DataField,
            payload,
            maxLength: opts.StreamMaxLength,
            useApproximateMaxLength: true);
    }
}
