using Flare.Api.Model;

namespace Flare.Api.Alerting;

/// <summary>
/// When an unacknowledged incident escalates, and whether a rule's escalation settings are valid -
/// pure, so it's unit-testable without ClickHouse. See
/// <c>docs-internal/adr/0125-alert-escalation.md</c>.
/// </summary>
public static class AlertEscalationPolicy
{
    public const int MaxEscalateAfterMinutes = 7 * 24 * 60;

    /// <summary>Prefix added to the rule name on an escalation, so the channel can tell it from the first page.</summary>
    public const string NamePrefix = "[Escalated] ";

    /// <summary>
    /// True when <paramref name="state"/>'s incident should take its first escalation step now: the
    /// rule has escalation on, someone was actually paged, the incident is
    /// <see cref="AlertRule.EscalateAfterMinutes"/> old, it hasn't escalated yet, and nobody has
    /// acknowledged it. A snooze does not stop it - it only mutes re-notifications - and a
    /// maintenance window defers it, like every other notification.
    /// </summary>
    public static bool IsDue(AlertRule rule, AlertFiringState? state, bool maintenanceWindowActive, DateTimeOffset now) =>
        NextStepDue(rule, state, maintenanceWindowActive, now) == 1;

    /// <summary>
    /// The escalation step (1 or 2) that is due for <paramref name="state"/>'s incident, or 0 when
    /// none is. Step 2 (ADR-0136) is due <see cref="AlertRule.SecondEscalateAfterMinutes"/> after
    /// step 1 fired, under the same conditions: still unacknowledged, not in a maintenance window.
    /// At most one step is due per tick, so a long outage cannot fire both at once.
    /// </summary>
    public static int NextStepDue(AlertRule rule, AlertFiringState? state, bool maintenanceWindowActive, DateTimeOffset now)
    {
        if (rule.EscalateAfterMinutes <= 0 || (rule.EscalationChannelIds.Count == 0 && rule.EscalationRotationId is null) || state is null)
        {
            return 0;
        }

        if (!state.Notified || state.IncidentNotifiedAt is not { } notifiedAt || maintenanceWindowActive || state.Ack is { Kind: AlertAckKind.Ack })
        {
            return 0;
        }

        if (state.EscalatedAt is null)
        {
            return now - notifiedAt >= TimeSpan.FromMinutes(rule.EscalateAfterMinutes) ? 1 : 0;
        }

        if (rule.SecondEscalateAfterMinutes > 0 && rule.SecondEscalationChannelIds.Count > 0 && state.SecondEscalatedAt is null
            && now - state.EscalatedAt.Value >= TimeSpan.FromMinutes(rule.SecondEscalateAfterMinutes))
        {
            return 2;
        }

        return 0;
    }

    /// <summary>Validates a rule's escalation settings; returns an error message or null.</summary>
    public static string? Validate(int escalateAfterMinutes, IReadOnlyList<Guid>? channelIds, Guid? rotationId = null, int secondEscalateAfterMinutes = 0, IReadOnlyList<Guid>? secondChannelIds = null)
    {
        var second = ValidateSecondStep(escalateAfterMinutes, secondEscalateAfterMinutes, secondChannelIds);
        if (second is not null)
        {
            return second;
        }

        if (escalateAfterMinutes is < 0 or > MaxEscalateAfterMinutes)
        {
            return $"escalateAfterMinutes must be 0 (off) or between 1 and {MaxEscalateAfterMinutes} (7 days).";
        }

        if (escalateAfterMinutes == 0)
        {
            return null;
        }

        if (rotationId == Guid.Empty)
        {
            return "escalationRotationId must not be an empty id.";
        }

        if (channelIds is not { Count: > 0 })
        {
            return rotationId is null ? "escalationChannelIds must list at least one channel (or set escalationRotationId) when escalateAfterMinutes is set." : null;
        }

        return channelIds.Distinct().Count() != channelIds.Count ? "escalationChannelIds must not repeat a channel." : null;
    }

    private static string? ValidateSecondStep(int escalateAfterMinutes, int secondEscalateAfterMinutes, IReadOnlyList<Guid>? secondChannelIds)
    {
        if (secondEscalateAfterMinutes is < 0 or > MaxEscalateAfterMinutes)
        {
            return $"secondEscalateAfterMinutes must be 0 (off) or between 1 and {MaxEscalateAfterMinutes} (7 days).";
        }

        if (secondEscalateAfterMinutes == 0)
        {
            return secondChannelIds is { Count: > 0 } ? "secondEscalationChannelIds needs secondEscalateAfterMinutes." : null;
        }

        if (escalateAfterMinutes == 0)
        {
            return "secondEscalateAfterMinutes needs escalateAfterMinutes (the first step).";
        }

        if (secondChannelIds is not { Count: > 0 })
        {
            return "secondEscalationChannelIds must list at least one channel when secondEscalateAfterMinutes is set.";
        }

        return secondChannelIds.Distinct().Count() != secondChannelIds.Count ? "secondEscalationChannelIds must not repeat a channel." : null;
    }
}
