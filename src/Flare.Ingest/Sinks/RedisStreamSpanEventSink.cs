using System.Text.Json;
using Flare.Ingest.Model;
using Flare.Ingest.Pipeline;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Flare.Ingest.Sinks;

/// <summary>
/// Durable buffer for the batched ClickHouse insert pipeline for spans: writes each
/// <see cref="SpanRecord"/> into a Redis Stream (<c>XADD</c>) instead of persisting it
/// directly, same rationale and shape as <see cref="RedisStreamLogEventSink"/>.
/// </summary>
public sealed class RedisStreamSpanEventSink(
    IConnectionMultiplexer connectionMultiplexer,
    IOptions<SpanEventPipelineOptions> options) : ISpanEventSink
{
    private static readonly RedisValue DataField = "data";

    public async ValueTask WriteAsync(SpanRecord span, CancellationToken cancellationToken = default)
    {
        var opts = options.Value;
        var payload = JsonSerializer.Serialize(span, SpanEventJsonContext.Default.SpanRecord);

        var db = connectionMultiplexer.GetDatabase();
        await db.StreamAddAsync(
            opts.StreamKey,
            DataField,
            payload,
            maxLength: opts.StreamMaxLength,
            useApproximateMaxLength: true);
    }
}
