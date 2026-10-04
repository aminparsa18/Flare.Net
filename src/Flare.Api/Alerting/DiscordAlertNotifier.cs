using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Alerting;

/// <summary>
/// POSTs a fired alert to a Discord webhook (<see cref="NotificationChannel.WebhookUrl"/>) as
/// a <c>content</c> message - the generic <see cref="WebhookAlertNotifier"/> payload has no
/// <c>content</c> field, which Discord requires. Used by <see cref="CompositeAlertNotifier"/>
/// for a <see cref="NotificationChannelType.Discord"/> channel.
/// </summary>
/// <remarks>
/// Discord caps <c>content</c> at 2000 characters, so longer text is truncated. Mentions are
/// disabled (<c>allowed_mentions.parse = []</c>): an alert's service/log text is untrusted and
/// must not be able to ping <c>@everyone</c>. A custom template is sent as plain text.
/// </remarks>
public sealed class DiscordAlertNotifier(HttpClient httpClient, IOptions<AlertLinkOptions> linkOptions) : IAlertNotifier
{
    internal const int MaxContentLength = 2000;

    internal static string BuildContent(AlertMessage message)
    {
        var content = message.Title is null ? message.Text : $"**{message.Title}**\n{message.Text}";
        return content.Length <= MaxContentLength ? content : content[..(MaxContentLength - 1)] + "…";
    }

    public Task<NotificationResult> SendAsync(AlertRule rule, NotificationChannel channel, double observedValue, DateTimeOffset firedAt, CancellationToken cancellationToken, bool isTest = false, string? metricUnit = null, bool noData = false, AnomalyScore? anomaly = null, bool resolved = false, string? logSamples = null)
    {
        var message = AlertMessageFormatter.BuildMessage(rule, observedValue, isTest, linkOptions.Value.PublicUrl, metricUnit, firedAt, noData, anomaly, resolved: resolved, logSamples: logSamples);
        var payload = new
        {
            username = "Flare",
            content = BuildContent(message),
            allowed_mentions = new { parse = Array.Empty<string>() },
        };

        return WebhookPost.SendAsync(httpClient, channel.WebhookUrl, payload, cancellationToken);
    }
}
