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
        Assert.True(context.JsonSeen);
    }

    [Fact]
    public void Json_LevelFallsBackToSeverity()
    {
        var context = Context("""{service_name="api"} | json | level="error" """);

        Assert.Contains("if(JSONHas(Body, ", context.WhereSql);
        Assert.Contains(LokiLabelResolver.LevelSql, context.WhereSql);
    }

    [Fact]
    public void Json_ExplicitExpressionUsesThePath()
    {
        var context = Context("""{service_name="api"} | json uid="user.id" | uid="42" """);

        var values = context.Parameters.OfType<ClickHouseDbParameter>().Select(p => p.Value).ToList();
        Assert.Contains("user", values);
        Assert.Contains("id", values);
        Assert.Equal(new Dictionary<string, string> { ["uid"] = "user.id" }, context.Json!.Expressions);
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
        Assert.Equal(LokiLabelKind.LevelOrJson, resolver.Resolve("level", afterJson: true).Kind);
        Assert.Equal(LokiLabelKind.ResourceAttribute, resolver.Resolve("env").Kind);
        Assert.Equal("http.method", resolver.Resolve("http_method").Key);
        Assert.Equal(LokiLabelKind.LogAttribute, resolver.Resolve("never_seen").Kind);
        Assert.Equal(LokiLabelKind.JsonField, resolver.Resolve("never_seen", afterJson: true).Kind);
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
}
