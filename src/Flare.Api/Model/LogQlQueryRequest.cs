namespace Flare.Api.Model;

/// <summary>
/// Request body for <c>POST /api/logs/query</c> - the Logs page's SQL-query-row feature.
/// See <c>Query.LogQl.LogQlParser</c> for the grammar <see cref="Query"/> is parsed
/// against.
/// </summary>
/// <remarks>
/// <see cref="From"/>/<see cref="To"/> are the page's *current time range* (resolved
/// client-side exactly like <c>VolumeChart</c>'s own chart does) - not part of the query
/// text, same split Seq itself has between "time range" and "query". Everything else
/// (service/severity/free-text filters) comes only from <see cref="Query"/>'s own
/// <c>where</c> clause, if any - the toolbar's filters are deliberately not applied here.
/// </remarks>
public sealed record LogQlQueryRequest
{
    public required string Query { get; init; }

    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }
}

/// <summary>Which shape <see cref="LogQlQueryResponse"/> actually carries - exactly one of <see cref="LogQlQueryResponse.Count"/>/<see cref="LogQlQueryResponse.Buckets"/>/<see cref="LogQlQueryResponse.Events"/> is populated, matching this.</summary>
public enum LogQlResultKind
{
    /// <summary><c>select count(*) from stream ...</c> with no <c>group by</c> - a single total.</summary>
    Count,

    /// <summary><c>select count(*) from stream ... group by time(...)</c> - a bucketed series, same shape as <c>/api/logs/aggregate</c>'s response.</summary>
    Series,

    /// <summary><c>select * from stream ...</c> - up to <c>Query.LogQl.LogQlQueryBuilder.RawRowLimit</c> raw matching events.</summary>
    Rows,
}

/// <summary>Response body for <c>POST /api/logs/query</c>.</summary>
public sealed record LogQlQueryResponse
{
    public required LogQlResultKind Kind { get; init; }

    /// <summary>Populated only when <see cref="Kind"/> is <see cref="LogQlResultKind.Count"/>.</summary>
    public long? Count { get; init; }

    /// <summary>Populated only when <see cref="Kind"/> is <see cref="LogQlResultKind.Series"/> - same bucket shape <c>/api/logs/aggregate</c> already returns.</summary>
    public IReadOnlyList<LogAggregateBucket>? Buckets { get; init; }

    /// <summary>Populated only when <see cref="Kind"/> is <see cref="LogQlResultKind.Rows"/>.</summary>
    public IReadOnlyList<LogEventDto>? Events { get; init; }

    /// <summary><see cref="Kind"/> is <see cref="LogQlResultKind.Rows"/> and more than <c>Query.LogQl.LogQlQueryBuilder.RawRowLimit</c> events matched - narrow the query/time range for the rest, there's no pagination here.</summary>
    public bool HasMoreRows { get; init; }
}
