using Cronos;
using Microsoft.AspNetCore.Http;

namespace Flare.Ingest.Pipeline;

/// <summary>Tuning knobs for check-in pings. Bound from the <c>CheckIns</c> configuration section.</summary>
public sealed class CheckInIngestOptions
{
    public const string SectionName = "CheckIns";

    /// <summary>Whether a ping carrying <c>create=1</c> may create its monitor (ADR-0262). When off, such a ping is rejected.</summary>
    public bool AutoCreate { get; set; } = true;

    /// <summary>The most live monitors auto-create will leave behind. Past it a <c>create=1</c> ping for a new slug is rejected, so a leaked ingest key cannot fill the table.</summary>
    public int MaxMonitors { get; set; } = 500;
}

/// <summary>
/// The monitor a ping asks Flare to create when none exists for its slug (ADR-0262): the <c>create</c>, <c>cron</c>,
/// <c>interval</c>, <c>tz</c>, <c>grace</c>, <c>max_runtime</c> and <c>name</c> query parameters. The limits mirror
/// <c>CheckInMonitorRequest</c> in <c>Flare.Api</c> (Ingest does not reference it).
/// </summary>
public sealed record CheckInAutoCreate(string Name, string CronExpression, int IntervalSeconds, string TimeZone, int GraceSeconds, int MaxRuntimeSeconds)
{
    public const int MaxNameLength = 200;
    public const int MaxCronLength = 100;
    public const int MinIntervalSeconds = 60;
    public const int MaxIntervalSeconds = 2_678_400;
    public const int MaxGraceSeconds = 604_800;
    public const int MaxRuntimeSecondsLimit = 604_800;
    public const int DefaultGraceSeconds = 300;

    /// <summary>
    /// Reads the auto-create request from <paramref name="query"/>. Returns false with no spec and no error when the ping does
    /// not ask for one (<c>create</c> absent or not <c>1</c>/<c>true</c>); false with an error when it does but is invalid.
    /// </summary>
    public static bool TryParse(IQueryCollection query, string slug, out CheckInAutoCreate? spec, out string? error)
    {
        spec = null;
        error = null;
        if (!IsTrue(query["create"].ToString()))
        {
            return false;
        }

        var cron = query["cron"].ToString().Trim();
        var intervalText = query["interval"].ToString().Trim();
        var interval = 0;
        if ((cron.Length > 0) == (intervalText.Length > 0))
        {
            error = "create=1 needs exactly one of cron and interval.";
            return false;
        }

        if (cron.Length > 0)
        {
            if (cron.Length > MaxCronLength || !IsCron(cron))
            {
                error = "cron must be a five-field cron expression (minute hour day-of-month month day-of-week) or a macro such as @daily.";
                return false;
            }
        }
        else if (!int.TryParse(intervalText, out interval) || interval is < MinIntervalSeconds or > MaxIntervalSeconds)
        {
            error = $"interval must be a number of seconds between {MinIntervalSeconds} and {MaxIntervalSeconds}.";
            return false;
        }

        var timeZone = "UTC";
        if (cron.Length > 0 && query["tz"].ToString().Trim() is { Length: > 0 } tz)
        {
            if (!IsZone(tz))
            {
                error = "tz must be a known IANA time zone id.";
                return false;
            }

            timeZone = tz;
        }

        if (!TryNumber(query["grace"].ToString(), DefaultGraceSeconds, MaxGraceSeconds, out var grace))
        {
            error = $"grace must be between 0 and {MaxGraceSeconds} seconds.";
            return false;
        }

        if (!TryNumber(query["max_runtime"].ToString(), 0, MaxRuntimeSecondsLimit, out var maxRuntime))
        {
            error = $"max_runtime must be between 0 and {MaxRuntimeSecondsLimit} seconds.";
            return false;
        }

        var name = query["name"].ToString().Trim();
        if (name.Length > MaxNameLength)
        {
            error = $"name must be at most {MaxNameLength} characters.";
            return false;
        }

        spec = new CheckInAutoCreate(name.Length > 0 ? name : slug, cron, cron.Length > 0 ? 0 : interval, timeZone, grace, maxRuntime);
        return true;
    }

    private static bool IsTrue(string value) => value is "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);

    private static bool TryNumber(string text, int fallback, int max, out int value)
    {
        value = fallback;
        return text.Length == 0 || (int.TryParse(text, out value) && value is >= 0 && value <= max);
    }

    private static bool IsCron(string expression)
    {
        try
        {
            Cronos.CronExpression.Parse(expression, CronFormat.Standard);
            return true;
        }
        catch (CronFormatException)
        {
            return false;
        }
    }

    private static bool IsZone(string id)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }
}
