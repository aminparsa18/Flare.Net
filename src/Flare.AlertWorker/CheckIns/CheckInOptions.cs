namespace Flare.AlertWorker.CheckIns;

/// <summary>Tuning knobs for <see cref="CheckInWorker"/>. Bound from the <c>CheckIn</c> configuration section.</summary>
public sealed class CheckInOptions
{
    public const string SectionName = "CheckIn";

    /// <summary>Set false to stop this worker judging check-in monitors (monitors can still be defined and pinged).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often every monitor is judged. A job is reported late at most this long after its grace period ends.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(15);
}
