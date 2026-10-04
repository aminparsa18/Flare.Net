using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Alerting;

/// <summary>
/// Opens and closes a Jira Service Management Operations alert per firing rule, through JSM
/// Ops' native integration gateway (the Opsgenie-compatible alerts API, authenticated with the
/// channel's <see cref="NotificationChannel.JsmOpsApiKey"/> as <c>GenieKey</c>). Used by
/// <see cref="CompositeAlertNotifier"/> for a <see cref="NotificationChannelType.JsmOps"/>
/// channel. See <c>docs-internal/adr/0099-jsm-ops-notification-channel.md</c>.
/// </summary>
/// <remarks>
/// The <c>alias</c> (<see cref="Alias"/>) is JSM Ops' own dedup key: a repeat create with an open
/// alias folds into the existing alert, so no search is needed. A recovery posts to
/// <c>/alerts/{alias}/close</c>; a fire after a close opens a fresh alert. A test send gets a
/// one-off alias. The API answers 202 and processes asynchronously.
/// </remarks>
public sealed class JsmOpsAlertNotifier(HttpClient httpClient, IOptions<AlertLinkOptions> linkOptions) : IAlertNotifier
{
    private const string ApiBase = "https://api.atlassian.com/jsm/ops/integration/v2/alerts";

    /// <summary>JSM Ops' documented caps.</summary>
    internal const int MaxMessageLength = 130;

    internal const int MaxDescriptionLength = 15_000;

    public static string Alias(AlertRule rule) => $"flare-alert-{rule.Id:N}";

    internal static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    /// <summary>Critical→P1 … Info→P5.</summary>
    internal static string Priority(AlertSeverity severity) => severity switch
    {
        AlertSeverity.Critical => "P1",
        AlertSeverity.Error => "P2",
        AlertSeverity.Warning => "P3",
        _ => "P5",
    };

    public Task<NotificationResult> SendAsync(AlertRule rule, NotificationChannel channel, double observedValue, DateTimeOffset firedAt, CancellationToken cancellationToken, bool isTest = false, string? metricUnit = null, bool noData = false, AnomalyScore? anomaly = null, bool resolved = false, string? logSamples = null)
    {
        if (resolved && !isTest)
        {
            return WebhookPost.SendAsync(
                httpClient,
                $"{ApiBase}/{Uri.EscapeDataString(Alias(rule))}/close?identifierType=alias",
                new { user = "Flare", source = "Flare", note = "Recovered." },
                cancellationToken,
                authorization: GenieKey(channel));
        }

        var message = AlertMessageFormatter.BuildMessage(rule, observedValue, isTest, linkOptions.Value.PublicUrl, metricUnit, firedAt, noData, anomaly, logSamples: logSamples);
        var payload = new
        {
            message = Truncate(message.Title ?? message.Text.Split('\n', 2)[0], MaxMessageLength),
            alias = isTest ? $"flare-test-{Guid.NewGuid():N}" : Alias(rule),
            description = Truncate(message.Text, MaxDescriptionLength),
            priority = isTest ? "P5" : Priority(rule.Severity),
            source = "Flare",
            tags = rule.Labels.Select(l => $"{l.Key}:{l.Value}").Prepend("flare").ToArray(),
            details = new Dictionary<string, string> { ["rule"] = rule.Name },
        };

        return WebhookPost.SendAsync(httpClient, ApiBase, payload, cancellationToken, authorization: GenieKey(channel));
    }

    private static System.Net.Http.Headers.AuthenticationHeaderValue GenieKey(NotificationChannel channel) => new("GenieKey", channel.JsmOpsApiKey);
}
