using Cronos;

namespace Flare.Api.Model;

/// <summary>
/// A schedule that emails a rendered dashboard (PDF or PNG): at each <see cref="Cron"/> tick, in
/// <see cref="TimeZone"/>, <c>Flare.AlertWorker</c> opens the dashboard with <see cref="TimeRange"/> and
/// <see cref="VariableQuery"/> applied, as <see cref="OwnerUserId"/>, and mails the result to
/// <see cref="Recipients"/>. See <c>docs-internal/adr/0142-scheduled-dashboard-reports.md</c>.
/// </summary>
public sealed record DashboardSchedule
{
    public required Guid Id { get; init; }

    public required Guid DashboardId { get; init; }

    public required string Name { get; init; }

    public bool Enabled { get; init; } = true;

    /// <summary>Five-field cron expression (minute precision), read in <see cref="TimeZone"/>.</summary>
    public required string Cron { get; init; }

    public required string TimeZone { get; init; }

    /// <summary>Comma- or semicolon-separated addresses.</summary>
    public required string Recipients { get; init; }

    /// <summary>The dashboard's <c>?range=</c> preset, e.g. <c>7d</c>; empty uses the dashboard's own default.</summary>
    public string TimeRange { get; init; } = "";

    /// <summary>The dashboard's <c>var-&lt;id&gt;=...</c> query string without the leading <c>?</c>; empty keeps each variable's default.</summary>
    public string VariableQuery { get; init; } = "";

    /// <summary><c>pdf</c> or <c>png</c>.</summary>
    public string Format { get; init; } = DashboardScheduleRequest.PdfFormat;

    /// <summary>The user whose access the render runs with: the creator. Null when auth is disabled.</summary>
    public Guid? OwnerUserId { get; init; }

    /// <summary>The next time the worker should run this schedule. "Send now" sets it to the current time.</summary>
    public required DateTimeOffset NextRunAt { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>One attempt to render and send a <see cref="DashboardSchedule"/>.</summary>
public sealed record DashboardReportRun
{
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";

    public required Guid Id { get; init; }

    public required Guid ScheduleId { get; init; }

    public required Guid DashboardId { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required int DurationMs { get; init; }

    public required string Status { get; init; }

    /// <summary>The failure reason; empty on success.</summary>
    public string Error { get; init; } = "";

    public int RecipientCount { get; init; }

    public long SizeBytes { get; init; }
}

public sealed record DashboardScheduleListResponse(IReadOnlyList<DashboardSchedule> Schedules);

public sealed record DashboardReportRunListResponse(IReadOnlyList<DashboardReportRun> Runs);

/// <summary>Create/update body for <c>/api/dashboards/{id}/schedules</c>.</summary>
public sealed record DashboardScheduleRequest
{
    public const string PdfFormat = "pdf";
    public const string PngFormat = "png";
    public const int MaxNameLength = 200;
    public const int MaxRecipients = 50;
    public const int MaxVariableQueryLength = 4000;

    /// <summary>The <c>?range=</c> values the dashboard viewer accepts (fixed-duration presets).</summary>
    private static readonly HashSet<string> RangePresets = ["5m", "15m", "1h", "6h", "24h", "7d", "30d", "90d", "365d"];

    public required string Name { get; init; }

    public bool? Enabled { get; init; }

    public required string Cron { get; init; }

    public required string TimeZone { get; init; }

    public required string Recipients { get; init; }

    public string? TimeRange { get; init; }

    public string? VariableQuery { get; init; }

    public string? Format { get; init; }

    /// <summary>Returns an error message, or null when this request is valid.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return "name is required.";
        }

        if (Name.Length > MaxNameLength)
        {
            return $"name must be at most {MaxNameLength} characters.";
        }

        if (!TryParseCron(Cron, out _))
        {
            return "cron must be a five-field cron expression (minute hour day-of-month month day-of-week).";
        }

        if (string.IsNullOrWhiteSpace(TimeZone) || !Alerting.OnCallSchedule.TryFindZone(TimeZone, out _))
        {
            return "timeZone must be a known IANA time zone id.";
        }

        var recipients = SplitRecipients(Recipients);
        if (recipients.Count == 0)
        {
            return "recipients must list at least one email address.";
        }

        if (recipients.Count > MaxRecipients)
        {
            return $"recipients must list at most {MaxRecipients} addresses.";
        }

        if (recipients.Any(r => !MimeKit.MailboxAddress.TryParse(r, out var mailbox) || !mailbox.Address.Contains('@')))
        {
            return "recipients contains an address that is not a valid email address.";
        }

        if (!string.IsNullOrEmpty(TimeRange) && !RangePresets.Contains(TimeRange))
        {
            return $"timeRange must be one of {string.Join(", ", RangePresets)}, or empty.";
        }

        if (VariableQuery is { Length: > MaxVariableQueryLength })
        {
            return $"variableQuery must be at most {MaxVariableQueryLength} characters.";
        }

        if (Format is not null && Format is not (PdfFormat or PngFormat))
        {
            return "format must be 'pdf' or 'png'.";
        }

        return null;
    }

    /// <summary>Splits on commas/semicolons, trimming and dropping empties.</summary>
    public static IReadOnlyList<string> SplitRecipients(string recipients) =>
        recipients.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static bool TryParseCron(string? expression, out CronExpression cron)
    {
        cron = null!;
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        try
        {
            cron = CronExpression.Parse(expression.Trim(), CronFormat.Standard);
            return true;
        }
        catch (CronFormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// The first occurrence of <paramref name="expression"/> strictly after <paramref name="after"/>, read in
    /// <paramref name="timeZoneId"/>; null when the expression never fires again or the inputs are invalid.
    /// </summary>
    public static DateTimeOffset? NextOccurrence(string expression, string timeZoneId, DateTimeOffset after)
    {
        if (!TryParseCron(expression, out var cron) || !Alerting.OnCallSchedule.TryFindZone(timeZoneId, out var zone))
        {
            return null;
        }

        var next = cron.GetNextOccurrence(after.UtcDateTime, zone);
        return next is null ? null : new DateTimeOffset(DateTime.SpecifyKind(next.Value, DateTimeKind.Utc));
    }
}
