namespace Flare.Api.Slos;

/// <summary>
/// The error-budget arithmetic, kept pure so the dashboard numbers, the status endpoint and
/// <c>AlertEvaluationWorker</c>'s burn-rate rules all agree. See
/// <c>docs-internal/adr/0108-slo-error-budgets.md</c>.
/// </summary>
/// <remarks>
/// An SLO's target is the percent of events that must be good, so the budget is the allowed
/// bad fraction <c>1 - target/100</c>. The burn rate is how fast that budget is being spent:
/// 1.0 spends exactly the whole budget over the SLO window; 14.4 spends 2% of a 30-day budget
/// in one hour. Every result is null for a window with no events - "no traffic" is neither a
/// breach nor a healthy 100%.
/// </remarks>
public static class SloCalculator
{
    /// <summary>The fraction of events allowed to be bad, e.g. 0.005 for a 99.5% target.</summary>
    public static double AllowedBadFraction(double targetPercent) => 1 - (targetPercent / 100d);

    /// <summary>Percent of good events, or null with no events.</summary>
    public static double? Sli(long total, long bad) =>
        total <= 0 ? null : 100d * (total - bad) / total;

    /// <summary><c>(bad / total) / allowed</c>, or null with no events.</summary>
    public static double? BurnRate(long total, long bad, double targetPercent)
    {
        var allowed = AllowedBadFraction(targetPercent);
        if (total <= 0 || allowed <= 0)
        {
            return null;
        }

        return (double)bad / total / allowed;
    }

    /// <summary>
    /// Fraction of the window's error budget still unspent: 1 = none used, 0 = exactly spent,
    /// negative = overspent. Null with no events.
    /// </summary>
    public static double? BudgetRemaining(long total, long bad, double targetPercent) =>
        BurnRate(total, bad, targetPercent) is { } burn ? 1 - burn : null;

    /// <summary>
    /// The multi-window burn-rate test: both the long window (is the burn real and sustained)
    /// and the short window (is it still happening) must be at or above
    /// <paramref name="threshold"/>. A window with no events never breaches.
    /// </summary>
    public static bool IsBurning(double? longBurnRate, double? shortBurnRate, double threshold) =>
        longBurnRate is { } l && shortBurnRate is { } s && l >= threshold && s >= threshold;
}
