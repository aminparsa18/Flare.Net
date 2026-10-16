using System.Globalization;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Loki;

/// <summary>A built <c>SELECT</c> and its bound parameters, ready for <see cref="LokiQueryService"/>.</summary>
internal sealed record LokiSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>What the optional <c>| json</c> stage of a pipeline extracts: the explicit <c>label="path"</c> mappings, or every field when there are none.</summary>
internal sealed record LokiJsonSpec(IReadOnlyDictionary<string, string> Expressions);

/// <summary>
/// Compiles a LogQL log selector and pipeline to the <c>WHERE</c> clause of a ClickHouse query over
/// <c>logs</c> (ADR-0236), plus the SQL for the other queries the Loki API needs (log lines, bucketed
/// counts, label discovery). The time bound and the per-request service scope come from
/// <see cref="LogFilterSqlBuilder"/>, so a scoped token sees exactly what it sees in the dashboard.
/// Every user-controlled string is a bound parameter; only closed sets (column names, operators) are
/// interpolated. Pure, no ClickHouse dependency.
/// </summary>
internal sealed class LokiSqlContext
{
    private const int MaxFlattenedParts = 6;

    private readonly List<string> _clauses;
    private readonly PromotedAttributeColumns? _promoted;
    private readonly Dictionary<string, string> _jsonExpressions = new(StringComparer.Ordinal);
    private int _counter;

    public LokiSqlContext(LokiLabelResolver resolver, DateTimeOffset from, DateTimeOffset to, DateTimeOffset now, PromotedAttributeColumns? promoted)
    {
        Resolver = resolver;
        _promoted = promoted;
        var baseFilter = LogFilterSqlBuilder.Build(new LogFilter { From = from, To = to }, now, promoted);
        Parameters = baseFilter.Parameters;
        _clauses = [baseFilter.WhereSql];
    }

    public LokiLabelResolver Resolver { get; }

    public ClickHouseParameterCollection Parameters { get; }

    /// <summary>True once a <c>| json</c> stage has been applied, so later label names may be body fields.</summary>
    public bool JsonSeen { get; private set; }

    public LokiJsonSpec? Json => JsonSeen ? new LokiJsonSpec(_jsonExpressions) : null;

    public string WhereSql => string.Join(" AND ", _clauses);

    /// <summary>Applies a selector's matchers and pipeline, in order, to the <c>WHERE</c> clause.</summary>
    public void AddSelector(LokiLogSelector selector)
    {
        foreach (var matcher in selector.Matchers)
        {
            var label = Resolver.Resolve(matcher.Name);
            _clauses.Add(Compare(LabelSql(label), ToCompareOp(matcher.Op), matcher.Value, null));
        }

        foreach (var stage in selector.Stages)
        {
            switch (stage)
            {
                case LokiLineFilter line:
                    if (LineFilterSql(line) is { } lineSql)
                    {
                        _clauses.Add(lineSql);
                    }

                    break;
                case LokiJsonStage json:
                    JsonSeen = true;
                    foreach (var (label, path) in json.Expressions)
                    {
                        _jsonExpressions[label] = path;
                    }

                    break;
                case LokiLabelFilterStage filter:
                    _clauses.Add(PredicateSql(filter.Predicate));
                    break;
                case LokiDropStage:
                    break;
            }
        }
    }

    /// <summary>The ClickHouse expression for a label name at the current point of the pipeline.</summary>
    public string LabelSql(string name) => LabelSql(Resolver.Resolve(name, JsonSeen, _jsonExpressions));

    private string LabelSql(LokiLabel label)
    {
        switch (label.Kind)
        {
            case LokiLabelKind.Service:
                return "ServiceName";
            case LokiLabelKind.Level:
                return LokiLabelResolver.LevelSql;
            case LokiLabelKind.LevelOrJson:
            {
                var args = JsonPathArgs("level");
                return $"if(JSONHas(Body, {args}), {JsonValueSql(args)}, {LokiLabelResolver.LevelSql})";
            }

            case LokiLabelKind.SeverityText:
                return "SeverityText";
            case LokiLabelKind.Scope:
                return "ScopeName";
            case LokiLabelKind.TraceId:
                return "TraceId";
            case LokiLabelKind.SpanId:
                return "SpanId";
            case LokiLabelKind.ParseError:
                return label.Key == "json" ? "if(isValidJSON(Body), '', 'JSONParserErr')" : "''";
            case LokiLabelKind.ResourceAttribute:
                return AttributeSql(AttributeBag.Resource, label.Key);
            case LokiLabelKind.LogAttribute:
                return AttributeSql(AttributeBag.Log, label.Key);
            default:
                return JsonFieldSql(label);
        }
    }

