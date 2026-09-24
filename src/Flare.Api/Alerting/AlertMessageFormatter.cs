using System.Globalization;
using System.Text.Json;
using Flare.Api.Json;
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
    /// <param name="firedAt">
    /// End of the evaluated window (the worker's own <c>now</c>, see
    /// <c>AlertEvaluationWorker.EvaluateRuleAsync</c>), passed straight through to
    /// <see cref="BuildMatchingLogsUrl"/> - null omits that link, leaving only the rule link.
    /// </param>
    /// <param name="noData">
    /// True for an absent-data fire (<see cref="AlertRule.NoDataWindowSeconds"/>) - reports
    /// "no data" over the no-data window instead of a threshold breach, and omits the
    /// matching-logs link (there are none to show). Ignored when <paramref name="isTest"/>.
    /// </param>
    /// <param name="anomaly">
    /// Set for an <see cref="AlertConditionKind.Anomaly"/> fire - reports the current value
    /// against its seasonal baseline mean and z-score instead of a fixed threshold. Ignored
    /// when <paramref name="isTest"/> or <paramref name="noData"/>.
    /// </param>
    public static string BuildText(AlertRule rule, double observedValue, bool isTest = false, string? publicUrl = null, string? metricUnit = null, DateTimeOffset? firedAt = null, bool noData = false, AnomalyScore? anomaly = null)
    {
        var text = isTest
            ? $":test_tube: Test notification for alert \"{rule.Name}\" - if you're seeing this, the channel is configured correctly."
            : noData
                ? BuildNoDataText(rule)
                : anomaly is not null
                    ? BuildAnomalyText(rule, anomaly, metricUnit)
                    : BuildFiredText(rule, observedValue, metricUnit);

        if (!noData && firedAt is { } at && BuildMatchingLogsUrl(rule, publicUrl, at) is { } logsUrl)
        {
            text = $"{text}\nMatching logs: {logsUrl}";
        }

        var ruleUrl = BuildRuleUrl(rule, publicUrl);
        return ruleUrl is null ? text : $"{text}\n{ruleUrl}";
    }

    private static string BuildNoDataText(AlertRule rule)
    {
        var what = AnomalyScoring.SeriesKind(rule.ConditionKind, rule.AnomalyCondition) == AlertConditionKind.MetricThreshold
            ? $"metric {rule.MetricCondition?.MetricName ?? "?"} reported no data points"
            : "no matching log events";
        return $":warning: Alert \"{rule.Name}\" fired: no data - {what} in the last {rule.NoDataWindowSeconds}s";
    }

    private static string BuildAnomalyText(AlertRule rule, AnomalyScore anomaly, string? metricUnit)
    {
        var mean = anomaly.BaselineMean ?? double.NaN;
        var z = anomaly.ZScore ?? 0;
        var source = rule.AnomalyCondition?.Source ?? AlertConditionKind.LogCount;

        string current, usual;
        if (source == AlertConditionKind.MetricThreshold)
        {
            var scale = MetricUnitFormatter.ResolveScale(metricUnit, Math.Max(Math.Abs(anomaly.Current), Math.Abs(mean)));
            current = $"{rule.MetricCondition?.MetricName ?? "?"} = {MetricUnitFormatter.Format(anomaly.Current, scale)}";
            usual = MetricUnitFormatter.Format(mean, scale);
        }
        else
        {
            var count = ((ulong)anomaly.Current).ToString(CultureInfo.InvariantCulture);
            current = source == AlertConditionKind.ExceptionCount
                ? $"{rule.ExceptionCondition?.ExceptionType ?? "?"} occurred {count} times"
                : $"{count} events";
            usual = mean.ToString("0.#", CultureInfo.InvariantCulture);
        }

        var periods = rule.AnomalyCondition?.BaselinePeriods ?? 0;
        var unit = rule.AnomalyCondition?.Seasonality == AnomalySeasonality.Weekly ? "weeks" : "days";
        var emoji = z < 0 ? ":chart_with_downwards_trend:" : ":chart_with_upwards_trend:";
        return $"{emoji} Alert \"{rule.Name}\" fired: anomaly - {current} in the last {rule.WindowSeconds}s vs a usual {usual} " +
               $"(z = {z.ToString("+0.0;-0.0", CultureInfo.InvariantCulture)}, same window over the previous {periods} {unit})";
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

    /// <summary>
    /// Builds a deep link from a fired alert straight into the Logs Explorer, scoped to
    /// exactly what the rule counted: its <see cref="AlertRule.Condition"/> over the
    /// evaluated window <c>[firedAt - WindowSeconds, firedAt]</c> - the same range
    /// <c>AlertEvaluationWorker</c> passes to <c>CountMatchingLogsAsync</c>. Serialized as a
    /// Logs saved-view state payload (<c>LogsSavedViewState</c> in the dashboard's
    /// <c>lib/logs/state.svelte.ts</c>) in a <c>?state=</c> param, so the dashboard restores
    /// it through the exact same <c>applySavedViewState</c> path a saved view uses - see
    /// <c>parseLogsStateDeepLinkParam</c> in <c>lib/deep-links.ts</c>.
    /// </summary>
    /// <remarks>
    /// Null (callers fall back to <see cref="BuildRuleUrl"/> alone) when
    /// <paramref name="publicUrl"/> is unset, for rules whose series isn't a log count (an
    /// <see cref="AlertConditionKind.Anomaly"/> rule over a <see cref="AlertConditionKind.LogCount"/>
    /// source does get the link) (neither <c>/errors</c> nor <c>/metrics</c> hydrates filter state from the URL
    /// yet), and when the condition sets <see cref="LogFilter.TraceId"/>/<see cref="LogFilter.SpanId"/>/<see cref="LogFilter.PatternId"/> -
    /// a saved-view state has no slot for those, and a link that silently dropped them would
    /// show a wider result than what fired. No link beats a misleading one.
    /// Standard (not URL-safe) base64, then percent-escaped: base64url's <c>_</c> is an
    /// italic marker under <see cref="TelegramAlertNotifier"/>'s <c>parse_mode: Markdown</c>,
    /// and an unmatched one fails the whole send.
    /// </remarks>
    public static string? BuildMatchingLogsUrl(AlertRule rule, string? publicUrl, DateTimeOffset firedAt)
    {
        var condition = rule.Condition;
        if (string.IsNullOrWhiteSpace(publicUrl)
            || AnomalyScoring.SeriesKind(rule.ConditionKind, rule.AnomalyCondition) != AlertConditionKind.LogCount
            || !string.IsNullOrEmpty(condition.TraceId)
            || !string.IsNullOrEmpty(condition.SpanId)
            || !string.IsNullOrEmpty(condition.PatternId))
        {
            return null;
        }

        var state = new LogsDeepLinkState
        {
            CustomRange = new LogsDeepLinkRange
            {
                From = FormatIso(firedAt - TimeSpan.FromSeconds(rule.WindowSeconds)),
                To = FormatIso(firedAt),
            },
            Services = condition.Services ?? [],
            SeverityNumbers = condition.SeverityNumbers?.Select(n => (int)n).ToList() ?? [],
            Search = condition.Search ?? "",
            AttributeFilters = condition.Attributes ?? [],
            BodyJsonFilters = condition.BodyJsonFilters ?? [],
        };

        var json = JsonSerializer.SerializeToUtf8Bytes(state, AlertDeepLinkJsonContext.Default.LogsDeepLinkState);
        return $"{publicUrl.TrimEnd('/')}/?state={Uri.EscapeDataString(Convert.ToBase64String(json))}";
    }

    private static string FormatIso(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}

/// <summary>Mirrors the dashboard's <c>LogsSavedViewState</c> - only the members a <see cref="LogFilter"/> can populate; the rest take <c>applySavedViewState</c>'s own defaults. See <see cref="AlertMessageFormatter.BuildMatchingLogsUrl"/>.</summary>
internal sealed record LogsDeepLinkState
{
    public string TimeRangePreset { get; init; } = "custom";
    public required LogsDeepLinkRange CustomRange { get; init; }
    public required IReadOnlyList<string> Services { get; init; }
    public required IReadOnlyList<int> SeverityNumbers { get; init; }
    public required string Search { get; init; }
    public required IReadOnlyList<AttributeFilter> AttributeFilters { get; init; }
    public required IReadOnlyList<BodyJsonFilter> BodyJsonFilters { get; init; }
}

internal sealed record LogsDeepLinkRange
{
    public required string From { get; init; }
    public required string To { get; init; }
}
