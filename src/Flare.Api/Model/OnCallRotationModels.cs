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

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Create/update body for <c>/api/oncall-rotations</c>.</summary>
public sealed record OnCallRotationRequest
{
    public const int MaxNameLength = 200;
    public const int MaxParticipants = 50;
    public const int MaxShiftHours = 24 * 365;

    public required string Name { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<Guid>? ChannelIds { get; init; }

    public int? ShiftHours { get; init; }

    public required DateTimeOffset StartsAt { get; init; }

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

        return ShiftHours is null or < 1 or > MaxShiftHours ? $"shiftHours must be between 1 and {MaxShiftHours}." : null;
    }
}

/// <summary>A rotation plus who is on call right now - one row of <see cref="OnCallRotationListResponse"/>.</summary>
/// <param name="OnCallChannelId">The channel on call at the request time.</param>
/// <param name="ShiftEndsAt">When that shift ends.</param>
/// <param name="NextChannelId">Who takes over at <paramref name="ShiftEndsAt"/>.</param>
public sealed record OnCallRotationStatus(OnCallRotation Rotation, Guid OnCallChannelId, DateTimeOffset ShiftEndsAt, Guid NextChannelId);

/// <summary>Response body for <c>GET /api/oncall-rotations</c>.</summary>
public sealed record OnCallRotationListResponse(IReadOnlyList<OnCallRotationStatus> Rotations);
