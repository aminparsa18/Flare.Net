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
    /// True when <paramref name="state"/>'s incident should be escalated now: the rule has
    /// escalation on, someone was actually paged, the incident is
    /// <see cref="AlertRule.EscalateAfterMinutes"/> old, it hasn't escalated yet, and nobody has
    /// acknowledged it. A snooze does not stop it - it only mutes re-notifications - and a
    /// maintenance window defers it, like every other notification.
    /// </summary>
    public static bool IsDue(AlertRule rule, AlertFiringState? state, bool maintenanceWindowActive, DateTimeOffset now)
    {
        if (rule.EscalateAfterMinutes <= 0 || rule.EscalationChannelIds.Count == 0 || state is null)
        {
            return false;
        }

        if (!state.Notified || state.IncidentNotifiedAt is not { } notifiedAt || state.EscalatedAt is not null || maintenanceWindowActive)
        {
            return false;
        }

        if (state.Ack is { Kind: AlertAckKind.Ack })
        {
            return false;
        }

        return now - notifiedAt >= TimeSpan.FromMinutes(rule.EscalateAfterMinutes);
    }

    /// <summary>Validates a rule's escalation settings; returns an error message or null.</summary>
    public static string? Validate(int escalateAfterMinutes, IReadOnlyList<Guid>? channelIds)
    {
        if (escalateAfterMinutes is < 0 or > MaxEscalateAfterMinutes)
        {
            return $"escalateAfterMinutes must be 0 (off) or between 1 and {MaxEscalateAfterMinutes} (7 days).";
        }

        if (escalateAfterMinutes == 0)
        {
            return null;
        }

        if (channelIds is not { Count: > 0 })
        {
            return "escalationChannelIds must list at least one channel when escalateAfterMinutes is set.";
        }

        return channelIds.Distinct().Count() != channelIds.Count ? "escalationChannelIds must not repeat a channel." : null;
    }
}
