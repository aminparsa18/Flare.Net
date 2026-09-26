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
    /// <see cref="BuildFiredDataUrl"/> - null omits that link, leaving only the rule link.
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

        if (!noData && firedAt is { } at && BuildFiredDataUrl(rule, publicUrl, at) is { } dataUrl)
        {
            text = $"{text}\n{FiredDataLabel(rule)}: {dataUrl}";
        }

        var ruleUrl = BuildRuleUrl(rule, publicUrl);
        return ruleUrl is null ? text : $"{text}\n{ruleUrl}";
    }

    /// <summary>
    /// What every notifier actually sends: <see cref="BuildText"/>'s built-in wording, unless
    /// the rule sets <see cref="AlertRule.NotificationTitleTemplate"/>/<see cref="AlertRule.NotificationBodyTemplate"/>,
    /// in which case those are rendered through <see cref="AlertTemplateRenderer"/> instead.
    /// A custom body replaces the whole text, links included - it carries
    /// <c>{{rule_url}}</c>/<c>{{logs_url}}</c> itself wherever the author wants them. A test
    /// send keeps the custom wording (that's what's being tested) but prefixes
    /// <see cref="TestPrefix"/> to the title (or the body, without a title template), so
    /// nobody mistakes it for a real incident.
    /// </summary>
    /// <param name="appendLinks">
    /// False for <see cref="PagerDutyAlertNotifier"/>, whose built-in summary never carried
    /// link lines (it has <c>client_url</c>/<c>links</c> for those). Only affects the built-in
    /// text - the <c>{{rule_url}}</c>/<c>{{logs_url}}</c> placeholders resolve either way.
    /// </param>
    public static AlertMessage BuildMessage(AlertRule rule, double observedValue, bool isTest, string? publicUrl, string? metricUnit, DateTimeOffset firedAt, bool noData, AnomalyScore? anomaly, bool appendLinks = true)
    {
        var titleTemplate = rule.NotificationTitleTemplate;
        var bodyTemplate = rule.NotificationBodyTemplate;
        if (string.IsNullOrEmpty(titleTemplate) && string.IsNullOrEmpty(bodyTemplate))
        {
            return new AlertMessage(null, BuildText(rule, observedValue, isTest, appendLinks ? publicUrl : null, metricUnit, appendLinks ? firedAt : null, noData, anomaly), IsCustom: false);
        }

        var values = BuildTemplateValues(rule, observedValue, isTest, publicUrl, metricUnit, firedAt, noData, anomaly);
        var labels = BuildTemplateLabels(rule);
        var prefix = isTest ? TestPrefix : "";

        var title = string.IsNullOrEmpty(titleTemplate) ? null : prefix + AlertTemplateRenderer.Render(titleTemplate, values, labels);
        // Only one of the two carries the prefix - with a title it's already the first thing
        // every channel shows, and a second "[Test]" on the body line would just be noise.
        var text = string.IsNullOrEmpty(bodyTemplate)
            ? BuildText(rule, observedValue, isTest, appendLinks ? publicUrl : null, metricUnit, appendLinks ? firedAt : null, noData, anomaly)
            : (title is null ? prefix : "") + AlertTemplateRenderer.Render(bodyTemplate, values, labels);
        return new AlertMessage(title, text, IsCustom: true);
    }

    /// <summary>Prefixed to a custom-templated title/body on a test send - see <see cref="BuildMessage"/>.</summary>
    public const string TestPrefix = "[Test] ";

    /// <summary>
    /// The value behind every <see cref="AlertTemplateRenderer.Names"/> placeholder. Numbers
    /// go through the same formatting the built-in text uses (unit-scaled for metrics, whole
    /// counts otherwise), so <c>{{value}}</c> reads the same as it would in the default message.
    /// A placeholder that doesn't apply to this rule/fire (<c>{{metric}}</c> on a log-count rule,
    /// <c>{{threshold}}</c> on an anomaly rule, <c>{{logs_url}}</c> on a metric rule or without a
    /// public URL) is "". <c>{{data_url}}</c> is the kind-appropriate <see cref="BuildFiredDataUrl"/>
    /// link; <c>{{logs_url}}</c> stays logs-only, as it was before metric/exception links existed.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> BuildTemplateValues(AlertRule rule, double observedValue, bool isTest, string? publicUrl, string? metricUnit, DateTimeOffset firedAt, bool noData, AnomalyScore? anomaly)
    {
        var seriesKind = RuleSeriesKind(rule);
        var isAnomaly = rule.ConditionKind == AlertConditionKind.Anomaly;

        string value, threshold = "", comparator = "";
        if (seriesKind == AlertConditionKind.MetricThreshold)
        {
            var current = anomaly?.Current ?? observedValue;
            var reference = isAnomaly ? anomaly?.BaselineMean ?? 0 : rule.MetricThresholdValue ?? 0;
            var scale = MetricUnitFormatter.ResolveScale(metricUnit, Math.Max(Math.Abs(current), Math.Abs(reference)));
            value = double.IsNaN(current) ? "" : MetricUnitFormatter.Format(current, scale);
            if (!isAnomaly && rule.MetricThresholdValue is { } tv)
            {
                threshold = MetricUnitFormatter.Format(tv, scale);
            }
        }
        else
        {
            value = ((ulong)(anomaly?.Current ?? observedValue)).ToString(CultureInfo.InvariantCulture);
            if (!isAnomaly)
            {
                threshold = rule.Threshold.Count.ToString(CultureInfo.InvariantCulture);
            }
        }

        if (!isAnomaly)
        {
            comparator = rule.Threshold.Comparator == ThresholdComparator.GreaterThanOrEqual ? ">=" : "<";
        }

        if (noData && !isTest)
        {
            value = "no data";
        }

        var windowSeconds = noData && !isTest ? rule.NoDataWindowSeconds : rule.WindowSeconds;
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["rule_name"] = rule.Name,
            ["rule_id"] = rule.Id.ToString(),
            ["description"] = rule.Description,
            ["status"] = isTest ? "test" : noData ? "no data" : anomaly is not null ? "anomaly" : "firing",
            ["condition_kind"] = rule.ConditionKind.ToString(),
            ["value"] = value,
            ["threshold"] = threshold,
            ["comparator"] = comparator,
            ["window"] = $"{windowSeconds}s",
            ["window_seconds"] = windowSeconds.ToString(CultureInfo.InvariantCulture),
            ["metric"] = seriesKind == AlertConditionKind.MetricThreshold ? rule.MetricCondition?.MetricName ?? "" : "",
            ["exception_type"] = seriesKind == AlertConditionKind.ExceptionCount ? rule.ExceptionCondition?.ExceptionType ?? "" : "",
            ["baseline_mean"] = anomaly?.BaselineMean is { } mean ? mean.ToString("0.##", CultureInfo.InvariantCulture) : "",
            ["z_score"] = anomaly?.ZScore is { } z ? z.ToString("+0.0;-0.0", CultureInfo.InvariantCulture) : "",
            ["fired_at"] = FormatIso(firedAt),
            ["rule_url"] = BuildRuleUrl(rule, publicUrl) ?? "",
            ["logs_url"] = noData && !isTest ? "" : BuildMatchingLogsUrl(rule, publicUrl, firedAt) ?? "",
            ["data_url"] = noData && !isTest ? "" : BuildFiredDataUrl(rule, publicUrl, firedAt) ?? "",
            // The built-in wording without its link lines - lets a template wrap rather than
            // replace it ("{{message}} - runbook: https://...").
            ["message"] = BuildText(rule, observedValue, isTest, publicUrl: null, metricUnit, firedAt: null, noData, anomaly),
        };
    }

    /// <summary>
    /// The <c>{{labels.&lt;key&gt;}}</c> values: what the rule is scoped to, read from its
    /// condition's equality filters - <c>service.name</c> from the service filter, plus each
    /// log attribute filter using <see cref="AttributeFilterOperator.Equals"/> (other operators
    /// don't pin a single value) or metric attribute filter (always an equality). Alert rules
    /// evaluate one aggregate series, not one per group, so these are the rule's own scope,
    /// not per-series group labels. Several values for one key are joined with ", ".
    /// </summary>
    internal static IReadOnlyDictionary<string, string> BuildTemplateLabels(AlertRule rule)
    {
        var labels = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        void Add(string key, string value)
        {
            if (!labels.TryGetValue(key, out var list))
            {
                labels[key] = list = [];
            }

            if (!list.Contains(value))
            {
                list.Add(value);
            }
        }

        switch (RuleSeriesKind(rule))
        {
            case AlertConditionKind.MetricThreshold when rule.MetricCondition is { } metric:
                foreach (var service in metric.Filter.Services ?? [])
                {
                    Add("service.name", service);
                }

                foreach (var attribute in metric.Filter.Attributes ?? [])
                {
                    Add(attribute.Key, attribute.Value);
                }

                break;
            case AlertConditionKind.ExceptionCount when rule.ExceptionCondition is { } exception:
                foreach (var service in exception.Filter.Services ?? [])
                {
                    Add("service.name", service);
                }

                break;
            case AlertConditionKind.LogCount:
                foreach (var service in rule.Condition.Services ?? [])
                {
                    Add("service.name", service);
                }

                foreach (var attribute in rule.Condition.Attributes ?? [])
                {
                    if (attribute.Operator == AttributeFilterOperator.Equals)
                    {
                        Add(attribute.Key, attribute.Value);
                    }
                }

                break;
        }

        return labels.ToDictionary(kv => kv.Key, kv => string.Join(", ", kv.Value), StringComparer.Ordinal);
    }

    private static AlertConditionKind RuleSeriesKind(AlertRule rule) => AnomalyScoring.SeriesKind(rule.ConditionKind, rule.AnomalyCondition);

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
    /// <c>parseStateDeepLinkParam</c> in <c>lib/deep-links.ts</c>.
    /// </summary>
    /// <remarks>
    /// Null (callers fall back to <see cref="BuildRuleUrl"/> alone) when
    /// <paramref name="publicUrl"/> is unset, for rules whose series isn't a log count (an
    /// <see cref="AlertConditionKind.Anomaly"/> rule over a <see cref="AlertConditionKind.LogCount"/>
    /// source does get the link; metric/exception rules get theirs from <see cref="BuildFiredDataUrl"/>),
    /// and when the condition sets <see cref="LogFilter.TraceId"/>/<see cref="LogFilter.SpanId"/>/<see cref="LogFilter.PatternId"/> -
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
            || RuleSeriesKind(rule) != AlertConditionKind.LogCount
            || !string.IsNullOrEmpty(condition.TraceId)
            || !string.IsNullOrEmpty(condition.SpanId)
            || !string.IsNullOrEmpty(condition.PatternId))
        {
            return null;
        }

        var state = new LogsDeepLinkState
        {
            CustomRange = EvaluatedWindow(rule, firedAt),
            Services = condition.Services ?? [],
            SeverityNumbers = condition.SeverityNumbers?.Select(n => (int)n).ToList() ?? [],
            Search = condition.Search ?? "",
            AttributeFilters = condition.Attributes ?? [],
            BodyJsonFilters = condition.BodyJsonFilters ?? [],
            ScopeNames = condition.ScopeNames ?? [],
        };

        return BuildStateUrl(publicUrl, "/", JsonSerializer.SerializeToUtf8Bytes(state, AlertDeepLinkJsonContext.Default.LogsDeepLinkState));
    }

    /// <summary>
    /// The "show me what fired" link for any rule kind: <see cref="BuildMatchingLogsUrl"/> for a
    /// log-count series, <see cref="BuildMetricChartUrl"/> for a metric one,
    /// <see cref="BuildMatchingExceptionsUrl"/> for an exception-count one (an
    /// <see cref="AlertConditionKind.Anomaly"/> rule follows its source's kind). Null whenever
    /// the matching builder is - see each one's remarks. <see cref="FiredDataLabel"/> names it.
    /// </summary>
    public static string? BuildFiredDataUrl(AlertRule rule, string? publicUrl, DateTimeOffset firedAt) =>
        RuleSeriesKind(rule) switch
        {
            AlertConditionKind.MetricThreshold => BuildMetricChartUrl(rule, publicUrl, firedAt),
            AlertConditionKind.ExceptionCount => BuildMatchingExceptionsUrl(rule, publicUrl, firedAt),
            _ => BuildMatchingLogsUrl(rule, publicUrl, firedAt),
        };

    /// <summary>What <see cref="BuildFiredDataUrl"/>'s link opens, for the text line in front of it and PagerDuty's link text.</summary>
    public static string FiredDataLabel(AlertRule rule) =>
        RuleSeriesKind(rule) switch
        {
            AlertConditionKind.MetricThreshold => "Metric chart",
            AlertConditionKind.ExceptionCount => "Matching exceptions",
            _ => "Matching logs",
        };

    /// <summary>
    /// Deep link into the Metrics Explorer charting the rule's metric over the evaluated window,
    /// as a Metrics saved-view state (<c>MetricsSavedViewState</c> in the dashboard's
    /// <c>lib/metrics/state.svelte.ts</c>) in <c>/metrics?state=</c> - same encoding and restore
    /// path as <see cref="BuildMatchingLogsUrl"/>.
    /// </summary>
    /// <remarks>
    /// <c>selectedMetric</c> carries no <c>serviceName</c>: the explorer charts one
    /// (metric, service) pair at a time, but a rule with no service filter (all the rule form
    /// creates) aggregates every service. The dashboard picks the first matching pair within
    /// the rule's own services and narrows the picker to that metric name, so the other
    /// services' entries sit right beside it. Null when the condition has attribute filters -
    /// the explorer has no attribute filter to restore them into, and the chart would show a
    /// wider series than what fired.
    /// </remarks>
    public static string? BuildMetricChartUrl(AlertRule rule, string? publicUrl, DateTimeOffset firedAt)
    {
        if (string.IsNullOrWhiteSpace(publicUrl)
            || RuleSeriesKind(rule) != AlertConditionKind.MetricThreshold
            || rule.MetricCondition is not { } metric
            || metric.Filter.Attributes is { Count: > 0 })
        {
            return null;
        }

        var state = new MetricsDeepLinkState
        {
            CustomRange = EvaluatedWindow(rule, firedAt),
            Services = metric.Filter.Services ?? [],
            SelectedMetric = new MetricsDeepLinkMetric { MetricName = metric.MetricName, Type = metric.Type },
        };

        return BuildStateUrl(publicUrl, "/metrics", JsonSerializer.SerializeToUtf8Bytes(state, AlertDeepLinkJsonContext.Default.MetricsDeepLinkState));
    }

    /// <summary>
    /// Deep link into the Exceptions page scoped to the rule's exception type (and message, when
    /// the rule narrows to one) and services over the evaluated window, in <c>/errors?state=</c> -
    /// read by <c>parseErrorsStateDeepLinkParam</c> in the dashboard's <c>lib/deep-links.ts</c>.
    /// Every <see cref="ExceptionCountCondition"/> field has a slot there, so this is never null
    /// for an exception-count rule with a public URL.
    /// </summary>
    public static string? BuildMatchingExceptionsUrl(AlertRule rule, string? publicUrl, DateTimeOffset firedAt)
    {
        if (string.IsNullOrWhiteSpace(publicUrl)
            || RuleSeriesKind(rule) != AlertConditionKind.ExceptionCount
            || rule.ExceptionCondition is not { } exception)
        {
            return null;
        }

        var state = new ErrorsDeepLinkState
        {
            CustomRange = EvaluatedWindow(rule, firedAt),
            Services = exception.Filter.Services ?? [],
            ExceptionType = exception.ExceptionType,
            ExceptionMessage = exception.ExceptionMessage,
        };

        return BuildStateUrl(publicUrl, "/errors", JsonSerializer.SerializeToUtf8Bytes(state, AlertDeepLinkJsonContext.Default.ErrorsDeepLinkState));
    }

    /// <summary><c>[firedAt - WindowSeconds, firedAt]</c> - the range <c>AlertEvaluationWorker</c> evaluated over.</summary>
    private static DeepLinkRange EvaluatedWindow(AlertRule rule, DateTimeOffset firedAt) => new()
    {
        From = FormatIso(firedAt - TimeSpan.FromSeconds(rule.WindowSeconds)),
        To = FormatIso(firedAt),
    };

    /// <summary>See <see cref="BuildMatchingLogsUrl"/>'s remarks for why standard base64 + percent-escaping.</summary>
    private static string BuildStateUrl(string publicUrl, string path, byte[] json) =>
        $"{publicUrl.TrimEnd('/')}{path}?state={Uri.EscapeDataString(Convert.ToBase64String(json))}";

    private static string FormatIso(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}

