using System.Collections.Concurrent;

namespace Flare.Api.Auditing;

/// <summary>
/// Collapses repeated reads (ADR-0204): the same actor reading the same thing again within the
/// window is not recorded again, so a dashboard that refreshes every 10 seconds produces one
/// event a minute instead of six. In memory, so a restart simply records the next read.
/// </summary>
public sealed class AuditReadThrottle(TimeProvider timeProvider)
{
    private const int PruneThreshold = 2048;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastRecorded = new();

    public bool ShouldRecord(string key, TimeSpan window)
    {
        if (window <= TimeSpan.Zero)
        {
            return true;
        }

        var now = timeProvider.GetUtcNow();
        if (_lastRecorded.TryGetValue(key, out var last) && now - last < window)
        {
            return false;
        }

        _lastRecorded[key] = now;
        if (_lastRecorded.Count > PruneThreshold)
        {
            foreach (var (k, v) in _lastRecorded)
            {
                if (now - v >= window)
                {
                    _lastRecorded.TryRemove(k, out _);
                }
            }
        }

        return true;
    }
}
