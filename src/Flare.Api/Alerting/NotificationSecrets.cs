using Flare.Api.Model;

namespace Flare.Api.Alerting;

/// <summary>
/// Masks notification credentials in every API response and audit snapshot, and maps a
/// masked value sent back on update to the stored secret.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="NotificationChannel"/>/<see cref="AlertRule"/> rows carry live credentials:
/// bot tokens, routing/API keys, and webhook URLs (a Slack/Teams/Discord/incident.io
/// webhook URL is itself the credential). Reads are open to every authenticated caller,
/// Viewers and PATs included, so those fields leave the API only as
/// <see cref="Mask"/>ed values. Notifiers read the stored rows, never a response, so
/// sending is unaffected.
/// </para>
/// <para>
/// Edit forms load the masked value and send it back unchanged when the user didn't touch
/// the field. <see cref="Restore(string?, string)"/> treats a value equal to the mask of
/// the stored secret as "keep the stored one", so the dashboard, the CLI and any script
/// doing GET → PUT round-trip without ever holding the real secret. A changed value is a
/// new secret and replaces it.
/// </para>
/// </remarks>
public static class NotificationSecrets
{
    public const string MaskMarker = "••••••••";

    /// <summary>
    /// "" for an empty value. An absolute http(s) URL keeps its scheme and host (so the
    /// destination is still recognisable) and hides path/query; anything else keeps only
    /// its last 4 characters, and only when it's long enough that 4 characters reveal
    /// little.
    /// </summary>
    public static string Mask(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        var tail = value.Length > 12 ? value[^4..] : "";
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
        {
            return $"{uri.Scheme}://{uri.Authority}/{MaskMarker}{tail}";
        }

        return MaskMarker + tail;
    }

    /// <summary>The stored secret when <paramref name="requested"/> is exactly its mask, otherwise <paramref name="requested"/> as sent.</summary>
    public static string? Restore(string? requested, string stored) =>
        !string.IsNullOrEmpty(requested) && !string.IsNullOrEmpty(stored) && requested == Mask(stored) ? stored : requested;

    public static NotificationChannel Redact(NotificationChannel channel) => channel with
    {
        WebhookUrl = Mask(channel.WebhookUrl),
        TelegramBotToken = Mask(channel.TelegramBotToken),
        PagerDutyRoutingKey = Mask(channel.PagerDutyRoutingKey),
        JiraApiToken = Mask(channel.JiraApiToken),
        IncidentIoToken = Mask(channel.IncidentIoToken),
        JsmOpsApiKey = Mask(channel.JsmOpsApiKey),
    };

    public static AlertRule Redact(AlertRule rule) => rule with
    {
        WebhookUrl = Mask(rule.WebhookUrl),
        TelegramBotToken = Mask(rule.TelegramBotToken),
        PagerDutyRoutingKey = Mask(rule.PagerDutyRoutingKey),
    };

    public static NotificationChannel? RedactOrNull(NotificationChannel? channel) => channel is null ? null : Redact(channel);

    public static AlertRule? RedactOrNull(AlertRule? rule) => rule is null ? null : Redact(rule);

    /// <summary>Puts <paramref name="existing"/>'s secrets back wherever <paramref name="request"/> still carries their masks. No-op when <paramref name="existing"/> is null.</summary>
    public static NotificationChannelRequest Restore(NotificationChannelRequest request, NotificationChannel? existing) => existing is null ? request : request with
    {
        WebhookUrl = Restore(request.WebhookUrl, existing.WebhookUrl),
        TelegramBotToken = Restore(request.TelegramBotToken, existing.TelegramBotToken),
        PagerDutyRoutingKey = Restore(request.PagerDutyRoutingKey, existing.PagerDutyRoutingKey),
        JiraApiToken = Restore(request.JiraApiToken, existing.JiraApiToken),
        IncidentIoToken = Restore(request.IncidentIoToken, existing.IncidentIoToken),
        JsmOpsApiKey = Restore(request.JsmOpsApiKey, existing.JsmOpsApiKey),
    };

    /// <inheritdoc cref="Restore(NotificationChannelRequest, NotificationChannel?)"/>
    public static AlertRuleRequest Restore(AlertRuleRequest request, AlertRule? existing) => existing is null ? request : request with
    {
        WebhookUrl = Restore(request.WebhookUrl, existing.WebhookUrl),
        TelegramBotToken = Restore(request.TelegramBotToken, existing.TelegramBotToken),
        PagerDutyRoutingKey = Restore(request.PagerDutyRoutingKey, existing.PagerDutyRoutingKey),
    };
}
