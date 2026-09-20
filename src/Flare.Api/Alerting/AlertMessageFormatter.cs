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
    /// <param name="observedValue">See <see cref="IAlertNotifier.SendAsync"/>'s doc comment - a row count for <see cref="AlertConditionKind.LogCount"/> rules, a metric-query result for <see cref="AlertConditionKind.MetricThreshold"/> ones.</param>
    /// <param name="isTest">
    /// True from the "send test alert" endpoints - <paramref name="observedValue"/> isn't
    /// a real breach in that case, so the text says so instead of reporting it as one.
    /// </param>
    /// <param name="publicUrl">
    /// <see cref="AlertLinkOptions.PublicUrl"/>, passed straight through to
    /// <see cref="BuildRuleUrl"/> - null/blank omits the link. A bare URL on its own line
    /// (rather than channel-specific link markup) is deliberate: Slack's incoming-webhook
    /// parser, Telegram (even under <c>parse_mode: Markdown</c>), and every common email
    /// client all auto-linkify a plain <c>http(s)://</c> URL, so one plain-text form works
    /// unchanged for every channel <see cref="CompositeAlertNotifier"/> can pick.
    /// </param>
    /// <param name="metricUnit">
    /// The evaluated <see cref="AlertRule.MetricCondition"/> metric's declared OTel/UCUM
    /// unit (from the same <c>Unit</c> column <c>IAlertQueryService.EvaluateMetricConditionAsync</c>
    /// reads back), only meaningful for <see cref="AlertConditionKind.MetricThreshold"/> rules.
    /// Formatted through <see cref="MetricUnitFormatter"/> - the same scale table the
    /// dashboard's <c>lib/metrics/axis.ts</c> uses - so a byte metric reads "4 GiB" rather
    /// than the raw declared-unit value. Null (unresolved/non-metric rule) falls back to a
    /// plain dimensionless number, matching this method's behavior before units existed here.
    /// </param>
    public static string BuildText(AlertRule rule, double observedValue, bool isTest = false, string? publicUrl = null, string? metricUnit = null)
    {
        var text = isTest
            ? $":test_tube: Test notification for alert \"{rule.Name}\" - if you're seeing this, the channel is configured correctly."
            : BuildFiredText(rule, observedValue, metricUnit);

        var ruleUrl = BuildRuleUrl(rule, publicUrl);
        return ruleUrl is null ? text : $"{text}\n{ruleUrl}";
    }

    private static string BuildFiredText(AlertRule rule, double observedValue, string? metricUnit)
    {
        var comparatorSymbol = rule.Threshold.Comparator == ThresholdComparator.GreaterThanOrEqual ? ">=" : "<";

        if (rule.ConditionKind == AlertConditionKind.MetricThreshold)
        {
            var metricName = rule.MetricCondition?.MetricName ?? "?";
            var thresholdValue = rule.MetricThresholdValue;
            var peak = Math.Max(Math.Abs(observedValue), Math.Abs(thresholdValue ?? 0));
            var scale = MetricUnitFormatter.ResolveScale(metricUnit, peak);
            var thresholdText = thresholdValue is { } tv ? MetricUnitFormatter.Format(tv, scale) : "";
            return $":rotating_light: Alert \"{rule.Name}\" fired: {metricName} = {MetricUnitFormatter.Format(observedValue, scale)} " +
                   $"({comparatorSymbol} {thresholdText}) over the last {rule.WindowSeconds}s";
        }

        if (rule.ConditionKind == AlertConditionKind.ExceptionCount)
        {
            var exceptionType = rule.ExceptionCondition?.ExceptionType ?? "?";
            return $":rotating_light: Alert \"{rule.Name}\" fired: {exceptionType} occurred {(ulong)observedValue} times " +
                   $"({comparatorSymbol} {rule.Threshold.Count}) in the last {rule.WindowSeconds}s";
        }

        return $":rotating_light: Alert \"{rule.Name}\" fired: {(ulong)observedValue} events " +
               $"({comparatorSymbol} {rule.Threshold.Count}) in the last {rule.WindowSeconds}s";
    }

    /// <summary>
    /// Builds the deep link from a fired-alert notification back to <paramref name="rule"/>
    /// in the dashboard (<c>{publicUrl}/alerts?rule={rule.Id}</c> - see
    /// <c>src/dashboard/src/routes/alerts/+page.svelte</c>, which opens that rule's history
    /// sheet when the <c>rule</c> query param is present), or null when
    /// <paramref name="publicUrl"/> is unset. Pulled out from <see cref="BuildText"/> so
    /// <see cref="PagerDutyAlertNotifier"/>/<see cref="WebhookAlertNotifier"/> can also put
    /// it in a dedicated structured field (PagerDuty's <c>client_url</c>, the generic
    /// webhook payload's <c>ruleUrl</c>), not just inline in the text.
    /// </summary>
    public static string? BuildRuleUrl(AlertRule rule, string? publicUrl)
    {
        if (string.IsNullOrWhiteSpace(publicUrl))
        {
            return null;
        }

        return $"{publicUrl.TrimEnd('/')}/alerts?rule={Uri.EscapeDataString(rule.Id.ToString())}";
    }
}