/// <summary>
/// A notification's rendered content - see <see cref="AlertMessageFormatter.BuildMessage"/>.
/// <see cref="Title"/> is null unless the rule sets a title template, in which case each
/// notifier uses its own built-in subject/heading (Email's subject line, etc.).
/// <see cref="IsCustom"/> is true when either template was applied - <see cref="TelegramAlertNotifier"/>
/// then sends plain text, since user-authored text isn't guaranteed to be valid Telegram Markdown.
/// </summary>
public sealed record AlertMessage(string? Title, string Text, bool IsCustom)
{
    /// <summary>Title and text as one plain-text message, for channels with no separate subject field.</summary>
    public string Combined => Title is null ? Text : $"{Title}\n{Text}";
}

/// <summary>Mirrors the dashboard's <c>LogsSavedViewState</c> - only the members a <see cref="LogFilter"/> can populate; the rest take <c>applySavedViewState</c>'s own defaults. See <see cref="AlertMessageFormatter.BuildMatchingLogsUrl"/>.</summary>
internal sealed record LogsDeepLinkState
{
    public string TimeRangePreset { get; init; } = "custom";
    public required DeepLinkRange CustomRange { get; init; }
    public required IReadOnlyList<string> Services { get; init; }
    public required IReadOnlyList<int> SeverityNumbers { get; init; }
    public required string Search { get; init; }
    public required IReadOnlyList<AttributeFilter> AttributeFilters { get; init; }
    public required IReadOnlyList<BodyJsonFilter> BodyJsonFilters { get; init; }
    public required IReadOnlyList<string> ScopeNames { get; init; }
}

