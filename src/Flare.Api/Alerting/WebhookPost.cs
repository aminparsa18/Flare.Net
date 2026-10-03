namespace Flare.Api.Alerting;

/// <summary>
/// The POST-and-record-the-outcome step <see cref="TeamsAlertNotifier"/> and
/// <see cref="DiscordAlertNotifier"/> share: a network/URL/timeout failure becomes a failed
/// <see cref="NotificationResult"/> rather than an exception that would abort the tick for
/// every other rule (same contract as <see cref="WebhookAlertNotifier"/>).
/// </summary>
internal static class WebhookPost
{
    public static async Task<NotificationResult> SendAsync(HttpClient httpClient, string url, object payload, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync(url, payload, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new NotificationResult(true, (int)response.StatusCode, null);
            }

            // Both services put a short human-readable reason in the body on a rejection
            // (Discord: {"message": "..."}, Teams: plain text) - worth surfacing, capped.
            string? detail = null;
            try
            {
                detail = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                // Body unreadable - fall back to the bare status below.
            }

            if (detail is { Length: > 300 })
            {
                detail = detail[..300] + "…";
            }

            return new NotificationResult(false, (int)response.StatusCode, string.IsNullOrEmpty(detail) ? $"HTTP {(int)response.StatusCode}" : $"HTTP {(int)response.StatusCode}: {detail}");
        }
        catch (Exception ex) when (ex is HttpRequestException or UriFormatException)
        {
            return new NotificationResult(false, 0, ex.Message);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Client-side timeout, not app shutdown - see WebhookAlertNotifier.
            return new NotificationResult(false, 0, ex.Message);
        }
    }
}