    private string AttributeSql(AttributeBag bag, string key)
    {
        if (_promoted is not null && _promoted.TryGetColumn(bag, key, out var column))
        {
            return column;
        }

        return $"{LogFilterSqlBuilder.ColumnFor(bag)}[{Bind(key)}]";
    }

    private string JsonFieldSql(LokiLabel label)
    {
        var candidates = label.Guessed ? FlattenedCandidates(label.Key) : [label.Key];
        if (candidates.Count == 1)
        {
            return JsonValueSql(JsonPathArgs(candidates[0]));
        }

        var branches = new List<string>();
        foreach (var path in candidates)
        {
            var args = JsonPathArgs(path);
            branches.Add($"JSONHas(Body, {args}), {JsonValueSql(args)}");
        }

        return $"multiIf({string.Join(", ", branches)}, '')";
    }

    /// <summary>
    /// The paths a flattened label could come from. Loki's <c>| json</c> joins nested keys with
    /// <c>_</c> (<c>{"request":{"method":"GET"}}</c> is <c>request_method</c>), so <c>a_b_c</c> could be
    /// <c>a_b_c</c>, <c>a.b_c</c>, <c>a_b.c</c> or <c>a.b.c</c>. They are tried in that order of fewest
    /// splits first; with more than <see cref="MaxFlattenedParts"/> parts only the literal key is tried.
    /// </summary>
    internal static List<string> FlattenedCandidates(string name)
    {
        var parts = name.Split('_');
        if (parts.Length < 2 || parts.Length > MaxFlattenedParts || parts.Any(p => p.Length == 0))
        {
            return [name];
        }

        var gaps = parts.Length - 1;
        var masks = Enumerable.Range(0, 1 << gaps)
            .OrderBy(m => int.PopCount(m))
            .ThenBy(m => m);
        var result = new List<string>();
        foreach (var mask in masks)
        {
            // A set bit i splits between part i and part i+1 (a path separator); a clear bit keeps the underscore.
            var path = parts[0];
            for (var i = 0; i < gaps; i++)
            {
                path += ((mask >> i) & 1) == 1 ? "." + parts[i + 1] : "_" + parts[i + 1];
            }

            result.Add(path);
        }

        return result;
    }

    private string JsonPathArgs(string path) =>
        LogFilterSqlBuilder.BodyJsonPathArgs(path, $"lkj{_counter++}", Parameters);

    /// <summary>A JSON value as text: a string's content, anything else (number, boolean, object) as raw JSON.</summary>
    private static string JsonValueSql(string pathArgs) =>
        $"if(JSONType(Body, {pathArgs}) = 'String', JSONExtractString(Body, {pathArgs}), JSONExtractRaw(Body, {pathArgs}))";

    private string Bind(string value)
    {
        var name = $"lk{_counter++}";
        Parameters.AddParameter(name, value);
        return $"{{{name}:String}}";
    }

    private static LokiCompareOp ToCompareOp(LokiMatchOp op) => op switch
    {
        LokiMatchOp.Equal => LokiCompareOp.Equal,
        LokiMatchOp.NotEqual => LokiCompareOp.NotEqual,
        LokiMatchOp.Regex => LokiCompareOp.Regex,
        _ => LokiCompareOp.NotRegex,
    };

    /// <summary>A label comparison. Label values are anchored (<c>^(?:re)$</c>) as in Prometheus and Loki; an absent label is the empty string.</summary>
    private string Compare(string expr, LokiCompareOp op, string text, double? number)
    {
        if (number is { } n)
        {
            var name = $"lk{_counter++}";
            Parameters.AddParameter(name, n);
            var symbol = op switch
            {
                LokiCompareOp.Equal => "=",
                LokiCompareOp.NotEqual => "!=",
                LokiCompareOp.Greater => ">",
                LokiCompareOp.GreaterOrEqual => ">=",
                LokiCompareOp.Less => "<",
                _ => "<=",
            };
            // A value that is not a number never satisfies a comparison, except "!=" which it trivially does.
            var fallback = op == LokiCompareOp.NotEqual ? 1 : 0;
            return $"ifNull(toFloat64OrNull({expr}) {symbol} {{{name}:Float64}}, {fallback.ToString(CultureInfo.InvariantCulture)})";
        }

        return op switch
        {
            LokiCompareOp.Equal => $"{expr} = {Bind(text)}",
            LokiCompareOp.NotEqual => $"{expr} != {Bind(text)}",
            LokiCompareOp.Regex => $"match({expr}, {Bind(Anchored(text))})",
            LokiCompareOp.NotRegex => $"NOT match({expr}, {Bind(Anchored(text))})",
            _ => throw new LokiException("a quoted value is required for this comparison."),
        };
    }

