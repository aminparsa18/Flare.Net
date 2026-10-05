using Flare.Api.Model;

namespace Flare.Api.Alerting;

/// <summary>
/// Which ack/snooze applies to a firing incident and whether it silences re-notifications -
/// pure, so it's unit-testable without ClickHouse. See
/// <c>docs-internal/adr/0124-alert-acknowledgement-and-snooze.md</c>.
/// </summary>
public static class AlertAckPolicy
{
    public const int MaxSnoozeMinutes = 7 * 24 * 60;

    /// <summary>
    /// The rule's newest ack action if it applies to the current incident: made after the last
    /// resolution, and not a <see cref="AlertAckKind.Clear"/>. Null otherwise.
    /// </summary>
    public static AlertAck? Effective(AlertAck? latest, DateTimeOffset? lastResolvedAt)
    {
        if (latest is null || latest.Kind == AlertAckKind.Clear)
        {
            return null;
        }

        return lastResolvedAt is { } resolved && latest.AckedAt <= resolved ? null : latest;
    }

    /// <summary>True when the ack is an ack, or a snooze that hasn't expired - a breach then re-notifies nobody.</summary>
    public static bool Silences(AlertAck? effective, DateTimeOffset now) => effective switch
    {
        null => false,
        { Kind: AlertAckKind.Ack } => true,
        { Kind: AlertAckKind.Snooze, SnoozedUntil: { } until } => until > now,
        _ => false,
    };

    /// <summary>Validates a snooze duration; returns an error message or null.</summary>
    public static string? ValidateSnooze(int? minutes) => minutes switch
    {
        null or <= 0 => "snoozeMinutes must be a positive number of minutes.",
        > MaxSnoozeMinutes => $"snoozeMinutes can be at most {MaxSnoozeMinutes} (7 days).",
        _ => null,
    };
}
