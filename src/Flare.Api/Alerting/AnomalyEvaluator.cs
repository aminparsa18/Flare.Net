using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Alerting;

/// <summary>
/// Runs an <see cref="AlertConditionKind.Anomaly"/> rule's queries - the source series over the
/// current window and each baseline window - and scores them via <see cref="AnomalyScoring"/>.
/// Shared by <c>AlertEvaluationWorker</c> and the <c>/api/alerts/*/test</c> dry-run endpoints,
/// same reasoning as <see cref="AlertNoDataEvaluator"/>. See
/// <c>docs-internal/adr/0048-anomaly-detection-alerting.md</c>.
/// </summary>
/// <remarks>
/// Queries run one after another rather than concurrently: an anomaly rule already costs
/// up to <see cref="AnomalyCondition.MaxBaselinePeriods"/> + 1 queries, and bursting them all
/// at ClickHouse at once would make one rule's evaluation a load spike for every other query.
/// </remarks>
public static class AnomalyEvaluator
{
    /// <summary>Null when the source's own condition is missing (an incomplete draft) - reported as "wouldn't fire", same as the other kinds' dry runs.</summary>
    public static async Task<(AnomalyScore Score, string? MetricUnit)?> EvaluateAsync(
        IAlertQueryService alerts,
        AnomalyCondition anomaly,
        LogFilter condition,
        MetricAlertCondition? metricCondition,
        ExceptionCountCondition? exceptionCondition,
        int windowSeconds,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Func<DateTimeOffset, DateTimeOffset, Task<(double Value, string? Unit)>>? series = anomaly.Source switch
        {
            AlertConditionKind.LogCount =>
                async (from, to) => (await alerts.CountMatchingLogsAsync(condition, from, to, cancellationToken), null),
            AlertConditionKind.MetricThreshold when metricCondition is not null =>
                (from, to) => alerts.EvaluateMetricConditionAsync(metricCondition, from, to, cancellationToken),
            AlertConditionKind.ExceptionCount when exceptionCondition is not null =>
                async (from, to) => (await alerts.CountMatchingExceptionsAsync(exceptionCondition, from, to, cancellationToken), null),
            _ => null,
        };

        if (series is null)
        {
            return null;
        }

        var window = TimeSpan.FromSeconds(windowSeconds);
        var (current, unit) = await series(now - window, now);

        var baseline = new List<double>(anomaly.BaselinePeriods);
        foreach (var (from, to) in AnomalyScoring.BaselineWindows(anomaly, window, now))
        {
            baseline.Add((await series(from, to)).Value);
        }

        return (AnomalyScoring.Score(current, baseline, anomaly), unit);
    }
}
