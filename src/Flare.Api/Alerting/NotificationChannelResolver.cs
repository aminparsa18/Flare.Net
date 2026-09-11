using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Alerting;

/// <summary>
/// Resolves the <see cref="NotificationChannel"/>(s) a rule notifies on breach - either
/// its <see cref="AlertRule.ChannelIds"/>, looked up from the store, or (when that list
/// is empty) one ephemeral channel synthesized from the rule's legacy inline fields,
/// never persisted. Used by both <c>AlertEvaluationWorker</c> (the real fire path) and
/// <c>AlertEndpoints</c>'s send-test handlers, so "which channels does this rule notify"
/// has exactly one implementation.
/// </summary>
public static class NotificationChannelResolver
{
    /// <summary>
    /// Sentinel <see cref="NotificationChannel.Id"/> for a channel synthesized from a
    /// rule's legacy inline fields rather than looked up from <c>notification_channels</c> -
    /// same "<see cref="Guid.Empty"/> means not a real persisted row" convention
    /// <c>AlertEndpoints.HandleSendTestDraftAsync</c>'s draft <see cref="AlertRule"/>
    /// already uses for an unsaved rule.
    /// </summary>
    public static readonly Guid LegacyChannelId = Guid.Empty;

    public static async Task<IReadOnlyList<NotificationChannel>> ResolveAsync(AlertRule rule, INotificationChannelQueryService channels, CancellationToken cancellationToken)
    {
        if (rule.ChannelIds.Count > 0)
        {
            // Silently drops any ChannelId that no longer resolves (a channel deleted
            // after the rule was saved) - the remaining channels still fire rather than
            // the whole notification silently no-opping; there's no per-rule "channel
            // missing" surfacing yet (a named follow-up, not built now).
            return await channels.GetByIdsAsync(rule.ChannelIds, cancellationToken);
        }

        return LegacyChannel(rule) is { } legacy ? [legacy] : [];
    }

    /// <summary>
    /// Synthesizes the one ephemeral <see cref="NotificationChannel"/> a rule's legacy
    /// inline fields describe, or null if none are set (a rule mid-migration to
    /// <see cref="AlertRule.ChannelIds"/>, or an invalid/incomplete draft -
    /// <see cref="AlertRuleRequest.ValidateChannel"/> is what rejects that shape on
    /// create/update; this stays lenient, same precedent <c>AlertEndpoints.EvaluateAsync</c>'s
    /// own doc comment already sets for dry-runs).
    /// </summary>
    public static NotificationChannel? LegacyChannel(AlertRule rule)
    {
        if (!string.IsNullOrWhiteSpace(rule.WebhookUrl))
        {
            return Build(rule, NotificationChannelType.Webhook, "Webhook", webhookUrl: rule.WebhookUrl);
        }

        if (!string.IsNullOrWhiteSpace(rule.TelegramBotToken) && !string.IsNullOrWhiteSpace(rule.TelegramChatId))
        {
            return Build(rule, NotificationChannelType.Telegram, "Telegram", telegramBotToken: rule.TelegramBotToken, telegramChatId: rule.TelegramChatId);
        }

        if (!string.IsNullOrWhiteSpace(rule.EmailTo))
        {
            return Build(rule, NotificationChannelType.Email, "Email", emailTo: rule.EmailTo);
        }

        if (!string.IsNullOrWhiteSpace(rule.PagerDutyRoutingKey))
        {
            return Build(rule, NotificationChannelType.PagerDuty, "PagerDuty", pagerDutyRoutingKey: rule.PagerDutyRoutingKey);
        }

        return null;
    }

    private static NotificationChannel Build(
        AlertRule rule,
        NotificationChannelType type,
        string name,
        string webhookUrl = "",
        string telegramBotToken = "",
        string telegramChatId = "",
        string emailTo = "",
        string pagerDutyRoutingKey = "") => new()
    {
        Id = LegacyChannelId,
        Name = name,
        Type = type,
        WebhookUrl = webhookUrl,
        TelegramBotToken = telegramBotToken,
        TelegramChatId = telegramChatId,
        EmailTo = emailTo,
        PagerDutyRoutingKey = pagerDutyRoutingKey,
        CreatedAt = rule.CreatedAt,
        UpdatedAt = rule.UpdatedAt,
    };
}
