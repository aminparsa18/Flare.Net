using Flare.Api.Model;

namespace Flare.Api.Alerting;

/// <summary>
/// Who is on call in an <see cref="OnCallRotation"/> at an instant - pure, so it's unit-testable
/// without ClickHouse. See <c>docs-internal/adr/0126-alert-oncall-rotations.md</c>.
/// </summary>
public static class OnCallSchedule
{
    /// <summary>
    /// The participant on call at <paramref name="now"/>: shift <c>floor((now - StartsAt) / ShiftHours)</c>
    /// modulo the participant count. Before <see cref="OnCallRotation.StartsAt"/> the first
    /// participant is on call, with the first shift ending at its start.
    /// </summary>
    public static OnCallRotationStatus Resolve(OnCallRotation rotation, DateTimeOffset now)
    {
        var count = rotation.ChannelIds.Count;
        var shift = TimeSpan.FromHours(rotation.ShiftHours);
        if (now < rotation.StartsAt)
        {
            return new OnCallRotationStatus(rotation, rotation.ChannelIds[0], rotation.StartsAt, rotation.ChannelIds[0]);
        }

        var index = (long)Math.Floor((now - rotation.StartsAt) / shift);
        var slot = (int)(index % count);
        return new OnCallRotationStatus(
            rotation,
            rotation.ChannelIds[slot],
            rotation.StartsAt + (shift * (index + 1)),
            rotation.ChannelIds[(slot + 1) % count]);
    }

    /// <summary>
    /// The channels an escalation goes to: the rule's fixed escalation channels plus the one on
    /// call in <paramref name="rotation"/> (null when the rule has none, or it was deleted), without repeats.
    /// </summary>
    public static IReadOnlyList<Guid> EscalationTargets(IReadOnlyList<Guid> fixedChannelIds, OnCallRotation? rotation, DateTimeOffset now)
    {
        if (rotation is null || rotation.ChannelIds.Count == 0)
        {
            return fixedChannelIds;
        }

        return [.. fixedChannelIds.Append(Resolve(rotation, now).OnCallChannelId).Distinct()];
    }
}
