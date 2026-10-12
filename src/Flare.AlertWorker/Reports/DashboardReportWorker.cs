using System.Diagnostics;
using Flare.Api.Alerting;
using Flare.Api.Model;
using Flare.Api.Reports;
using Flare.Api.Query;
using Flare.Identity.Auth;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Flare.AlertWorker.Reports;

/// <summary>
/// Runs dashboard schedules (ADR-0142): every <see cref="ReportsOptions.PollInterval"/> it claims the
/// schedules whose <see cref="DashboardSchedule.NextRunAt"/> has passed, renders each dashboard in a headless
/// browser as the schedule's owner, emails the file, and records the attempt.
/// </summary>
/// <remarks>
/// Claiming is the only part under the Redis lock: a schedule's <c>NextRunAt</c> moves to its next cron
/// occurrence before it renders, so a second replica, or a restart in the middle of a render, cannot send the
/// same report twice. The price is at-most-once - a render that dies with the process is not retried, and its
/// run is missing from the history. Rendering happens outside the lock because it can take minutes.
/// </remarks>
public sealed class DashboardReportWorker(
    IDashboardScheduleQueryService schedules,
    IDashboardQueryService dashboards,
    IDashboardRenderer renderer,
    IDashboardReportMailer mailer,
    IDashboardRenderTokenSigner tokens,
    IConnectionMultiplexer redis,
    IOptions<ReportsOptions> options,
    IOptions<AlertLinkOptions> linkOptions,
    TimeProvider timeProvider,
    ILogger<DashboardReportWorker> logger) : BackgroundService
{
    private static readonly RedisKey LockKey = "flare:reports:claim-lock";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (!opts.Enabled)
        {
            logger.LogInformation("Scheduled dashboard reports are off (Reports:Enabled is false).");
            return;
        }

        if (string.IsNullOrWhiteSpace(opts.ApiUrl))
        {
            logger.LogWarning("Reports:Enabled is true but Reports:ApiUrl is blank; scheduled dashboard reports stay off.");
            return;
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var claimed = await ClaimDueAsync(opts, stoppingToken);
                foreach (var schedule in claimed)
                {
                    await RunAsync(schedule, opts, stoppingToken);
                }

                await Task.Delay(opts.PollInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async Task<IReadOnlyList<DashboardSchedule>> ClaimDueAsync(ReportsOptions opts, CancellationToken cancellationToken)
    {
        var db = redis.GetDatabase();
        RedisValue lockToken = Guid.NewGuid().ToString("N");
        if (!await db.LockTakeAsync(LockKey, lockToken, TimeSpan.FromSeconds(30)))
        {
            return [];
        }

        try
        {
            var now = timeProvider.GetUtcNow();
            var due = await schedules.ListDueAsync(now, cancellationToken);
            var claimed = new List<DashboardSchedule>(due.Count);
            foreach (var schedule in due)
            {
                var next = DashboardScheduleRequest.NextOccurrence(schedule.Cron, schedule.TimeZone, now) ?? DateTimeOffset.MaxValue;
                await schedules.SetNextRunAsync(schedule, next, cancellationToken);
                claimed.Add(schedule);
            }

            return claimed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to claim due dashboard schedules; skipping this tick.");
            return [];
        }
        finally
        {
            await db.LockReleaseAsync(LockKey, lockToken);
        }
    }

    private async Task RunAsync(DashboardSchedule schedule, ReportsOptions opts, CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetUtcNow();
        var stopwatch = Stopwatch.StartNew();
        long size = 0;
        string? error = null;
        var recipients = DashboardScheduleRequest.SplitRecipients(schedule.Recipients).Count;

        try
        {
            size = await RenderAndSendAsync(schedule, opts, startedAt, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            logger.LogWarning(ex, "Dashboard report {ScheduleId} ({Name}) failed.", schedule.Id, schedule.Name);
        }

        try
        {
            await schedules.AddRunAsync(new DashboardReportRun
            {
                Id = Guid.NewGuid(),
                ScheduleId = schedule.Id,
                DashboardId = schedule.DashboardId,
                StartedAt = startedAt,
                DurationMs = (int)Math.Min(stopwatch.ElapsedMilliseconds, int.MaxValue),
                Status = error is null ? DashboardReportRun.Succeeded : DashboardReportRun.Failed,
                Error = error is null ? "" : Truncate(error, 1000),
                RecipientCount = error is null ? recipients : 0,
                SizeBytes = size,
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to record the run of dashboard schedule {ScheduleId}.", schedule.Id);
        }
    }

    private async Task<long> RenderAndSendAsync(DashboardSchedule schedule, ReportsOptions opts, DateTimeOffset startedAt, CancellationToken cancellationToken)
    {
        var baseUrl = !string.IsNullOrWhiteSpace(opts.DashboardUrl) ? opts.DashboardUrl : linkOptions.Value.PublicUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("The dashboard URL is not configured (Reports:DashboardUrl or Alerting:PublicUrl).");
        }

        var dashboard = await dashboards.GetAsync(schedule.DashboardId, cancellationToken)
            ?? throw new InvalidOperationException("The dashboard no longer exists.");

        var token = schedule.OwnerUserId is { } owner
            ? tokens.Create(owner, dashboard.Id, opts.TokenLifetime)
            : null;

        var report = await renderer.RenderAsync(DashboardReportUrl.Build(baseUrl, schedule), token, schedule.Format, cancellationToken);
        if (report.Content.Length > opts.MaxAttachmentBytes)
        {
            throw new InvalidOperationException($"The rendered report is {report.Content.Length / 1024 / 1024} MB, over the {opts.MaxAttachmentBytes / 1024 / 1024} MB limit; use a shorter time range or fewer panels.");
        }

        await mailer.SendAsync(schedule, dashboard.Name, DashboardReportUrl.BuildViewLink(baseUrl, schedule), report, startedAt, cancellationToken);
        return report.Content.Length;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
