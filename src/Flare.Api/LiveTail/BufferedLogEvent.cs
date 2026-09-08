using MemoryPack;

namespace Flare.Api.LiveTail;

/// <summary>
/// Deserialization shape for one Redis Stream entry's <c>data</c> field on the
/// <c>flare:logs</c> stream - a field-for-field mirror of
/// <c>Flare.Ingest.Model.LogEvent</c> (the type <c>Flare.Ingest</c>'s
/// <c>RedisStreamLogEventSink</c> actually serializes), kept as a separate type here
/// rather than a project reference - same "read side doesn't pull in the write side's
/// dependency graph just to borrow a model shape" convention <see cref="Model.LogEventDto"/>
/// already documents against that same source type. <b>Must be kept in sync by hand</b>
/// with <c>Flare.Ingest.Model.LogEvent</c> - a field added/renamed/retyped there needs the
/// matching change here, or <see cref="BufferedLogEventJsonContext"/>/
/// <see cref="MemoryPackableAttribute"/> silently stops round-tripping that field.
/// </summary>
/// <remarks>
/// Nullable/typed exactly like <c>Flare.Ingest.Model.LogEvent</c> (not
/// <see cref="Model.LogEventDto"/>'s ClickHouse-normalized non-nullable shape) - these
/// events haven't gone through <c>ClickHouseRowMapper</c>'s null-coalescing yet, so
/// <see cref="BufferedLogEventMapper.ToDto"/> is where that normalization happens on this
/// side, mirroring <c>ClickHouseRowMapper.ToRow</c>'s conventions.
/// <para/>
/// <see cref="MemoryPackableAttribute"/> makes this able to decode the tagged MemoryPack
/// payload <c>RedisStreamLogEventSink</c> now writes (ADR-0017) - <see cref="LogTailBroadcaster"/>
/// is a second, independent reader of the same <c>flare:logs</c> stream
/// <c>ClickHouseFlushWorker</c> reads, so it needs the identical
/// <see cref="BufferedEventPayload.Decode{T}"/> tagged/legacy-JSON handling, not just a
/// same-shaped record - field declaration order here must keep matching
/// <c>Flare.Ingest.Model.LogEvent</c>'s exactly, since MemoryPack's default (non-
/// <c>VersionTolerant</c>) mode is positional.
/// </remarks>
[MemoryPackable]
public sealed partial record BufferedLogEvent
{
    public required Guid EventId { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public DateTimeOffset? ObservedTimestamp { get; init; }

    /// <summary>Mirrors <c>Flare.Ingest.Model.LogEvent.IngestedAt</c> - see that field's remarks and ADR-0014.</summary>
    public required DateTimeOffset IngestedAt { get; init; }

    public required int SeverityNumber { get; init; }

    public string? SeverityText { get; init; }

    public string? Body { get; init; }

    public string? TraceId { get; init; }

    public string? SpanId { get; init; }

    public byte TraceFlags { get; init; }

    public string? ServiceName { get; init; }

    public string? ResourceSchemaUrl { get; init; }

    public required IReadOnlyDictionary<string, string> ResourceAttributes { get; init; }

    public string? ScopeSchemaUrl { get; init; }

    public string? ScopeName { get; init; }

    public string? ScopeVersion { get; init; }

    public required IReadOnlyDictionary<string, string> ScopeAttributes { get; init; }

    public required IReadOnlyDictionary<string, string> LogAttributes { get; init; }

    public string? EventName { get; init; }

    /// <summary>
    /// Always empty at this point in the pipeline in practice: <c>Flare.Ingest</c>'s
    /// <c>LogPatternAnnotator</c> only runs in <c>ClickHouseFlushWorker</c>, after the
    /// Redis Stream write this type deserializes - a live-tailed event predates pattern
    /// annotation. Kept here anyway for the field-for-field mirror contract this type's
    /// own remarks document, and because <c>LogEvent</c>'s JSON contract always includes
    /// it (defaulting to <see cref="string.Empty"/>, never absent).
    /// </summary>
    public string? PatternId { get; init; }

    /// <summary>See <see cref="PatternId"/>'s remarks.</summary>
    public string? PatternTemplate { get; init; }
}
