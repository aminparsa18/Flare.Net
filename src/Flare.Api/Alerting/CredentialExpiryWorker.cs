using Flare.Api.Query;
using Flare.Api.Status;
using Flare.Identity.IngestKeys;
using Flare.Identity.PersonalAccessTokens;
using Flare.Identity.Users;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Flare.Api.Alerting;

/// <summary>
/// Warns the configured notification channels before an ingest key or personal access token expires (ADR-0208).
/// Each credential gets one warning per <see cref="CredentialExpiryOptions.WarnDays"/> threshold; a Redis
/// <c>SET NX</c> records it so restarts and several Flare.Api replicas don't repeat it. Like the other I/O workers
/// it is covered by end-to-end runs, with the decisions in <see cref="CredentialExpiryPolicy"/> unit-tested.
/// </summary>
public sealed class CredentialExpiryWorker(
    IIngestApiKeyStore ingestKeys,
    IPersonalAccessTokenStore tokens,
    IUserStore users,
    INotificationChannelQueryService channels,
    CompositeAlertNotifier notifier,
    IConnectionMultiplexer redis,
    IOptions<CredentialExpiryOptions> options,
    IOptions<AlertLinkOptions> linkOptions,
    TimeProvider timeProvider,
    ILogger<CredentialExpiryWorker> logger) : BackgroundService
{
    private static readonly TimeSpan DedupTtl = TimeSpan.FromDays(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.CheckInterval, timeProvider);
        do
        {
            try
            {
                await CheckAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Credential expiry check failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task CheckAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (settings.ChannelIds.Count == 0)
        {
            return;
        }

        var targets = (await channels.GetByIdsAsync(settings.ChannelIds, cancellationToken)).Where(c => StatusIncidentNotifier.IsSupported(c.Type)).ToList();
        if (targets.Count == 0)
        {
            logger.LogWarning("CredentialExpiry:ChannelIds names no usable notification channel; no expiry warnings will be sent");
            return;
        }

        var now = timeProvider.GetUtcNow();
        var publicUrl = linkOptions.Value.PublicUrl;
        var db = redis.GetDatabase();

        foreach (var key in await ingestKeys.ListAsync(cancellationToken))
        {
            if (key.RevokedAt is null && key.ExpiresAt is { } expiresAt
                && await TryClaimAsync(db, "ingest-key", key.Id, expiresAt, now, settings.WarnDays) is not null)
            {
                await SendAsync(CredentialExpiryPolicy.BuildRule("Ingest key", key.Name, null, key.Id, expiresAt, now, publicUrl, "/settings/ingest-keys"), targets, now, cancellationToken);
            }
        }

        foreach (var token in await tokens.ListWithExpiryAsync(cancellationToken))
        {
            if (token.ExpiresAt is not { } expiresAt)
            {
                continue;
            }

            if (await TryClaimAsync(db, "pat", token.Id, expiresAt, now, settings.WarnDays) is null)
            {
                continue;
            }

            var owner = (await users.FindByIdAsync(token.UserId, cancellationToken))?.Username;
            await SendAsync(CredentialExpiryPolicy.BuildRule("Personal access token", token.Name, owner, token.Id, expiresAt, now, publicUrl, "/settings/access-tokens"), targets, now, cancellationToken);
        }
    }

    /// <summary>Returns the threshold when a warning is due and this process won the right to send it; otherwise null.</summary>
    private static async Task<int?> TryClaimAsync(IDatabase db, string kind, Guid id, DateTimeOffset expiresAt, DateTimeOffset now, List<int> warnDays)
    {
        if (CredentialExpiryPolicy.DueThreshold(expiresAt, now, warnDays) is not { } threshold)
        {
            return null;
        }

        var claimed = await db.StringSetAsync(CredentialExpiryPolicy.DedupKey(kind, id, expiresAt, threshold), "1", DedupTtl, When.NotExists);
        return claimed ? threshold : null;
    }

    private async Task SendAsync(Model.AlertRule rule, IReadOnlyList<Model.NotificationChannel> targets, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var results = await notifier.SendAllAsync(rule, targets, 0, now, cancellationToken);
        for (var i = 0; i < results.Count; i++)
        {
            if (!results[i].Success)
            {
                logger.LogWarning("Credential expiry warning '{Title}' to channel '{Channel}' failed: {Error}", rule.NotificationTitleTemplate, targets[i].Name, results[i].Error);
            }
        }
    }
}
