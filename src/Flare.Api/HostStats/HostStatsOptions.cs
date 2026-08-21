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
}
