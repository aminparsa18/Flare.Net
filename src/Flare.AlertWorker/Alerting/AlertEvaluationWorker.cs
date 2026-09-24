using Flare.Api.Alerting;
using Flare.Api.Model;
using Flare.Api.Query;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Flare.AlertWorker.Alerting;

/// <summary>
/// Periodically re-evaluates every enabled <see cref="AlertRule"/>'s saved condition over its
/// rolling window (a count, a metric value, or - for <see cref="AlertConditionKind.Anomaly"/> -
/// a z-score against its seasonal baseline), and notifies (subject to cooldown) on breach.
/// </summary>
/// <remarks>
/// Runs in its own process (<c>Flare.AlertWorker</c>), not inside <c>Flare.Api</c> - see
/// <c>docs-internal/adr/0018-alert-worker-extraction.md</c> for why. <see cref="IAlertQueryService"/>/
/// <see cref="IAlertNotifier"/>/<see cref="AlertRule"/> etc. are still defined in
/// <c>Flare.Api</c> and reused here via a plain <c>ProjectReference</c> (that project's
/// own CRUD/send-test endpoints still need them) - not duplicated.
/// <para>
/// Same poll-loop <see cref="BackgroundService"/> idiom as
/// <c>Flare.Ingest</c>'s <c>ClickHouseFlushWorker</c> and <c>Flare.Api</c>'s own
/// <c>LogTailBroadcaster</c> - <c>while (!stoppingToken.IsCancellationRequested) { ...;
/// await Task.Delay(...); }</c>. Registered as a plain hosted service (not also a
/// singleton other components reach into, unlike <c>LogTailBroadcaster</c>'s "one
/// instance, two roles" registration) - nothing else needs to reach into this worker; the
/// <c>/api/alerts/*/test</c> endpoints (in <c>Flare.Api</c>) are stateless dry-runs
/// through <see cref="IAlertQueryService"/> directly, not calls into this class.
/// </para>
/// <para>
/// Every replica runs this same loop independently, so without coordination N replicas
/// would each evaluate every rule and could each send a duplicate notification for the
/// same breach (per-rule cooldown alone doesn't prevent two replicas both passing the
/// cooldown check in the same tick - see the roadmap's "Multi-node scaling" item). Each
/// tick is gated behind a single Redis-backed mutual-exclusion lock
/// (<see cref="LockKey"/>) so only one replica actually evaluates rules per tick; the
/// rest skip that tick entirely. The lock's expiry equals <see cref="AlertingOptions.PollInterval"/>,
/// so a replica that crashes mid-tick self-heals - the lock simply expires by the time
/// the next tick would fire, letting another replica pick it up. This uses
/// StackExchange.Redis's built-in single-node lock helper
/// (<see cref="IDatabaseAsync.LockTakeAsync"/>/<see cref="IDatabaseAsync.LockReleaseAsync"/>),
/// not a full Redlock - Flare already has exactly one Redis as a hard dependency, so the
/// extra complexity of a multi-node lock algorithm buys nothing here.
/// </para>
/// <para>
/// A rule with a non-zero <see cref="AlertRule.EvaluationIntervalSeconds"/> is evaluated only
/// on ticks where it's due, tracked by a per-rule Redis key (<c>flare:alerts:last-eval:{id}</c>)
/// rather than per process, for the same moving-lock-holder reason - see
/// <c>docs-internal/adr/0046-per-rule-alert-evaluation-interval.md</c>.
/// </para>
/// </remarks>
public sealed class AlertEvaluationWorker(
    IAlertQueryService alerts,
    INotificationChannelQueryService channels,
    CompositeAlertNotifier notifier,
    IConnectionMultiplexer redis,
    IOptions<AlertingOptions> options,
    TimeProvider timeProvider,
    ILogger<AlertEvaluationWorker> logger) : BackgroundService
{
    private static readonly RedisKey LockKey = "flare:alerts:eval-lock";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await TickIfLockedAsync(opts, stoppingToken);
                await Task.Delay(opts.PollInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    /// <summary>
    /// Acquires <see cref="LockKey"/> for this tick only; if another replica already
    /// holds it, skips evaluating entirely this tick (expected, steady-state behavior
    /// under &gt;1 replica - not an error).
    /// </summary>
    private async Task TickIfLockedAsync(AlertingOptions opts, CancellationToken cancellationToken)
    {
        var db = redis.GetDatabase();
        RedisValue lockToken = Guid.NewGuid().ToString("N");
        if (!await db.LockTakeAsync(LockKey, lockToken, opts.PollInterval))
        {
            logger.LogDebug("Another replica holds {LockKey}; skipping this tick.", LockKey);
            return;
        }

        try
        {
            await TickAsync(opts, cancellationToken);
        }
        finally
        {
            await db.LockReleaseAsync(LockKey, lockToken);
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

        var dueRules = await FilterDueRulesAsync(rules, opts);

        foreach (var rule in dueRules.Take(opts.MaxRulesPerTick))
        {
            try
            {
                if (rule.EvaluationIntervalSeconds > 0)
                {
                    // Marked before evaluating, not after: a slow rule that keeps timing out
                    // (exactly the kind a longer interval is for) is retried at its own
                    // interval rather than every tick. The TTL self-cleans deleted rules'
                    // markers.
                    await redis.GetDatabase().StringSetAsync(
                        LastEvaluatedKey(rule.Id),
                        timeProvider.GetUtcNow().ToUnixTimeMilliseconds(),
                        TimeSpan.FromSeconds(rule.EvaluationIntervalSeconds));
                }

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

    private static RedisKey LastEvaluatedKey(Guid ruleId) => $"flare:alerts:last-eval:{ruleId:N}";

    /// <summary>
    /// Drops rules with a per-rule <see cref="AlertRule.EvaluationIntervalSeconds"/> that
    /// aren't due yet (<see cref="AlertEvaluationSchedule.IsDue"/>), reading every such rule's
    /// last-evaluated marker in one <c>MGET</c>. Every-tick (0) rules never touch Redis here.
    /// The markers live in Redis, not an in-memory dictionary, because the tick lock moves
    /// between replicas - see <c>docs-internal/adr/0046-per-rule-alert-evaluation-interval.md</c>.
    /// </summary>
    private async Task<IReadOnlyList<AlertRule>> FilterDueRulesAsync(IReadOnlyList<AlertRule> rules, AlertingOptions opts)
    {
        var scheduled = rules.Where(r => r.EvaluationIntervalSeconds > 0).ToList();
        if (scheduled.Count == 0)
        {
            return rules;
        }

        RedisValue[] markers;
        try
        {
            markers = await redis.GetDatabase().StringGetAsync(scheduled.Select(r => LastEvaluatedKey(r.Id)).ToArray());
        }
        catch (Exception ex)
        {
            // Fail open: evaluating a slow rule early is far cheaper than silently not
            // alerting because the schedule couldn't be read.
            logger.LogWarning(ex, "Failed to read per-rule evaluation markers; evaluating every rule this tick.");
            return rules;
        }

        var lastEvaluated = new Dictionary<Guid, DateTimeOffset>(scheduled.Count);
        for (var i = 0; i < scheduled.Count; i++)
        {
            if (markers[i].TryParse(out long unixMs))
            {
                lastEvaluated[scheduled[i].Id] = DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
            }
        }

        var now = timeProvider.GetUtcNow();
        return rules
            .Where(r => AlertEvaluationSchedule.IsDue(
                r.EvaluationIntervalSeconds,
                lastEvaluated.TryGetValue(r.Id, out var last) ? last : null,
                now,
                opts.PollInterval))
            .ToList();
    }

    private async Task EvaluateRuleAsync(AlertRule rule, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var from = now - TimeSpan.FromSeconds(rule.WindowSeconds);

        bool breached;
        ulong observedCount = 0;
        double? observedValue = null;
        string? metricUnit = null;
        AnomalyScore? anomaly = null;

        // Absent-data check first (ADR-0045): when the condition matched nothing at all over
        // the no-data window, fire that instead of evaluating the threshold - over an empty
        // window the threshold result is meaningless anyway (NaN for a metric, which never
        // breaches; 0 for a count, which only breaches a LessThan rule and would then report
        // a misleading "0 events" rather than "no data").
        var seriesKind = AnomalyScoring.SeriesKind(rule.ConditionKind, rule.AnomalyCondition);
        var noData = await AlertNoDataEvaluator.IsAbsentAsync(alerts, seriesKind, rule.Condition, rule.MetricCondition, rule.NoDataWindowSeconds, now, cancellationToken);
        if (noData)
        {
            breached = true;
        }
        else if (rule.ConditionKind == AlertConditionKind.MetricThreshold)
        {
            if (rule.MetricCondition is null || rule.MetricThresholdValue is not { } thresholdValue)
            {
                logger.LogWarning("Alert rule {RuleId} ({RuleName}) is MetricThreshold but has no metric condition/threshold; skipping.", rule.Id, rule.Name);
                return;
            }

            // Minimum sample size (ADR-0050): too few points in the window is "insufficient
            // data", not a breach - checked after no-data, so zero points still fires no-data
            // when that's enabled.
            var pointCount = await AlertMinDataPointsEvaluator.CountAsync(alerts, rule.MetricCondition, rule.MinDataPoints, from, now, cancellationToken);
            if (AlertMinDataPointsEvaluator.IsInsufficient(rule.MinDataPoints, pointCount))
            {
                logger.LogDebug("Alert rule {RuleId} ({RuleName}) has {PointCount}/{MinDataPoints} data points in its window; insufficient data, not evaluating.", rule.Id, rule.Name, pointCount, rule.MinDataPoints);
                return;
            }

            (var value, metricUnit) = await alerts.EvaluateMetricConditionAsync(rule.MetricCondition, from, now, cancellationToken);
            observedValue = value;
            breached = rule.Threshold.IsBreachedValue(observedValue.Value, thresholdValue);
        }
        else if (rule.ConditionKind == AlertConditionKind.Anomaly)
        {
            var evaluated = rule.AnomalyCondition is null
                ? null
                : await AnomalyEvaluator.EvaluateAsync(alerts, rule.AnomalyCondition, rule.Condition, rule.MetricCondition, rule.ExceptionCondition, rule.WindowSeconds, now, cancellationToken);
            if (evaluated is not { } result)
            {
                logger.LogWarning("Alert rule {RuleId} ({RuleName}) is Anomaly but has no anomaly condition or source condition; skipping.", rule.Id, rule.Name);
                return;
            }

            (anomaly, metricUnit) = result;
            observedValue = anomaly.Current;
            breached = anomaly.Breached;
        }
        else if (rule.ConditionKind == AlertConditionKind.ExceptionCount)
        {
            if (rule.ExceptionCondition is null)
            {
                logger.LogWarning("Alert rule {RuleId} ({RuleName}) is ExceptionCount but has no exception condition; skipping.", rule.Id, rule.Name);
                return;
            }

            observedCount = await alerts.CountMatchingExceptionsAsync(rule.ExceptionCondition, from, now, cancellationToken);
            breached = rule.Threshold.IsBreached(observedCount);
        }
        else
        {
            observedCount = await alerts.CountMatchingLogsAsync(rule.Condition, from, now, cancellationToken);
            breached = rule.Threshold.IsBreached(observedCount);
        }

        if (!breached)
        {
            return;
        }

        var lastFired = await alerts.GetLastFiredAsync(rule.Id, cancellationToken);
        if (lastFired is { } last && now - last < TimeSpan.FromSeconds(rule.CooldownSeconds))
        {
            logger.LogDebug("Alert rule {RuleId} ({RuleName}) breached{NoData} but in cooldown; suppressing.", rule.Id, rule.Name, noData ? " (no data)" : "");
            return;
        }

        var ruleChannels = await NotificationChannelResolver.ResolveAsync(rule, channels, cancellationToken);
        if (ruleChannels.Count == 0)
        {
            logger.LogWarning("Alert rule {RuleId} ({RuleName}) breached but has no resolvable notification channel; skipping notify.", rule.Id, rule.Name);
            return;
        }

        var results = await notifier.SendAllAsync(rule, ruleChannels, observedValue ?? observedCount, now, cancellationToken, metricUnit: metricUnit, noData: noData, anomaly: anomaly);
        var channelResults = ruleChannels.Zip(results, (channel, result) => new AlertChannelResult
        {
            ChannelId = channel.Id == NotificationChannelResolver.LegacyChannelId ? null : channel.Id,
            ChannelName = channel.Name,
            Type = channel.Type,
            Success = result.Success,
            StatusCode = result.StatusCode,
            Error = result.Error ?? "",
        }).ToList();

        var failed = channelResults.Where(r => !r.Success).ToList();
        if (failed.Count > 0)
        {
            logger.LogWarning(
                "Alert rule {RuleId} ({RuleName}) fired but {FailedCount}/{TotalCount} channel notification(s) failed: {Errors}",
                rule.Id,
                rule.Name,
                failed.Count,
                channelResults.Count,
                string.Join("; ", failed.Select(r => $"{r.ChannelName}: {r.Error}")));
        }

        await alerts.InsertEventAsync(
            new AlertHistoryEntry
            {
                EventId = Guid.NewGuid(),
                RuleId = rule.Id,
                RuleName = rule.Name,
                FiredAt = now,
                ObservedCount = observedCount,
                ThresholdCount = rule.Threshold.Count,
                WindowSeconds = noData ? rule.NoDataWindowSeconds : rule.WindowSeconds,
                // Backward-compatible summary across every channel - "Sent" only if all
                // of them succeeded, same contract this field had before fan-out existed
                // (a single-channel rule's summary is unchanged). ChannelResults below
                // carries the per-channel detail.
                NotificationStatus = failed.Count == 0 ? "Sent" : "Failed",
                NotificationStatusCode = channelResults[0].StatusCode,
                NotificationError = failed.Count == 0 ? "" : string.Join("; ", failed.Select(r => $"{r.ChannelName}: {r.Error}")),
                ConditionKind = rule.ConditionKind,
                ObservedValue = observedValue,
                // An anomaly rule has no fixed threshold - its MetricThresholdValue is a
                // leftover placeholder when the source is a metric, not something to record.
                ThresholdValue = rule.ConditionKind == AlertConditionKind.Anomaly ? null : rule.MetricThresholdValue,
                ChannelResults = channelResults,
                NoData = noData,
                BaselineMean = anomaly?.BaselineMean,
                ZScore = anomaly?.ZScore,
            },
            cancellationToken);
    }
}
