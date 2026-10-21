using System.Text;
using System.Text.RegularExpressions;

namespace Flare.Api.Model;

/// <summary>
/// A scheduled job Flare expects to hear from. The job pings <c>Flare.Ingest</c>'s <c>/v1/checkins/{slug}</c> when it starts
/// and finishes; <c>Flare.AlertWorker</c> compares those pings with the schedule and writes <see cref="CheckInMetrics.Up"/>.
/// JSON only - not MemoryPack'd. See <c>docs-internal/adr/0260-cron-job-checkins.md</c>.
/// </summary>
public sealed record CheckInMonitor
{
    public required Guid Id { get; init; }

    /// <summary>The name a job puts in the ping URL. Unique among live monitors and fixed after creation.</summary>
    public required string Slug { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = "";

    public bool Enabled { get; init; } = true;

    /// <summary>A five-field cron expression or macro read in <see cref="TimeZone"/>. Empty when the schedule is an interval.</summary>
    public string CronExpression { get; init; } = "";

    /// <summary>A fixed period between pings. 0 when the schedule is a cron expression.</summary>
    public int IntervalSeconds { get; init; }

    public string TimeZone { get; init; } = "UTC";

    /// <summary>How long after the expected time a ping may arrive before the job counts as late.</summary>
    public int GraceSeconds { get; init; } = CheckInMonitorRequest.DefaultGraceSeconds;

    /// <summary>A run that has started and not finished within this many seconds counts as failed. 0 means no limit.</summary>
    public int MaxRuntimeSeconds { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>The current state, from the pings received. Filled only by the list endpoint.</summary>
    public CheckInStatus? Status { get; init; }
}

/// <summary>Where a monitor stands. <see cref="Late"/>, <see cref="Failed"/> and <see cref="Overran"/> are the unhealthy states.</summary>
public enum CheckInState
{
    /// <summary>No ping yet, and the first one is not overdue.</summary>
    Pending,

    /// <summary>The last run finished and the next one is not overdue.</summary>
    Ok,

    /// <summary>A run has started and not finished, within its maximum runtime.</summary>
    Running,

    /// <summary>The next expected ping did not arrive within the grace period.</summary>
    Late,

    /// <summary>The last run reported failure.</summary>
    Failed,

    /// <summary>A run started and did not finish within <see cref="CheckInMonitor.MaxRuntimeSeconds"/>.</summary>
    Overran,

    /// <summary>The monitor is switched off. It writes no metric.</summary>
    Disabled,
}

/// <summary>A monitor's state with the pings behind it.</summary>
public sealed record CheckInStatus(CheckInState State, DateTimeOffset? LastPing, DateTimeOffset? NextExpected)
{
    /// <summary>Whether <see cref="CheckInMetrics.Up"/> reads 1: every state except Late, Failed and Overran.</summary>
    public bool Up => State is not (CheckInState.Late or CheckInState.Failed or CheckInState.Overran);
}

/// <summary>The newest pings stored for one slug.</summary>
/// <param name="LastStart">The newest <c>start</c> ping.</param>
/// <param name="LastFinish">The newest <c>ok</c> or <c>fail</c> ping.</param>
/// <param name="LastFinishFailed">Whether that finish ping was <c>fail</c>.</param>
public sealed record CheckInPings(DateTimeOffset? LastStart, DateTimeOffset? LastFinish, bool LastFinishFailed)
{
    public static readonly CheckInPings None = new(null, null, false);

    public DateTimeOffset? LastPing =>
        LastStart is { } start && LastFinish is { } finish ? (start > finish ? start : finish) : LastStart ?? LastFinish;
}

/// <summary>Names and attribute keys of the gauge a monitor writes. Every point carries <see cref="MonitorAttribute"/> (the slug).</summary>
public static class CheckInMetrics
{
    public const string ServiceName = "flare-checkin";

    /// <summary>1 while the job is on schedule, 0 while it is late, failed or overran.</summary>
    public const string Up = "checkin.up";

    public const string MonitorAttribute = "monitor";
}

/// <summary>Decides a monitor's <see cref="CheckInStatus"/>. Pure: the worker and the list endpoint share it, so they cannot disagree.</summary>
public static class CheckInEvaluator
{
    /// <summary>
    /// The next ping is expected at the first scheduled time after the last ping (or after the monitor was last edited, when
    /// that is later, so creating, re-enabling or re-scheduling a monitor never reads as late at once), and is late once
    /// <see cref="CheckInMonitor.GraceSeconds"/> pass without it. A last run that reported failure stays Failed until a
    /// run reports success. A started run that has not finished is Running until its maximum runtime, if any, runs out.
    /// </summary>
    public static CheckInStatus Evaluate(CheckInMonitor monitor, CheckInPings pings, DateTimeOffset now)
    {
        var lastPing = pings.LastPing;
        var reference = lastPing is { } ping && ping > monitor.UpdatedAt ? ping : monitor.UpdatedAt;
        var next = NextExpected(monitor, reference);

        if (!monitor.Enabled)
        {
            return new CheckInStatus(CheckInState.Disabled, lastPing, next);
        }

        if (pings.LastFinish is not null && pings.LastFinishFailed)
        {
            return new CheckInStatus(CheckInState.Failed, lastPing, next);
        }

        var started = pings.LastStart is { } start && (pings.LastFinish is not { } finish || start > finish);
        if (started && monitor.MaxRuntimeSeconds > 0 && now - pings.LastStart!.Value > TimeSpan.FromSeconds(monitor.MaxRuntimeSeconds))
        {
            return new CheckInStatus(CheckInState.Overran, lastPing, next);
        }

        if (next is { } due && now > due.AddSeconds(monitor.GraceSeconds))
        {
            return new CheckInStatus(CheckInState.Late, lastPing, next);
        }

        var state = started ? CheckInState.Running : pings.LastFinish is not null ? CheckInState.Ok : CheckInState.Pending;
        return new CheckInStatus(state, lastPing, next);
    }

    /// <summary>The first scheduled time strictly after <paramref name="after"/>; null when a cron expression never fires again.</summary>
    public static DateTimeOffset? NextExpected(CheckInMonitor monitor, DateTimeOffset after) =>
        monitor.CronExpression.Length > 0
            ? DashboardScheduleRequest.NextOccurrence(monitor.CronExpression, monitor.TimeZone, after)
            : after.AddSeconds(monitor.IntervalSeconds);
}

/// <summary>One ping as stored in <c>checkin_events</c>. <see cref="Status"/> is <c>start</c>, <c>ok</c> or <c>fail</c>.</summary>
public sealed record CheckInPing(DateTimeOffset Time, string Status, string Message);

/// <summary>How a run ended. <see cref="Unfinished"/> is a start that was never followed by a finish ping.</summary>
public enum CheckInRunOutcome
{
    Ok,
    Failed,

    /// <summary>Started and not finished yet, within the monitor's maximum runtime (or it has none).</summary>
    Running,

    /// <summary>Started and never finished: a later start replaced it, or it outlived the maximum runtime.</summary>
    Unfinished,
}

/// <summary>One run of a job, paired from its pings. A job that only sends a finish ping has no <see cref="StartedAt"/> or duration.</summary>
public sealed record CheckInRun(DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt, double? DurationSeconds, CheckInRunOutcome Outcome, string Message);

/// <summary>Response body for <c>GET /api/checkin-monitors/{id}/runs</c>, newest first.</summary>
public sealed record CheckInRunListResponse(IReadOnlyList<CheckInRun> Runs);

/// <summary>
/// Pairs a job's pings into runs. Pure, like <see cref="CheckInEvaluator"/>. A finish ping closes the latest open start before
/// it; a second start while one is open leaves the first <see cref="CheckInRunOutcome.Unfinished"/>. Pings carry no run id, so
/// two overlapping runs of the same job can pair wrongly - the same limit the evaluator has.
/// </summary>
public static class CheckInRunHistory
{
    /// <summary>The runs in <paramref name="pings"/> (oldest first), newest first.</summary>
    public static IReadOnlyList<CheckInRun> Build(IEnumerable<CheckInPing> pings, int maxRuntimeSeconds, DateTimeOffset now)
    {
        var runs = new List<CheckInRun>();
        CheckInPing? open = null;
        foreach (var ping in pings)
        {
            if (ping.Status == "start")
            {
                if (open is not null)
                {
                    runs.Add(new CheckInRun(open.Time, null, null, CheckInRunOutcome.Unfinished, open.Message));
                }

                open = ping;
                continue;
            }

            var failed = ping.Status == "fail";
            runs.Add(new CheckInRun(
                open?.Time,
                ping.Time,
                open is null ? null : Math.Max(0, (ping.Time - open.Time).TotalSeconds),
                failed ? CheckInRunOutcome.Failed : CheckInRunOutcome.Ok,
                ping.Message.Length > 0 ? ping.Message : open?.Message ?? ""));
            open = null;
        }

        if (open is not null)
        {
            var overran = maxRuntimeSeconds > 0 && now - open.Time > TimeSpan.FromSeconds(maxRuntimeSeconds);
            runs.Add(new CheckInRun(open.Time, null, null, overran ? CheckInRunOutcome.Unfinished : CheckInRunOutcome.Running, open.Message));
        }

        runs.Reverse();
        return runs;
    }
}

/// <summary>Response body for <c>GET /api/checkin-monitors</c>.</summary>
public sealed record CheckInMonitorListResponse(IReadOnlyList<CheckInMonitor> Monitors);

/// <summary>Create/update body for <c>/api/checkin-monitors</c>. <see cref="Slug"/> is read on create only.</summary>
public sealed partial record CheckInMonitorRequest
{
    public const int MaxNameLength = 200;
    public const int MaxSlugLength = 64;
    public const int MaxCronLength = 100;
    public const int MinIntervalSeconds = 60;
    public const int MaxIntervalSeconds = 2_678_400;
    public const int MaxGraceSeconds = 604_800;
    public const int MaxRuntimeSecondsLimit = 604_800;
    public const int DefaultGraceSeconds = 300;

    public required string Name { get; init; }

    /// <summary>Optional on create (derived from <see cref="Name"/> when blank); ignored on update.</summary>
    public string? Slug { get; init; }

    public string? Description { get; init; }

    public bool? Enabled { get; init; }

    /// <summary>A five-field cron expression or macro. Set this or <see cref="IntervalSeconds"/>, not both.</summary>
    public string? CronExpression { get; init; }

    public int? IntervalSeconds { get; init; }

    public string? TimeZone { get; init; }

    public int? GraceSeconds { get; init; }

    public int? MaxRuntimeSeconds { get; init; }

    /// <summary>The slug to create with: <see cref="Slug"/> when given, else <see cref="Name"/> reduced to lower-case letters, digits and hyphens.</summary>
    public string EffectiveSlug() => string.IsNullOrWhiteSpace(Slug) ? Slugify(Name) : Slug.Trim();

    public static bool IsValidSlug(string slug) => SlugPattern().IsMatch(slug);

    /// <summary>Lower-cases <paramref name="text"/>, turns each run of other characters into one hyphen and clips to <see cref="MaxSlugLength"/>.</summary>
    public static string Slugify(string text)
    {
        var slug = new StringBuilder();
        foreach (var c in text.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                slug.Append(c);
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        var result = slug.ToString().Trim('-');
        return result.Length > MaxSlugLength ? result[..MaxSlugLength].TrimEnd('-') : result;
    }

    /// <summary>Returns an error message, or null when this request is valid. <paramref name="creating"/> also checks the slug.</summary>
    public string? Validate(bool creating)
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > MaxNameLength)
        {
            return $"name is required and must be at most {MaxNameLength} characters.";
        }

        if (creating && !IsValidSlug(EffectiveSlug()))
        {
            return $"slug must be 1-{MaxSlugLength} lower-case letters, digits, '-' or '_', starting with a letter or digit.";
        }

        var hasCron = !string.IsNullOrWhiteSpace(CronExpression);
        var hasInterval = IntervalSeconds is > 0;
        if (hasCron == hasInterval)
        {
            return "set exactly one of cronExpression and intervalSeconds.";
        }

        if (hasCron)
        {
            if (CronExpression!.Length > MaxCronLength || !DashboardScheduleRequest.TryParseCron(CronExpression, out _))
            {
                return "cronExpression must be a five-field cron expression (minute hour day-of-month month day-of-week) or a macro such as @daily.";
            }

            if (!string.IsNullOrWhiteSpace(TimeZone) && !Alerting.OnCallSchedule.TryFindZone(TimeZone, out _))
            {
                return "timeZone must be a known IANA time zone id.";
            }
        }
        else if (IntervalSeconds is < MinIntervalSeconds or > MaxIntervalSeconds)
        {
            return $"intervalSeconds must be between {MinIntervalSeconds} and {MaxIntervalSeconds}.";
        }

        if (GraceSeconds is < 0 or > MaxGraceSeconds)
        {
            return $"graceSeconds must be between 0 and {MaxGraceSeconds}.";
        }

        if (MaxRuntimeSeconds is < 0 or > MaxRuntimeSecondsLimit)
        {
            return $"maxRuntimeSeconds must be between 0 and {MaxRuntimeSecondsLimit}.";
        }

        return null;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,63}$")]
    private static partial Regex SlugPattern();
}
