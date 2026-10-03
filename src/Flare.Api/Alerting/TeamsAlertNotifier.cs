using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Alerting;

/// <summary>
/// POSTs a fired alert to a Microsoft Teams Workflows webhook
/// (<see cref="NotificationChannel.WebhookUrl"/>) as a <c>message</c> carrying one Adaptive
/// Card - the generic <see cref="WebhookAlertNotifier"/> payload isn't accepted by Teams, whose
/// Workflows ("Post to a channel when a webhook request is received") trigger requires the card
/// envelope. Used by <see cref="CompositeAlertNotifier"/> for a
/// <see cref="NotificationChannelType.Teams"/> channel.
/// </summary>
/// <remarks>
/// Teams answers 202 Accepted. The card is a title/body <c>TextBlock</c> pair plus
/// <c>Action.OpenUrl</c> buttons for the rule and the fired data. A custom template is sent as
/// plain text (Adaptive Card <c>TextBlock</c> would interpret its own Markdown subset).
/// </remarks>
public sealed class TeamsAlertNotifier(HttpClient httpClient, IOptions<AlertLinkOptions> linkOptions) : IAlertNotifier
{
    internal static object BuildPayload(AlertMessage message, string? ruleUrl, string? dataUrl, string dataLabel)
    {
        var body = new List<object>();
        if (message.Title is not null)
        {
            body.Add(new Dictionary<string, object?> { ["type"] = "TextBlock", ["text"] = message.Title, ["weight"] = "Bolder", ["size"] = "Medium", ["wrap"] = true });
        }

        body.Add(new Dictionary<string, object?> { ["type"] = "TextBlock", ["text"] = message.Text, ["wrap"] = true });

        var actions = new List<object>();
        if (ruleUrl is not null)
        {
            actions.Add(new Dictionary<string, object?> { ["type"] = "Action.OpenUrl", ["title"] = "View rule", ["url"] = ruleUrl });
        }

        if (dataUrl is not null)
        {
            actions.Add(new Dictionary<string, object?> { ["type"] = "Action.OpenUrl", ["title"] = $"{dataLabel} in Flare", ["url"] = dataUrl });
        }

        var card = new Dictionary<string, object?>
        {
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["type"] = "AdaptiveCard",
            ["version"] = "1.4",
            ["body"] = body,
        };
        if (actions.Count > 0)
        {
            card["actions"] = actions;
        }

        return new
        {
            type = "message",
            attachments = new[]
            {
                new Dictionary<string, object?>
                {
                    ["contentType"] = "application/vnd.microsoft.card.adaptive",
                    ["contentUrl"] = null,
                    ["content"] = card,
                },
            },
        };
    }

    public Task<NotificationResult> SendAsync(AlertRule rule, NotificationChannel channel, double observedValue, DateTimeOffset firedAt, CancellationToken cancellationToken, bool isTest = false, string? metricUnit = null, bool noData = false, AnomalyScore? anomaly = null, bool resolved = false)
    {
        var publicUrl = linkOptions.Value.PublicUrl;
        // The buttons carry the links, so the built-in text doesn't repeat them (appendLinks: false).
        var message = AlertMessageFormatter.BuildMessage(rule, observedValue, isTest, publicUrl, metricUnit, firedAt, noData, anomaly, appendLinks: false, resolved: resolved);
        var ruleUrl = AlertMessageFormatter.BuildRuleUrl(rule, publicUrl);
        // No fired-data link for a no-data fire - by definition there is no matching data to show.
        var dataUrl = noData ? null : AlertMessageFormatter.BuildFiredDataUrl(rule, publicUrl, firedAt);
        var payload = BuildPayload(message, ruleUrl, dataUrl, AlertMessageFormatter.FiredDataLabel(rule));
        return WebhookPost.SendAsync(httpClient, channel.WebhookUrl, payload, cancellationToken);
    }
}
