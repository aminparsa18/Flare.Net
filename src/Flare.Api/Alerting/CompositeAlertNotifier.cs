using Flare.Api.Model;

namespace Flare.Api.Alerting;

/// <summary>
/// The <see cref="IAlertNotifier"/> actually registered for DI - picks Telegram or
/// webhook/Slack per rule and delegates, so <see cref="AlertEvaluationWorker"/> (which
/// only ever depends on <see cref="IAlertNotifier"/>) needs no per-channel branching of
/// its own.
/// </summary>
/// <remarks>
/// Rules are single-channel by design (see <c>Endpoints.AlertEndpoints</c>'s channel
/// validation, which rejects a rule with both a webhook URL and Telegram fields, or
/// neither) - this only needs an if/else, not a fan-out loop over multiple notifiers.
/// </remarks>
public sealed class CompositeAlertNotifier(WebhookAlertNotifier webhook, TelegramAlertNotifier telegram) : IAlertNotifier
{
    public Task<NotificationResult> SendAsync(AlertRule rule, ulong observedCount, DateTimeOffset firedAt, CancellationToken cancellationToken)
    {
        var isTelegram = !string.IsNullOrWhiteSpace(rule.TelegramBotToken) && !string.IsNullOrWhiteSpace(rule.TelegramChatId);
        var notifier = isTelegram ? (IAlertNotifier)telegram : webhook;
        return notifier.SendAsync(rule, observedCount, firedAt, cancellationToken);
    }
}
