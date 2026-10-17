using System.Globalization;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Loki;

/// <summary>A built <c>SELECT</c> and its bound parameters, ready for <see cref="LokiQueryService"/>.</summary>
internal sealed record LokiSql(string Sql, ClickHouseParameterCollection Parameters);

internal enum LokiParserKind
{
    Json,
    Logfmt,
}

/// <summary>One <c>| json</c> or <c>| logfmt</c> stage: the explicit <c>label="path"</c> mappings, or every field when there are none. <see cref="Strict"/> is <c>logfmt --strict</c>.</summary>
internal sealed record LokiParserSpec(LokiParserKind Kind, IReadOnlyDictionary<string, string> Expressions, bool Strict = false);

/// <summary>What a response needs to know about a pipeline to label and render an entry: its parser stages in order, and the final <c>line_format</c>.</summary>
internal sealed record LokiEntryPlan(IReadOnlyList<LokiParserSpec> Parsers, LokiTemplate? LineFormat)
{
    public static readonly LokiEntryPlan None = new([], null);
}

/// <summary>Escapes a string as a ClickHouse string literal, for the fixed text of a query (user text is always a bound parameter).</summary>
internal static class LokiSqlLiteral
{
    public static string Of(string value) => "'" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal) + "'";
}

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
    private readonly List<LokiParserSpec> _parsers = [];
    private int _counter;
    private bool _unwrapError;

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

    /// <summary>The parser stages applied so far, in order, so later label names may be body fields.</summary>
    public IReadOnlyList<LokiParserSpec> Parsers => _parsers;

    /// <summary>The pipeline's final <c>line_format</c>, applied to returned lines in C# (ADR-0237).</summary>
    public LokiTemplate? LineFormat { get; private set; }

    /// <summary>The numeric ClickHouse expression of <c>| unwrap</c> (a <c>Nullable(Float64)</c>), or null when the pipeline has none.</summary>
    public string? UnwrapSql { get; private set; }

    /// <summary>What a response needs to label and render an entry for this pipeline.</summary>
    public LokiEntryPlan Plan => new(_parsers, LineFormat);

    public string WhereSql => string.Join(" AND ", _clauses);

    /// <summary>The <c>WITH</c> clause a query must start with when the pipeline reads logfmt labels (an empty string otherwise).</summary>
    public string WithSql => _parsers.Any(p => p.Kind == LokiParserKind.Logfmt)
        ? "WITH " + (_parsers.Any(p => p is { Kind: LokiParserKind.Logfmt, Strict: true }) ? LokiLogfmt.StrictMapSql : LokiLogfmt.MapSql) + "\n"
        : string.Empty;

    private LokiParserKinds Kinds => _parsers.Aggregate(
        LokiParserKinds.None, (kinds, p) => kinds | (p.Kind == LokiParserKind.Json ? LokiParserKinds.Json : LokiParserKinds.Logfmt));

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
                case LokiPatternFilter pattern:
                {
                    var match = $"match(Body, {Bind(LokiPatternMatcher.ToRegex(pattern.Pattern))})";
                    _clauses.Add(pattern.Negated ? $"NOT {match}" : match);
                    break;
                }

                case LokiJsonStage json:
                    _parsers.Add(new LokiParserSpec(LokiParserKind.Json, ToMap(json.Expressions)));
                    break;
                case LokiLogfmtStage logfmt:
                    _parsers.Add(new LokiParserSpec(LokiParserKind.Logfmt, ToMap(logfmt.Expressions), logfmt.Strict));
                    break;
                case LokiLabelFilterStage filter:
                    _clauses.Add(PredicateSql(filter.Predicate));
                    break;
                case LokiLineFormatStage format:
                    LineFormat = format.Template;
                    break;
                case LokiUnwrapStage unwrap:
                {
                    // A line without the label is not a sample; one whose value does not convert is a sample of 0 that
                    // carries __error__="SampleExtractionErr" (ADR-0238), which a later filter or the evaluator deals with.
                    var raw = LabelSql(unwrap.Label);
                    UnwrapSql = UnwrapValueSql(raw, unwrap.Conversion);
                    _unwrapError = true;
                    _clauses.Add($"{raw} != ''");
                    break;
                }

                case LokiDropStage drop:
                    _unwrapError &= !drop.ResetsError;
                    break;
            }
        }
    }

    /// <summary>Adds a parser stage the query did not spell out (<c>detected_field/{name}/values</c> parses with both, as Loki does).</summary>
    public void AddParser(LokiParserKind kind) => _parsers.Add(new LokiParserSpec(kind, new Dictionary<string, string>()));

    private static Dictionary<string, string> ToMap(IReadOnlyList<(string Label, string Target)> expressions)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (label, target) in expressions)
        {
            map[label] = target;
        }

        return map;
    }

    /// <summary>The ClickHouse expression for a label name at the current point of the pipeline.</summary>
    public string LabelSql(string name) =>
        _parsers.Any(p => p.Expressions.ContainsKey(name))
            ? ParsedFieldSql(name, fallback: null)
            : LabelSql(Resolver.Resolve(name, Kinds));

    private string LabelSql(LokiLabel label)
    {
        switch (label.Kind)
        {
            case LokiLabelKind.Service:
                return "ServiceName";
            case LokiLabelKind.Level:
                return LokiLabelResolver.LevelSql;
            case LokiLabelKind.LevelOrParsed:
                return ParsedFieldSql("level", fallback: LokiLabelResolver.LevelSql);
            case LokiLabelKind.SeverityText:
                return "SeverityText";
            case LokiLabelKind.Scope:
                return "ScopeName";
            case LokiLabelKind.TraceId:
                return "TraceId";
            case LokiLabelKind.SpanId:
                return "SpanId";
            case LokiLabelKind.ParseError:
                return ErrorSql();
            case LokiLabelKind.ResourceAttribute:
                return AttributeSql(AttributeBag.Resource, label.Key);
            case LokiLabelKind.LogAttribute:
                return AttributeSql(AttributeBag.Log, label.Key);
            default:
                return ParsedFieldSql(label.Key, fallback: null);
        }
    }

    /// <summary>
    /// <c>__error__</c> at the current point of the pipeline: the error the latest failing stage left on the
    /// line. An unwrap conversion error comes last in the pipeline, so it wins; then the parser stages from
    /// the last to the first (<c>| json</c> on a body that is not JSON, <c>| logfmt --strict</c> on a malformed
    /// one). Empty when nothing failed.
    /// </summary>
    private string ErrorSql()
    {
        var branches = new List<string>();
        if (_unwrapError && UnwrapSql is { } unwrap)
        {
            branches.Add($"({unwrap}) IS NULL, 'SampleExtractionErr'");
        }

        for (var i = _parsers.Count - 1; i >= 0; i--)
        {
            if (_parsers[i].Kind == LokiParserKind.Json)
            {
                branches.Add("NOT isValidJSON(Body), 'JSONParserErr'");
            }
            else if (_parsers[i].Strict)
            {
                branches.Add("NOT lkok, 'LogfmtParserErr'");
            }
        }

        return branches.Count == 0 ? "''" : $"multiIf({string.Join(", ", branches)}, '')";
    }

    private string AttributeSql(AttributeBag bag, string key)
    {
        if (_promoted is not null && _promoted.TryGetColumn(bag, key, out var column))
        {
            return column;
        }

        return $"{LogFilterSqlBuilder.ColumnFor(bag)}[{Bind(key)}]";
    }

    /// <summary>
    /// A label extracted by the pipeline's parser stages. Each stage that could have produced it
    /// contributes a branch (latest stage first, as a later label of the same name replaces an earlier
    /// one): <c>| json</c> reads the body's JSON, <c>| logfmt</c> the <c>lkv</c> map of <see cref="WithSql"/>.
    /// With one branch the value is returned bare (an absent field is the empty string); with several the
    /// first stage that finds the field wins, then <paramref name="fallback"/>, then the empty string.
    /// </summary>
    private string ParsedFieldSql(string name, string? fallback)
    {
        var branches = new List<(string Condition, string Value)>();
        for (var i = _parsers.Count - 1; i >= 0; i--)
        {
            var parser = _parsers[i];
            var explicitMapping = parser.Expressions.Count > 0;
            string? target = null;
            if (explicitMapping && !parser.Expressions.TryGetValue(name, out target))
            {
                continue;
            }

            if (parser.Kind == LokiParserKind.Json)
            {
                foreach (var path in explicitMapping ? [target!] : FlattenedCandidates(name))
                {
                    var args = JsonPathArgs(path);
                    branches.Add(($"JSONHas(Body, {args})", JsonValueSql(args)));
                }
            }
            else
            {
                var key = Bind(explicitMapping ? LokiLogfmt.Sanitize(target!) : name);
                branches.Add(($"mapContains(lkv, {key})", $"lkv[{key}]"));
            }
        }

        if (branches.Count == 0)
        {
            return fallback ?? "''";
        }

        if (branches.Count == 1 && fallback is null)
        {
            return branches[0].Value;
        }

        return $"multiIf({string.Join(", ", branches.SelectMany(b => new[] { b.Condition, b.Value }))}, {fallback ?? "''"})";
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

    /// <summary>
    /// The variadic key arguments of <c>JSONHas</c>/<c>JSONExtract*</c> for a path: one bound parameter per
    /// segment, a <c>String</c> for a key and an <c>Int64</c> for an array index (ClickHouse counts from 1).
    /// </summary>
    private string JsonPathArgs(string path)
    {
        var prefix = $"lkj{_counter++}";
        var args = new List<string>();
        foreach (var (segment, i) in LokiJsonPath.Parse(path).Select((s, i) => (s, i)))
        {
            var name = $"{prefix}_{i}";
            if (segment.Key is { } key)
            {
                Parameters.AddParameter(name, key);
                args.Add($"{{{name}:String}}");
            }
            else
            {
                Parameters.AddParameter(name, (long)segment.Index + 1);
                args.Add($"{{{name}:Int64}}");
            }
        }

        return string.Join(", ", args);
    }

    /// <summary>A JSON value as text: a string's content, anything else (number, boolean, object) as raw JSON.</summary>
    private static string JsonValueSql(string pathArgs) =>
        $"if(JSONType(Body, {pathArgs}) = 'String', JSONExtractString(Body, {pathArgs}), JSONExtractRaw(Body, {pathArgs}))";

    private const string DurationUnits = "ns|us|µs|ms|s|m|h";

    /// <summary>
    /// The number <c>| unwrap</c> aggregates, as a nullable <c>Float64</c>: a plain number, a Go duration in
    /// seconds, or a size in bytes. A value that does not parse is NULL: the line stays a sample of 0 with
    /// <c>__error__="SampleExtractionErr"</c> (<see cref="LokiSqlContext.AddSelector"/>), as in Loki.
    /// The label expression is evaluated once, through a one-element lambda.
    /// </summary>
    internal static string UnwrapValueSql(string labelSql, LokiUnwrapConversion conversion) => conversion switch
    {
        LokiUnwrapConversion.Duration =>
            $"arrayMap(s -> if(match(s, {LokiSqlLiteral.Of($"^(?:[0-9]+(?:\\.[0-9]+)?(?:{DurationUnits}))+$")}), " +
            "arraySum(arrayMap(p -> toFloat64OrZero(extract(p, '^[0-9.]+')) * multiIf(endsWith(p, 'ns'), 1e-9, endsWith(p, 'us') OR endsWith(p, 'µs'), 1e-6, " +
            "endsWith(p, 'ms'), 1e-3, endsWith(p, 's'), 1., endsWith(p, 'm'), 60., 3600.), " +
            $"extractAll(s, {LokiSqlLiteral.Of($"[0-9.]+(?:{DurationUnits})")}))), NULL), [{labelSql}])[1]",
        LokiUnwrapConversion.Bytes =>
            "arrayMap(s -> if(match(s, '^[0-9]+(?:\\\\.[0-9]+)?[ ]*[a-zA-Z]*$'), toFloat64OrNull(extract(s, '^[0-9.]+')) * nullIf(" +
            "map('', 1., 'b', 1., 'k', 1e3, 'kb', 1e3, 'm', 1e6, 'mb', 1e6, 'g', 1e9, 'gb', 1e9, 't', 1e12, 'tb', 1e12, 'p', 1e15, 'pb', 1e15, " +
            "'ki', 1024., 'kib', 1024., 'mi', 1048576., 'mib', 1048576., 'gi', 1073741824., 'gib', 1073741824., " +
            "'ti', 1099511627776., 'tib', 1099511627776., 'pi', 1125899906842624., 'pib', 1125899906842624.)[lower(extract(s, '[a-zA-Z]+$'))], 0.), NULL), " +
            $"[{labelSql}])[1]",
        _ => $"toFloat64OrNull({labelSql})",
    };

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
        var sql = context.WithSql + "SELECT toUnixTimestamp64Nano(Timestamp) AS Ns, Body, ServiceName, SeverityText, SeverityNumber, TraceId, SpanId, " +
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
        var sql = $"{context.WithSql}SELECT {string.Join(", ", select)}\nFROM logs\nWHERE {context.WhereSql}" +
            (groupBy.Count > 0 ? $"\nGROUP BY {string.Join(", ", groupBy)}" : string.Empty) +
            "\nORDER BY BucketIdx";
        return new LokiSql(sql, context.Parameters);
    }

    /// <summary>The most common attribute keys of the window's logs, resource and log bags together: <c>r:key</c> or <c>l:key</c>.</summary>
    public static LokiSql AttributeKeys(LokiSqlContext context)
    {
        context.Parameters.AddParameter("lkSample", (uint)KeySampleRows);
        context.Parameters.AddParameter("lkKeys", (uint)MaxKeys);
        var sql = context.WithSql + "SELECT arrayJoin(arrayConcat(arrayMap(k -> concat('r:', k), mapKeys(R)), arrayMap(k -> concat('l:', k), mapKeys(L)))) AS K, count() AS Cnt\n" +
            $"FROM (SELECT ResourceAttributes AS R, LogAttributes AS L FROM logs WHERE {context.WhereSql} LIMIT {{lkSample:UInt32}})\n" +
            "GROUP BY K\nORDER BY Cnt DESC, K\nLIMIT {lkKeys:UInt32}";
        return new LokiSql(sql, context.Parameters);
    }

    /// <summary>Distinct non-empty values of one label.</summary>
    public static LokiSql LabelValues(LokiSqlContext context, string label, int limit)
    {
        context.Parameters.AddParameter("lkValuesLimit", (uint)Math.Clamp(limit, 1, DefaultMetadataLimit));
        var sql = $"{context.WithSql}SELECT {context.LabelSql(label)} AS V\nFROM logs\nWHERE {context.WhereSql}\nGROUP BY V\nHAVING V != ''\nORDER BY V\nLIMIT {{lkValuesLimit:UInt32}}";
        return new LokiSql(sql, context.Parameters);
    }

    /// <summary>Distinct stream label sets: one row per combination of <paramref name="dimensions"/>.</summary>
    public static LokiSql Streams(LokiSqlContext context, IReadOnlyList<string> dimensions, int limit)
    {
        context.Parameters.AddParameter("lkStreamsLimit", (uint)Math.Clamp(limit, 1, DefaultMetadataLimit));
        var select = string.Join(", ", dimensions.Select((d, i) => $"{context.LabelSql(d)} AS d{i}"));
        var group = string.Join(", ", dimensions.Select((_, i) => $"d{i}"));
        var sql = $"{context.WithSql}SELECT {select}\nFROM logs\nWHERE {context.WhereSql}\nGROUP BY {group}\nORDER BY {group}\nLIMIT {{lkStreamsLimit:UInt32}}";
        return new LokiSql(sql, context.Parameters);
    }

    /// <summary>Entries, bytes and distinct services for <c>index/stats</c>.</summary>
    public static LokiSql Stats(LokiSqlContext context) =>
        new($"{context.WithSql}SELECT toUInt64(count()) AS Entries, toUInt64(sum(length(Body))) AS Bytes, toUInt64(uniqExact(ServiceName)) AS Streams\nFROM logs\nWHERE {context.WhereSql}", context.Parameters);

    /// <summary>
    /// An unwrapped range function's input: per (time bucket, label values) the statistics every mergeable
    /// <c>*_over_time</c> function can be computed from - count, sum, min, max, first and last value (by
    /// time), mean and the sum of squared deviations (<c>M2</c>, for Chan's parallel variance). The bucket
    /// grid is that of <see cref="Buckets"/>. The unwrapped value is computed once, in the inner select.
    /// </summary>
    public static LokiSql UnwrapBuckets(LokiSqlContext context, IReadOnlyList<string> dimensions, long originMs, long bucketMs)
    {
        var unwrap = context.UnwrapSql ?? throw new LokiException("this function needs '| unwrap label'.");
        var inner = new List<string>();
        var outerGroup = new List<string>();
        if (bucketMs > 0)
        {
            context.Parameters.AddParameter("lkOrigin", originMs);
            context.Parameters.AddParameter("lkBucketMs", bucketMs);
            inner.Add("intDiv(toUnixTimestamp64Milli(Timestamp) - {lkOrigin:Int64}, {lkBucketMs:Int64}) AS BucketIdx");
        }
        else
        {
            inner.Add("toInt64(0) AS BucketIdx");
        }

        outerGroup.Add("BucketIdx");
        for (var i = 0; i < dimensions.Count; i++)
        {
            inner.Add($"{context.LabelSql(dimensions[i])} AS d{i}");
            outerGroup.Add($"d{i}");
        }

        inner.Add($"ifNull({unwrap}, 0.) AS V");
        inner.Add("Timestamp");
        var sql = "SELECT " + string.Join(", ", outerGroup) +
            ", toFloat64(count()) AS N, sum(V), min(V), max(V), argMin(V, Timestamp), argMax(V, Timestamp), avg(V), varPop(V) * count()\n" +
            $"FROM (\n{context.WithSql}SELECT {string.Join(", ", inner)}\nFROM logs\nWHERE {context.WhereSql})\n" +
            $"GROUP BY {string.Join(", ", outerGroup)}\nORDER BY BucketIdx";
        return new LokiSql(sql, context.Parameters);
    }

    /// <summary>
    /// <c>quantile_over_time</c>: an exact quantile cannot be merged from buckets, so each row is expanded to
    /// the evaluation steps whose window <c>[step - range, step)</c> contains it and the quantile (linear
    /// interpolation, as in Prometheus) is taken per step in ClickHouse. A row lands in about
    /// <c>range / step</c> windows; the caller bounds that factor.
    /// </summary>
    public static LokiSql UnwrapQuantiles(LokiSqlContext context, IReadOnlyList<string> dimensions, double quantile, long firstStepMs, long stepMs, int stepCount, long rangeMs)
    {
        var unwrap = context.UnwrapSql ?? throw new LokiException("this function needs '| unwrap label'.");
        context.Parameters.AddParameter("lkFirst", firstStepMs);
        context.Parameters.AddParameter("lkStep", stepMs);
        context.Parameters.AddParameter("lkRange", rangeMs);
        context.Parameters.AddParameter("lkLastStep", (long)stepCount - 1);
        context.Parameters.AddParameter("lkQuantile", quantile);

        // u is the offset from the start of the first window, so it is never negative.
        const string u = "(toUnixTimestamp64Milli(Timestamp) - {lkFirst:Int64} + {lkRange:Int64})";
        var inner = new List<string>
        {
            $"arrayJoin(range(toUInt64(if({u} < {{lkRange:Int64}}, 0, intDiv({u} - {{lkRange:Int64}}, {{lkStep:Int64}}) + 1)), toUInt64(least(intDiv({u}, {{lkStep:Int64}}), {{lkLastStep:Int64}})) + 1)) AS StepIdx",
        };
        var group = new List<string> { "StepIdx" };
        for (var i = 0; i < dimensions.Count; i++)
        {
            inner.Add($"{context.LabelSql(dimensions[i])} AS d{i}");
            group.Add($"d{i}");
        }

        inner.Add($"ifNull({unwrap}, 0.) AS V");
        var sql = $"SELECT {string.Join(", ", group)}, quantileExactInclusive({{lkQuantile:Float64}})(V) AS Q\n" +
            $"FROM (\n{context.WithSql}SELECT {string.Join(", ", inner)}\nFROM logs\nWHERE {context.WhereSql})\n" +
            $"GROUP BY {string.Join(", ", group)}\nORDER BY StepIdx";
        return new LokiSql(sql, context.Parameters);
    }

    /// <summary>
    /// <c>rate_counter</c>'s input: per (time bucket, label values) what its extrapolated rate needs from the
    /// bucket's samples taken in time order - the count, the time (unix ns) and value of the first and last
    /// sample, and the counter-reset correction (the sum of the values a reset fell from). Buckets merge in
    /// <see cref="LokiCounterStats.Then"/>. The samples of a bucket are held in memory to be ordered, so a very
    /// wide bucket of a very busy counter is bounded by the query's memory limit.
    /// </summary>
    public static LokiSql UnwrapCounterBuckets(LokiSqlContext context, IReadOnlyList<string> dimensions, long originMs, long bucketMs)
    {
        var unwrap = context.UnwrapSql ?? throw new LokiException("this function needs '| unwrap label'.");
        var inner = new List<string>();
        var group = new List<string> { "BucketIdx" };
        if (bucketMs > 0)
        {
            context.Parameters.AddParameter("lkOrigin", originMs);
            context.Parameters.AddParameter("lkBucketMs", bucketMs);
            inner.Add("intDiv(toUnixTimestamp64Milli(Timestamp) - {lkOrigin:Int64}, {lkBucketMs:Int64}) AS BucketIdx");
        }
        else
        {
            inner.Add("toInt64(0) AS BucketIdx");
        }

        for (var i = 0; i < dimensions.Count; i++)
        {
            inner.Add($"{context.LabelSql(dimensions[i])} AS d{i}");
            group.Add($"d{i}");
        }

        inner.Add($"ifNull({unwrap}, 0.) AS V");
        inner.Add("toUnixTimestamp64Nano(Timestamp) AS Tn");
        var keys = string.Join(", ", group);
        var sql = $"SELECT {keys}, toFloat64(length(arr)), arr[1].1, arr[1].2, arr[-1].1, arr[-1].2, " +
            "arraySum(arrayMap(i -> if(arr[i].2 < arr[i - 1].2, arr[i - 1].2, 0.), range(2, length(arr) + 1))) AS Corr\n" +
            $"FROM (\nSELECT {keys}, arraySort(x -> x.1, groupArray((Tn, V))) AS arr\n" +
            $"FROM (\n{context.WithSql}SELECT {string.Join(", ", inner)}\nFROM logs\nWHERE {context.WhereSql})\nGROUP BY {keys})\n" +
            "ORDER BY BucketIdx";
        return new LokiSql(sql, context.Parameters);
    }

    /// <summary>Bytes and entries per combination of <paramref name="dimensions"/>, biggest first (<c>index/volume</c>).</summary>
    public static LokiSql Volume(LokiSqlContext context, IReadOnlyList<string> dimensions, int limit)
    {
        context.Parameters.AddParameter("lkVolumeLimit", (uint)Math.Clamp(limit, 1, VolumeMaxLimit));
        var select = dimensions.Select((d, i) => $"{context.LabelSql(d)} AS d{i}").Append("toUInt64(sum(length(Body))) AS Bytes").Append("toUInt64(count()) AS Entries");
        var group = dimensions.Count == 0 ? string.Empty : $"\nGROUP BY {string.Join(", ", dimensions.Select((_, i) => $"d{i}"))}";
        var sql = $"{context.WithSql}SELECT {string.Join(", ", select)}\nFROM logs\nWHERE {context.WhereSql}{group}\nORDER BY Bytes DESC\nLIMIT {{lkVolumeLimit:UInt32}}";
        return new LokiSql(sql, context.Parameters);
    }

    public const int VolumeMaxLimit = 5000;

    public const int DefaultPatternLimit = 100;

    /// <summary>
    /// Drain pattern clusters over time (<c>patterns</c>): per (pattern, level, bucket) the line count, for the
    /// <paramref name="topN"/> busiest patterns. The template is the latest one seen in the bucket, as Drain
    /// generalises a template as more lines arrive (ADR-0007).
    /// </summary>
    public static LokiSql Patterns(LokiSqlContext context, long originMs, long stepMs, int topN)
    {
        context.Parameters.AddParameter("lkOrigin", originMs);
        context.Parameters.AddParameter("lkBucketMs", stepMs);
        context.Parameters.AddParameter("lkTopN", (uint)Math.Clamp(topN, 1, 1000));
        var where = $"{context.WhereSql} AND PatternId != ''";
        var sql = $"{context.WithSql}SELECT PatternId, {LokiLabelResolver.LevelSql} AS Lvl, argMax(PatternTemplate, Timestamp) AS T, " +
            "intDiv(toUnixTimestamp64Milli(Timestamp) - {lkOrigin:Int64}, {lkBucketMs:Int64}) AS B, toUInt64(count()) AS C\n" +
            $"FROM logs\nWHERE {where} AND PatternId IN (SELECT PatternId FROM logs WHERE {where} GROUP BY PatternId ORDER BY count() DESC LIMIT {{lkTopN:UInt32}})\n" +
            "GROUP BY PatternId, Lvl, B\nORDER BY B";
        return new LokiSql(sql, context.Parameters);
    }

    /// <summary>The number of distinct values of each of <paramref name="labels"/> (<c>detected_labels</c>), in one pass.</summary>
    public static LokiSql Cardinalities(LokiSqlContext context, IReadOnlyList<string> labels)
    {
        var select = string.Join(", ", labels.Select(l => $"toUInt64(uniq({context.LabelSql(l)}))"));
        return new LokiSql($"{context.WithSql}SELECT {select}\nFROM logs\nWHERE {context.WhereSql}", context.Parameters);
    }
}
