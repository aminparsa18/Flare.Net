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
    /// <param name="isTest">
    /// True from the "send test alert" endpoints - <paramref name="observedCount"/> isn't
    /// a real breach in that case, so the text says so instead of reporting it as one.
    /// </param>
    public static string BuildText(AlertRule rule, ulong observedCount, bool isTest = false)
    {
        if (isTest)
        {
            return $":test_tube: Test notification for alert \"{rule.Name}\" - if you're seeing this, the channel is configured correctly.";
        }

        var comparatorSymbol = rule.Threshold.Comparator == ThresholdComparator.GreaterThanOrEqual ? ">=" : "<";
        return $":rotating_light: Alert \"{rule.Name}\" fired: {observedCount} events " +
               $"({comparatorSymbol} {rule.Threshold.Count}) in the last {rule.WindowSeconds}s";
    }
}
