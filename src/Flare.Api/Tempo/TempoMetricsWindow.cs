using System.Globalization;

namespace Flare.Api.Tempo;

/// <summary>The time range and step of a metrics query after defaults and alignment.</summary>
/// <param name="FromSeconds">Unix seconds, a multiple of <paramref name="StepSeconds"/> (not aligned for an instant query).</param>
/// <param name="ToSeconds">Exclusive; a multiple of <paramref name="StepSeconds"/> for a range query.</param>
public sealed record TempoMetricsWindow(long FromSeconds, long ToSeconds, int StepSeconds)
{
    /// <summary>The number of steps between <see cref="FromSeconds"/> and <see cref="ToSeconds"/>.</summary>
    public int Steps => (int)((ToSeconds - FromSeconds) / StepSeconds);

    /// <summary>The steps Tempo's default aims for when the client names no <c>step</c>.</summary>
    public const int DefaultSteps = 200;

    public static readonly TimeSpan DefaultSince = TimeSpan.FromHours(1);

    /// <summary>
    /// Resolves Tempo's time parameters. <c>start</c> and <c>end</c> are unix seconds, unix nanoseconds or RFC3339;
    /// <c>since</c> (a duration) stands in for <c>start</c>, one hour by default. A range query rounds <c>start</c>
    /// down and <c>end</c> up to a multiple of the step, which is a duration (<c>30s</c>) or whole seconds, one second
    /// at least, and by default the range in <see cref="DefaultSteps"/> steps. An instant query is one step as long as
    /// the range. More than <paramref name="maxSteps"/> steps is an <see cref="ArgumentException"/>.
    /// </summary>
    public static TempoMetricsWindow Resolve(string? start, string? end, string? since, string? step, DateTimeOffset now, bool instant, int maxSteps)
    {
        var toSeconds = ParseTime(end, "end") ?? now.ToUnixTimeSeconds();
        var fromSeconds = ParseTime(start, "start") ?? toSeconds - (long)ParseSince(since).TotalSeconds;
        if (toSeconds <= fromSeconds)
        {
            throw new ArgumentException("'end' must be after 'start'.");
        }

        if (instant)
        {
            return new TempoMetricsWindow(fromSeconds, toSeconds, (int)Math.Min(toSeconds - fromSeconds, int.MaxValue));
        }

        var stepSeconds = ParseStep(step) ?? (int)Math.Max(1, (toSeconds - fromSeconds + DefaultSteps - 1) / DefaultSteps);
        var alignedFrom = fromSeconds - Mod(fromSeconds, stepSeconds);
        var alignedTo = toSeconds + Mod(-toSeconds, stepSeconds);
        var window = new TempoMetricsWindow(alignedFrom, alignedTo, stepSeconds);
        if (window.Steps > maxSteps)
        {
            throw new ArgumentException($"a step of {stepSeconds}s over this range is {window.Steps} steps; at most {maxSteps} are allowed, so use a larger step or a shorter range.");
        }

        return window;
    }

    private static long Mod(long value, long modulus) => ((value % modulus) + modulus) % modulus;

    private static TimeSpan ParseSince(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return DefaultSince;
        }

        return TraceQlParser.TryParseDuration(text, out var nanos) && nanos >= 1_000_000_000
            ? TimeSpan.FromSeconds(nanos / 1_000_000_000)
            : throw new ArgumentException($"invalid 'since' '{text}': expected a duration of at least a second, such as 3h.");
    }

    private static int? ParseStep(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        double seconds;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var plain))
        {
            seconds = plain;
        }
        else if (TraceQlParser.TryParseDuration(text, out var nanos))
        {
            seconds = nanos / 1e9;
        }
        else
        {
            throw new ArgumentException($"invalid step '{text}': expected a duration such as 30s, or whole seconds.");
        }

        return seconds is >= 1 and < int.MaxValue
            ? (int)Math.Ceiling(seconds)
            : throw new ArgumentException($"invalid step '{text}': the step must be at least one second.");
    }

    private static long? ParseTime(string? text, string name)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            // Seconds until the year 5138; nanoseconds are twenty times larger even for 1970.
            return number >= 100_000_000_000 ? number / 1_000_000_000 : number;
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUnixTimeSeconds()
            : throw new ArgumentException($"invalid '{name}' '{text}': expected unix seconds, unix nanoseconds or RFC3339.");
    }
}
