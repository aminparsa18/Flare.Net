using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;

namespace Flare.Api.Query.LogQl;

/// <summary>
/// Compiles a parsed <c>where</c> expression (<see cref="LogQlExpr"/>) into a
/// parameterized ClickHouse SQL fragment - every literal is bound as a
/// <see cref="ClickHouseParameterCollection"/> parameter, never interpolated, same
/// discipline <see cref="LogFilterSqlBuilder"/> already documents for the structured
/// filter path. <see cref="LogQlColumn"/> -> real-column mapping is a closed switch (the
/// parser already rejected anything not in that enum), so there's no path from request
/// text to a column/table name either.
/// </summary>
public static class LogQlWhereTranslator
{
    public static string Translate(LogQlExpr expr, ClickHouseParameterCollection parameters)
    {
        var counter = 0;
        return TranslateNode(expr, parameters, ref counter);
    }

    private static string TranslateNode(LogQlExpr expr, ClickHouseParameterCollection parameters, ref int counter)
    {
        switch (expr)
        {
            case LogQlBinary binary:
                var left = TranslateNode(binary.Left, parameters, ref counter);
                var right = TranslateNode(binary.Right, parameters, ref counter);
                var op = binary.Op == LogQlBoolOp.And ? "AND" : "OR";
                return $"({left} {op} {right})";

            case LogQlNot not:
                return $"NOT ({TranslateNode(not.Operand, parameters, ref counter)})";

            case LogQlComparison comparison:
                return TranslateComparison(comparison, parameters, ref counter);

            default:
                throw new InvalidOperationException($"Unknown LogQl expression node type '{expr.GetType()}'.");
        }
    }

    private static string TranslateComparison(LogQlComparison comparison, ClickHouseParameterCollection parameters, ref int counter)
    {
        var column = ColumnName(comparison.Column);
        var paramName = $"qlp{counter++}";

        if (comparison.Op is LogQlOp.Like or LogQlOp.NotLike)
        {
            // Case-insensitive, same as the existing free-text search's Body match (see
            // LogFilterSqlBuilder.Build) - unlike that one, the literal is bound exactly
            // as written (no auto '%' wrapping): this is a SQL LIKE, so the caller
            // supplies their own wildcards (e.g. "'%timeout%'"), same as real SQL.
            parameters.AddParameter(paramName, comparison.Literal);
            var likeSql = $"{column} ILIKE {{{paramName}:String}}";
            return comparison.Op == LogQlOp.Like ? likeSql : $"NOT ({likeSql})";
        }

        parameters.AddParameter(paramName, comparison.Literal);
        var sqlOp = comparison.Op switch
        {
            LogQlOp.Eq => "=",
            LogQlOp.NotEq => "!=",
            LogQlOp.Lt => "<",
            LogQlOp.Lte => "<=",
            LogQlOp.Gt => ">",
            LogQlOp.Gte => ">=",
            _ => throw new InvalidOperationException($"Unhandled LogQlOp '{comparison.Op}'."),
        };
        return $"{column} {sqlOp} {{{paramName}:String}}";
    }

    /// <summary>
    /// Real ClickHouse column name for every <see cref="LogQlColumn"/>. Used both here
    /// (where-clause translation) and by <c>LogQlQueryBuilder</c> for select/aggregate SQL -
    /// <see cref="LogQlColumn.SeverityNumber"/> is never actually reachable from a where
    /// clause (the parser rejects it there - see LogQlParser.ParseComparison), but the
    /// mapping still needs an entry here for the switch to stay exhaustive.
    /// </summary>
    internal static string ColumnName(LogQlColumn column) => column switch
    {
        LogQlColumn.Service => "ServiceName",
        LogQlColumn.Level => "SeverityText",
        LogQlColumn.Body => "Body",
        LogQlColumn.TraceId => "TraceId",
        LogQlColumn.SpanId => "SpanId",
        LogQlColumn.SeverityNumber => "SeverityNumber",
        _ => throw new InvalidOperationException($"Unhandled LogQlColumn '{column}'."),
    };
}
