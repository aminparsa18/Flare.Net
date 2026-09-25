using Flare.Api.Model;

namespace Flare.Api.Alerting;

/// <summary>
/// Pure "is this <see cref="MaintenanceWindow"/> active at this instant?" logic - used by
/// <c>Flare.AlertWorker</c>'s <c>AlertEvaluationWorker</c> to decide whether a breach notifies
/// or is recorded as suppressed, and by <c>GET /api/maintenance-windows</c> to report which
/// windows are active now. See <c>docs-internal/adr/0055-alert-maintenance-windows.md</c>.
/// </summary>
/// <remarks>
/// A recurring window's occurrences start at <see cref="MaintenanceWindow.StartsAt"/>'s
/// local time of day in <see cref="MaintenanceWindow.TimeZone"/> - on every local day
/// (<see cref="MaintenanceWindowRecurrence.Daily"/>) or on each selected local weekday
/// (<see cref="MaintenanceWindowRecurrence.Weekly"/>) - and each lasts
/// <c>EndsAt - StartsAt</c>. Computing in local time (rather than repeating a fixed UTC
/// instant every 24h) keeps "02:00 every night" at 02:00 across DST changes. A local start
/// time skipped by a spring-forward transition shifts forward by an hour; one repeated by a
/// fall-back transition uses its first (earlier) instant.
/// </remarks>
public static class MaintenanceWindowSchedule
{
    /// <summary>The first window in <paramref name="windows"/> covering <paramref name="ruleId"/> that is active at <paramref name="now"/>, or null.</summary>
    public static MaintenanceWindow? FindActive(IEnumerable<MaintenanceWindow> windows, Guid ruleId, DateTimeOffset now) =>
        windows.FirstOrDefault(w => Covers(w, ruleId) && IsActive(w, now));

    /// <summary>True when <paramref name="window"/> silences <paramref name="ruleId"/> - an empty <see cref="MaintenanceWindow.RuleIds"/> covers every rule.</summary>
    public static bool Covers(MaintenanceWindow window, Guid ruleId) =>
        window.RuleIds.Count == 0 || window.RuleIds.Contains(ruleId);

    public static bool IsActive(MaintenanceWindow window, DateTimeOffset now)
    {
        if (now < window.StartsAt)
        {
            return false;
        }

        if (window.Recurrence == MaintenanceWindowRecurrence.None)
        {
            return now < window.EndsAt;
        }

        var duration = window.EndsAt - window.StartsAt;
        if (duration <= TimeSpan.Zero)
        {
            return false;
        }

        var zone = ResolveTimeZone(window.TimeZone);
        var timeOfDay = TimeZoneInfo.ConvertTime(window.StartsAt, zone).TimeOfDay;
        var today = TimeZoneInfo.ConvertTime(now, zone).Date;

        // Any occurrence covering `now` started at most `duration` ago, so only local days back
        // that far can hold it (+1 for the day boundary shifting under a DST offset change).
        var lookbackDays = (int)Math.Ceiling(duration.TotalDays) + 1;
        for (var day = today.AddDays(-lookbackDays); day <= today; day = day.AddDays(1))
        {
            if (window.Recurrence == MaintenanceWindowRecurrence.Weekly && !window.DaysOfWeek.Contains(day.DayOfWeek))
            {
                continue;
            }

            var start = ToInstant(day + timeOfDay, zone);
            if (start < window.StartsAt || (window.RepeatUntil is { } until && start >= until))
            {
                continue;
            }

            if (now >= start && now < start + duration)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// <paramref name="timeZoneId"/>'s zone, or UTC when the host can't resolve it (the
    /// create/update endpoints already reject unknown ids, so this only matters for a host
    /// missing time-zone data that the API host had).
    /// </summary>
    internal static TimeZoneInfo ResolveTimeZone(string timeZoneId) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone) ? zone : TimeZoneInfo.Utc;

    private static DateTimeOffset ToInstant(DateTime local, TimeZoneInfo zone)
    {
        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }
}
