using Flare.Api.Model;

namespace Flare.Api.Alerting;

/// <summary>
/// Builds the human-readable fired-alert message shared by every <see cref="IAlertNotifier"/> -
/// <see cref="WebhookAlertNotifier"/> puts it in the payload's top-level <c>text</c> field
/// (what Slack's incoming-webhook parser renders), <see cref="TelegramAlertNotifier"/> puts
/// it in Telegram's <c>sendMessage</c> <c>text</c> parameter. Pulled out on its own so
/// adding a channel never means re-deriving this string a second time.
/// </summary>
public static class AlertMessageFormatter
{
    public static string BuildText(AlertRule rule, ulong observedCount)
    {
        var comparatorSymbol = rule.Threshold.Comparator == ThresholdComparator.GreaterThanOrEqual ? ">=" : "<";
        return $":rotating_light: Alert \"{rule.Name}\" fired: {observedCount} events " +
               $"({comparatorSymbol} {rule.Threshold.Count}) in the last {rule.WindowSeconds}s";
    }
}