/// <summary>Mirrors the dashboard's <c>MetricsSavedViewState</c> - just the range, services and metric; the rest take <c>applySavedViewState</c>'s defaults. See <see cref="AlertMessageFormatter.BuildMetricChartUrl"/>.</summary>
internal sealed record MetricsDeepLinkState
{
    public string TimeRangePreset { get; init; } = "custom";
    public required DeepLinkRange CustomRange { get; init; }
    public required IReadOnlyList<string> Services { get; init; }
    public required MetricsDeepLinkMetric SelectedMetric { get; init; }
}

internal sealed record MetricsDeepLinkMetric
{
    public required string MetricName { get; init; }
    public required MetricPointType Type { get; init; }
}

/// <summary>Mirrors the dashboard's <c>ErrorsDeepLinkState</c> (<c>lib/deep-links.ts</c>). See <see cref="AlertMessageFormatter.BuildMatchingExceptionsUrl"/>.</summary>
internal sealed record ErrorsDeepLinkState
{
    public required DeepLinkRange CustomRange { get; init; }
    public required IReadOnlyList<string> Services { get; init; }
    public required string ExceptionType { get; init; }
    public required string ExceptionMessage { get; init; }
}

internal sealed record DeepLinkRange
{
    public required string From { get; init; }
    public required string To { get; init; }
}
