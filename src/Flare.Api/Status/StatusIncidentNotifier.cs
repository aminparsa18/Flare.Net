using Flare.Api.Alerting;
using Flare.Api.Model;
using Flare.Api.Query;
using Microsoft.Extensions.Options;

namespace Flare.Api.Status;

/// <summary>
/// Tells a status page's subscribed channels (ADR-0161) and verified email subscribers (ADR-0162) that an incident
/// was opened or updated. Channels are sent through
/// the existing alert notifiers by dressing the incident as an <see cref="AlertRule"/> whose title and
/// body are literal text, so no channel needs incident-specific code.
/// </summary>
public interface IStatusIncidentNotifier
{
    /// <summary>Sends <paramref name="update"/> to the page's subscribers. Never throws: a dead channel must not fail the incident write.</summary>
    Task NotifyAsync(StatusPage page, StatusIncident incident, StatusIncidentUpdate update, CancellationToken cancellationToken);
}

public sealed class StatusIncidentNotifier(
    CompositeAlertNotifier notifier,
    INotificationChannelQueryService channels,
    IOptions<AlertLinkOptions> linkOptions,
    IStatusSubscriberQueryService subscribers,
    IStatusSubscriberMailer mailer,
    IStatusSubscriptionLinkSigner signer,
    ILogger<StatusIncidentNotifier> logger) : IStatusIncidentNotifier
{
    /// <summary>Channel types that make sense for a human-readable announcement; PagerDuty, Jira and the like would open a ticket or page someone.</summary>
    public static bool IsSupported(NotificationChannelType type) =>
        type is NotificationChannelType.Webhook or NotificationChannelType.Telegram or NotificationChannelType.Email
            or NotificationChannelType.Teams or NotificationChannelType.Discord;

    public async Task NotifyAsync(StatusPage page, StatusIncident incident, StatusIncidentUpdate update, CancellationToken cancellationToken)
    {
        // Independent: a dead channel must not stop the emails, and the reverse.
        await NotifyChannelsAsync(page, incident, update, cancellationToken);
        await NotifyEmailSubscribersAsync(page, incident, update, cancellationToken);
    }

    private async Task NotifyEmailSubscribersAsync(StatusPage page, StatusIncident incident, StatusIncidentUpdate update, CancellationToken cancellationToken)
    {
        if (!mailer.IsAvailable)
        {
            return;
        }

        try
        {
            var recipients = (await subscribers.ListAsync(page.Id, cancellationToken))
                .Where(s => s.Verified && StatusSubscriptions.Wants(s, page.Id, incident.Components))
                .Select(s => signer.UnsubscribeUrl(page.Id, s.Email) is { } url ? new StatusMailRecipient(s.Email, url, page.Components.Count > 1 ? signer.PreferencesUrl(page.Id, s.Email) : null) : null)
                .OfType<StatusMailRecipient>()
                .ToList();
            var failed = await mailer.SendIncidentAsync(page, incident, update, ComponentNames(page, incident), recipients, cancellationToken);
            if (failed > 0)
            {
                logger.LogWarning("Status page '{Slug}': {Failed} of {Total} subscriber emails were not delivered", page.Slug, failed, recipients.Count);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Status page '{Slug}' subscriber emails failed", page.Slug);
        }
    }

    private async Task NotifyChannelsAsync(StatusPage page, StatusIncident incident, StatusIncidentUpdate update, CancellationToken cancellationToken)
    {
        if (page.SubscriberChannelIds.Count == 0)
        {
            return;
        }

        try
        {
            var targets = (await channels.GetByIdsAsync(page.SubscriberChannelIds, cancellationToken)).Where(c => IsSupported(c.Type)).ToList();
            if (targets.Count == 0)
            {
                return;
            }

            var rule = BuildRule(page, incident, update, ComponentNames(page, incident), linkOptions.Value.PublicUrl);
            var results = await notifier.SendAllAsync(rule, targets, 0, update.At, cancellationToken);
            for (var i = 0; i < results.Count; i++)
            {
                if (!results[i].Success)
                {
                    logger.LogWarning("Status page '{Slug}' incident notification to channel '{Channel}' failed: {Error}", page.Slug, targets[i].Name, results[i].Error);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Status page '{Slug}' incident notification failed", page.Slug);
        }
    }

    private static IReadOnlyList<string> ComponentNames(StatusPage page, StatusIncident incident) =>
        page.Components.Where(c => incident.Components.Contains(c.RefId)).Select(c => c.Name).ToList();

    /// <summary>The synthetic rule carrying the announcement. Pure, so the wording is unit-testable.</summary>
    internal static AlertRule BuildRule(StatusPage page, StatusIncident incident, StatusIncidentUpdate update, IReadOnlyList<string> components, string? publicUrl)
    {
        var lines = new List<string> { Literal(update.Message) };
        if (components.Count > 0)
        {
            lines.Add("Affected: " + Literal(string.Join(", ", components)));
        }

        if (!string.IsNullOrWhiteSpace(publicUrl))
        {
            lines.Add($"{publicUrl.TrimEnd('/')}/status/{page.Slug}");
        }

        return new AlertRule
        {
            Id = incident.Id,
            Name = page.Title,
            Condition = new LogFilter(),
            Threshold = new AlertThreshold { Count = 1 },
            WindowSeconds = 60,
            NotificationTitleTemplate = Literal($"[{page.Title}] {update.Status}: {incident.Title}"),
            NotificationBodyTemplate = string.Join("\n\n", lines),
            CreatedAt = incident.CreatedAt,
            UpdatedAt = update.At,
        };
    }

    /// <summary>Breaks <c>{{</c> so admin-written text is never read as an alert-template placeholder.</summary>
    internal static string Literal(string text) => text.Replace("{{", "{ {", StringComparison.Ordinal);
}
