using MemoryPack;

namespace Flare.Api.Model;

/// <summary>How a <see cref="MaintenanceWindow"/> repeats. See <c>docs-internal/adr/0055-alert-maintenance-windows.md</c>.</summary>
public enum MaintenanceWindowRecurrence
{
    /// <summary>One-off: active from <see cref="MaintenanceWindow.StartsAt"/> to <see cref="MaintenanceWindow.EndsAt"/> only.</summary>
    None,

    /// <summary>Every day, at <see cref="MaintenanceWindow.StartsAt"/>'s local time of day.</summary>
    Daily,

    /// <summary>On each of <see cref="MaintenanceWindow.DaysOfWeek"/>, at <see cref="MaintenanceWindow.StartsAt"/>'s local time of day.</summary>
    Weekly,
}

/// <summary>
/// A planned maintenance window: while one is active, <c>Flare.AlertWorker</c> still evaluates
/// the rules it covers but records a breach as a suppressed <see cref="AlertHistoryEntry"/>
/// (<see cref="AlertHistoryEntry.NotificationStatus"/> "Suppressed") instead of notifying.
/// Whether it's active at a given instant is <c>Alerting.MaintenanceWindowSchedule.IsActive</c>.
/// See <c>docs-internal/adr/0055-alert-maintenance-windows.md</c>.
/// </summary>
[MemoryPackable]
public sealed partial record MaintenanceWindow
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = "";

    /// <summary>The alert rules this window silences; empty = every rule.</summary>
    public IReadOnlyList<Guid> RuleIds { get; init; } = [];

    /// <summary>Start of the first (for <see cref="MaintenanceWindowRecurrence.None"/>, the only) occurrence. For a recurring window, also the earliest instant any occurrence can start.</summary>
    public required DateTimeOffset StartsAt { get; init; }

    /// <summary>End of the first occurrence - <c>EndsAt - StartsAt</c> is every occurrence's duration.</summary>
    public required DateTimeOffset EndsAt { get; init; }

    public MaintenanceWindowRecurrence Recurrence { get; init; } = MaintenanceWindowRecurrence.None;

    /// <summary>Meaningful only for <see cref="MaintenanceWindowRecurrence.Weekly"/>: the local (<see cref="TimeZone"/>) weekdays an occurrence starts on.</summary>
    public IReadOnlyList<DayOfWeek> DaysOfWeek { get; init; } = [];

    /// <summary>Recurring only: no occurrence starts at or after this instant. Null = repeats forever.</summary>
    public DateTimeOffset? RepeatUntil { get; init; }

    /// <summary>
    /// IANA time zone id a recurring window's local time of day and weekdays are computed in,
    /// so "every Sunday 02:00 Europe/Berlin" stays at 02:00 local across DST changes. Doesn't
    /// affect a one-off window, whose <see cref="StartsAt"/>/<see cref="EndsAt"/> are absolute.
    /// </summary>
    public string TimeZone { get; init; } = "UTC";

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// Create/update request body for <c>/api/maintenance-windows</c>. Same nullable-optional
/// shape as <see cref="AlertRuleRequest"/>, for the same reason (see that type's remarks);
/// the defaults are resolved in <c>MaintenanceWindowQueryService.ResolveDefaults</c>.
/// </summary>
[MemoryPackable]
public sealed partial record MaintenanceWindowRequest
{
    public const int MaxNameLength = 200;

    /// <summary>Longest occurrence a <see cref="MaintenanceWindowRecurrence.Daily"/> window allows - longer would overlap the next day's occurrence, which is really a one-off window.</summary>
    public static readonly TimeSpan MaxDailyDuration = TimeSpan.FromDays(1);

    /// <summary>Same reasoning as <see cref="MaxDailyDuration"/>, for <see cref="MaintenanceWindowRecurrence.Weekly"/>.</summary>
    public static readonly TimeSpan MaxWeeklyDuration = TimeSpan.FromDays(7);

    public required string Name { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<Guid>? RuleIds { get; init; }

    public required DateTimeOffset StartsAt { get; init; }

    public required DateTimeOffset EndsAt { get; init; }

    public MaintenanceWindowRecurrence? Recurrence { get; init; }

    public IReadOnlyList<DayOfWeek>? DaysOfWeek { get; init; }

    public DateTimeOffset? RepeatUntil { get; init; }

    public string? TimeZone { get; init; }

    /// <summary>Returns an error message, or null when this request is valid. Called by <c>MaintenanceWindowEndpoints</c>'s create/update handlers.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return "name is required.";
        }

        if (Name.Length > MaxNameLength)
        {
            return $"name must be at most {MaxNameLength} characters.";
        }

        if (EndsAt <= StartsAt)
        {
            return "endsAt must be after startsAt.";
        }

        if (!System.TimeZoneInfo.TryFindSystemTimeZoneById(TimeZone ?? "UTC", out _))
        {
            return $"Unknown timeZone '{TimeZone}'.";
        }

        var days = DaysOfWeek ?? [];
        var duration = EndsAt - StartsAt;
        switch (Recurrence ?? MaintenanceWindowRecurrence.None)
        {
            case MaintenanceWindowRecurrence.None:
                if (RepeatUntil is not null || days.Count > 0)
                {
                    return "repeatUntil and daysOfWeek apply only to a recurring window.";
                }

                break;
            case MaintenanceWindowRecurrence.Daily:
                if (days.Count > 0)
                {
                    return "daysOfWeek applies only to a Weekly window.";
                }

                if (duration > MaxDailyDuration)
                {
                    return "A Daily window's occurrence can be at most 24 hours long.";
                }

                break;
            case MaintenanceWindowRecurrence.Weekly:
                if (days.Count == 0)
                {
                    return "A Weekly window needs at least one daysOfWeek entry.";
                }

                if (days.Any(d => !Enum.IsDefined(d)) || days.Distinct().Count() != days.Count)
                {
                    return "daysOfWeek must be distinct days of the week.";
                }

                if (duration > MaxWeeklyDuration)
                {
                    return "A Weekly window's occurrence can be at most 7 days long.";
                }

                break;
            default:
                return "Unknown recurrence.";
        }

        if (RepeatUntil is { } until && until <= StartsAt)
        {
            return "repeatUntil must be after startsAt.";
        }

        return null;
    }
}

/// <summary>Response body for <c>GET /api/maintenance-windows</c>.</summary>
[MemoryPackable]
public sealed partial record MaintenanceWindowListResponse
{
    public required IReadOnlyList<MaintenanceWindow> Windows { get; init; }

    /// <summary>
    /// Ids of <see cref="Windows"/> active right now, per <c>MaintenanceWindowSchedule.IsActive</c> -
    /// computed server-side so the dashboard doesn't reimplement time-zone-aware recurrence.
    /// </summary>
    public IReadOnlyList<Guid> ActiveWindowIds { get; init; } = [];
}
