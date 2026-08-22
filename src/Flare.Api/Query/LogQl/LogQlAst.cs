namespace Flare.Api.Query.LogQl;

/// <summary>
/// Parsed shape of a SQL-query-row query - see <see cref="LogQlParser"/> for the grammar.
/// Deliberately small/closed (no arbitrary column list, no joins, no subqueries): every
/// node here maps 1:1 onto something <see cref="LogQlWhereTranslator"/>/<see cref="LogQlQueryBuilder"/>
/// can compile to a parameterized ClickHouse fragment, so there's no path from user text
/// to literal SQL.
/// </summary>
public sealed record LogQlQuery(LogQlSelectKind Select, LogQlExpr? Where, LogQlGroupBy? GroupBy);

/// <summary><c>count(*)</c> (an aggregate) vs. <c>*</c> (raw matching events).</summary>
public enum LogQlSelectKind
{
    Count,
    Raw,
}

/// <summary>
/// <c>group by time(&lt;duration&gt;)[, service|level]</c> - always time-bucketed in v1
/// (see this feature's plan doc for why an un-time-bucketed <c>group by</c> isn't
/// supported), with an optional secondary dimension reusing the same
/// <see cref="Model.LogAggregateGroupBy"/> enum the existing <c>/api/logs/aggregate</c>
/// endpoint already exposes.
/// </summary>
public sealed record LogQlGroupBy(int TimeBucketSeconds, Model.LogAggregateGroupBy Secondary);

/// <summary>Closed column allowlist for <c>where</c> comparisons - never request text past this point, same shape as <see cref="LogFilterSqlBuilder"/>'s attribute-bag enum.</summary>
public enum LogQlColumn
{
    Service,
    Level,
    Body,
    TraceId,
    SpanId,
}

public enum LogQlOp
{
    Eq,
    NotEq,
    Lt,
    Lte,
    Gt,
    Gte,
    Like,
    NotLike,
}

/// <summary>Base type for a <c>where</c>-clause expression node.</summary>
public abstract record LogQlExpr;

public enum LogQlBoolOp
{
    And,
    Or,
}

public sealed record LogQlBinary(LogQlBoolOp Op, LogQlExpr Left, LogQlExpr Right) : LogQlExpr;

public sealed record LogQlNot(LogQlExpr Operand) : LogQlExpr;

/// <summary><paramref name="Literal"/> is always the raw (unquoted) string value - see <see cref="LogQlParser"/>'s "literals are single-quoted strings only" v1 restriction.</summary>
public sealed record LogQlComparison(LogQlColumn Column, LogQlOp Op, string Literal) : LogQlExpr;
