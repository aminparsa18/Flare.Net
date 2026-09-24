using Flare.Identity.IngestKeys;

namespace Flare.Ingest.Auth;

/// <summary>
/// Pure "is this key over its limit right now" check (ADR-0051), split out of
/// <see cref="IngestApiKeyValidationMiddleware"/> so it's unit-testable without Redis -
/// same "test the pure function" precedent as <see cref="Stats.IngestionStatsKeys"/>.
/// </summary>
/// <remarks>
/// A soft limit: a request is admitted while usage so far is strictly below every cap, and
/// its whole count is added afterwards (the record count isn't known until the body is
/// parsed). So a window can overshoot by at most the requests already in flight when it
/// crossed the cap - bounded, and far cheaper than parsing every body before deciding.
/// </remarks>
public static class IngestKeyLimitEvaluator
{
    /// <summary>Returns how long the client should wait before retrying if any enforced
    /// cap is reached, or null if the request may proceed. A reached daily cap wins over a
    /// reached per-minute one, since retrying before UTC midnight would just be rejected
    /// again.</summary>
    public static TimeSpan? Evaluate(IngestApiKeyLimits limits, IngestKeyUsage usage, DateTimeOffset now)
    {
        if (!limits.IsEnforced)
        {
            return null;
        }

        if (Reached(usage.Day.Events, limits.MaxEventsPerDay) || Reached(usage.Day.Bytes, limits.MaxBytesPerDay))
        {
            var nextDay = DateTimeOffset.FromUnixTimeSeconds((IngestApiKeyUsageKeys.EpochDay(now) + 1) * 86_400);
            return nextDay - now;
        }

        if (Reached(usage.Minute.Events, limits.MaxEventsPerMinute) || Reached(usage.Minute.Bytes, limits.MaxBytesPerMinute))
        {
            var nextMinute = DateTimeOffset.FromUnixTimeSeconds((IngestApiKeyUsageKeys.EpochMinute(now) + 1) * 60);
            return nextMinute - now;
        }

        return null;
    }

    private static bool Reached(long used, long? cap) => cap is { } max && used >= max;
}
