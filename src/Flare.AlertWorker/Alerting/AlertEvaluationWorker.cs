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
/// <para>
/// A breach while a <see cref="MaintenanceWindow"/> covering the rule is active is still
/// recorded in history, as "Suppressed", but not notified - see
/// <c>docs-internal/adr/0055-alert-maintenance-windows.md</c>.
/// </para>
/// <para>
/// When a firing rule evaluates as not breached, a "Resolved" notification goes to its
/// channels that opted in (<see cref="NotificationChannel.SendResolved"/>) and a resolution
/// row is recorded - firing/ok state is derived from those <c>alert_events</c> rows, read once
/// per tick (<see cref="IAlertQueryService.GetFiringStatesAsync"/>). See
/// <see cref="AlertResolutionPolicy"/> and <c>docs-internal/adr/0064-alert-resolved-notifications.md</c>.
/// </para>
/// </remarks>
public sealed class AlertEvaluationWorker(
    IAlertQueryService alerts,
    INotificationChannelQueryService channels,
    IMaintenanceWindowQueryService maintenanceWindows,
    CompositeAlertNotifier notifier,
    IIncidentSummaryService incidentSummaries,
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

        var dueRules = (await FilterDueRulesAsync(rules, opts)).Take(opts.MaxRulesPerTick).ToList();
        var windows = await LoadMaintenanceWindowsAsync(cancellationToken);
        var firingStates = await LoadFiringStatesAsync(dueRules, cancellationToken);

        foreach (var rule in dueRules)
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

                await EvaluateRuleAsync(rule, windows, firingStates?.GetValueOrDefault(rule.Id), firingStates is not null, cancellationToken);
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

    /// <summary>
    /// Every maintenance window, read once per tick. Fails open to none: paging during a
    /// planned window is far cheaper than silently dropping a real alert because the windows
    /// couldn't be read - same trade-off as <see cref="FilterDueRulesAsync"/>.
    /// </summary>
    private async Task<IReadOnlyList<MaintenanceWindow>> LoadMaintenanceWindowsAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await maintenanceWindows.ListAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to load maintenance windows; notifying as if none were active this tick.");
            return [];
        }
    }

    /// <summary>
    /// Which of <paramref name="rules"/> are currently firing, read once per tick. Null when it
    /// couldn't be read - recovery handling is skipped for the tick (a resolution is retried on
    /// the next ok tick), rather than guessing every rule is ok or firing.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, AlertFiringState>?> LoadFiringStatesAsync(IReadOnlyList<AlertRule> rules, CancellationToken cancellationToken)
    {
        try
        {
            return await alerts.GetFiringStatesAsync(rules.Select(r => r.Id).ToList(), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to load alert firing states; skipping resolved notifications this tick.");
            return null;
        }
    }

    /// <param name="firingState">The rule's firing state from <see cref="LoadFiringStatesAsync"/>; null when it's ok.</param>
    /// <param name="firingStateKnown">False when the firing states couldn't be read this tick - no recovery is handled then.</param>
    private async Task EvaluateRuleAsync(AlertRule rule, IReadOnlyList<MaintenanceWindow> windows, AlertFiringState? firingState, bool firingStateKnown, CancellationToken cancellationToken)
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

            // Threshold unit (ADR-0080): the rule's threshold and recovery threshold were typed
            // in rule.ThresholdUnit; express them in the unit the points were recorded in. The
            // converted rule is what the notification and history below read, so they format the
            // threshold at the series' own unit like the observed value.
            if (!string.IsNullOrEmpty(rule.ThresholdUnit))
            {
                if (!MetricUnitConverter.TryConvert(thresholdValue, rule.ThresholdUnit, metricUnit, out _))
                {
                    logger.LogWarning("Alert rule {RuleId} ({RuleName}) has threshold unit {ThresholdUnit} but its metric reports unit {MetricUnit}; comparing the threshold as typed.", rule.Id, rule.Name, rule.ThresholdUnit, metricUnit ?? "(none)");
                }

                thresholdValue = MetricUnitConverter.ToSeriesUnit(thresholdValue, rule.ThresholdUnit, metricUnit);
                rule = rule with
                {
                    MetricThresholdValue = thresholdValue,
                    RecoveryThreshold = rule.RecoveryThreshold is { } rawRecovery ? MetricUnitConverter.ToSeriesUnit(rawRecovery, rule.ThresholdUnit, metricUnit) : null,
                };
            }

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
            if (firingStateKnown)
            {
                // Hysteresis (ADR-0076): a firing rule whose value is inside the band between
                // the threshold and its recovery threshold stays firing - no resolution, and no
                // new notification either (it isn't breached).
                if (firingState is not null && rule.RecoveryThreshold is { } recovery && rule.ConditionKind != AlertConditionKind.Anomaly
                    && rule.Threshold.HoldsFiring(observedValue ?? observedCount, recovery))
                {
                    logger.LogDebug("Alert rule {RuleId} ({RuleName}) is below its threshold but hasn't crossed its recovery threshold {RecoveryThreshold}; staying firing.", rule.Id, rule.Name, recovery);
                    return;
                }

                await ResolveIfFiringAsync(rule, firingState, windows, now, observedCount, observedValue, metricUnit, anomaly, cancellationToken);
            }

            return;
        }

        // Inside a maintenance window, cooldown counts suppressed events too - one suppressed
        // history row per cooldown, not one per tick. Outside, it ignores them, so a breach
        // that outlasts the window notifies as soon as the window ends.
        var window = MaintenanceWindowSchedule.FindActive(windows, rule, now);
        var lastFired = await alerts.GetLastFiredAsync(rule.Id, includeSuppressed: window is not null, cancellationToken);
        if (lastFired is { } last && now - last < TimeSpan.FromSeconds(rule.CooldownSeconds))
        {
            logger.LogDebug("Alert rule {RuleId} ({RuleName}) breached{NoData} but in cooldown; suppressing.", rule.Id, rule.Name, noData ? " (no data)" : "");
            return;
        }

        if (window is not null)
        {
            logger.LogInformation("Alert rule {RuleId} ({RuleName}) breached{NoData} during maintenance window {WindowName}; recording without notifying.", rule.Id, rule.Name, noData ? " (no data)" : "", window.Name);
            await alerts.InsertEventAsync(
                BuildHistoryEntry(rule, now, noData, observedCount, observedValue, anomaly) with
                {
                    NotificationStatus = "Suppressed",
                    SuppressedByWindow = window.Name,
                },
                cancellationToken);
            return;
        }

        var ruleChannels = await NotificationChannelResolver.ResolveAsync(rule, channels, cancellationToken);
        if (ruleChannels.Count == 0)
        {
            logger.LogWarning("Alert rule {RuleId} ({RuleName}) breached but has no resolvable notification channel; skipping notify.", rule.Id, rule.Name);
            return;
        }

        var results = await notifier.SendAllAsync(rule, ruleChannels, observedValue ?? observedCount, now, cancellationToken, metricUnit: metricUnit, noData: noData, anomaly: anomaly);
        var firedEntry = WithNotificationOutcome(BuildHistoryEntry(rule, now, noData, observedCount, observedValue, anomaly), rule, ruleChannels, results, "fired");
        await alerts.InsertEventAsync(firedEntry, cancellationToken);

        // After the plain notification and history row are out (ADR-0104): the optional AI summary
        // runs on its own bounded background task and can never delay or lose the alert.
        incidentSummaries.Enqueue(rule, firedEntry, ruleChannels, metricUnit, anomaly);
    }

    /// <summary>
    /// The firing→ok transition: sends "Resolved" to the rule's opted-in channels and records a
    /// resolution row, per <see cref="AlertResolutionPolicy.Decide"/>. A rule that wasn't firing,
    /// or whose resolution waits out a maintenance window, records nothing.
    /// </summary>
    private async Task ResolveIfFiringAsync(AlertRule rule, AlertFiringState? firingState, IReadOnlyList<MaintenanceWindow> windows, DateTimeOffset now, ulong observedCount, double? observedValue, string? metricUnit, AnomalyScore? anomaly, CancellationToken cancellationToken)
    {
        var window = MaintenanceWindowSchedule.FindActive(windows, rule, now);
        var action = AlertResolutionPolicy.Decide(firingState, window is not null);
        if (action == AlertResolutionAction.None)
        {
            return;
        }

        if (action == AlertResolutionAction.Defer)
        {
            logger.LogDebug("Alert rule {RuleId} ({RuleName}) recovered during maintenance window {WindowName}; resolving once it ends.", rule.Id, rule.Name, window!.Name);
            return;
        }

        var entry = BuildHistoryEntry(rule, now, noData: false, observedCount, observedValue, anomaly) with { Resolved = true };
        var ruleChannels = action == AlertResolutionAction.Notify
            ? (await NotificationChannelResolver.ResolveAsync(rule, channels, cancellationToken)).Where(c => c.SendResolved).ToList()
            : [];
        if (ruleChannels.Count == 0)
        {
            // Nobody was paged, or every channel opted out - still recorded, so the rule reads
            // as ok and the next breach starts a new incident.
            logger.LogInformation("Alert rule {RuleId} ({RuleName}) resolved; no resolved notification to send.", rule.Id, rule.Name);
            await alerts.InsertEventAsync(entry with { NotificationStatus = "Skipped" }, cancellationToken);
            return;
        }

        logger.LogInformation("Alert rule {RuleId} ({RuleName}) resolved; notifying {ChannelCount} channel(s).", rule.Id, rule.Name, ruleChannels.Count);
        var results = await notifier.SendAllAsync(rule, ruleChannels, observedValue ?? observedCount, now, cancellationToken, metricUnit: metricUnit, anomaly: anomaly, resolved: true);
        await alerts.InsertEventAsync(WithNotificationOutcome(entry, rule, ruleChannels, results, "resolved"), cancellationToken);
    }

    /// <summary>Folds a fan-out's per-channel results into <paramref name="entry"/>, logging any failures - shared by a fire and a resolution.</summary>
    private AlertHistoryEntry WithNotificationOutcome(AlertHistoryEntry entry, AlertRule rule, IReadOnlyList<NotificationChannel> ruleChannels, IReadOnlyList<NotificationResult> results, string verb)
    {
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
                "Alert rule {RuleId} ({RuleName}) {Verb} but {FailedCount}/{TotalCount} channel notification(s) failed: {Errors}",
                rule.Id,
                rule.Name,
                verb,
                failed.Count,
                channelResults.Count,
                string.Join("; ", failed.Select(r => $"{r.ChannelName}: {r.Error}")));
        }

        return entry with
        {
            // Backward-compatible summary across every channel - "Sent" only if all
            // of them succeeded, same contract this field had before fan-out existed
            // (a single-channel rule's summary is unchanged). ChannelResults below
            // carries the per-channel detail.
            NotificationStatus = failed.Count == 0 ? "Sent" : "Failed",
            NotificationStatusCode = channelResults[0].StatusCode,
            NotificationError = failed.Count == 0 ? "" : string.Join("; ", failed.Select(r => $"{r.ChannelName}: {r.Error}")),
            ChannelResults = channelResults,
        };
    }

    /// <summary>The evaluation's observation fields, shared by a notified fire, a maintenance-suppressed fire and a resolution; the caller sets the notification outcome.</summary>
    private static AlertHistoryEntry BuildHistoryEntry(AlertRule rule, DateTimeOffset now, bool noData, ulong observedCount, double? observedValue, AnomalyScore? anomaly) => new()
    {
        EventId = Guid.NewGuid(),
        RuleId = rule.Id,
        RuleName = rule.Name,
        FiredAt = now,
        ObservedCount = observedCount,
        ThresholdCount = rule.Threshold.Count,
        WindowSeconds = noData ? rule.NoDataWindowSeconds : rule.WindowSeconds,
        NotificationStatus = "",
        ConditionKind = rule.ConditionKind,
        ObservedValue = observedValue,
        // An anomaly rule has no fixed threshold - its MetricThresholdValue is a
        // leftover placeholder when the source is a metric, not something to record.
        ThresholdValue = rule.ConditionKind == AlertConditionKind.Anomaly ? null : rule.MetricThresholdValue,
        NoData = noData,
        BaselineMean = anomaly?.BaselineMean,
        ZScore = anomaly?.ZScore,
    };
}
