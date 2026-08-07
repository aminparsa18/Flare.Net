namespace Flare.Api.Model;

/// <summary>Which OTel attribute bag an <see cref="AttributeFilter"/> targets.</summary>
public enum AttributeBag
{
    Log,
    Resource,
    Scope,
}

/// <summary>
/// One equality filter against a <c>Map(LowCardinality(String), String)</c> column -
/// <c>LogAttributes</c>/<c>ResourceAttributes</c>/<c>ScopeAttributes</c> in
/// <c>db/clickhouse/0001_logs.sql</c>, selected via <see cref="Bag"/>.
/// </summary>
public sealed record AttributeFilter
{
    public AttributeBag Bag { get; init; } = AttributeBag.Log;

    public required string Key { get; init; }

    public required string Value { get; init; }
}

/// <summary>
/// Structured filter shared by both <c>/api/logs/search</c> and
/// <c>/api/logs/aggregate</c> - see <see cref="Query.LogFilterSqlBuilder"/> for how this
/// compiles to a parameterized ClickHouse <c>WHERE</c> clause.
/// </summary>
/// <remarks>
/// <see cref="From"/>/<see cref="To"/> are both optional on the wire; the query
/// builders apply a default/max bounded range so an unfiltered request can't trigger an
/// unbounded scan (see <c>Flare.Api</c>'s README, "Time range defaults").
/// </remarks>
public sealed record LogFilter
{
    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    /// <summary>Exact <c>ServiceName</c> match, ANDed with every other filter. Empty/null = all services.</summary>
    public IReadOnlyList<string>? Services { get; init; }

    /// <summary>Exact OTel <c>SeverityNumber</c> match (0-24). Empty/null = all levels.</summary>
    public IReadOnlyList<byte>? SeverityNumbers { get; init; }

    /// <summary>Exact lower-hex <c>TraceId</c> match.</summary>
    public string? TraceId { get; init; }

    /// <summary>
    /// Free-text, case-insensitive substring match against <c>Body</c>
    /// (<c>ILIKE '%term%'</c>). See <c>Flare.Api</c>'s README for why this is
    /// substring/case-insensitive rather than token-aligned, and what that costs against
    /// the <c>Body</c> column's <c>tokenbf_v1</c> skip index.
    /// </summary>
    public string? Search { get; init; }

    /// <summary>Equality filters over the three attribute bags, ANDed together.</summary>
    public IReadOnlyList<AttributeFilter>? Attributes { get; init; }
}
