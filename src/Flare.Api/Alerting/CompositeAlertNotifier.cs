using Flare.Api.Model;

namespace Flare.Api.Alerting;

/// <summary>
/// The <see cref="IAlertNotifier"/> actually registered for DI - picks Telegram, Email,
/// or webhook/Slack per rule and delegates, so <see cref="AlertEvaluationWorker"/> (which
/// only ever depends on <see cref="IAlertNotifier"/>) needs no per-channel branching of
/// its own.
/// </summary>
/// <remarks>
/// Rules are single-channel by design (see <see cref="Model.AlertRuleRequest.ValidateChannel"/>,
/// which rejects a rule with more than one of a webhook URL, Telegram fields, or an email
/// recipient set, or none of them) - this only needs an if/else-if chain, not a fan-out
/// loop over multiple notifiers.
/// </remarks>
public sealed class CompositeAlertNotifier(WebhookAlertNotifier webhook, TelegramAlertNotifier telegram, EmailAlertNotifier email) : IAlertNotifier
{
    public Task<NotificationResult> SendAsync(AlertRule rule, ulong observedCount, DateTimeOffset firedAt, CancellationToken cancellationToken)
    {
        var isTelegram = !string.IsNullOrWhiteSpace(rule.TelegramBotToken) && !string.IsNullOrWhiteSpace(rule.TelegramChatId);
        var isEmail = !string.IsNullOrWhiteSpace(rule.EmailTo);
        var notifier = isTelegram ? (IAlertNotifier)telegram : isEmail ? email : webhook;
        return notifier.SendAsync(rule, observedCount, firedAt, cancellationToken);
    }
}
