using Flare.Api.Model;

namespace Flare.Api.Alerting;

/// <summary>
/// Whether a rule with a per-rule <see cref="AlertRule.EvaluationIntervalSeconds"/> is due
/// this poll tick. Pure and Redis-free - <c>AlertEvaluationWorker</c> owns reading/writing the
/// per-rule last-evaluated timestamp; this only decides. Lives here rather than in
/// <c>Flare.AlertWorker</c> (which has no test project) for the same reason
/// <see cref="AlertNoDataEvaluator"/> does: so it's unit-tested in <c>Flare.Api.Tests</c>.
/// See <c>docs-internal/adr/0046-per-rule-alert-evaluation-interval.md</c>.
/// </summary>
public static class AlertEvaluationSchedule
{
    /// <summary>
    /// True when the rule should be evaluated at <paramref name="now"/>: always for a 0
    /// (every-tick) interval or a rule never evaluated before (or whose last-evaluated marker
    /// expired); otherwise once at least the interval, minus half a poll interval, has
    /// elapsed since <paramref name="lastEvaluatedAt"/>.
    /// </summary>
    /// <remarks>
    /// The half-poll tolerance absorbs tick jitter - ticks drift by however long each one
    /// takes, and under more than one replica the lock holder's tick phase differs per tick.
    /// Without it, a 60s rule on a 30s poll whose next tick lands at 59.9s would wait a full
    /// extra tick (to ~90s). With it, the effective interval stays within
    /// <c>interval ± pollInterval/2</c>.
    /// </remarks>
    public static bool IsDue(int evaluationIntervalSeconds, DateTimeOffset? lastEvaluatedAt, DateTimeOffset now, TimeSpan pollInterval)
    {
        if (evaluationIntervalSeconds <= 0 || lastEvaluatedAt is not { } last)
        {
            return true;
        }

        return now - last >= TimeSpan.FromSeconds(evaluationIntervalSeconds) - pollInterval / 2;
    }
}
