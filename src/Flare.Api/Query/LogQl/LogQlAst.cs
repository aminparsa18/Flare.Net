namespace Flare.Api.Query.LogQl;

/// <summary>
/// Parsed shape of a SQL-query-row query - see <see cref="LogQlParser"/> for the grammar.
/// Deliberately small/closed (no arbitrary column list, no joins, no subqueries): every
/// node here maps 1:1 onto something <see cref="LogQlWhereTranslator"/>/<see cref="LogQlQueryBuilder"/>
/// can compile to a parameterized ClickHouse fragment, so there's no path from user text
/// to literal SQL.
/// </summary>
public sealed record LogQlQuery(LogQlSelect Select, LogQlExpr? Where, LogQlGroupBy? GroupBy);

/// <summary>Base type for a <c>select</c> list - exactly one of <see cref="LogQlSelectStar"/>/<see cref="LogQlSelectColumns"/>/<see cref="LogQlSelectAggregate"/>.</summary>
public abstract record LogQlSelect;

/// <summary><c>select *</c> - every column (the existing full-<c>LogEventDto</c> row shape).</summary>
public sealed record LogQlSelectStar : LogQlSelect;

/// <summary><c>select Col1, Col2, ...</c> - a specific projection, rendered as a generic column/row table (not the full event shape).</summary>
public sealed record LogQlSelectColumns(IReadOnlyList<LogQlColumn> Columns) : LogQlSelect;

/// <summary><c>select count(*)</c> / <c>avg(Col)</c> / <c>sum(Col)</c>. <see cref="Column"/> is null only for <see cref="LogQlAggFunc.Count"/>.</summary>
public sealed record LogQlSelectAggregate(LogQlAggFunc Func, LogQlColumn? Column) : LogQlSelect;

public enum LogQlAggFunc
{
    Count,
    Avg,
    Sum,
}

/// <summary>
/// <c>group by time(&lt;duration&gt;)[, service|level]</c> - always time-bucketed in v1
/// (see this feature's plan doc for why an un-time-bucketed <c>group by</c> isn't
/// supported), with an optional secondary dimension reusing the same
/// <see cref="Model.LogAggregateGroupBy"/> enum the existing <c>/api/logs/aggregate</c>
/// endpoint already exposes.
/// </summary>
public sealed record LogQlGroupBy(int TimeBucketSeconds, Model.LogAggregateGroupBy Secondary);

/// <summary>
/// Closed column allowlist - never request text past this point, same shape as
/// <see cref="LogFilterSqlBuilder"/>'s attribute-bag enum. <see cref="SeverityNumber"/> is
/// select/aggregate-only (the only numeric column exposed today) - <see cref="LogQlParser"/>
/// rejects it in a <c>where</c> comparison with its own explicit message, since every
/// literal there is a string (see this grammar's "no bare/numeric literals" restriction),
/// which would silently type-mismatch against a numeric column.
/// </summary>
public enum LogQlColumn
{
    Service,
    Level,
    Body,
    TraceId,
    SpanId,
    SeverityNumber,
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
