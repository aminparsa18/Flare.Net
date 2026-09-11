using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Alerting;

/// <summary>
/// POSTs a fired alert to PagerDuty's Events API v2 <c>enqueue</c> endpoint (the
/// channel's <see cref="NotificationChannel.PagerDutyRoutingKey"/>) - the PagerDuty
/// counterpart to
/// <see cref="WebhookAlertNotifier"/>/<see cref="TelegramAlertNotifier"/>/<see cref="EmailAlertNotifier"/>,
/// picked by <see cref="CompositeAlertNotifier"/> instead of them for a
/// <see cref="NotificationChannelType.PagerDuty"/> channel.
/// </summary>
/// <remarks>
/// Unlike <see cref="EmailOptions"/>, there's no app-wide server config here: the routing
/// key alone addresses <see cref="EventsApiUrl"/>, a fixed PagerDuty endpoint, not a
/// per-deployment server. Every send is an <c>event_action: "trigger"</c> with no
/// <c>dedup_key</c> - each breach (or test send) opens a new PagerDuty incident rather
/// than deduplicating/auto-resolving against a prior one; a follow-up if that's ever
/// wanted, not built now (same "smallest thing that satisfies the four-channel shape"
/// scope as the other three notifiers).
/// <para/>
/// PagerDuty returns HTTP 202 with <c>{"status":"success",...}</c> on success and a 4xx
/// with <c>{"status":"invalid event","errors":[...]}</c> on a malformed payload/bad
/// routing key - unlike Telegram, a non-2xx status reliably means failure, so
/// <see cref="System.Net.Http.HttpResponseMessage.IsSuccessStatusCode"/> alone decides
/// <see cref="NotificationResult.Success"/>; the response body is only parsed for a
/// clearer <see cref="NotificationResult.Error"/> message on failure.
/// </remarks>
public sealed class PagerDutyAlertNotifier(HttpClient httpClient, IOptions<AlertLinkOptions> linkOptions) : IAlertNotifier
{
    private const string EventsApiUrl = "https://events.pagerduty.com/v2/enqueue";

    public async Task<NotificationResult> SendAsync(AlertRule rule, NotificationChannel channel, double observedValue, DateTimeOffset firedAt, CancellationToken cancellationToken, bool isTest = false)
    {
        // Not folded into `summary` below the way the other three notifiers append it to
        // their plain-text message - PagerDuty renders `summary` as a single-line incident
        // title (truncated in list views), not a place for a trailing URL line. `client_url`
        // is PagerDuty's own dedicated field for this: it renders as a clickable "View in
        // {client}" link on the incident. Null when Alerting:PublicUrl isn't configured -
        // PagerDuty tolerates the field's absence/null the same way the other notifiers omit
        // the link entirely.
        var ruleUrl = AlertMessageFormatter.BuildRuleUrl(rule, linkOptions.Value.PublicUrl);
        var isMetric = rule.ConditionKind == AlertConditionKind.MetricThreshold;
        var payload = new
        {
            routing_key = channel.PagerDutyRoutingKey,
            event_action = "trigger",
            client = "Flare",
            client_url = ruleUrl,
            payload = new
            {
                summary = AlertMessageFormatter.BuildText(rule, observedValue, isTest),
                source = "flare",
                severity = isTest ? "info" : "critical",
                timestamp = firedAt,
                custom_details = new
                {
                    ruleId = rule.Id,
                    ruleName = rule.Name,
                    conditionKind = rule.ConditionKind.ToString(),
                    observedCount = isMetric ? 0UL : (ulong)observedValue,
                    thresholdCount = rule.Threshold.Count,
                    observedValue,
                    thresholdValue = rule.MetricThresholdValue,
                    metricName = rule.MetricCondition?.MetricName,
                    windowSeconds = rule.WindowSeconds,
                    ruleUrl,
                },
            },
        };

        try
        {
            using var response = await httpClient.PostAsJsonAsync(EventsApiUrl, payload, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new NotificationResult(true, (int)response.StatusCode, null);
            }

            PagerDutyEnqueueResponse? body = null;
            try
            {
                body = await response.Content.ReadFromJsonAsync(PagerDutyJsonContext.Default.PagerDutyEnqueueResponse, cancellationToken);
            }
            catch (JsonException)
            {
                // Fall through - body is left null and a generic HTTP-status error is reported below.
            }

            var error = body?.Errors is { Count: > 0 } errors
                ? string.Join("; ", errors)
                : body?.Message ?? $"HTTP {(int)response.StatusCode}";
            return new NotificationResult(false, (int)response.StatusCode, error);
        }
        catch (Exception ex) when (ex is HttpRequestException or UriFormatException)
        {
            // DNS/connection failures and a malformed routing key/URL - recorded as a
            // failed notification rather than left to bubble up and abort the tick for
            // every other rule.
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

/// <summary>
/// Minimal shape of PagerDuty's Events API v2 <c>enqueue</c> response - only what
/// <see cref="PagerDutyAlertNotifier.SendAsync"/> needs for a clearer failure message.
/// Top-level (not nested) so <see cref="PagerDutyJsonContext"/>, in a different
/// namespace, can see it.
/// </summary>
public sealed record PagerDutyEnqueueResponse
{
    public string? Status { get; init; }

    public string? Message { get; init; }

    public IReadOnlyList<string>? Errors { get; init; }
}
