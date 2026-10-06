namespace Flare.Api.Model;

/// <summary>
/// An on-call rotation: <see cref="ChannelIds"/> (notification channels, one per person or team)
/// take turns, each for <see cref="ShiftHours"/>, starting with the first at
/// <see cref="StartsAt"/> and repeating. A rule with <see cref="AlertRule.EscalationRotationId"/>
/// escalates to whichever channel is on call at that moment. Who that is is
/// <c>Alerting.OnCallSchedule.Resolve</c>. JSON only - not MemoryPack'd, like the other small
/// config responses the dashboard reads. See <c>docs-internal/adr/0126-alert-oncall-rotations.md</c>.
/// </summary>
public sealed record OnCallRotation
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = "";

    /// <summary>Participants in shift order.</summary>
    public IReadOnlyList<Guid> ChannelIds { get; init; } = [];

    public int ShiftHours { get; init; } = 168;

    /// <summary>Start of the first shift (participant 0). Before it, participant 0 is on call.</summary>
    public required DateTimeOffset StartsAt { get; init; }

    /// <summary>One-off swaps: while one covers the current instant, its channel is on call instead of the scheduled participant.</summary>
    public IReadOnlyList<OnCallOverride> Overrides { get; init; } = [];

    /// <summary>When set, the rotation only pages inside this weekly window (ADR-0139); null means always.</summary>
    public OnCallCoverage? Coverage { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>A one-off swap in an <see cref="OnCallRotation"/>: <see cref="ChannelId"/> is on call from <see cref="StartsAt"/> (inclusive) to <see cref="EndsAt"/> (exclusive).</summary>
public sealed record OnCallOverride(DateTimeOffset StartsAt, DateTimeOffset EndsAt, Guid ChannelId);

/// <summary>
/// A weekly window an <see cref="OnCallRotation"/> is limited to, read in <see cref="TimeZone"/>.
/// <see cref="Days"/> are <see cref="DayOfWeek"/> values (0 = Sunday); the window runs from
/// <see cref="StartMinute"/> to <see cref="EndMinute"/> (minutes after local midnight, 0-1440).
/// An end before the start wraps past midnight and belongs to the day it starts on.
/// </summary>
public sealed record OnCallCoverage(string TimeZone, IReadOnlyList<int> Days, int StartMinute, int EndMinute)
{
    /// <summary>Returns an error message, or null when this window is valid.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(TimeZone) || !Alerting.OnCallSchedule.TryFindZone(TimeZone, out _))
        {
            return "coverage.timeZone must be a known IANA time zone id.";
        }

        if (Days is not { Count: > 0 } || Days.Any(d => d is < 0 or > 6))
        {
            return "coverage.days must list at least one weekday, 0 (Sunday) to 6.";
        }

        if (StartMinute is < 0 or > 1439 || EndMinute is < 1 or > 1440 || StartMinute == EndMinute)
        {
            return "coverage needs a start in 0-1439 and an end in 1-1440 that differs from it.";
        }

        return null;
    }
}

/// <summary>Create/update body for <c>/api/oncall-rotations</c>.</summary>
public sealed record OnCallRotationRequest
{
    public const int MaxNameLength = 200;
    public const int MaxParticipants = 50;
    public const int MaxShiftHours = 24 * 365;
    public const int MaxOverrides = 100;

    public required string Name { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<Guid>? ChannelIds { get; init; }

    public int? ShiftHours { get; init; }

    public required DateTimeOffset StartsAt { get; init; }

    public IReadOnlyList<OnCallOverride>? Overrides { get; init; }

    public OnCallCoverage? Coverage { get; init; }

    /// <summary>Returns an error message, or null when this request is valid.</summary>
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

        if (ChannelIds is not { Count: > 0 })
        {
            return "channelIds must list at least one channel.";
        }

        if (ChannelIds.Count > MaxParticipants)
        {
            return $"channelIds can list at most {MaxParticipants} channels.";
        }

        // The same channel may appear twice (a person covering two slots), so repeats are allowed;
        // an empty id is not.
        if (ChannelIds.Any(id => id == Guid.Empty))
        {
            return "channelIds must not contain an empty id.";
        }

        if (ShiftHours is null or < 1 or > MaxShiftHours)
        {
            return $"shiftHours must be between 1 and {MaxShiftHours}.";
        }

        if (Overrides is { Count: > MaxOverrides })
        {
            return $"overrides can list at most {MaxOverrides} entries.";
        }

        // Overlaps are allowed (the latest-starting one wins); an empty or backwards range is not.
        if (Overrides?.Any(o => o.ChannelId == Guid.Empty || o.EndsAt <= o.StartsAt) == true)
        {
            return "each override needs a channel and an end after its start.";
        }

        return Coverage?.Validate();
    }
}

/// <summary>A rotation plus who is on call right now - one row of <see cref="OnCallRotationListResponse"/>.</summary>
/// <param name="OnCallChannelId">The channel on call at the request time.</param>
/// <param name="ShiftEndsAt">When that shift ends.</param>
/// <param name="NextChannelId">Who takes over at <paramref name="ShiftEndsAt"/>.</param>
/// <param name="IsOverride">True when <paramref name="OnCallChannelId"/> is a one-off override rather than the scheduled participant.</param>
/// <param name="InCoverage">False when the rotation's <see cref="OnCallRotation.Coverage"/> window is closed, so nobody is paged
/// (<paramref name="OnCallChannelId"/> is then who the schedule would page). Overrides always count as covered.</param>
public sealed record OnCallRotationStatus(OnCallRotation Rotation, Guid OnCallChannelId, DateTimeOffset ShiftEndsAt, Guid NextChannelId, bool IsOverride = false, bool InCoverage = true);

/// <summary>Response body for <c>GET /api/oncall-rotations</c>.</summary>
public sealed record OnCallRotationListResponse(IReadOnlyList<OnCallRotationStatus> Rotations);
