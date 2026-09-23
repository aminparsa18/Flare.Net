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
                return TranslateComparison(ColumnName(comparison.Column), comparison.Op, comparison.Literal, parameters, ref counter);

            case LogQlJsonComparison jsonComparison:
                return TranslateComparison(JsonExtractSql(jsonComparison.Path, parameters, ref counter), jsonComparison.Op, jsonComparison.Literal, parameters, ref counter);

            case LogQlAttributeComparison attributeComparison:
                return TranslateAttributeComparison(attributeComparison.Bag, attributeComparison.Key, attributeComparison.Op, attributeComparison.Literal, parameters, ref counter);

            case LogQlAttributeExists attributeExists:
                var (attrContainsSql, _) = AttributeAccessorSql(attributeExists.Bag, attributeExists.Key, parameters, ref counter);
                return attributeExists.Negate ? $"NOT {attrContainsSql}" : attrContainsSql;

            default:
                throw new InvalidOperationException($"Unknown LogQl expression node type '{expr.GetType()}'.");
        }
    }

    /// <summary>
    /// Shared by both <see cref="LogQlComparison"/> (<paramref name="lhsSql"/> is a real
    /// column name) and <see cref="LogQlJsonComparison"/> (<paramref name="lhsSql"/> is a
    /// <c>JSONExtractString(...)</c> call from <see cref="JsonExtractSql"/>) - once the
    /// left-hand side is resolved to a SQL fragment, op/literal compilation is identical.
    /// </summary>
    private static string TranslateComparison(string lhsSql, LogQlOp op, string literal, ClickHouseParameterCollection parameters, ref int counter)
    {
        var paramName = $"qlp{counter++}";

        if (op is LogQlOp.Like or LogQlOp.NotLike)
        {
            // Case-insensitive, same as the existing free-text search's Body match (see
            // LogFilterSqlBuilder.Build) - unlike that one, the literal is bound exactly
            // as written (no auto '%' wrapping): this is a SQL LIKE, so the caller
            // supplies their own wildcards (e.g. "'%timeout%'"), same as real SQL.
            parameters.AddParameter(paramName, literal);
            var likeSql = $"{lhsSql} ILIKE {{{paramName}:String}}";
            return op == LogQlOp.Like ? likeSql : $"NOT ({likeSql})";
        }

        parameters.AddParameter(paramName, literal);
        var sqlOp = op switch
        {
            LogQlOp.Eq => "=",
            LogQlOp.NotEq => "!=",
            LogQlOp.Lt => "<",
            LogQlOp.Lte => "<=",
            LogQlOp.Gt => ">",
            LogQlOp.Gte => ">=",
            _ => throw new InvalidOperationException($"Unhandled LogQlOp '{op}'."),
        };
        return $"{lhsSql} {sqlOp} {{{paramName}:String}}";
    }

    /// <summary>
    /// <c>JSONExtractString(Body, ...)</c> for a <see cref="LogQlJsonComparison.Path"/> -
    /// same dot-split, one-parameter-per-segment compilation as
    /// <see cref="Flare.Api.Query.LogFilterSqlBuilder"/>'s <c>BodyJsonClause</c> (ClickHouse's
    /// <c>JSONExtractString</c> takes one key/index per argument, not a single JSONPath
    /// string - see that method's own remarks for the live probe that confirmed this).
    /// Unlike <c>BodyJsonClause</c>, there's no <c>JSONHas</c> guard on any operator here -
    /// this mirrors <see cref="TranslateComparison"/>'s existing column-comparison
    /// semantics (an absent path reads back as ClickHouse's <c>JSONExtractString</c> zero
    /// value, <c>''</c>, same as a real column never being null), not
    /// <c>BodyJsonClause</c>'s richer exists/absent/in operator set, which this smaller
    /// LogQL grammar doesn't expose.
    /// </summary>
    private static string JsonExtractSql(string path, ClickHouseParameterCollection parameters, ref int counter)
    {
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var segmentArgs = new string[segments.Length];
        for (var s = 0; s < segments.Length; s++)
        {
            var segParam = $"qlp{counter++}";
            parameters.AddParameter(segParam, segments[s]);
            segmentArgs[s] = $"{{{segParam}:String}}";
        }

        return $"JSONExtractString(Body, {string.Join(", ", segmentArgs)})";
    }

    /// <summary>
    /// Binds the attribute key once and returns both SQL shapes built from it - the
    /// <c>mapContains(...)</c> presence check and the <c>column[key]</c> subscript -
    /// so every caller that needs both (see <see cref="TranslateAttributeComparison"/>)
    /// reuses the same bound parameter instead of adding it twice.
    /// </summary>
    private static (string ContainsSql, string SubscriptSql) AttributeAccessorSql(LogQlAttributeBag bag, string key, ClickHouseParameterCollection parameters, ref int counter)
    {
        var column = AttributeColumnName(bag);
        var keyParam = $"qlp{counter++}";
        parameters.AddParameter(keyParam, key);
        var keyRef = $"{{{keyParam}:String}}";
        return ($"mapContains({column}, {keyRef})", $"{column}[{keyRef}]");
    }

    /// <summary>
    /// Compiles one <c>attr(bag, 'key') op 'literal'</c> comparison - mirrors
    /// <see cref="Flare.Api.Query.LogFilterSqlBuilder"/>'s <c>AttributeClause</c>:
    /// <see cref="LogQlOp.Eq"/> alone is left unguarded (a missing key reads back as the
    /// map's <c>String</c> zero value, <c>''</c>, so "= ''" still means "absent or
    /// explicitly empty" the same way a real column would) - every other operator is
    /// guarded with <c>mapContains</c> first so a missing key never silently satisfies
    /// "!= x" or a range/LIKE comparison purely off that same empty-string default.
    /// </summary>
    private static string TranslateAttributeComparison(LogQlAttributeBag bag, string key, LogQlOp op, string literal, ClickHouseParameterCollection parameters, ref int counter)
    {
        var (containsSql, subscriptSql) = AttributeAccessorSql(bag, key, parameters, ref counter);
        var valueParam = $"qlp{counter++}";
        parameters.AddParameter(valueParam, literal);
        var valueRef = $"{{{valueParam}:String}}";

        if (op == LogQlOp.Eq)
        {
            return $"{subscriptSql} = {valueRef}";
        }

        if (op == LogQlOp.NotEq)
        {
            return $"NOT ({containsSql} AND {subscriptSql} = {valueRef})";
        }

        if (op is LogQlOp.Like or LogQlOp.NotLike)
        {
            var likeSql = $"({containsSql} AND {subscriptSql} ILIKE {valueRef})";
            return op == LogQlOp.Like ? likeSql : $"NOT {likeSql}";
        }

        var sqlOp = op switch
        {
            LogQlOp.Lt => "<",
            LogQlOp.Lte => "<=",
            LogQlOp.Gt => ">",
            LogQlOp.Gte => ">=",
            _ => throw new InvalidOperationException($"Unhandled LogQlOp '{op}'."),
        };
        return $"({containsSql} AND {subscriptSql} {sqlOp} {valueRef})";
    }

    /// <summary>Real ClickHouse column name for every <see cref="LogQlAttributeBag"/> - see <see cref="ColumnName"/> for the equivalent <see cref="LogQlColumn"/> mapping.</summary>
    private static string AttributeColumnName(LogQlAttributeBag bag) => bag switch
    {
        LogQlAttributeBag.Log => "LogAttributes",
        LogQlAttributeBag.Resource => "ResourceAttributes",
        LogQlAttributeBag.Scope => "ScopeAttributes",
        _ => throw new InvalidOperationException($"Unhandled LogQlAttributeBag '{bag}'."),
    };

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
