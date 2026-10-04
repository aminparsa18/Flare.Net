using Flare.Api.Alerting;
using Flare.Api.Model;

namespace Flare.Api.Ai;

/// <summary>
/// Turns an incident summary into a follow-up message for the channels that read like a chat
/// (ADR-0104). The summary rides the existing notifiers as a custom-templated message, so every
/// channel renders it its own way without a per-channel code path.
/// </summary>
public static class IncidentSummaryFollowUp
{
    public const string TitlePrefix = "AI summary: ";

    /// <summary>
    /// Chat-style channels only: Slack webhooks, Telegram, Teams, Discord and email. Incident/ticket
    /// channels (PagerDuty, Jira, incident.io, JSM Ops) would open a second incident for the same
    /// alert, and a generic webhook is machine-read and would see a second "firing" payload, so
    /// those are skipped; the summary is still in the alert history.
    /// </summary>
    public static bool IsEligible(NotificationChannel channel) => channel.Type switch
    {
        NotificationChannelType.Webhook => WebhookAlertNotifier.IsSlackWebhook(channel.WebhookUrl),
        NotificationChannelType.Telegram or NotificationChannelType.Teams or NotificationChannelType.Discord or NotificationChannelType.Email => true,
        _ => false,
    };

    /// <summary>A copy of <paramref name="rule"/> whose notification text is the summary.</summary>
    public static AlertRule BuildRule(AlertRule rule, string summary) => rule with
    {
        NotificationTitleTemplate = Literal(TitlePrefix + rule.Name),
        NotificationBodyTemplate = Literal(summary),
    };

    /// <summary>Templates substitute <c>{{name}}</c> tokens; model or rule text must stay literal.</summary>
    private static string Literal(string text) => text.Replace("{{", "{ {", StringComparison.Ordinal);
}
