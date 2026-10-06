using Flare.Api.Model;

namespace Flare.AlertWorker.Synthetic;

/// <summary>Tuning knobs for <see cref="SyntheticProbeWorker"/>. Bound from the <c>Synthetic</c> configuration section.</summary>
public sealed class SyntheticOptions
{
    public const string SectionName = "Synthetic";

    /// <summary>Set false to stop this worker running probes (monitors can still be defined).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often the monitor list is checked for probes that are due. Also the floor on timing accuracy.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The name of the place this worker probes from, e.g. <c>eu-west</c>. A monitor with a non-empty
    /// <c>Locations</c> list runs only on workers whose location is in it; one with none runs on every worker.
    /// Stamped on each result as the <c>location</c> attribute.
    /// </summary>
    public string Location { get; set; } = SyntheticMetrics.DefaultLocation;

    /// <summary>Probes running at once in this process.</summary>
    public int MaxConcurrency { get; set; } = 20;
}