    private static string Anchored(string pattern) => $"^(?:{pattern})$";

    private string PredicateSql(LokiPredicate predicate) => predicate switch
    {
        LokiAnd both => $"({PredicateSql(both.Left)} AND {PredicateSql(both.Right)})",
        LokiOr either => $"({PredicateSql(either.Left)} OR {PredicateSql(either.Right)})",
        LokiComparison c => Compare(LabelSql(c.Label), c.Op, c.Text, c.Number),
        _ => throw new LokiException("unsupported label filter."),
    };

    /// <summary>
    /// A line filter. <c>|=</c> is a case-sensitive substring match, so it is <c>position()</c> narrowed by
    /// the case-insensitive ngram-indexed <c>LIKE</c> the logs search uses (ADR-0073); <c>|~</c> is an
    /// unanchored RE2 <c>match()</c>. Returns null for a filter that matches every line.
    /// </summary>
    private string? LineFilterSql(LokiLineFilter filter)
    {
        var alternatives = new List<string>();
        foreach (var pattern in filter.Patterns)
        {
            if (filter.Op is LokiMatchOp.Equal or LokiMatchOp.NotEqual)
            {
                if (pattern.Length == 0)
                {
                    alternatives.Add("1");
                    continue;
                }

                var like = $"lkl{_counter++}";
                Parameters.AddParameter(like, LogFilterSqlBuilder.ContainsPattern(pattern));
                alternatives.Add($"({LogFilterSqlBuilder.BodyLikeSql($"{{{like}:String}}")} AND position(Body, {Bind(pattern)}) > 0)");
            }
            else
            {
                alternatives.Add($"match(Body, {Bind(pattern)})");
            }
        }

        var any = alternatives.Count == 1 ? alternatives[0] : $"({string.Join(" OR ", alternatives)})";
        var negated = filter.Op is LokiMatchOp.NotEqual or LokiMatchOp.NotRegex;
        return negated ? $"NOT {any}" : any;
    }
}

/// <summary>Builders for the queries behind each Loki endpoint. Pure.</summary>
internal static class LokiQueries
{
    public const int DefaultLimit = 100;

    /// <summary>Loki's default <c>max_entries_limit_per_query</c>.</summary>
    public const int MaxLimit = 5000;

    public const int DefaultMetadataLimit = 1000;

    /// <summary>Attribute keys sampled from at most this many log rows, as for span tag discovery (ADR-0217).</summary>
    public const int KeySampleRows = 50_000;

    public const int MaxKeys = 500;

    /// <summary>Log lines, newest first unless <paramref name="forward"/>. Timestamps are read as integer nanoseconds: <c>DateTime64(9)</c> does not survive the driver's 100 ns ticks.</summary>
    public static LokiSql LogLines(LokiSqlContext context, int limit, bool forward)
    {
        var order = forward ? "ASC" : "DESC";
        context.Parameters.AddParameter("lkLimit", (uint)Math.Clamp(limit, 1, MaxLimit));
        var sql = "SELECT toUnixTimestamp64Nano(Timestamp) AS Ns, Body, ServiceName, SeverityText, SeverityNumber, TraceId, SpanId, " +
            "ScopeName, ScopeVersion, ResourceAttributes, LogAttributes\n" +
            $"FROM logs\nWHERE {context.WhereSql}\n" +
            $"ORDER BY Timestamp {order}, EventId {order}\n" +
            "LIMIT {lkLimit:UInt32}";
        return new LokiSql(sql, context.Parameters);
    }

