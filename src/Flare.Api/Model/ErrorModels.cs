using MemoryPack;

namespace Flare.Api.Model;

/// <summary>
/// Window/service scope shared by both <c>/api/errors/*</c> endpoints - same
/// "one filter type reused across a family of endpoints that belong together" precedent
/// <see cref="LogFilter"/> sets (not a copy of <see cref="SpanFilter"/>, which diverges too
/// much - span kind/status/duration/attribute filters have no meaning here). Deliberately
/// narrower than <see cref="SpanFilter"/>: exception grouping only ever needs a time window
/// plus an optional service scope, nothing else in that type applies to an event-level query.
/// </summary>
[MemoryPackable]
public sealed partial record ExceptionFilter
{
    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    /// <summary>Exact <c>ServiceName</c> match, ANDed with the time window. Empty/null = all services.</summary>
    public IReadOnlyList<string>? Services { get; init; }
}

/// <summary>Request body for <c>POST /api/errors/groups</c>.</summary>
[MemoryPackable]
public sealed partial record ExceptionGroupsRequest
{
    /// <summary>
    /// Defaults via <c>= new()</c>, which - same caveat <see cref="LogPatternRequest.Filter"/>
    /// documents - doesn't survive System.Text.Json deserialization when the request body
    /// omits <c>"filter"</c> entirely; <see cref="Query.ExceptionGroupQueryBuilder.Build"/>
    /// null-coalesces against a fresh <see cref="ExceptionFilter"/> for that reason, same as
    /// every other filter-bearing request in this codebase.
    /// </summary>
    public ExceptionFilter Filter { get; init; } = new();

    /// <summary>Max groups to return, ranked by <see cref="ExceptionGroup.OccurrenceCount"/> descending. Clamped server-side - see <see cref="Query.ExceptionGroupQueryBuilder"/>; null uses the default.</summary>
    public int? TopN { get; init; }
}

/// <summary>
/// One (ExceptionType, ExceptionMessage) group - exact-match grouping, no fingerprint/
/// template normalization (unlike logs' Drain-clustered <see cref="LogPatternRow"/>, which
/// this type otherwise mirrors closely: same Count/FirstSeen/LastSeen shape). Matches the
/// roadmap item's literal "type/message" ask; normalizing messages that differ only by an
/// embedded id/value is a known follow-up, not attempted here.
/// </summary>
[MemoryPackable]
public sealed partial record ExceptionGroup
{
    /// <summary>The <c>exception.type</c> event attribute, e.g. <c>"System.NullReferenceException"</c>. Never empty - the query only groups events that set this attribute.</summary>
    public required string ExceptionType { get; init; }

    /// <summary><c>exception.message</c>. May be empty - unlike <see cref="ExceptionType"/>, OTel's semantic conventions don't require it.</summary>
    public required string ExceptionMessage { get; init; }

    public required ulong OccurrenceCount { get; init; }

    /// <summary>Earliest/latest exception event's own timestamp within the request's window - the event's <c>Events.TimeUnixNano</c>, not the containing span's <c>StartTime</c>. See <see cref="Query.ExceptionGroupQueryBuilder"/>'s remarks for why the two differ.</summary>
    public required DateTimeOffset FirstSeen { get; init; }

    public required DateTimeOffset LastSeen { get; init; }

    /// <summary>Every distinct <c>ServiceName</c> that recorded this (type, message) pair in the window, unordered.</summary>
    public required IReadOnlyList<string> AffectedServices { get; init; }
}

/// <summary>Response body for <c>POST /api/errors/groups</c>.</summary>
[MemoryPackable]
public sealed partial record ExceptionGroupsResponse
{
    /// <summary>Highest <see cref="ExceptionGroup.OccurrenceCount"/> first - see <see cref="Query.ExceptionGroupQueryBuilder"/>'s <c>ORDER BY</c>.</summary>
    public required IReadOnlyList<ExceptionGroup> Groups { get; init; }
}

/// <summary>
/// Request body for <c>POST /api/errors/occurrences</c> - the group-list row's click-through
/// drill-down, narrowing <see cref="ExceptionGroupsRequest"/>'s same filter down to one exact
/// (type, message) pair.
/// </summary>
[MemoryPackable]
public sealed partial record ExceptionOccurrencesRequest
{
    /// <summary>Same deserialization caveat as <see cref="ExceptionGroupsRequest.Filter"/>.</summary>
    public ExceptionFilter Filter { get; init; } = new();

    public required string ExceptionType { get; init; }

    public required string ExceptionMessage { get; init; }
}

/// <summary>
/// One sample occurrence of a specific (type, message) exception group - enough to jump to
/// the trace that produced it (<c>/traces/{TraceId}</c>, the dashboard's existing
/// dynamic-segment waterfall route - no new deep-link plumbing needed) and to read the full
/// stack trace inline.
/// </summary>
[MemoryPackable]
public sealed partial record ExceptionOccurrence
{
    public required string TraceId { get; init; }

    public required string SpanId { get; init; }

    public required string ServiceName { get; init; }

    /// <summary>The span's own <c>Name</c> - which operation was executing when the exception was recorded.</summary>
    public required string SpanName { get; init; }

    /// <summary>The exception event's own timestamp (<c>Events.TimeUnixNano</c>), not the span's <c>StartTime</c> - same distinction <see cref="ExceptionGroup.FirstSeen"/> documents.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary><c>exception.stacktrace</c>. May be empty - OTel's semantic conventions don't require it, and a merely-constructed-but-never-thrown exception has a null <c>StackTrace</c> in .NET (same caveat <c>ExampleApp.LogGenerator</c>'s own log-level exception sampling documents).</summary>
    public required string Stacktrace { get; init; }
}

/// <summary>
/// Response body for <c>POST /api/errors/occurrences</c>. Deliberately hand-written on the
/// MemoryPack TS side (not <c>[GenerateTypeScript]</c>) - same <c>IReadOnlyList&lt;T&gt;</c>-
/// member block as <see cref="ServiceOverviewResponse"/>/<see cref="ExceptionGroupsResponse"/>.
/// </summary>
[MemoryPackable]
public sealed partial record ExceptionOccurrencesResponse
{
    /// <summary>The group this drill-down is for, echoed back from the request - same "dialog titles itself off the response" convention as <see cref="ServiceCallBreakdownResponse.Service"/>.</summary>
    public required string ExceptionType { get; init; }

    public required string ExceptionMessage { get; init; }

    /// <summary>Most recent first, bounded to <see cref="Query.ExceptionOccurrenceQueryBuilder.MaxOccurrences"/> - a click-through detail list, not a paginated explorer (same scope <see cref="ServiceCallBreakdownResponse"/>'s call-group lists keep).</summary>
    public required IReadOnlyList<ExceptionOccurrence> Occurrences { get; init; }
}
