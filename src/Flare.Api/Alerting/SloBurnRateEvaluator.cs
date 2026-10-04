using Flare.Api.Model;
using Flare.Api.Query;
using Flare.Api.Slos;

namespace Flare.Api.Alerting;

/// <summary>The outcome of evaluating one <see cref="AlertConditionKind.SloBurnRate"/> rule.</summary>
/// <param name="LongBurnRate">Burn rate over the long window; null when it had no events.</param>
/// <param name="ShortBurnRate">Burn rate over the short window; null when it had no events.</param>
/// <param name="Breached">Both windows at or above the threshold.</param>
public sealed record SloBurnRateResult(double? LongBurnRate, double? ShortBurnRate, bool Breached);

/// <summary>
/// Runs a burn-rate rule's one query - good/bad counts of its SLO over the long and short
/// windows - and applies <see cref="SloCalculator.IsBurning"/>. Shared by
/// <c>AlertEvaluationWorker</c> and the dry-run test endpoints so "would this fire" means the
/// same thing in both. See <c>docs-internal/adr/0108-slo-error-budgets.md</c>.
/// </summary>
public static class SloBurnRateEvaluator
{
    /// <summary>Null when the rule's SLO no longer exists (deleted after the rule was saved).</summary>
    public static async Task<SloBurnRateResult?> EvaluateAsync(ISloQueryService slos, SloBurnRateCondition condition, CancellationToken cancellationToken)
    {
        var slo = await slos.GetAsync(condition.SloId, cancellationToken);
        if (slo is null)
        {
            return null;
        }

        var stats = await slos.GetWindowStatsAsync(slo, [condition.LongWindowSeconds, condition.ShortWindowSeconds], cancellationToken);
        return Decide(stats[0].BurnRate, stats[1].BurnRate, condition.BurnRateThreshold);
    }

    internal static SloBurnRateResult Decide(double? longBurnRate, double? shortBurnRate, double threshold) =>
        new(longBurnRate, shortBurnRate, SloCalculator.IsBurning(longBurnRate, shortBurnRate, threshold));
}
