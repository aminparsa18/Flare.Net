using MemoryPack;

namespace Flare.Ingest.Model;

/// <summary>
/// Internal representation of one OTLP profiles <c>Sample</c> after mapping from OTLP, with the
/// export-wide <c>ProfilesDictionary</c> already resolved away.
/// </summary>
/// <remarks>
/// Drives the ClickHouse <c>profile_samples</c> table (<c>db/clickhouse/0065_profile_samples.sql</c>),
/// same keep-in-sync convention as <see cref="SpanRecord"/>. See ADR-0141 for why one row per
/// sample with the stack inlined as frame names, rather than storing the dictionary.
/// <see cref="MemoryPackableAttribute"/> is the Redis Stream wire format (ADR-0017).
/// </remarks>
[MemoryPackable]
public sealed partial record ProfileSampleRecord
{
    /// <summary>Profile start (or the earliest per-sample timestamp).</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Lower-hex <c>Profile.profile_id</c>, or null when the sender didn't set one.</summary>
    public string? ProfileId { get; init; }

    /// <summary><c>Profile.duration_nano</c>.</summary>
    public required ulong DurationNano { get; init; }

    public string? ServiceName { get; init; }

    /// <summary>Profile sample type, e.g. <c>cpu</c>, <c>alloc_space</c>.</summary>
    public string? SampleType { get; init; }

    /// <summary>Unit of <see cref="Value"/>, e.g. <c>nanoseconds</c>, <c>bytes</c>.</summary>
    public string? SampleUnit { get; init; }

    /// <summary>Frame names, root first and leaf last (folded-stack order). Inlined functions are expanded into their own frames.</summary>
    public required IReadOnlyList<string> Stack { get; init; }

    /// <summary>Sum of <c>Sample.values</c>, or the timestamp count for a timestamps-only sample.</summary>
    public required long Value { get; init; }

    /// <summary>Lower-hex trace id of the span active when the sample was taken, from <c>Sample.link_index</c>.</summary>
    public string? TraceId { get; init; }

    /// <summary>Lower-hex span id of the span active when the sample was taken.</summary>
    public string? SpanId { get; init; }

    public required IReadOnlyDictionary<string, string> ResourceAttributes { get; init; }

    public required IReadOnlyDictionary<string, string> SampleAttributes { get; init; }

    /// <summary>Ingest's own receipt time for the export request - see <see cref="SpanRecord.IngestedAt"/>.</summary>
    public required DateTimeOffset IngestedAt { get; init; }
}
