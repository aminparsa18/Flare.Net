using Microsoft.Extensions.Options;

namespace Flare.Ingest.Pipeline.MetricRules;

/// <summary>In-memory snapshot of the enabled <see cref="MetricAttributeRule"/>s.</summary>
public interface IMetricAttributeRuleCache
{
    IReadOnlyList<MetricAttributeRule> CurrentRules { get; }
}

/// <summary>
/// Polls <see cref="IMetricAttributeRuleStore"/> on <see cref="MetricAttributeRuleOptions.RefreshInterval"/>.
/// Same idiom (and same no-coordination-needed reasoning) as <see cref="Rules.PipelineRuleCache"/>;
/// keeps the last good snapshot when a refresh fails.
/// </summary>
public sealed class MetricAttributeRuleCache(
    IMetricAttributeRuleStore store,
    IOptions<MetricAttributeRuleOptions> options,
    ILogger<MetricAttributeRuleCache> logger) : BackgroundService, IMetricAttributeRuleCache
{
    private volatile IReadOnlyList<MetricAttributeRule> currentRules = [];

    public IReadOnlyList<MetricAttributeRule> CurrentRules => currentRules;

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
                    currentRules = await store.GetEnabledRulesAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Failed to refresh metric attribute rules; keeping the last known set ({Count} rules).", currentRules.Count);
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
