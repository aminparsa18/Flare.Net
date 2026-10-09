using Flare.Api.Model;

namespace Flare.Api.Status;

/// <summary>
/// The pure rules that turn probe results and error budgets into what a status page shows, kept
/// free of ClickHouse so they can be unit-tested. See <c>docs-internal/adr/0158-status-pages.md</c>.
/// </summary>
public static class StatusEvaluator
{
    /// <summary>How many days of history a page shows.</summary>
    public const int HistoryDays = 90;

    /// <summary>
    /// A monitor is Unknown when it has never reported or its newest result is older than three
    /// intervals (a stopped or disabled monitor must not keep showing its last green result);
    /// Operational when every reporting location is up, Outage when none is, Degraded otherwise.
    /// </summary>
    public static StatusState MonitorState(SyntheticMonitor monitor, IReadOnlyList<SyntheticLocationStatus> locations, DateTimeOffset now)
    {
        var staleAfter = TimeSpan.FromSeconds(Math.Max(monitor.IntervalSeconds * 3, 120));
        var fresh = locations.Where(l => now - l.Status.Time <= staleAfter).ToList();
        if (!monitor.Enabled || fresh.Count == 0)
        {
            return StatusState.Unknown;
        }

        var up = fresh.Count(l => l.Status.Up);
        return up == fresh.Count ? StatusState.Operational : up == 0 ? StatusState.Outage : StatusState.Degraded;
    }

    /// <summary>An SLO is Operational while it has budget left, Degraded once overspent, Unknown with no traffic.</summary>
    public static StatusState SloState(double? errorBudgetRemaining) =>
        errorBudgetRemaining is not { } remaining ? StatusState.Unknown : remaining >= 0 ? StatusState.Operational : StatusState.Degraded;

    /// <summary>The worst component state wins; Unknown components are ignored unless nothing else is known.</summary>
    public static StatusState Overall(IReadOnlyCollection<StatusState> states)
    {
        if (states.Contains(StatusState.Outage))
        {
            return StatusState.Outage;
        }

        if (states.Contains(StatusState.Degraded))
        {
            return StatusState.Degraded;
        }

        return states.Contains(StatusState.Operational) ? StatusState.Operational : StatusState.Unknown;
    }

    /// <summary>The last <paramref name="count"/> UTC days ending at <paramref name="today"/>, oldest first, null where <paramref name="byDay"/> has nothing.</summary>
    public static IReadOnlyList<StatusDay> Days(IReadOnlyDictionary<DateOnly, double> byDay, DateOnly today, int count = HistoryDays) =>
        Enumerable.Range(0, count)
            .Select(i => today.AddDays(i - (count - 1)))
            .Select(day => new StatusDay(day.ToString("yyyy-MM-dd"), byDay.TryGetValue(day, out var percent) ? Math.Round(percent, 3) : null))
            .ToList();

    /// <summary>Mean of the days that have data, or null when none do.</summary>
    public static double? UptimePercent(IReadOnlyList<StatusDay> days)
    {
        var recorded = days.Where(d => d.UptimePercent is not null).Select(d => d.UptimePercent!.Value).ToList();
        return recorded.Count == 0 ? null : Math.Round(recorded.Average(), 3);
    }
}
