using Flare.Ingest.Model;
using Flare.Ingest.Sinks;
using Microsoft.Extensions.Options;

namespace Flare.Ingest.Sampling;

/// <summary>
/// Decides traces whose hold window has ended (<see cref="TraceSampler.Sweep"/>) and writes
/// the survivors to the underlying sink; on shutdown decides every still-held trace so a
/// graceful restart doesn't lose spans that were about to be kept.
/// </summary>
public sealed class TraceSamplingWorker(
    TraceSampler sampler,
    [FromKeyedServices(TraceSamplingWorker.InnerSinkKey)] ISpanEventSink inner,
    IOptions<TraceSamplingOptions> options,
    TimeProvider timeProvider,
    ILogger<TraceSamplingWorker> logger) : BackgroundService
{
    public const string InnerSinkKey = "sampling-inner";

    private long _lastReportedDropped;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.SweepInterval, timeProvider);
        var lastLog = timeProvider.GetUtcNow();
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var emit = new List<SpanRecord>();
                sampler.Sweep(timeProvider.GetUtcNow(), emit);
                await WriteAsync(emit, stoppingToken);

                if (timeProvider.GetUtcNow() - lastLog >= TimeSpan.FromMinutes(1))
                {
                    lastLog = timeProvider.GetUtcNow();
                    var dropped = sampler.Dropped;
                    if (dropped != _lastReportedDropped)
                    {
                        _lastReportedDropped = dropped;
                        logger.LogInformation(
                            "Trace sampling: {Held} span(s) held, {Hash} kept by head hash, {Tail} kept by tail/policy, {Dropped} dropped, {Overflow} skipped the hold (cap reached)",
                            sampler.HeldSpans, sampler.KeptByHash, sampler.KeptByTail, dropped, sampler.Overflowed);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        var emit = new List<SpanRecord>();
        sampler.FlushAll(emit);
        await WriteAsync(emit, cancellationToken);
    }

    private async Task WriteAsync(List<SpanRecord> spans, CancellationToken cancellationToken)
    {
        foreach (var span in spans)
        {
            try
            {
                await inner.WriteAsync(span, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Redis unavailable: the span was past its decision point, so it can't be
                // re-held; same loss window as any in-memory buffer ahead of the stream.
                logger.LogWarning(ex, "Failed to buffer a sampled-in span; dropping it");
            }
        }
    }
}
