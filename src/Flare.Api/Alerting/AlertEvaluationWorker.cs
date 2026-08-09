using Flare.Api.Model;
using Flare.Api.Query;
using Microsoft.Extensions.Options;

namespace Flare.Api.Alerting;

/// <summary>
/// Periodically re-evaluates every enabled <see cref="AlertRule"/>'s saved condition as a
/// count over its rolling window, and notifies (subject to cooldown) on breach.
/// </summary>
/// <remarks>
/// Same poll-loop <see cref="BackgroundService"/> idiom as
/// <c>Flare.Ingest</c>'s <c>ClickHouseFlushWorker</c> and this project's own
/// <c>LogTailBroadcaster</c> - <c>while (!stoppingToken.IsCancellationRequested) { ...;
/// await Task.Delay(...); }</c>. Registered as a plain hosted service (not also a
/// singleton other components reach into, unlike <c>LogTailBroadcaster</c>'s "one
/// instance, two roles" registration) - nothing else needs to reach into this worker; the
/// <c>/api/alerts/*/test</c> endpoints are stateless dry-runs through
/// <see cref="IAlertQueryService"/> directly, not calls into this class.
/// </remarks>
public sealed class AlertEvaluationWorker(
    IAlertQueryService alerts,
    IAlertNotifier notifier,
    IOptions<AlertingOptions> options,
    TimeProvider timeProvider,
    ILogger<AlertEvaluationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await TickAsync(opts, stoppingToken);
                await Task.Delay(opts.PollInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async Task TickAsync(AlertingOptions opts, CancellationToken cancellationToken)
    {
        IReadOnlyList<AlertRule> rules;
        try
        {
            rules = await alerts.GetEnabledRulesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load enabled alert rules; skipping this tick.");
            return;
        }

        foreach (var rule in rules.Take(opts.MaxRulesPerTick))
        {
            try
            {
                await EvaluateRuleAsync(rule, cancellationToken);
            }
            catch (Exception ex)
            {
                // One rule's failure (a bad condition, a transient ClickHouse error)
                // must not stop every other rule from being evaluated this tick.
                logger.LogError(ex, "Alert rule {RuleId} ({RuleName}) evaluation failed.", rule.Id, rule.Name);
            }
        }
    }

    private async Task EvaluateRuleAsync(AlertRule rule, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var from = now - TimeSpan.FromSeconds(rule.WindowSeconds);
        var count = await alerts.CountMatchingLogsAsync(rule.Condition, from, now, cancellationToken);

        if (!rule.Threshold.IsBreached(count))
        {
            return;
        }

        var lastFired = await alerts.GetLastFiredAsync(rule.Id, cancellationToken);
        if (lastFired is { } last && now - last < TimeSpan.FromSeconds(rule.CooldownSeconds))
        {
            logger.LogDebug("Alert rule {RuleId} ({RuleName}) breached but in cooldown; suppressing.", rule.Id, rule.Name);
            return;
        }

        var result = await notifier.SendAsync(rule, count, now, cancellationToken);
        if (!result.Success)
        {
            logger.LogWarning(
                "Alert rule {RuleId} ({RuleName}) fired but notification failed: {Error}",
                rule.Id,
                rule.Name,
                result.Error);
        }

        await alerts.InsertEventAsync(
            new AlertHistoryEntry
            {
                EventId = Guid.NewGuid(),
                RuleId = rule.Id,
                RuleName = rule.Name,
                FiredAt = now,
                ObservedCount = count,
                ThresholdCount = rule.Threshold.Count,
                WindowSeconds = rule.WindowSeconds,
                NotificationStatus = result.Success ? "Sent" : "Failed",
                NotificationStatusCode = result.StatusCode,
                NotificationError = result.Error ?? "",
            },
            cancellationToken);
    }
}