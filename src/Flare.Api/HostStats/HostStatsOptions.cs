namespace Flare.Api.HostStats;

/// <summary>
/// Tuning knobs for the Resources page's "Host overview" panel
/// (<c>GET /api/resources/host/snapshot</c> / <c>GET /api/resources/host/watch</c>).
/// Bound from the <c>HostStats</c> configuration section.
/// </summary>
/// <remarks>
/// Unlike <see cref="DockerResources.DockerResourcesOptions"/> there's no "off switch"
/// config here (no <c>ProxyUrl</c>-shaped opt-in) - this feature reads Linux
/// <c>/proc</c> files directly, nothing external to point at. Its actual on/off state is
/// decided at runtime by <see cref="HostStatsPoller"/> (Linux + <c>/proc/stat</c> present
/// or not), not by configuration.
/// </remarks>
public sealed class HostStatsOptions
{
    public const string SectionName = "HostStats";

    /// <summary>
    /// How often to resample <c>/proc</c>. Also the interval CPU usage is averaged over
    /// (see <see cref="ProcFileReader"/>'s remarks) - short enough to feel live, long
    /// enough that a sub-second burst doesn't swing the number around pointlessly.
    /// </summary>
    public TimeSpan PollDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How far back the Resource trends chart's in-memory history reaches (see
    /// <see cref="HostStatsPoller"/>'s remarks) - trimmed on every tick, time-based rather
    /// than a fixed sample count so it stays correct if <see cref="PollDelay"/> changes.
    /// Reset to empty on every Flare.Api restart - this is a live diagnostic window, not
    /// durable history.
    /// </summary>
    public TimeSpan HistoryWindow { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How often a disk-usage sample is added to the separate, much longer-lived growth-
    /// rate buffer (see <see cref="HostStatsPoller"/>'s remarks) - checked every
    /// <see cref="PollDelay"/> tick, but a point is only actually stored once this much
    /// time has passed since the last one. Coarser than <see cref="PollDelay"/> on purpose:
    /// disk usage doesn't need 2s resolution to answer "how fast is this filling up," and
    /// a coarser interval keeps the multi-day buffer tiny.
    /// </summary>
    public TimeSpan DiskGrowthSampleInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How far back the disk growth-rate buffer reaches - old samples are trimmed past this, same time-based trim <see cref="HistoryWindow"/> uses.</summary>
    public TimeSpan DiskGrowthWindow { get; set; } = TimeSpan.FromDays(2);

    /// <summary>
    /// Minimum span the growth buffer needs before <c>HostStatsSnapshot.DiskGrowthBytesPerDay</c>
    /// reports anything - below this, a day-scaled rate from a handful of samples would be
    /// noise dressed up as a real number (a single log-heavy burst in the first 10 minutes
    /// after a restart would extrapolate to a wildly wrong "GB/day"). Reported once this
    /// span is available, honestly labeled with however much span that actually is (see
    /// <c>HostStatsSnapshot.DiskGrowthWindowHours</c>) - not gated a second time at a full
    /// 24h.
    /// </summary>
    public TimeSpan DiskGrowthMinimumSpan { get; set; } = TimeSpan.FromHours(2);
}
