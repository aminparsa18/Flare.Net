namespace Flare.Api.Model;

/// <summary>
/// One sample in the Resources page's "Resource trends" chart - what
/// <c>GET /api/resources/host/history</c> returns an array of. Percent, not raw bytes, for
/// Memory/Disk - precomputed server-side once per sample (see
/// <c>HostStats.HostStatsPoller</c>) so up to ~1800 points (1h at the default 2s
/// <c>HostStats:PollDelay</c>) stay compact - one used/total division each instead of
/// carrying four extra <see langword="long"/> fields per point. Network has no fixed
/// ceiling to normalize against, so it stays in its natural unit (bytes/sec) instead.
/// </summary>
public sealed record HostStatsHistoryPoint
{
    public required DateTimeOffset Timestamp { get; init; }

    public required double CpuUsagePercent { get; init; }

    public required double MemoryUsedPercent { get; init; }

    public required double DiskUsedPercent { get; init; }

    public required double NetworkBytesPerSecond { get; init; }
}
