using Flare.Ingest.Model;
using Microsoft.Extensions.Options;

namespace Flare.Ingest.Pipeline.LogMetrics;

/// <summary>Turns a flushed log batch into log-based metric points and writes them to <c>metrics_sum</c>.</summary>
public interface ILogMetricEmitter
{
    Task EmitAsync(IReadOnlyList<LogEvent> events, CancellationToken cancellationToken);
}

/// <summary>
/// Runs <see cref="LogMetricAggregator"/> over a batch and hands the result to the shared
/// <see cref="IClickHouseMetricWriter"/>. Called by <see cref="ClickHouseFlushWorker"/> only after
/// the log batch itself was written, and its failures never fail the flush (see the worker): a
/// metric write that throws would otherwise leave the already-written logs un-acked and
/// duplicate them on retry.
/// </summary>
public sealed class LogMetricEmitter(
    ILogMetricCache cache,
    IClickHouseMetricWriter writer,
    IOptions<LogMetricOptions> options,
    TimeProvider timeProvider) : ILogMetricEmitter
{
    public async Task EmitAsync(IReadOnlyList<LogEvent> events, CancellationToken cancellationToken)
    {
        var opts = options.Value;
        if (!opts.Enabled)
        {
            return;
        }

        var points = LogMetricAggregator.Aggregate(events, cache.CurrentDefinitions, opts, timeProvider.GetUtcNow());
        if (points.Count == 0)
        {
            return;
        }

        await writer.WriteBatchAsync([], points, [], [], cancellationToken);
    }
}
