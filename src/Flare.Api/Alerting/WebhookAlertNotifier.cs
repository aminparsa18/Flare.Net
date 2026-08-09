using System.Net.Http.Json;
using Flare.Api.Model;

namespace Flare.Api.Alerting;

/// <summary>
/// POSTs a fired alert as JSON to <see cref="AlertRule.WebhookUrl"/> - covers both a
/// generic webhook consumer and a Slack incoming-webhook URL with one payload shape (see
/// remarks), rather than a per-rule "payload style" toggle.
/// </summary>
/// <remarks>
/// The payload always carries a top-level <c>text</c> field - what Slack's
/// incoming-webhook parser renders as the message - plus flat structured fields
/// (<c>ruleId</c>, <c>observedCount</c>, etc.) a generic webhook consumer can read
/// directly. Slack ignores unrecognized top-level JSON keys, so one shape serves both
/// audiences. Named follow-up if a consumer ever needs Slack <c>blocks</c> formatting or
/// a stricter generic-webhook schema: a per-rule payload-style field - not built now.
/// </remarks>
public sealed class WebhookAlertNotifier(HttpClient httpClient) : IAlertNotifier
{
    public async Task<NotificationResult> SendAsync(AlertRule rule, ulong observedCount, DateTimeOffset firedAt, CancellationToken cancellationToken)
    {
        var comparatorSymbol = rule.Threshold.Comparator == ThresholdComparator.GreaterThanOrEqual ? ">=" : "<";
        var payload = new
        {
            text = $":rotating_light: Alert \"{rule.Name}\" fired: {observedCount} events " +
                   $"({comparatorSymbol} {rule.Threshold.Count}) in the last {rule.WindowSeconds}s",
            ruleId = rule.Id,
            ruleName = rule.Name,
            observedCount,
            thresholdCount = rule.Threshold.Count,
            windowSeconds = rule.WindowSeconds,
            firedAt,
        };

        try
        {
            using var response = await httpClient.PostAsJsonAsync(rule.WebhookUrl, payload, cancellationToken);
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
