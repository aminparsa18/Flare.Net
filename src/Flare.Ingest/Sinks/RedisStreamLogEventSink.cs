using System.Text.Json;
using Flare.Ingest.Model;
using Flare.Ingest.Pipeline;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Flare.Ingest.Sinks;

/// <summary>
/// Durable buffer for the batched ClickHouse insert pipeline: writes each
/// <see cref="LogEvent"/> into a Redis Stream (<c>XADD</c>) instead of persisting it
/// directly, so events survive Flare.Ingest restarting before <see cref="Pipeline.ClickHouseFlushWorker"/>
/// has flushed them (Planning.md's "Buffering layer" decision, 2026-08-07).
/// </summary>
/// <remarks>
/// Each stream entry carries the whole <see cref="LogEvent"/> as one JSON blob in a
/// single <c>data</c> field, rather than flattened per-property fields. <see cref="LogEvent"/>
/// carries three <c>Map&lt;string,string&gt;</c> attribute bags whose key sets vary per
/// event - flattening those into synthesized Stream field names would require inventing
/// a prefixing/escaping scheme for zero benefit, since nothing reads individual Stream
/// fields directly; only <see cref="Pipeline.ClickHouseFlushWorker"/> ever reads this
/// entry back, and it deserializes the whole blob at once via <see cref="LogEventJsonContext"/>.
/// </remarks>
public sealed class RedisStreamLogEventSink(
    IConnectionMultiplexer connectionMultiplexer,
    IOptions<LogEventPipelineOptions> options) : ILogEventSink
{
    private static readonly RedisValue DataField = "data";

    public async ValueTask WriteAsync(LogEvent logEvent, CancellationToken cancellationToken = default)
    {
        var opts = options.Value;
        var payload = JsonSerializer.Serialize(logEvent, LogEventJsonContext.Default.LogEvent);

        var db = connectionMultiplexer.GetDatabase();
        await db.StreamAddAsync(
            opts.StreamKey,
            DataField,
            payload,
            maxLength: opts.StreamMaxLength,
            useApproximateMaxLength: true);
    }
}
