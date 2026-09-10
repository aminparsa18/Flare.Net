using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;

namespace Flare.Api.Alerting;

/// <summary>
/// POSTs a fired alert to PagerDuty's Events API v2 <c>enqueue</c> endpoint
/// (<see cref="AlertRule.PagerDutyRoutingKey"/>) - the PagerDuty counterpart to
/// <see cref="WebhookAlertNotifier"/>/<see cref="TelegramAlertNotifier"/>/<see cref="EmailAlertNotifier"/>,
/// picked by <see cref="CompositeAlertNotifier"/> instead of them when a rule has
/// <see cref="AlertRule.PagerDutyRoutingKey"/> set.
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
public sealed class PagerDutyAlertNotifier(HttpClient httpClient) : IAlertNotifier
{
    private const string EventsApiUrl = "https://events.pagerduty.com/v2/enqueue";

    public async Task<NotificationResult> SendAsync(AlertRule rule, ulong observedCount, DateTimeOffset firedAt, CancellationToken cancellationToken, bool isTest = false)
    {
        var payload = new
        {
            routing_key = rule.PagerDutyRoutingKey,
            event_action = "trigger",
            payload = new
            {
                summary = AlertMessageFormatter.BuildText(rule, observedCount, isTest),
                source = "flare",
                severity = isTest ? "info" : "critical",
                timestamp = firedAt,
                custom_details = new
                {
                    ruleId = rule.Id,
                    ruleName = rule.Name,
                    observedCount,
                    thresholdCount = rule.Threshold.Count,
                    windowSeconds = rule.WindowSeconds,
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
