using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Alerting;

/// <summary>
/// Minimum-sample-size check for <see cref="AlertRule.MinDataPoints"/> - shared by
/// <c>AlertEvaluationWorker</c> and the <c>/api/alerts/*/test</c> dry-run endpoints, same
/// reasoning <see cref="AlertNoDataEvaluator"/> is shared. See
/// <c>docs-internal/adr/0050-alert-minimum-data-points.md</c>.
/// </summary>
public static class AlertMinDataPointsEvaluator
{
    /// <summary>
    /// The raw point count <paramref name="metricCondition"/> matched over
    /// <c>[from, to]</c> when <paramref name="minDataPoints"/> is enabled (&gt; 0), or null
    /// when it's disabled - in which case nothing is queried, so a rule that doesn't opt in
    /// pays no extra ClickHouse round-trip.
    /// </summary>
    public static async Task<ulong?> CountAsync(
        IAlertQueryService alerts,
        MetricAlertCondition metricCondition,
        int minDataPoints,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken) =>
        minDataPoints > 0
            ? await alerts.CountMatchingMetricPointsAsync(metricCondition, from, to, cancellationToken)
            : null;

    /// <summary>True when <paramref name="pointCount"/> (from <see cref="CountAsync"/>) is below an enabled <paramref name="minDataPoints"/>.</summary>
    public static bool IsInsufficient(int minDataPoints, ulong? pointCount) =>
        minDataPoints > 0 && pointCount is { } count && count < (ulong)minDataPoints;
}
