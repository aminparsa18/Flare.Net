using Flare.Api.Model;

namespace Flare.Api.Alerting;

/// <summary>The outcome of scoring one <see cref="AlertConditionKind.Anomaly"/> evaluation - see <see cref="AnomalyScoring.Score"/>.</summary>
/// <param name="Current">The source series over the current window. <see cref="double.NaN"/> when a metric source reported nothing.</param>
/// <param name="SampleCount">Baseline windows that had data (after dropping missing ones).</param>
/// <param name="BaselineMean">Null when <paramref name="SampleCount"/> is below <see cref="AnomalyScoring.MinBaselineSamples"/>.</param>
/// <param name="ZScore">Null alongside <paramref name="BaselineMean"/>, and when <paramref name="Current"/> is <see cref="double.NaN"/>.</param>
public sealed record AnomalyScore(double Current, int SampleCount, double? BaselineMean, double? ZScore, bool Breached);

/// <summary>
/// Pure z-score math for <see cref="AlertConditionKind.Anomaly"/> rules, free of any query so
/// it's unit-testable on its own. <see cref="AnomalyEvaluator"/> supplies the values. See
/// <c>docs-internal/adr/0048-anomaly-detection-alerting.md</c> for why each rule below is
/// what it is.
/// </summary>
public static class AnomalyScoring
{
    /// <summary>Fewer baseline windows with data than this and the rule doesn't score at all ("not enough history yet").</summary>
    public const int MinBaselineSamples = 3;

    /// <summary>σ is floored at this fraction of |mean|, so a near-constant baseline doesn't turn a 1% wobble into z = 50.</summary>
    public const double MinRelativeStdDev = 0.05;

    /// <summary>
    /// The condition kind whose series a rule actually queries: <see cref="AnomalyCondition.Source"/>
    /// for an <see cref="AlertConditionKind.Anomaly"/> rule, <paramref name="kind"/> itself
    /// otherwise. What absent-data checks and notifier payloads branch on.
    /// </summary>
    public static AlertConditionKind SeriesKind(AlertConditionKind kind, AnomalyCondition? anomaly) =>
        kind == AlertConditionKind.Anomaly && anomaly is not null ? anomaly.Source : kind;

    public static TimeSpan PeriodOf(AnomalySeasonality seasonality) =>
        seasonality == AnomalySeasonality.Weekly ? TimeSpan.FromDays(7) : TimeSpan.FromDays(1);

    /// <summary>
    /// The baseline windows for a current window <c>[now - window, now]</c>: the same window
    /// shifted back by 1..<see cref="AnomalyCondition.BaselinePeriods"/> seasonal periods,
    /// most recent first.
    /// </summary>
    public static IReadOnlyList<(DateTimeOffset From, DateTimeOffset To)> BaselineWindows(AnomalyCondition condition, TimeSpan window, DateTimeOffset now)
    {
        var period = PeriodOf(condition.Seasonality);
        var windows = new List<(DateTimeOffset, DateTimeOffset)>(condition.BaselinePeriods);
        for (var i = 1; i <= condition.BaselinePeriods; i++)
        {
            var to = now - (period * i);
            windows.Add((to - window, to));
        }

        return windows;
    }

    /// <summary>
    /// Scores <paramref name="current"/> against <paramref name="baseline"/>. Baseline samples
    /// that are <see cref="double.NaN"/> or exactly 0 are treated as missing (ADR-0048,
    /// Scoring step 3); <paramref name="current"/> is never dropped - a current 0 is exactly
    /// the "traffic stopped" case this exists for. A <see cref="double.NaN"/> current never
    /// breaches.
    /// </summary>
    public static AnomalyScore Score(double current, IReadOnlyList<double> baseline, AnomalyCondition condition)
    {
        var samples = baseline.Where(v => !double.IsNaN(v) && v != 0).ToList();
        if (samples.Count < MinBaselineSamples)
        {
            return new AnomalyScore(current, samples.Count, null, null, false);
        }

        var mean = samples.Average();
        var stdDev = Math.Sqrt(samples.Sum(v => (v - mean) * (v - mean)) / samples.Count);
        var effectiveStdDev = Math.Max(stdDev, MinRelativeStdDev * Math.Abs(mean));
        if (double.IsNaN(current) || effectiveStdDev == 0)
        {
            return new AnomalyScore(current, samples.Count, mean, null, false);
        }

        var z = (current - mean) / effectiveStdDev;
        var breached = condition.Direction switch
        {
            AnomalyDirection.Above => z >= condition.ZScoreThreshold,
            AnomalyDirection.Below => z <= -condition.ZScoreThreshold,
            _ => Math.Abs(z) >= condition.ZScoreThreshold,
        };

        return new AnomalyScore(current, samples.Count, mean, z, breached);
    }
}
