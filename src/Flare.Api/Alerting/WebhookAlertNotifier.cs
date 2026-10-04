using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Alerting;

/// <summary>
/// POSTs a fired alert as JSON to the channel's <see cref="NotificationChannel.WebhookUrl"/> -
/// covers both a generic webhook consumer and a Slack incoming-webhook URL with one
/// payload shape (see remarks), rather than a per-rule "payload style" toggle.
/// </summary>
/// <remarks>
/// The payload always carries a top-level <c>text</c> field - what Slack's
/// incoming-webhook parser renders as the message - plus flat structured fields
/// (<c>ruleId</c>, <c>observedCount</c>, etc.) a generic webhook consumer can read
/// directly. Slack ignores unrecognized top-level JSON keys, so one shape serves both
/// audiences. Named follow-up if a consumer ever needs Slack <c>blocks</c> formatting or
/// a stricter generic-webhook schema: a per-rule payload-style field - not built now.
/// </remarks>
public sealed class WebhookAlertNotifier(HttpClient httpClient, IOptions<AlertLinkOptions> linkOptions) : IAlertNotifier
{
    /// <summary>
    /// True for a Slack incoming-webhook URL (<c>hooks.slack.com</c>/<c>hooks.slack-gov.com</c>),
    /// whose <c>text</c> is rendered as <c>mrkdwn</c> - a custom template then gets Slack
    /// formatting; any other URL gets plain text. Host-based, not a channel setting (ADR-0090).
    /// </summary>
    internal static bool IsSlackWebhook(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Host.Equals("hooks.slack.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("hooks.slack-gov.com", StringComparison.OrdinalIgnoreCase));

    public async Task<NotificationResult> SendAsync(AlertRule rule, NotificationChannel channel, double observedValue, DateTimeOffset firedAt, CancellationToken cancellationToken, bool isTest = false, string? metricUnit = null, bool noData = false, AnomalyScore? anomaly = null, bool resolved = false)
    {
        var ruleUrl = AlertMessageFormatter.BuildRuleUrl(rule, linkOptions.Value.PublicUrl);
        // No scoped-logs link for a no-data fire - by definition there are no matching logs to show.
        var logsUrl = noData ? null : AlertMessageFormatter.BuildMatchingLogsUrl(rule, linkOptions.Value.PublicUrl, firedAt);
        var dataUrl = noData ? null : AlertMessageFormatter.BuildFiredDataUrl(rule, linkOptions.Value.PublicUrl, firedAt);
        // A burn-rate rule's observed value is a rate (e.g. 14.4), not a count - same non-count handling as a metric.
        var isMetric = AnomalyScoring.SeriesKind(rule.ConditionKind, rule.AnomalyCondition) == AlertConditionKind.MetricThreshold || rule.ConditionKind == AlertConditionKind.SloBurnRate;
        var message = AlertMessageFormatter.BuildMessage(rule, observedValue, isTest, linkOptions.Value.PublicUrl, metricUnit, firedAt, noData, anomaly, resolved: resolved, format: IsSlackWebhook(channel.WebhookUrl) ? AlertMarkupFormat.SlackMrkdwn : AlertMarkupFormat.Plain);
        var payload = new
        {
            // Slack renders only `text`, so a custom title goes in as its first line; `title`
            // repeats it on its own for a generic consumer (null without a title template).
            text = message.Combined,
            // "firing" | "resolved" - Alertmanager's webhook vocabulary, so a consumer that
            // already routes on it needs no special case. A resolved payload's observed fields
            // are the recovered value and firedAt is when the recovery was observed.
            status = resolved && !isTest ? "resolved" : "firing",
            title = message.Title,
            ruleId = rule.Id,
            ruleName = rule.Name,
            // The rule's user-defined labels ({"team":"payments"}) - empty object when none.
            labels = rule.Labels,
            conditionKind = rule.ConditionKind.ToString(),
            // observedCount/thresholdCount stay ulong (unchanged wire shape for existing
            // LogCount consumers); observedValue/thresholdValue are the new generic doubles
            // a MetricThreshold consumer reads instead - same "field present, meaningful
            // only for one mode" convention the rest of AlertRule already uses.
            observedCount = isMetric ? 0UL : (ulong)observedValue,
            thresholdCount = rule.Threshold.Count,
            observedValue,
            // An anomaly rule has no fixed threshold - see baselineMean/zScore below instead.
            thresholdValue = AlertMessageFormatter.ThresholdValueOf(rule),
            metricName = rule.MetricCondition?.MetricName,
            windowSeconds = noData ? rule.NoDataWindowSeconds : rule.WindowSeconds,
            // True for an absent-data fire (AlertRule.NoDataWindowSeconds) - the observed
            // fields above are then 0/placeholders and windowSeconds is the no-data window.
            noData,
            // Set only for an Anomaly fire: the seasonal baseline observedValue was scored
            // against (ADR-0048). Null for every other kind.
            baselineMean = anomaly?.BaselineMean,
            zScore = anomaly?.ZScore,
            baselineSamples = anomaly?.SampleCount,
            firedAt,
            // Null when Alerting:PublicUrl isn't configured - same "no link rather than a
            // broken one" contract as the text field's own link line. Kept as a dedicated
            // field (rather than making the generic webhook consumer parse it back out of
            // text) since that consumer reads the flat fields above directly, not text.
            ruleUrl,
            // The Logs Explorer scoped to the rule's filter over the evaluated window - null
            // whenever BuildMatchingLogsUrl can't represent the rule faithfully (see its remarks).
            // Logs-only, unchanged for existing consumers - dataUrl below covers every kind.
            logsUrl,
            // The fired data for any rule kind (Logs, the metric's chart, or the matching
            // exceptions) - see AlertMessageFormatter.BuildFiredDataUrl. Equals logsUrl for LogCount.
            dataUrl,
        };

        try
        {
            using var response = await httpClient.PostAsJsonAsync(channel.WebhookUrl, payload, cancellationToken);
            return new NotificationResult(
                response.IsSuccessStatusCode,
                (int)response.StatusCode,
                response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or UriFormatException)
        {
            // DNS/connection failures and a malformed WebhookUrl - recorded as a failed
            // notification rather than left to bubble up and abort the tick for every
            // other rule.
            return new NotificationResult(false, 0, ex.Message);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // A client-side timeout (the resilience handler's own timeout, or
            // HttpClient.Timeout) throws OperationCanceledException too, but with the
            // *caller's* token still uncancelled - distinguishes that from real
            // cancellation-by-app-shutdown, which should propagate normally rather than
            // be recorded as a "failed" notification.
            return new NotificationResult(false, 0, ex.Message);
        }
    }
}