    /// <summary>
    /// A range aggregation's input: one row per (time bucket, label values) with the count or byte sum.
    /// Buckets are <paramref name="bucketMs"/> wide and start at <paramref name="originMs"/> (so they line up
    /// with the evaluation steps, whatever the epoch); the row carries the bucket's index. A
    /// <paramref name="bucketMs"/> of 0 is a single bucket over the whole window (an instant query).
    /// </summary>
    public static LokiSql Buckets(LokiSqlContext context, IReadOnlyList<string> dimensions, bool bytes, long originMs, long bucketMs)
    {
        var select = new List<string>();
        var groupBy = new List<string>();
        if (bucketMs > 0)
        {
            context.Parameters.AddParameter("lkOrigin", originMs);
            context.Parameters.AddParameter("lkBucketMs", bucketMs);
            select.Add("intDiv(toUnixTimestamp64Milli(Timestamp) - {lkOrigin:Int64}, {lkBucketMs:Int64}) AS BucketIdx");
            groupBy.Add("BucketIdx");
        }
        else
        {
            select.Add("toInt64(0) AS BucketIdx");
        }

        for (var i = 0; i < dimensions.Count; i++)
        {
            select.Add($"{context.LabelSql(dimensions[i])} AS d{i}");
            groupBy.Add($"d{i}");
        }

        select.Add(bytes ? "toFloat64(sum(length(Body))) AS V" : "toFloat64(count()) AS V");
        var sql = $"SELECT {string.Join(", ", select)}\nFROM logs\nWHERE {context.WhereSql}" +
            (groupBy.Count > 0 ? $"\nGROUP BY {string.Join(", ", groupBy)}" : string.Empty) +
            "\nORDER BY BucketIdx";
        return new LokiSql(sql, context.Parameters);
    }

    /// <summary>The most common attribute keys of the window's logs, resource and log bags together: <c>r:key</c> or <c>l:key</c>.</summary>
    public static LokiSql AttributeKeys(LokiSqlContext context)
    {
        context.Parameters.AddParameter("lkSample", (uint)KeySampleRows);
        context.Parameters.AddParameter("lkKeys", (uint)MaxKeys);
        var sql = "SELECT arrayJoin(arrayConcat(arrayMap(k -> concat('r:', k), mapKeys(R)), arrayMap(k -> concat('l:', k), mapKeys(L)))) AS K, count() AS Cnt\n" +
            $"FROM (SELECT ResourceAttributes AS R, LogAttributes AS L FROM logs WHERE {context.WhereSql} LIMIT {{lkSample:UInt32}})\n" +
            "GROUP BY K\nORDER BY Cnt DESC, K\nLIMIT {lkKeys:UInt32}";
        return new LokiSql(sql, context.Parameters);
    }

    /// <summary>Distinct non-empty values of one label.</summary>
    public static LokiSql LabelValues(LokiSqlContext context, string label, int limit)
    {
        context.Parameters.AddParameter("lkValuesLimit", (uint)Math.Clamp(limit, 1, DefaultMetadataLimit));
        var sql = $"SELECT {context.LabelSql(label)} AS V\nFROM logs\nWHERE {context.WhereSql}\nGROUP BY V\nHAVING V != ''\nORDER BY V\nLIMIT {{lkValuesLimit:UInt32}}";
        return new LokiSql(sql, context.Parameters);
    }

    /// <summary>Distinct stream label sets: one row per combination of <paramref name="dimensions"/>.</summary>
    public static LokiSql Streams(LokiSqlContext context, IReadOnlyList<string> dimensions, int limit)
    {
        context.Parameters.AddParameter("lkStreamsLimit", (uint)Math.Clamp(limit, 1, DefaultMetadataLimit));
        var select = string.Join(", ", dimensions.Select((d, i) => $"{context.LabelSql(d)} AS d{i}"));
        var group = string.Join(", ", dimensions.Select((_, i) => $"d{i}"));
        var sql = $"SELECT {select}\nFROM logs\nWHERE {context.WhereSql}\nGROUP BY {group}\nORDER BY {group}\nLIMIT {{lkStreamsLimit:UInt32}}";
        return new LokiSql(sql, context.Parameters);
    }

    /// <summary>Entries, bytes and distinct services for <c>index/stats</c>.</summary>
    public static LokiSql Stats(LokiSqlContext context) =>
        new($"SELECT toUInt64(count()) AS Entries, toUInt64(sum(length(Body))) AS Bytes, toUInt64(uniqExact(ServiceName)) AS Streams\nFROM logs\nWHERE {context.WhereSql}", context.Parameters);
}
