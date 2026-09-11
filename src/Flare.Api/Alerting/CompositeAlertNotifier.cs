using Flare.Api.Model;

namespace Flare.Api.Alerting;

/// <summary>
/// The <see cref="IAlertNotifier"/> actually registered for DI - picks Telegram, Email,
/// PagerDuty, or webhook/Slack per rule and delegates, so <see cref="AlertEvaluationWorker"/>
/// (which only ever depends on <see cref="IAlertNotifier"/>) needs no per-channel
/// branching of its own.
/// </summary>
/// <remarks>
/// Rules are single-channel by design (see <see cref="Model.AlertRuleRequest.ValidateChannel"/>,
/// which rejects a rule with more than one of a webhook URL, Telegram fields, an email
/// recipient, or a PagerDuty routing key set, or none of them) - this only needs an
/// if/else-if chain, not a fan-out loop over multiple notifiers.
/// </remarks>
public sealed class CompositeAlertNotifier(
    WebhookAlertNotifier webhook,
    TelegramAlertNotifier telegram,
    EmailAlertNotifier email,
    PagerDutyAlertNotifier pagerDuty) : IAlertNotifier
{
    public Task<NotificationResult> SendAsync(AlertRule rule, double observedValue, DateTimeOffset firedAt, CancellationToken cancellationToken, bool isTest = false)
    {
        var isTelegram = !string.IsNullOrWhiteSpace(rule.TelegramBotToken) && !string.IsNullOrWhiteSpace(rule.TelegramChatId);
        var isEmail = !string.IsNullOrWhiteSpace(rule.EmailTo);
        var isPagerDuty = !string.IsNullOrWhiteSpace(rule.PagerDutyRoutingKey);
        var notifier = isTelegram ? (IAlertNotifier)telegram : isEmail ? email : isPagerDuty ? pagerDuty : webhook;
        return notifier.SendAsync(rule, observedValue, firedAt, cancellationToken, isTest);
    }
}
