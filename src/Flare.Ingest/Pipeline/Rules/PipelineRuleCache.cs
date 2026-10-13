using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;

namespace Flare.Ingest.Pipeline.Rules;

/// <summary>
/// Polls <see cref="IPipelineRuleStore"/> on <see cref="PipelineRuleOptions.RefreshInterval"/>
/// and publishes the result as <see cref="CurrentRules"/> - same poll-loop
/// <see cref="BackgroundService"/> idiom as <c>Flare.Ingest</c>'s own
/// <c>ClickHouseFlushWorker</c> and <c>Flare.AlertWorker</c>'s <c>AlertEvaluationWorker</c>,
/// but with no cross-replica coordination lock: unlike alert evaluation (which must fire
/// exactly once per tick to avoid duplicate notifications), N <c>Flare.Ingest</c> replicas
/// each independently polling the same read-only query is harmless - every replica just
/// converges on the same snapshot.
/// </summary>
public sealed class PipelineRuleCache(
    IPipelineRuleStore store,
    IOptions<PipelineRuleOptions> options,
    ILogger<PipelineRuleCache> logger) : BackgroundService, IPipelineRuleCache
{
    private volatile IReadOnlyList<PipelineRule> currentRules = [];

    private volatile StrongBox<bool?> piiOverride = new(null);

    public IReadOnlyList<PipelineRule> CurrentRules => currentRules;

    public bool? PiiEnabledOverride => piiOverride.Value;

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
                await RefreshAsync(stoppingToken);
                await RefreshPiiOverrideAsync(stoppingToken);
                await Task.Delay(opts.RefreshInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    /// <summary>
    /// On failure, keeps the last-known-good <see cref="CurrentRules"/> snapshot rather
    /// than clearing it - a transient ClickHouse hiccup shouldn't silently disable every
    /// rule until the next successful poll.
    /// </summary>
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            currentRules = await store.GetEnabledRulesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to refresh pipeline rules; keeping the last known set ({Count} rules).", currentRules.Count);
        }
    }

    /// <summary>
    /// Separate from <see cref="RefreshAsync"/> so one failing query never freezes the other. On failure the
    /// last-known override stays: reverting to configuration on a transient error could silently switch
    /// redaction off (ADR-0210).
    /// </summary>
    private async Task RefreshPiiOverrideAsync(CancellationToken cancellationToken)
    {
        try
        {
            piiOverride = new StrongBox<bool?>(await store.GetPiiEnabledOverrideAsync(cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to refresh the PII redaction override; keeping the last known value ({Value}).", piiOverride.Value);
        }
    }
}
