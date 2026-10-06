namespace Flare.AlertWorker.Synthetic;

/// <summary>Tuning knobs for <see cref="SyntheticProbeWorker"/>. Bound from the <c>Synthetic</c> configuration section.</summary>
public sealed class SyntheticOptions
{
    public const string SectionName = "Synthetic";

    /// <summary>Set false to stop this worker running probes (monitors can still be defined).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often the monitor list is checked for probes that are due. Also the floor on timing accuracy.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Probes running at once in this process.</summary>
    public int MaxConcurrency { get; set; } = 20;
}
