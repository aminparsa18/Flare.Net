namespace Flare.Maui;

/// <summary>
/// A crash the operating system recorded about an earlier launch, found on the next one. <see cref="Kind"/> is one
/// of <c>Crash</c>, <c>NativeCrash</c>, <c>Anr</c>, <c>LowMemory</c>, <c>InitializationFailure</c> or
/// <c>ExcessiveResourceUsage</c>.
/// </summary>
/// <param name="Kind">What ended the launch.</param>
/// <param name="Reason">One line, used as the exception message.</param>
/// <param name="Description">Extra detail, used as the stack when there is none.</param>
/// <param name="StackTrace">A stack or trace the platform provided, if any.</param>
/// <param name="Timestamp">When it happened (Android), or the end of the report window (iOS).</param>
/// <param name="WindowStart">Set when <paramref name="Timestamp"/> is not exact: the report covers a window, and the run is guessed from it.</param>
internal sealed record NativeCrash(
    string Kind,
    string Reason,
    DateTimeOffset Timestamp,
    string? Description = null,
    string? StackTrace = null,
    DateTimeOffset? WindowStart = null)
{
    /// <summary>A plain crash: when the managed handler already reported the run's fatal exception, this is the same event.</summary>
    internal bool IsCrash => Kind is "Crash" or "NativeCrash";
}

/// <summary>Attributes native crash reports to the launch they ended.</summary>
internal static class NativeCrashMatcher
{
    internal readonly record struct Result(IReadOnlyList<(NativeCrash Crash, RunRecord Run)> ToReport, DateTimeOffset? Watermark);

    /// <summary>
    /// Exact-time reports (Android) go to the latest launch that started before them and are skipped when at or
    /// before <paramref name="watermark"/>. Windowed reports (iOS) go to the most recent launch that was last seen in
    /// the foreground inside the window and has no report yet. A plain crash on a launch whose managed fatal exception
    /// was already reported is the same event, so it is claimed but not reported again. Marks the claimed launches.
    /// </summary>
    public static Result Match(IReadOnlyList<NativeCrash> crashes, IReadOnlyList<RunRecord> prior, DateTimeOffset? watermark)
    {
        var report = new List<(NativeCrash, RunRecord)>();
        var newest = watermark;
        foreach (var crash in crashes.OrderBy(c => c.Timestamp))
        {
            RunRecord? run;
            if (crash.WindowStart is { } start)
            {
                run = prior
                    .Where(r => !r.NativeReported && r.Foreground && r.LastSeenAt >= start && r.LastSeenAt <= crash.Timestamp)
                    .OrderByDescending(r => r.LastSeenAt)
                    .FirstOrDefault();
            }
            else
            {
                if (watermark is { } w && crash.Timestamp <= w) continue;
                if (newest is not { } n || crash.Timestamp > n) newest = crash.Timestamp;
                run = prior.LastOrDefault(r => r.StartedAt <= crash.Timestamp);
                if (run is { NativeReported: true } && crash.IsCrash) continue;
            }
            if (run is null) continue;
            if (crash.IsCrash) run.NativeReported = true;
            if (crash.IsCrash && run.FatalReported) continue;
            report.Add((crash, run));
        }
        return new Result(report, newest);
    }
}
