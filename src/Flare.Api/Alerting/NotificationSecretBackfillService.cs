using Flare.Api.Query;

namespace Flare.Api.Alerting;

/// <summary>
/// Encrypts notification secrets that were stored before ADR-0205 by re-saving the rows that still
/// hold a plaintext value. Runs once at startup; a later run (or another replica running at the same
/// time) finds nothing left to do, so it is safe to repeat. Failure is logged, not fatal: the rows
/// keep working as plaintext and the next start tries again.
/// </summary>
public sealed class NotificationSecretBackfillService(
    INotificationChannelQueryService channels,
    IAlertQueryService alerts,
    ILogger<NotificationSecretBackfillService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Let ClickHouse finish starting; the API is useful before this completes.
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            var channelCount = await channels.ProtectLegacySecretsAsync(stoppingToken);
            var ruleCount = await alerts.ProtectLegacySecretsAsync(stoppingToken);
            if (channelCount + ruleCount > 0)
            {
                logger.LogInformation("Encrypted stored notification secrets on {Channels} channel(s) and {Rules} alert rule(s)", channelCount, ruleCount);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not encrypt legacy notification secrets; will retry on next start");
        }
    }
}
