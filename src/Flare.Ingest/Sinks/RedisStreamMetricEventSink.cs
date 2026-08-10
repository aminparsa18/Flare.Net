using System.Text.Json;
using Flare.Ingest.Model;
using Flare.Ingest.Pipeline;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Flare.Ingest.Sinks;

/// <summary>
/// Durable buffer for the batched ClickHouse insert pipeline for metrics: writes each
/// <see cref="MetricPointRecord"/> into a single Redis Stream (<c>XADD</c>) instead of
/// persisting it directly, same rationale and shape as <see cref="RedisStreamSpanEventSink"/>.
/// </summary>
public sealed class RedisStreamMetricEventSink(
    IConnectionMultiplexer connectionMultiplexer,
    IOptions<MetricEventPipelineOptions> options) : IMetricEventSink
{
    private static readonly RedisValue DataField = "data";

    public async ValueTask WriteAsync(MetricPointRecord point, CancellationToken cancellationToken = default)
    {
        var opts = options.Value;
        var payload = JsonSerializer.Serialize(point, MetricEventJsonContext.Default.MetricPointRecord);

        var db = connectionMultiplexer.GetDatabase();
        await db.StreamAddAsync(
            opts.StreamKey,
            DataField,
            payload,
            maxLength: opts.StreamMaxLength,
            useApproximateMaxLength: true);
    }
}
