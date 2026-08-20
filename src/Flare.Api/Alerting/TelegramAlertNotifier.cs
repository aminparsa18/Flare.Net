using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;

namespace Flare.Api.Alerting;

/// <summary>
/// POSTs a fired alert to a Telegram bot's <c>sendMessage</c> method
/// (<see cref="AlertRule.TelegramBotToken"/> + <see cref="AlertRule.TelegramChatId"/>) -
/// the Telegram counterpart to <see cref="WebhookAlertNotifier"/>, picked by
/// <see cref="CompositeAlertNotifier"/> instead of it when a rule has Telegram fields set.
/// </summary>
/// <remarks>
/// Telegram's Bot API returns HTTP 200 with <c>{"ok":false,"description":"..."}</c> for
/// most delivery failures (bad chat ID, bot blocked/kicked, etc.) rather than a non-2xx
/// status - unlike Slack/generic webhooks, <c>response.IsSuccessStatusCode</c> alone would
/// misreport those as "Sent". <see cref="NotificationResult.Success"/> is therefore derived
/// from the parsed <c>ok</c> field, falling back to the HTTP status only when the body
/// itself can't be parsed (a malformed/unexpected response, e.g. from a proxy in front of
/// api.telegram.org).
/// </remarks>
public sealed class TelegramAlertNotifier(HttpClient httpClient) : IAlertNotifier
{
    public async Task<NotificationResult> SendAsync(AlertRule rule, ulong observedCount, DateTimeOffset firedAt, CancellationToken cancellationToken)
    {
        var payload = new
        {
            chat_id = rule.TelegramChatId,
            text = AlertMessageFormatter.BuildText(rule, observedCount),
            parse_mode = "Markdown",
        };

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                $"https://api.telegram.org/bot{rule.TelegramBotToken}/sendMessage",
                payload,
                cancellationToken);

            TelegramSendMessageResponse? body = null;
            try
            {
                body = await response.Content.ReadFromJsonAsync(TelegramJsonContext.Default.TelegramSendMessageResponse, cancellationToken);
            }
            catch (JsonException)
            {
                // Fall through - body is left null and the HTTP status alone decides below.
            }

            if (body is not null)
            {
                return new NotificationResult(
                    body.Ok,
                    (int)response.StatusCode,
                    body.Ok ? null : body.Description ?? $"Telegram returned ok=false (HTTP {(int)response.StatusCode})");
            }

            return new NotificationResult(
                response.IsSuccessStatusCode,
                (int)response.StatusCode,
                response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or UriFormatException)
        {
            // DNS/connection failures and a malformed bot token/URL - recorded as a failed
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

/// <summary>
/// Minimal shape of Telegram's <c>sendMessage</c> response - only what
/// <see cref="TelegramAlertNotifier.SendAsync"/> needs. Top-level (not nested) so
/// <see cref="TelegramJsonContext"/>, in a different namespace, can see it.
/// </summary>
public sealed record TelegramSendMessageResponse
{
    public bool Ok { get; init; }

    public string? Description { get; init; }
}
