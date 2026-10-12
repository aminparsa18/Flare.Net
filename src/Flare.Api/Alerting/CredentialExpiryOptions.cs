namespace Flare.Api.Alerting;

/// <summary>
/// Bound from the <c>CredentialExpiry</c> section (ADR-0208). The warning is off until at least one notification
/// channel is named: there is no sensible default recipient for "an ingest key is about to stop working".
/// </summary>
public sealed class CredentialExpiryOptions
{
    public const string SectionName = "CredentialExpiry";

    /// <summary>Saved notification channels that receive the warning; empty disables it. Webhook/Slack, Telegram, email, Teams and Discord only.</summary>
    public List<Guid> ChannelIds { get; set; } = [];

    /// <summary>Days before expiry at which a warning is sent, one per threshold per credential.</summary>
    public List<int> WarnDays { get; set; } = [14, 3];

    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromHours(6);
}
