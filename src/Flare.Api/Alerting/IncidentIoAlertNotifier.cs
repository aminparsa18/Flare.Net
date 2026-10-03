using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Alerting;

/// <summary>
/// Sends a fired alert to an incident.io HTTP alert source (Alert Events V2) - the source URL in
/// <see cref="NotificationChannel.WebhookUrl"/>, its bearer token in
/// <see cref="NotificationChannel.IncidentIoToken"/>. Used by <see cref="CompositeAlertNotifier"/>
/// for a <see cref="NotificationChannelType.IncidentIo"/> channel. See
/// <c>docs-internal/adr/0098-incidentio-notification-channel.md</c>.
/// </summary>
/// <remarks>
/// A rule's firing and its recovery share <see cref="DeduplicationKey"/>, so a <c>resolved</c>
/// event closes the alert the <c>firing</c> one opened, and a re-fire while open is dropped by
/// incident.io as a duplicate. A test send gets a one-off key. Rule labels go out as
/// <c>metadata</c> so they can be mapped to incident.io attributes and used for routing.
/// </remarks>
public sealed class IncidentIoAlertNotifier(HttpClient httpClient, IOptions<AlertLinkOptions> linkOptions) : IAlertNotifier
{
    /// <summary>incident.io documents a 512 KB payload limit; the description is capped well under it.</summary>
    internal const int MaxDescriptionLength = 100_000;

    public static string DeduplicationKey(AlertRule rule) => $"flare-alert-{rule.Id:N}";

    internal static string CapDescription(string text) =>
        text.Length <= MaxDescriptionLength ? text : text[..(MaxDescriptionLength - 1)] + "…";

    public Task<NotificationResult> SendAsync(AlertRule rule, NotificationChannel channel, double observedValue, DateTimeOffset firedAt, CancellationToken cancellationToken, bool isTest = false, string? metricUnit = null, bool noData = false, AnomalyScore? anomaly = null, bool resolved = false)
    {
        var publicUrl = linkOptions.Value.PublicUrl;
        var message = AlertMessageFormatter.BuildMessage(rule, observedValue, isTest, publicUrl, metricUnit, firedAt, noData, anomaly, resolved: resolved);
        var metadata = new Dictionary<string, string>(rule.Labels)
        {
            ["rule"] = rule.Name,
            ["severity"] = isTest ? "info" : rule.Severity.ToString().ToLowerInvariant(),
        };
        var payload = new
        {
            deduplication_key = isTest ? $"flare-test-{Guid.NewGuid():N}" : DeduplicationKey(rule),
            title = message.Title ?? message.Text.Split('\n', 2)[0],
            description = CapDescription(message.Text),
            status = resolved && !isTest ? "resolved" : "firing",
            source_url = AlertMessageFormatter.BuildRuleUrl(rule, publicUrl),
            metadata,
        };

        return WebhookPost.SendAsync(httpClient, channel.WebhookUrl, payload, cancellationToken, channel.IncidentIoToken);
    }
}
