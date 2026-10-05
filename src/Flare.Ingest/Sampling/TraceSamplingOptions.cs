namespace Flare.Ingest.Sampling;

/// <summary>
/// Head + tail sampling of spans at ingest (ADR-0122). Bound from the <c>Sampling</c>
/// configuration section. Off by default: with <see cref="Enabled"/> false the span sink is
/// the plain Redis one and nothing in this folder runs.
/// </summary>
/// <remarks>
/// A trace is held in memory for <see cref="HoldWindow"/> after its first span arrives. If
/// any span in it errors or is slower than the matching policy's slow threshold, the whole
/// trace is kept (every span weighted 1). Otherwise, when the window ends, a span survives
/// only if its trace id hashes into its policy's <c>1-in-N</c> bucket and is stored with
/// <c>SampleWeight = N</c>, so the weighted pre-aggregations stay unbiased.
/// </remarks>
public sealed class TraceSamplingOptions
{
    public const string SectionName = "Sampling";

    public bool Enabled { get; set; }

    /// <summary>Keep 1 in N healthy traces for a span no <see cref="Rules"/> entry matches. 1 keeps everything.</summary>
    public int DefaultKeepOneIn { get; set; } = 1;

    /// <summary>A span at least this long marks its whole trace "slow" and therefore kept. <see cref="TimeSpan.Zero"/> disables the slow rule (errors are still kept).</summary>
    public TimeSpan SlowThreshold { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long a trace's spans are held waiting for an error/slow span before the head
    /// decision applies. Memory scales with spans/second × this window; bounded by
    /// <see cref="MaxHeldSpans"/>.
    /// </summary>
    public TimeSpan HoldWindow { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long a trace's verdict is remembered so late-arriving spans follow it.</summary>
    public TimeSpan DecisionTtl { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Cap on spans held across all traces. Past it, new spans skip the hold and take the
    /// head decision immediately (no tail protection) rather than growing memory.
    /// </summary>
    public int MaxHeldSpans { get; set; } = 200_000;

    /// <summary>How often expired holds are decided and flushed.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(1);

    public List<TraceSamplingRule> Rules { get; set; } = [];

    /// <summary>Throws on a value that would silently misbehave (N below 1, non-positive windows).</summary>
    public void Validate()
    {
        if (DefaultKeepOneIn < 1)
        {
            throw new InvalidOperationException($"{SectionName}:{nameof(DefaultKeepOneIn)} must be at least 1.");
        }
        if (HoldWindow <= TimeSpan.Zero || DecisionTtl <= TimeSpan.Zero || SweepInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{SectionName}: HoldWindow, DecisionTtl and SweepInterval must be positive.");
        }
        if (MaxHeldSpans < 1)
        {
            throw new InvalidOperationException($"{SectionName}:{nameof(MaxHeldSpans)} must be at least 1.");
        }
        foreach (var rule in Rules)
        {
            if (rule.KeepOneIn < 1)
            {
                throw new InvalidOperationException($"{SectionName}:{nameof(Rules)} entries need KeepOneIn >= 1.");
            }
            if (rule.Service is null && rule.IngestKeyId is null)
            {
                throw new InvalidOperationException($"{SectionName}:{nameof(Rules)} entries need a Service, an IngestKeyId, or both.");
            }
        }
    }
}

/// <summary>
/// Overrides the default policy for spans from one service, one ingest key, or both. When
/// several rules match, the one naming both beats service-only, which beats key-only; ties
/// go to the first listed.
/// </summary>
public sealed class TraceSamplingRule
{
    /// <summary>Exact <c>service.name</c>; null matches any service.</summary>
    public string? Service { get; set; }

    /// <summary>Id of the ingest key the export was authenticated with; null matches any key.</summary>
    public Guid? IngestKeyId { get; set; }

    public int KeepOneIn { get; set; } = 1;

    /// <summary>Overrides <see cref="TraceSamplingOptions.SlowThreshold"/> for matching spans.</summary>
    public TimeSpan? SlowThreshold { get; set; }
}
