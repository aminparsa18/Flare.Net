using ClickHouse.Driver.ADO.Parameters;
using Flare.Api.Loki;
using Xunit;

namespace Flare.Api.Tests.Loki;

public class LokiSqlTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private static LokiLabelResolver Resolver() => new(
        ["service.name", "k8s.pod.name", "host.name", "http.route"],
        ["http.method", "user.id"]);

    private static LokiSqlContext Context(string query, LokiLabelResolver? resolver = null)
    {
        var context = new LokiSqlContext(resolver ?? Resolver(), Now.AddHours(-1), Now, Now, promoted: null);
        context.AddSelector(LokiQlParser.ParseSelector(query));
        return context;
    }

    /// <summary>A context for a pipeline ending in <c>| unwrap</c>, which only parses inside a range function.</summary>
    private static LokiSqlContext UnwrapContext(string selector)
    {
        var range = Assert.IsType<LokiRangeAggregation>(LokiQlParser.Parse($"sum_over_time({selector} [1m])"));
        var context = new LokiSqlContext(Resolver(), Now.AddHours(-1), Now, Now, promoted: null);
        context.AddSelector(range.Selector);
        return context;
    }

    private static object? Param(LokiSqlContext context, string name) =>
        context.Parameters.OfType<ClickHouseDbParameter>().Single(p => p.ParameterName == name).Value;

    [Fact]
    public void Matchers_BecomeParameterizedClauses()
    {
        var context = Context("""{service_name="api", k8s_pod_name=~"web-.*", detected_level!="debug"}""");

        Assert.Contains("ServiceName = {lk0:String}", context.WhereSql);
        Assert.Equal("api", Param(context, "lk0"));
        Assert.Contains("match(ResourceAttributes[{lk1:String}], {lk2:String})", context.WhereSql);
        Assert.Equal("k8s.pod.name", Param(context, "lk1"));
        // Label regexes are anchored, as in Loki and Prometheus.
        Assert.Equal("^(?:web-.*)$", Param(context, "lk2"));
        Assert.Contains($"{LokiLabelResolver.LevelSql} != {{lk3:String}}", context.WhereSql);
    }

    [Fact]
    public void UserTextIsNeverInterpolated()
    {
        var context = Context("""{service_name="a'; DROP TABLE logs; --"} |= "x'y" """);

        Assert.DoesNotContain("DROP", context.WhereSql);
        Assert.DoesNotContain("x'y", context.WhereSql);
    }

    [Fact]
    public void LineFilters_ContainsRegexAndNegation()
    {
        var context = Context("""{service_name="api"} |= "timeout" != "health" |~ "5\\d\\d" !~ "ping" """);

        Assert.Contains("position(Body, ", context.WhereSql);
        Assert.Contains("> 0", context.WhereSql);
        Assert.Contains("NOT (", context.WhereSql);
        Assert.Contains("match(Body, ", context.WhereSql);
        Assert.Contains("NOT match(Body, ", context.WhereSql);
        // |~ is a search anywhere in the line: the pattern is not anchored.
        Assert.Contains(context.Parameters.OfType<ClickHouseDbParameter>(), p => p.Value is "5\\d\\d");
    }

    [Fact]
    public void LineFilter_AlternativesAreOred()
    {
        var context = Context("""{service_name="api"} |= "a" or "b" """);

        Assert.Contains(" OR ", context.WhereSql);
    }

    [Fact]
    public void EmptyContainsFilter_MatchesEverything()
    {
        var context = Context("""{service_name="api"} |= "" """);

        Assert.DoesNotContain("position(", context.WhereSql);
    }

    [Fact]
    public void LabelFilter_NumericComparisonUsesFloat()
    {
        var context = Context("""{service_name="api"} | http_route != "" | user_id > 5""");

        Assert.Contains("ifNull(toFloat64OrNull(LogAttributes[{lk", context.WhereSql);
        Assert.Contains("> {lk", context.WhereSql);
        Assert.Contains(":Float64}", context.WhereSql);
    }

    [Fact]
    public void LabelFilter_ResolvesAttributeBags()
    {
        var context = Context("""{service_name="api"} | http_method="GET" | host_name="h1" """);

        Assert.Contains("LogAttributes[{", context.WhereSql);
        Assert.Contains("ResourceAttributes[{", context.WhereSql);
    }

    [Fact]
    public void Json_UnknownLabelsAreBodyFields()
    {
        var context = Context("""{service_name="api"} | json | status="500" """);

        Assert.Contains("JSONExtractString(Body", context.WhereSql);
        Assert.Equal(LokiParserKind.Json, Assert.Single(context.Parsers).Kind);
    }

    [Fact]
    public void Json_LevelFallsBackToSeverity()
    {
        var context = Context("""{service_name="api"} | json | level="error" """);

        Assert.Contains("multiIf(JSONHas(Body, ", context.WhereSql);
        Assert.Contains(LokiLabelResolver.LevelSql, context.WhereSql);
    }

    [Fact]
    public void Json_ExplicitExpressionUsesThePath()
    {
        var context = Context("""{service_name="api"} | json uid="user.id" | uid="42" """);

        var values = context.Parameters.OfType<ClickHouseDbParameter>().Select(p => p.Value).ToList();
        Assert.Contains("user", values);
        Assert.Contains("id", values);
        Assert.Equal(new Dictionary<string, string> { ["uid"] = "user.id" }, Assert.Single(context.Parsers).Expressions);
    }

    [Fact]
    public void Json_FlattenedNameTriesEverySplit()
    {
        Assert.Equal(["a_b_c", "a.b_c", "a_b.c", "a.b.c"], LokiSqlContext.FlattenedCandidates("a_b_c"));
        Assert.Equal(["status"], LokiSqlContext.FlattenedCandidates("status"));
        Assert.Equal(["a_b_c_d_e_f_g"], LokiSqlContext.FlattenedCandidates("a_b_c_d_e_f_g"));
    }

    [Fact]
    public void ParseError_IsEmptyWithoutJson_AndAnErrorOnNonJsonWithIt()
    {
        var plain = Context("""{service_name="api"} | __error__="" """);
        var json = Context("""{service_name="api"} | json | __error__="" """);

        Assert.Contains("'' = {", plain.WhereSql);
        Assert.Contains("isValidJSON(Body)", json.WhereSql);
    }

    [Fact]
    public void ScopedRequests_KeepTheServiceScopeClause()
    {
        Flare.Api.Query.ServiceScope.Current = ["payments*"];
        try
        {
            var context = Context("""{service_name=~".+"}""");

            Assert.Contains("startsWith(ServiceName", context.WhereSql);
        }
        finally
        {
            Flare.Api.Query.ServiceScope.Current = null;
        }
    }

    [Fact]
    public void LogLines_NewestFirstByDefault_AndCapped()
    {
        var context = Context("""{service_name="api"}""");

        var backward = LokiQueries.LogLines(context, 999_999, forward: false);

        Assert.Contains("ORDER BY Timestamp DESC, EventId DESC", backward.Sql);
        Assert.Equal((uint)LokiQueries.MaxLimit, Param(context, "lkLimit"));
        Assert.Contains("ORDER BY Timestamp ASC", LokiQueries.LogLines(Context("""{service_name="api"}"""), 10, forward: true).Sql);
    }

    [Fact]
    public void Buckets_AlignToTheOriginAndGroupByDimensions()
    {
        var context = Context("""{service_name="api"}""");

        var built = LokiQueries.Buckets(context, ["detected_level", "k8s_pod_name"], bytes: false, originMs: 1000, bucketMs: 15_000);

        Assert.Contains("intDiv(toUnixTimestamp64Milli(Timestamp) - {lkOrigin:Int64}, {lkBucketMs:Int64}) AS BucketIdx", built.Sql);
        Assert.Contains("GROUP BY BucketIdx, d0, d1", built.Sql);
        Assert.Contains("toFloat64(count()) AS V", built.Sql);
        Assert.Equal(15_000L, Param(context, "lkBucketMs"));
    }

    [Fact]
    public void Buckets_BytesAndInstant()
    {
        var built = LokiQueries.Buckets(Context("""{service_name="api"}"""), [], bytes: true, originMs: 0, bucketMs: 0);

        Assert.Contains("sum(length(Body))", built.Sql);
        Assert.DoesNotContain("GROUP BY", built.Sql);
    }

    [Fact]
    public void Resolver_PrefersFixedNamesThenResourceThenLog()
    {
        var resolver = new LokiLabelResolver(["env", "region"], ["env", "http.method"]);

        Assert.Equal(LokiLabelKind.Service, resolver.Resolve("service_name").Kind);
        Assert.Equal(LokiLabelKind.Level, resolver.Resolve("level").Kind);
        Assert.Equal(LokiLabelKind.LevelOrParsed, resolver.Resolve("level", LokiParserKinds.Json).Kind);
        Assert.Equal(LokiLabelKind.LevelOrParsed, resolver.Resolve("level", LokiParserKinds.Logfmt).Kind);
        Assert.Equal(LokiLabelKind.ResourceAttribute, resolver.Resolve("env").Kind);
        Assert.Equal("http.method", resolver.Resolve("http_method").Key);
        Assert.Equal(LokiLabelKind.LogAttribute, resolver.Resolve("never_seen").Kind);
        Assert.Equal(LokiLabelKind.ParsedField, resolver.Resolve("never_seen", LokiParserKinds.Logfmt).Kind);
    }

    [Fact]
    public void Resolver_StreamLabelsAreServiceAndIndexedResourceKeys()
    {
        var resolver = new LokiLabelResolver(["service.name", "k8s.pod.name", "host.name", "deployment.environment"], []);

        Assert.Equal(["service_name", "deployment_environment", "k8s_pod_name"], resolver.StreamLabelNames());
    }

    [Theory]
    [InlineData(0, "unknown")]
    [InlineData(1, "trace")]
    [InlineData(9, "info")]
    [InlineData(13, "warn")]
    [InlineData(17, "error")]
    [InlineData(21, "fatal")]
    public void LevelName_BucketsTheSeverityNumber(byte severity, string expected) =>
        Assert.Equal(expected, LokiLabelResolver.LevelName(severity));

    // ---- logfmt, mixed parsers, pattern filters, line_format, unwrap (ADR-0237) ----

    [Fact]
    public void Logfmt_LabelsComeFromTheLkvMap_AndTheQueryDefinesIt()
    {
        var context = Context("""{service_name="api"} | logfmt | status="500" """);

        Assert.Contains("lkv[{lk", context.WhereSql);
        Assert.StartsWith("WITH extractAllGroups(", context.WithSql);
        Assert.Contains("AS lkv", context.WithSql);
        Assert.Equal(LokiParserKind.Logfmt, Assert.Single(context.Parsers).Kind);
    }

    [Fact]
    public void NoLogfmt_NoWithClause()
    {
        Assert.Equal(string.Empty, Context("""{service_name="api"} | json | status="500" """).WithSql);
    }

    [Fact]
    public void Logfmt_ExplicitExpressionReadsTheNamedKey()
    {
        var context = Context("""{service_name="api"} | logfmt code="http.status" | code="500" """);

        var values = context.Parameters.OfType<ClickHouseDbParameter>().Select(p => p.Value).ToList();
        // Keys are sanitised like the labels of a bare logfmt, so the expression's key is looked up sanitised.
        Assert.Contains("http_status", values);
        Assert.Contains("500", values);
    }

    [Fact]
    public void Logfmt_OnlyTheMappedLabelsExist()
    {
        // `other` is not mapped by the explicit expression, so it can only be an attribute and never reads the map.
        var context = Context("""{service_name="api"} | logfmt code="status" | other="x" """);

        Assert.Contains("'' = {lk1:String}", context.WhereSql);
        Assert.DoesNotContain("lkv[", context.WhereSql);
    }

    [Fact]
    public void MixedJsonAndLogfmt_ChainsBothWithLatestStageFirst()
    {
        var context = Context("""{service_name="api"} | json | logfmt | drop __error__, __error_details__ | status="500" """);

        Assert.Equal([LokiParserKind.Json, LokiParserKind.Logfmt], context.Parsers.Select(p => p.Kind));
        var where = context.WhereSql;
        var logfmt = where.IndexOf("mapContains(lkv", StringComparison.Ordinal);
        var json = where.IndexOf("JSONHas(Body", StringComparison.Ordinal);
        Assert.True(logfmt >= 0 && json > logfmt, where);
        Assert.Contains("multiIf(", where);
    }

    [Fact]
    public void MixedParsers_LevelFallsBackToSeverityAfterBoth()
    {
        var context = Context("""{service_name="api"} | json | logfmt | level="error" """);

        Assert.Contains("mapContains(lkv", context.WhereSql);
        Assert.Contains("JSONHas(Body", context.WhereSql);
        // With neither parser finding a level, the severity-derived level is the fallback.
        Assert.Contains($", {LokiLabelResolver.LevelSql}) = ", context.WhereSql);
    }

    [Fact]
    public void ErrorLabel_IsAJsonParseErrorOnlyWhenJsonRan()
    {
        Assert.Contains("JSONParserErr", Context("""{service_name="api"} | json | __error__="" """).WhereSql);
        Assert.DoesNotContain("JSONParserErr", Context("""{service_name="api"} | logfmt | __error__="" """).WhereSql);
        Assert.Contains("JSONParserErr", Context("""{service_name="api"} | json | logfmt | __error_details__="" """).WhereSql);
    }

    [Fact]
    public void PatternFilter_IsAnchoredRegexMatchOnTheBody()
    {
        var context = Context("""{service_name="api"} |> "GET <_> 200" !> "<_> health <_>" """);

        Assert.Contains("match(Body, {lk", context.WhereSql);
        Assert.Contains("NOT match(Body, {lk", context.WhereSql);
        var values = context.Parameters.OfType<ClickHouseDbParameter>().Select(p => p.Value as string).ToList();
        Assert.Contains("(?s)^GET .*? 200", values);
        Assert.Contains("(?s).*? health .*?", values);
    }

    [Fact]
    public void LineFormat_IsNotPartOfTheSql_ButIsOnThePlan()
    {
        var context = Context("""{service_name="api"} | json | line_format "{{.msg}}" """);

        Assert.DoesNotContain("msg", context.WhereSql);
        Assert.NotNull(context.Plan.LineFormat);
        Assert.Single(context.Plan.Parsers);
    }

    [Fact]
    public void Unwrap_RequiresANumberAndDropsLinesWithout()
    {
        var context = UnwrapContext("""{service_name="api"} | json | unwrap latency""");

        Assert.StartsWith("toFloat64OrNull(", context.UnwrapSql);
        Assert.Contains($"{context.UnwrapSql} IS NOT NULL", context.WhereSql);
    }

    [Fact]
    public void Unwrap_DurationAndBytesConversionsEvaluateTheLabelOnce()
    {
        var duration = UnwrapContext("""{service_name="api"} | json | unwrap duration(latency)""").UnwrapSql!;
        var bytes = UnwrapContext("""{service_name="api"} | json | unwrap bytes(size)""").UnwrapSql!;

        Assert.Contains("arrayMap(s -> if(match(s, ", duration);
        Assert.Contains("ns|us|µs|ms|s|m|h", duration);
        Assert.EndsWith("])[1]", duration);
        Assert.Contains("'kib', 1024.", bytes);
        Assert.EndsWith("])[1]", bytes);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(duration, @"JSONHas|JSONType\(Body, \{").Select(m => m.Value).Distinct());
    }

    [Fact]
    public void UnwrapBuckets_SelectsTheMergeableStatisticsPerBucketAndLabel()
    {
        var context = UnwrapContext("""{service_name="api"} | json | unwrap latency""");

        var sql = LokiQueries.UnwrapBuckets(context, ["service_name"], originMs: 1_000, bucketMs: 60_000).Sql;

        Assert.Contains("intDiv(toUnixTimestamp64Milli(Timestamp) - {lkOrigin:Int64}, {lkBucketMs:Int64}) AS BucketIdx", sql);
        Assert.Contains("ServiceName AS d0", sql);
        Assert.Contains("ifNull(", sql);
        Assert.Contains("sum(V), min(V), max(V), argMin(V, Timestamp), argMax(V, Timestamp), avg(V), varPop(V) * count()", sql);
        Assert.Contains("GROUP BY BucketIdx, d0", sql);
    }

    [Fact]
    public void UnwrapBuckets_InstantQueryIsOneBucket()
    {
        var context = UnwrapContext("""{service_name="api"} | json | unwrap latency""");

        var sql = LokiQueries.UnwrapBuckets(context, [], originMs: 0, bucketMs: 0).Sql;

        Assert.Contains("toInt64(0) AS BucketIdx", sql);
        Assert.DoesNotContain("lkBucketMs", sql);
    }

    [Fact]
    public void UnwrapBuckets_WithLogfmtPutsTheWithClauseInsideTheSubquery()
    {
        var context = UnwrapContext("""{service_name="api"} | logfmt | unwrap latency""");

        var sql = LokiQueries.UnwrapBuckets(context, [], originMs: 0, bucketMs: 1000).Sql;

        Assert.StartsWith("SELECT ", sql);
        Assert.Contains("FROM (\nWITH extractAllGroups(", sql);
    }

    [Fact]
    public void UnwrapQuantiles_ExpandsEachRowToTheWindowsContainingIt()
    {
        var context = UnwrapContext("""{service_name="api"} | json | unwrap latency""");

        var built = LokiQueries.UnwrapQuantiles(context, ["service_name"], 0.95, firstStepMs: 600_000, stepMs: 60_000, stepCount: 10, rangeMs: 300_000);

        Assert.Contains("arrayJoin(range(", built.Sql);
        Assert.Contains("quantileExactInclusive({lkQuantile:Float64})(V)", built.Sql);
        Assert.Contains("GROUP BY StepIdx, d0", built.Sql);
        Assert.Equal(0.95, Param(context, "lkQuantile"));
        Assert.Equal(600_000L, Param(context, "lkFirst"));
        Assert.Equal(9L, Param(context, "lkLastStep"));
    }

    [Fact]
    public void Volume_GroupsByTheTargetLabelsBiggestFirst()
    {
        var context = Context("""{service_name=~".+"}""");

        var sql = LokiQueries.Volume(context, ["service_name"], 50).Sql;

        Assert.Contains("ServiceName AS d0", sql);
        Assert.Contains("sum(length(Body))", sql);
        Assert.Contains("GROUP BY d0", sql);
        Assert.Contains("ORDER BY Bytes DESC", sql);
        Assert.Equal(50u, Param(context, "lkVolumeLimit"));
    }

    [Fact]
    public void Volume_NoLabelsIsOneTotal()
    {
        var sql = LokiQueries.Volume(Context("""{service_name=~".+"}"""), [], 50).Sql;

        Assert.DoesNotContain("GROUP BY", sql);
    }

    [Fact]
    public void Patterns_AreBucketedPerLevelAndLimitedToTheBusiestClusters()
    {
        var context = Context("""{service_name="api"}""");

        var sql = LokiQueries.Patterns(context, originMs: 0, stepMs: 60_000, topN: 25).Sql;

        Assert.Contains("PatternId != ''", sql);
        Assert.Contains("argMax(PatternTemplate, Timestamp)", sql);
        Assert.Contains("GROUP BY PatternId, Lvl, B", sql);
        Assert.Contains("PatternId IN (SELECT PatternId FROM logs WHERE", sql);
        Assert.Contains("ORDER BY count() DESC LIMIT {lkTopN:UInt32}", sql);
        Assert.Equal(25u, Param(context, "lkTopN"));
    }

    [Fact]
    public void Cardinalities_AreOneUniqPerLabel()
    {
        var sql = LokiQueries.Cardinalities(Context("""{service_name="api"}"""), ["service_name", "k8s_pod_name"]).Sql;

        Assert.Contains("toUInt64(uniq(ServiceName))", sql);
        Assert.Contains("uniq(ResourceAttributes[{lk", sql);
    }

    [Fact]
    public void ImplicitParsers_LetAnUnknownFieldResolveAgainstBothParsers()
    {
        var context = Context("""{service_name="api"}""");
        context.AddParser(LokiParserKind.Json);
        context.AddParser(LokiParserKind.Logfmt);

        var sql = context.LabelSql("never_seen_field");

        Assert.Contains("mapContains(lkv", sql);
        Assert.Contains("JSONHas(Body", sql);
    }

    [Fact]
    public void JsonExpression_BracketPathBindsKeysAsStringsAndIndexesAsInt64()
    {
        var context = Context("""{service_name="api"} | json first="servers[1].name", m="[\"request\"][\"method\"]" | first="x" | m="GET" """);

        var all = context.Parameters.OfType<ClickHouseDbParameter>().ToList();
        Assert.Contains(all, p => p.Value is "servers");
        // ClickHouse counts array positions from 1; LogQL from 0.
        Assert.Contains(all, p => p.Value is 2L);
        Assert.Matches(@"\{lkj\d+_1:Int64\}", context.WhereSql);
        Assert.Contains(all, p => p.Value is "request");
    }
}
