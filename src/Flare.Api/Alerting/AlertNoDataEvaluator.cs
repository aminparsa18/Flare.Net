using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Alerting;

/// <summary>
/// Absent-data ("no data") check for <see cref="AlertRule.NoDataWindowSeconds"/> - shared by
/// <c>AlertEvaluationWorker</c> (the real evaluation loop) and the <c>/api/alerts/*/test</c>
/// dry-run endpoints, so "would this fire on no data" means the same thing in both, same
/// reasoning <see cref="AlertThreshold.IsBreached"/> is shared. See
/// <c>docs-internal/adr/0045-absent-data-alerting.md</c>.
/// </summary>
public static class AlertNoDataEvaluator
{
    /// <summary>
    /// True when <paramref name="noDataWindowSeconds"/> is enabled (&gt; 0) and the condition
    /// matched nothing at all over <c>[now - noDataWindowSeconds, now]</c>. Always false for
    /// a disabled window, for <see cref="AlertConditionKind.ExceptionCount"/> (unsupported -
    /// see <see cref="AlertRule.NoDataWindowSeconds"/>), and for a
    /// <see cref="AlertConditionKind.MetricThreshold"/> rule/draft with no
    /// <paramref name="metricCondition"/> yet (nothing to count against - an incomplete draft,
    /// not a silent source).
    /// </summary>
    public static async Task<bool> IsAbsentAsync(
        IAlertQueryService alerts,
        AlertConditionKind conditionKind,
        LogFilter condition,
        MetricAlertCondition? metricCondition,
        int noDataWindowSeconds,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (noDataWindowSeconds <= 0)
        {
            return false;
        }

        var from = now - TimeSpan.FromSeconds(noDataWindowSeconds);
        return conditionKind switch
        {
            AlertConditionKind.LogCount =>
                await alerts.CountMatchingLogsAsync(condition, from, now, cancellationToken) == 0,
            AlertConditionKind.MetricThreshold when metricCondition is not null =>
                await alerts.CountMatchingMetricPointsAsync(metricCondition, from, now, cancellationToken) == 0,
            _ => false,
        };
    }
}
