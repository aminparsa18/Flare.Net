using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class PromotedAttributeColumnsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    private static readonly PromotedAttributeColumns Promoted = new([
        (AttributeBag.Log, "http.route", "attr_log_http_route"),
        (AttributeBag.Resource, "k8s.namespace.name", "attr_res_k8s_namespace_name"),
    ]);

    private static LogFilterSql BuildWith(AttributeFilter filter) =>
        LogFilterSqlBuilder.Build(new LogFilter { Attributes = [filter] }, Now, Promoted);

    [Theory]
    [InlineData(AttributeBag.Log, "http.route", "attr_log_http_route")]
    [InlineData(AttributeBag.Resource, "k8s.namespace.name", "attr_res_k8s_namespace_name")]
    [InlineData(AttributeBag.Scope, "my-lib/version:1@x", "attr_scope_my_lib_version_1_x")]
    public void ColumnNameFor_PrefixesBag_AndReplacesNonIdentifierCharacters(AttributeBag bag, string key, string expected)
    {
        Assert.Equal(expected, PromotedAttributeColumns.ColumnNameFor(bag, key));
    }

    [Theory]
    [InlineData("http.route", true)]
    [InlineData("service.instance.id", true)]
    [InlineData("a/b:c-d@e_f", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("it's", false)]
    [InlineData(@"back\slash", false)]
    [InlineData("has space", false)]
    [InlineData("x']; DROP TABLE logs; --", false)]
    public void IsValidKey_AdmitsOnlyLiteralSafeCharacters(string? key, bool expected)
    {
        Assert.Equal(expected, PromotedAttributeColumns.IsValidKey(key));
    }

    [Fact]
    public void IsValidKey_RejectsOverlongKeys()
    {
        Assert.False(PromotedAttributeColumns.IsValidKey(new string('a', PromotedAttributeColumns.MaxKeyLength + 1)));
    }

    [Theory]
    [InlineData("LogAttributes['http.route']", AttributeBag.Log, "http.route")]
    [InlineData("ResourceAttributes['k8s.pod.name']", AttributeBag.Resource, "k8s.pod.name")]
    [InlineData("ScopeAttributes['lib']", AttributeBag.Scope, "lib")]
    public void TryParseExpression_ReversesThePromotionExpression(string expression, AttributeBag bag, string key)
    {
        Assert.True(PromotedAttributeColumns.TryParseExpression(expression, out var parsedBag, out var parsedKey));
        Assert.Equal(bag, parsedBag);
        Assert.Equal(key, parsedKey);
    }

    [Theory]
    [InlineData("lower(LogAttributes['http.route'])")]
    [InlineData("SpanAttributes['http.route']")]
    [InlineData("LogAttributes['a'] || LogAttributes['b']")]
    public void TryParseExpression_IgnoresAnythingElse(string expression)
    {
        Assert.False(PromotedAttributeColumns.TryParseExpression(expression, out _, out _));
    }

    [Fact]
    public void PromoteStatements_SingleNode_AddsColumnAndIndexTogether_ThenBackfills()
    {
        var statements = PromotedAttributeColumns.PromoteStatements(AttributeBag.Log, "http.route", clusterMode: false, backfill: true);

        Assert.Equal(
        [
            "ALTER TABLE logs ADD COLUMN IF NOT EXISTS attr_log_http_route String MATERIALIZED LogAttributes['http.route'] CODEC(ZSTD(1)), " +
            "ADD INDEX IF NOT EXISTS idx_attr_log_http_route attr_log_http_route TYPE bloom_filter(0.01) GRANULARITY 1",
            "ALTER TABLE logs MATERIALIZE COLUMN attr_log_http_route",
            "ALTER TABLE logs MATERIALIZE INDEX idx_attr_log_http_route",
        ], statements);
    }

    [Fact]
    public void PromoteStatements_WithoutBackfill_SkipsMutations()
    {
        var statements = PromotedAttributeColumns.PromoteStatements(AttributeBag.Log, "http.route", clusterMode: false, backfill: false);

        Assert.Single(statements);
    }

    [Fact]
    public void PromoteStatements_ClusterMode_AltersLocalTableFirst_ThenDistributed()
    {
        var statements = PromotedAttributeColumns.PromoteStatements(AttributeBag.Resource, "k8s.pod.name", clusterMode: true, backfill: true);

        Assert.Equal(4, statements.Count);
        Assert.StartsWith("ALTER TABLE logs_local ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS attr_res_k8s_pod_name String MATERIALIZED ResourceAttributes['k8s.pod.name']", statements[0]);
        Assert.Contains("ADD INDEX IF NOT EXISTS idx_attr_res_k8s_pod_name", statements[0]);
        Assert.StartsWith("ALTER TABLE logs ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS attr_res_k8s_pod_name", statements[1]);
        Assert.DoesNotContain("INDEX", statements[1]);
        Assert.Equal("ALTER TABLE logs_local ON CLUSTER 'flare_cluster' MATERIALIZE COLUMN attr_res_k8s_pod_name", statements[2]);
        Assert.Equal("ALTER TABLE logs_local ON CLUSTER 'flare_cluster' MATERIALIZE INDEX idx_attr_res_k8s_pod_name", statements[3]);
    }

    [Fact]
    public void PromoteStatements_RejectsUnsafeKey()
    {
        Assert.Throws<ArgumentException>(() =>
            PromotedAttributeColumns.PromoteStatements(AttributeBag.Log, "x'] FROM", clusterMode: false, backfill: false));
    }

    [Fact]
    public void DemoteStatements_DropIndexBeforeColumn_DistributedFirstInClusterMode()
    {
        Assert.Equal(
        [
            "ALTER TABLE logs DROP INDEX IF EXISTS idx_attr_log_http_route",
            "ALTER TABLE logs DROP COLUMN IF EXISTS attr_log_http_route",
        ], PromotedAttributeColumns.DemoteStatements("attr_log_http_route", clusterMode: false));

        Assert.Equal(
        [
            "ALTER TABLE logs ON CLUSTER 'flare_cluster' DROP COLUMN IF EXISTS attr_log_http_route",
            "ALTER TABLE logs_local ON CLUSTER 'flare_cluster' DROP INDEX IF EXISTS idx_attr_log_http_route",
            "ALTER TABLE logs_local ON CLUSTER 'flare_cluster' DROP COLUMN IF EXISTS attr_log_http_route",
        ], PromotedAttributeColumns.DemoteStatements("attr_log_http_route", clusterMode: true));
    }

    [Fact]
    public void Build_Equals_OnPromotedKey_ReadsColumnInsteadOfMap()
    {
        var result = BuildWith(new AttributeFilter { Key = "http.route", Value = "/orders" });

        Assert.Contains("attr_log_http_route = {attrValue0:String}", result.WhereSql);
        Assert.DoesNotContain("LogAttributes", result.WhereSql);
        Assert.False(result.Parameters.ToDictionary().ContainsKey("attrKey0"));
    }

    [Fact]
    public void Build_PromotionIsPerBag()
    {
        var result = BuildWith(new AttributeFilter { Key = "http.route", Value = "/orders", Bag = AttributeBag.Resource });

        Assert.Contains("ResourceAttributes[{attrKey0:String}] = {attrValue0:String}", result.WhereSql);
    }

    [Fact]
    public void Build_UnpromotedKey_KeepsMapLookup()
    {
        var result = BuildWith(new AttributeFilter { Key = "user.id", Value = "42" });

        Assert.Contains("LogAttributes[{attrKey0:String}] = {attrValue0:String}", result.WhereSql);
    }

    [Fact]
    public void Build_NotEquals_NonEmptyValue_DropsGuard()
    {
        var result = BuildWith(new AttributeFilter { Key = "http.route", Value = "/health", Operator = AttributeFilterOperator.NotEquals });

        Assert.Contains("attr_log_http_route != {attrValue0:String}", result.WhereSql);
        Assert.DoesNotContain("mapContains", result.WhereSql);
    }

    [Fact]
    public void Build_NotEquals_EmptyValue_FallsBackToGuardedMapForm()
    {
        var result = BuildWith(new AttributeFilter { Key = "http.route", Value = "", Operator = AttributeFilterOperator.NotEquals });

        Assert.Contains("NOT (mapContains(LogAttributes, {attrKey0:String})", result.WhereSql);
    }

    [Theory]
    [InlineData(AttributeFilterOperator.In, "attr_res_k8s_namespace_name IN {attrValues0:Array(String)}")]
    [InlineData(AttributeFilterOperator.NotIn, "attr_res_k8s_namespace_name NOT IN {attrValues0:Array(String)}")]
    public void Build_InNotIn_WithoutEmptyValue_ReadsColumn(AttributeFilterOperator op, string expected)
    {
        var result = BuildWith(new AttributeFilter { Key = "k8s.namespace.name", Value = "", Operator = op, Values = ["prod", "staging"], Bag = AttributeBag.Resource });

        Assert.Contains(expected, result.WhereSql);
        Assert.DoesNotContain("mapContains", result.WhereSql);
    }

    [Fact]
    public void Build_In_WithEmptyValue_FallsBackToGuardedMapForm()
    {
        var result = BuildWith(new AttributeFilter { Key = "http.route", Value = "", Operator = AttributeFilterOperator.In, Values = ["/a", ""] });

        Assert.Contains("mapContains(LogAttributes, {attrKey0:String})", result.WhereSql);
    }

    [Theory]
    [InlineData(AttributeFilterOperator.Exists)]
    [InlineData(AttributeFilterOperator.Absent)]
    [InlineData(AttributeFilterOperator.Regex)]
    [InlineData(AttributeFilterOperator.NotRegex)]
    public void Build_PresenceSensitiveOperators_KeepMapForm(AttributeFilterOperator op)
    {
        var result = BuildWith(new AttributeFilter { Key = "http.route", Value = ".*", Operator = op });

        Assert.Contains("mapContains(LogAttributes, {attrKey0:String})", result.WhereSql);
        Assert.DoesNotContain("attr_log_http_route", result.WhereSql);
    }
}
