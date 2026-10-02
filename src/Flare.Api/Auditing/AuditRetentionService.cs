using Flare.Identity.Audit;
using Microsoft.Extensions.Options;

namespace Flare.Api.Auditing;

/// <summary>Hourly prune of audit events past <see cref="AuditOptions.RetentionDays"/>.</summary>
public sealed class AuditRetentionService(
    IAuditEventStore store,
    IOptions<AuditOptions> options,
    TimeProvider timeProvider,
    ILogger<AuditRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), timeProvider);
        do
        {
            try
            {
                var days = options.Value.RetentionDays;
                if (days > 0)
                {
                    var deleted = await store.PruneAsync(timeProvider.GetUtcNow().AddDays(-days), stoppingToken);
                    if (deleted > 0)
                    {
                        logger.LogInformation("Pruned {Count} audit events older than {Days} days", deleted, days);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Audit retention prune failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
