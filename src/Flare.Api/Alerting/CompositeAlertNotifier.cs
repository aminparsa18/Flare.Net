using Flare.Api.Model;

namespace Flare.Api.Alerting;

/// <summary>
/// The <see cref="IAlertNotifier"/> actually registered for DI - picks Telegram, Email,
/// PagerDuty, Teams, Discord, Jira, incident.io, JSM Ops, or webhook/Slack per <see cref="NotificationChannel.Type"/> and delegates,
/// so <see cref="AlertEvaluationWorker"/> (which only ever depends on
/// <see cref="IAlertNotifier"/>/<see cref="SendAllAsync"/>) needs no per-channel branching
/// of its own.
/// </summary>
/// <remarks>
/// <see cref="SendAsync"/> (one channel) is still the full <see cref="IAlertNotifier"/>
/// contract, used directly by the "send test" endpoints against a single channel.
/// <see cref="SendAllAsync"/> (zero or more channels) is the fan-out entrypoint
/// <see cref="AlertEvaluationWorker"/> uses for a real fire - a rule can now notify more
/// than one saved <see cref="NotificationChannel"/> (see <see cref="AlertRule.ChannelIds"/>),
/// unlike the single legacy inline channel every rule was limited to before. Results come
/// back in the same order as <paramref name="channels"/> is passed, so the caller can zip
/// them back together to build a per-channel <see cref="Model.AlertChannelResult"/> list.
/// </remarks>
public sealed class CompositeAlertNotifier(
    WebhookAlertNotifier webhook,
    TelegramAlertNotifier telegram,
    EmailAlertNotifier email,
    PagerDutyAlertNotifier pagerDuty,
    TeamsAlertNotifier teams,
    DiscordAlertNotifier discord,
    JiraAlertNotifier jira,
    IncidentIoAlertNotifier incidentIo,
    JsmOpsAlertNotifier jsmOps) : IAlertNotifier
{
    public Task<NotificationResult> SendAsync(AlertRule rule, NotificationChannel channel, double observedValue, DateTimeOffset firedAt, CancellationToken cancellationToken, bool isTest = false, string? metricUnit = null, bool noData = false, AnomalyScore? anomaly = null, bool resolved = false, string? logSamples = null, string? ackUrl = null)
    {
        IAlertNotifier notifier = channel.Type switch
        {
            NotificationChannelType.Telegram => telegram,
            NotificationChannelType.Email => email,
            NotificationChannelType.PagerDuty => pagerDuty,
            NotificationChannelType.Teams => teams,
            NotificationChannelType.Discord => discord,
            NotificationChannelType.Jira => jira,
            NotificationChannelType.IncidentIo => incidentIo,
            NotificationChannelType.JsmOps => jsmOps,
            _ => webhook,
        };

        // Dispose restores the flag on return; the callee already captured it in its own async flow.
        using var _ = Flare.ServiceDefaults.HttpRetryScope.SingleShot(isTest);
        return notifier.SendAsync(rule, channel, observedValue, firedAt, cancellationToken, isTest, metricUnit, noData, anomaly, resolved, logSamples, ackUrl);
    }

    /// <summary>See this class's remarks. Sends to every one of <paramref name="channels"/> concurrently - independent I/O against unrelated third-party endpoints, so there's no reason to serialize them.</summary>
    public async Task<IReadOnlyList<NotificationResult>> SendAllAsync(AlertRule rule, IReadOnlyList<NotificationChannel> channels, double observedValue, DateTimeOffset firedAt, CancellationToken cancellationToken, bool isTest = false, string? metricUnit = null, bool noData = false, AnomalyScore? anomaly = null, bool resolved = false, string? logSamples = null, string? ackUrl = null)
    {
        var sends = channels.Select(channel => SendAsync(rule, channel, observedValue, firedAt, cancellationToken, isTest, metricUnit, noData, anomaly, resolved, logSamples, ackUrl));
        return await Task.WhenAll(sends);
    }
}
