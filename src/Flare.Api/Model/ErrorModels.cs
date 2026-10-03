using MemoryPack;

namespace Flare.Api.Model;

/// <summary>
/// Window/service/resource scope shared by both <c>/api/errors/*</c> endpoints - same
/// "one filter type reused across a family of endpoints that belong together" precedent
/// <see cref="LogFilter"/> sets (not a copy of <see cref="SpanFilter"/>, which diverges too
/// much - span kind/status/duration/attribute filters have no meaning here). Deliberately
/// narrower than <see cref="SpanFilter"/>: exception grouping only needs a time window, an
/// optional service scope, and optional resource-attribute equality filters (e.g. "production
/// only", "only <c>service.version</c> 2.3") - nothing else in that type applies to an
/// event-level query.
/// </summary>
/// <remarks>
/// <see cref="ResourceAttributes"/> is the last member on purpose: MemoryPack tolerates a
/// member appended at the end (an older client's 3-member payload still deserializes), and
/// persisted <see cref="ExceptionCountCondition"/> JSON without the field reads it back as null.
/// </remarks>
[MemoryPackable]
public sealed partial record ExceptionFilter
{
    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    /// <summary>Exact <c>ServiceName</c> match, ANDed with the time window. Empty/null = all services.</summary>
    public IReadOnlyList<string>? Services { get; init; }

    /// <summary>Equality filters against the span's <c>ResourceAttributes</c>, ANDed together - same <see cref="ResourceAttributeFilter"/> shape (and SQL, via <see cref="Query.ResourceAttributeFilterSqlBuilder"/>) as the Traces &gt; Services tab's filter chips. Null/empty = no narrowing.</summary>
    public IReadOnlyList<ResourceAttributeFilter>? ResourceAttributes { get; init; }
}

/// <summary>Request body for <c>POST /api/errors/groups</c>.</summary>
[MemoryPackable]
public sealed partial record ExceptionGroupsRequest
{
    /// <summary>
    /// Never null. See <see cref="LogSearchRequest.Filter"/> for why the <c>init</c> accessor
    /// replaces a null (an omitted <c>"filter"</c>) with an empty filter.
    /// </summary>
    public ExceptionFilter Filter { get; init => field = value ?? new(); } = new();

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
    /// <summary>Never null, same as <see cref="ExceptionGroupsRequest.Filter"/>.</summary>
    public ExceptionFilter Filter { get; init => field = value ?? new(); } = new();

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

    /// <summary><c>exception.stacktrace</c>. May be empty - OTel's semantic conventions don't require it, and a merely-constructed-but-never-thrown exception has a null <c>StackTrace</c> in .NET.</summary>
    public required string Stacktrace { get; init; }

    /// <summary>The emitting app's build revision, for linking stack frames to source: the span's <c>vcs.ref.head.revision</c> / <c>vcs.revision</c> resource attribute, else <c>service.version</c> (SourceLink-stamped .NET versions look like <c>1.2.3+abc1234</c>). Empty when none is set. Last member on purpose - MemoryPack tolerates trailing additions.</summary>
    public string Revision { get; init; } = "";
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

/// <summary>What <see cref="ExceptionFacetValuesRequest"/> enumerates - the /errors facet sidebar's two section kinds.</summary>
public enum ExceptionFacetField
{
    /// <summary><c>ServiceName</c> - same form <see cref="ExceptionFilter.Services"/> takes.</summary>
    Service,

    /// <summary><c>ResourceAttributes[<see cref="ExceptionFacetValuesRequest.Key"/>]</c> - same form a <see cref="ResourceAttributeFilter.Value"/> takes.</summary>
    ResourceAttribute,
}

/// <summary>
/// Request body for <c>POST /api/errors/facet-values</c> - distinct values of one field over
/// the exception events <see cref="Filter"/> matches, with how many exceptions carry each. The
/// /errors page's equivalent of <see cref="SpanAttributeValuesRequest"/>, a separate type for
/// the same reason <see cref="ExceptionFilter"/> is: counts are exception events (an
/// <c>ARRAY JOIN</c> over <c>Events</c>), not spans. See <see cref="Query.ExceptionFacetValuesQueryBuilder"/>.
/// </summary>
[MemoryPackable]
public sealed partial record ExceptionFacetValuesRequest
{
    /// <summary>See <see cref="ExceptionGroupsRequest.Filter"/> - never null, same reason. The caller strips the facet's own selection from this, so a section lists its alternatives rather than only what's already picked.</summary>
    public ExceptionFilter Filter { get; init => field = value ?? new(); } = new();

    public ExceptionFacetField Field { get; init; } = ExceptionFacetField.Service;

    /// <summary>The resource-attribute key to enumerate. Required when <see cref="Field"/> is <see cref="ExceptionFacetField.ResourceAttribute"/>, ignored otherwise.</summary>
    public string Key { get; init; } = "";

    /// <summary>Max distinct values returned, most-frequent first. Clamped server-side; null (or a JSON body that omits it) uses the default.</summary>
    public int? Limit { get; init; }
}

/// <summary>One distinct value of <see cref="ExceptionFacetValuesRequest.Field"/>, with how many in-scope exception events carry it.</summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record ExceptionFacetValue
{
    public required string Value { get; init; }

    public required long Count { get; init; }
}

/// <summary>Response body for <c>POST /api/errors/facet-values</c>, ordered by <see cref="ExceptionFacetValue.Count"/> descending.</summary>
[MemoryPackable]
public sealed partial record ExceptionFacetValuesResponse
{
    public required IReadOnlyList<ExceptionFacetValue> Values { get; init; }
}
