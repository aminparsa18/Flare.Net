using Microsoft.Extensions.Options;

namespace Flare.Ingest.Pipeline.LogMetrics;

/// <summary>In-memory snapshot of the enabled <see cref="LogMetricDefinition"/>s.</summary>
public interface ILogMetricCache
{
    IReadOnlyList<LogMetricDefinition> CurrentDefinitions { get; }
}

/// <summary>
/// Polls <see cref="ILogMetricStore"/> on <see cref="LogMetricOptions.RefreshInterval"/>. Same
/// idiom (and same no-coordination-needed reasoning) as <see cref="Rules.PipelineRuleCache"/>;
/// keeps the last good snapshot when a refresh fails.
/// </summary>
public sealed class LogMetricCache(
    ILogMetricStore store,
    IOptions<LogMetricOptions> options,
    ILogger<LogMetricCache> logger) : BackgroundService, ILogMetricCache
{
    private volatile IReadOnlyList<LogMetricDefinition> currentDefinitions = [];

    public IReadOnlyList<LogMetricDefinition> CurrentDefinitions => currentDefinitions;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (!opts.Enabled)
        {
            return;
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    currentDefinitions = await store.GetEnabledAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Failed to refresh log metrics; keeping the last known set ({Count} definitions).", currentDefinitions.Count);
                }

                await Task.Delay(opts.RefreshInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }
}
