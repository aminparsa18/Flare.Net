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
        Assert.Equal(expected, PromotedAttributeColumns.ColumnNameFor(PromotedAttributeTable.Logs, bag, key));
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
        Assert.True(PromotedAttributeColumns.TryParseExpression(PromotedAttributeTable.Logs, expression, out var parsedBag, out var parsedKey));
        Assert.Equal(bag, parsedBag);
        Assert.Equal(key, parsedKey);
    }

    [Theory]
    [InlineData("lower(LogAttributes['http.route'])")]
    [InlineData("SpanAttributes['http.route']")]
    [InlineData("LogAttributes['a'] || LogAttributes['b']")]
    public void TryParseExpression_IgnoresAnythingElse(string expression)
    {
        Assert.False(PromotedAttributeColumns.TryParseExpression(PromotedAttributeTable.Logs, expression, out _, out _));
    }

    [Fact]
    public void PromoteStatements_SingleNode_AddsColumnAndIndexTogether_ThenBackfills()
    {
        var statements = PromotedAttributeColumns.PromoteStatements(PromotedAttributeTable.Logs, AttributeBag.Log, "http.route", clusterMode: false, backfill: true);

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
        var statements = PromotedAttributeColumns.PromoteStatements(PromotedAttributeTable.Logs, AttributeBag.Log, "http.route", clusterMode: false, backfill: false);

        Assert.Single(statements);
    }

    [Fact]
    public void PromoteStatements_ClusterMode_AltersLocalTableFirst_ThenDistributed()
    {
        var statements = PromotedAttributeColumns.PromoteStatements(PromotedAttributeTable.Logs, AttributeBag.Resource, "k8s.pod.name", clusterMode: true, backfill: true);

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
            PromotedAttributeColumns.PromoteStatements(PromotedAttributeTable.Logs, AttributeBag.Log, "x'] FROM", clusterMode: false, backfill: false));
    }

    [Fact]
    public void DemoteStatements_DropIndexBeforeColumn_DistributedFirstInClusterMode()
    {
        Assert.Equal(
        [
            "ALTER TABLE logs DROP INDEX IF EXISTS idx_attr_log_http_route",
            "ALTER TABLE logs DROP COLUMN IF EXISTS attr_log_http_route",
        ], PromotedAttributeColumns.DemoteStatements(PromotedAttributeTable.Logs, "attr_log_http_route", clusterMode: false));

        Assert.Equal(
        [
            "ALTER TABLE logs ON CLUSTER 'flare_cluster' DROP COLUMN IF EXISTS attr_log_http_route",
            "ALTER TABLE logs_local ON CLUSTER 'flare_cluster' DROP INDEX IF EXISTS idx_attr_log_http_route",
            "ALTER TABLE logs_local ON CLUSTER 'flare_cluster' DROP COLUMN IF EXISTS attr_log_http_route",
        ], PromotedAttributeColumns.DemoteStatements(PromotedAttributeTable.Logs, "attr_log_http_route", clusterMode: true));
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

    // ---- spans (ADR-0063) ----

    private static readonly PromotedAttributeColumns PromotedSpans = new([
        (AttributeBag.Log, "http.route", "attr_span_http_route"),
        (AttributeBag.Resource, "k8s.namespace.name", "attr_res_k8s_namespace_name"),
    ]);

    private static SpanFilterSql BuildSpansWith(SpanAttributeFilter filter) =>
        SpanFilterSqlBuilder.Build(new SpanFilter { Attributes = [filter] }, Now, PromotedSpans);

    [Theory]
    [InlineData(AttributeBag.Log, "http.route", "attr_span_http_route")]
    [InlineData(AttributeBag.Resource, "k8s.namespace.name", "attr_res_k8s_namespace_name")]
    [InlineData(AttributeBag.Scope, "lib", "attr_scope_lib")]
    public void ColumnNameFor_Spans_UsesSpanPrefixForOwnBag(AttributeBag bag, string key, string expected)
    {
        Assert.Equal(expected, PromotedAttributeColumns.ColumnNameFor(PromotedAttributeTable.Spans, bag, key));
    }

    [Theory]
    [InlineData(PromotedAttributeTable.Spans, "SpanAttributes['http.route']", true)]
    [InlineData(PromotedAttributeTable.Spans, "ResourceAttributes['k8s.pod.name']", true)]
    [InlineData(PromotedAttributeTable.Spans, "LogAttributes['http.route']", false)]
    [InlineData(PromotedAttributeTable.Logs, "SpanAttributes['http.route']", false)]
    public void TryParseExpression_OnlyAcceptsTheTablesOwnMap(PromotedAttributeTable table, string expression, bool expected)
    {
        Assert.Equal(expected, PromotedAttributeColumns.TryParseExpression(table, expression, out _, out _));
    }

    [Fact]
    public void TryParseExpression_Spans_MapsSpanAttributesToLogBag()
    {
        Assert.True(PromotedAttributeColumns.TryParseExpression(PromotedAttributeTable.Spans, "SpanAttributes['http.route']", out var bag, out var key));
        Assert.Equal(AttributeBag.Log, bag);
        Assert.Equal("http.route", key);
    }

    [Fact]
    public void PromoteStatements_Spans_SingleNode_TargetsSpansTable()
    {
        var statements = PromotedAttributeColumns.PromoteStatements(PromotedAttributeTable.Spans, AttributeBag.Log, "http.route", clusterMode: false, backfill: true);

        Assert.Equal(
        [
            "ALTER TABLE spans ADD COLUMN IF NOT EXISTS attr_span_http_route String MATERIALIZED SpanAttributes['http.route'] CODEC(ZSTD(1)), " +
            "ADD INDEX IF NOT EXISTS idx_attr_span_http_route attr_span_http_route TYPE bloom_filter(0.01) GRANULARITY 1",
            "ALTER TABLE spans MATERIALIZE COLUMN attr_span_http_route",
            "ALTER TABLE spans MATERIALIZE INDEX idx_attr_span_http_route",
        ], statements);
    }

    [Fact]
    public void PromoteStatements_Spans_ClusterMode_AltersSpansLocalThenDistributed()
    {
        var statements = PromotedAttributeColumns.PromoteStatements(PromotedAttributeTable.Spans, AttributeBag.Resource, "k8s.pod.name", clusterMode: true, backfill: false);

        Assert.Equal(2, statements.Count);
        Assert.StartsWith("ALTER TABLE spans_local ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS attr_res_k8s_pod_name String MATERIALIZED ResourceAttributes['k8s.pod.name']", statements[0]);
        Assert.StartsWith("ALTER TABLE spans ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS attr_res_k8s_pod_name", statements[1]);
    }

    [Fact]
    public void DemoteStatements_Spans_ClusterMode()
    {
        Assert.Equal(
        [
            "ALTER TABLE spans ON CLUSTER 'flare_cluster' DROP COLUMN IF EXISTS attr_span_http_route",
            "ALTER TABLE spans_local ON CLUSTER 'flare_cluster' DROP INDEX IF EXISTS idx_attr_span_http_route",
            "ALTER TABLE spans_local ON CLUSTER 'flare_cluster' DROP COLUMN IF EXISTS attr_span_http_route",
        ], PromotedAttributeColumns.DemoteStatements(PromotedAttributeTable.Spans, "attr_span_http_route", clusterMode: true));
    }

    [Fact]
    public void BuildSpans_Equals_OnPromotedKey_ReadsColumnInsteadOfMap()
    {
        var result = BuildSpansWith(new SpanAttributeFilter { Key = "http.route", Value = "/orders" });

        Assert.Contains("attr_span_http_route = {attrValue0:String}", result.WhereSql);
        Assert.DoesNotContain("SpanAttributes", result.WhereSql);
        Assert.False(result.Parameters.ToDictionary().ContainsKey("attrKey0"));
    }

    [Fact]
    public void BuildSpans_PromotionIsPerBag()
    {
        var result = BuildSpansWith(new SpanAttributeFilter { Key = "http.route", Value = "/orders", Bag = SpanAttributeBag.Resource });

        Assert.Contains("ResourceAttributes[{attrKey0:String}] = {attrValue0:String}", result.WhereSql);
    }

    [Fact]
    public void BuildSpans_NotEquals_NonEmptyValue_DropsGuard()
    {
        var result = BuildSpansWith(new SpanAttributeFilter { Key = "http.route", Value = "/health", Operator = SpanAttributeFilterOperator.NotEquals });

        Assert.Contains("attr_span_http_route != {attrValue0:String}", result.WhereSql);
        Assert.DoesNotContain("mapContains", result.WhereSql);
    }

    [Fact]
    public void BuildSpans_NotEquals_EmptyValue_FallsBackToGuardedMapForm()
    {
        var result = BuildSpansWith(new SpanAttributeFilter { Key = "http.route", Value = "", Operator = SpanAttributeFilterOperator.NotEquals });

        Assert.Contains("NOT (mapContains(SpanAttributes, {attrKey0:String})", result.WhereSql);
    }

    [Theory]
    [InlineData(SpanAttributeFilterOperator.In, "attr_res_k8s_namespace_name IN {attrValues0:Array(String)}")]
    [InlineData(SpanAttributeFilterOperator.NotIn, "attr_res_k8s_namespace_name NOT IN {attrValues0:Array(String)}")]
    public void BuildSpans_InNotIn_WithoutEmptyValue_ReadsColumn(SpanAttributeFilterOperator op, string expected)
    {
        var result = BuildSpansWith(new SpanAttributeFilter { Key = "k8s.namespace.name", Value = "", Operator = op, Values = ["prod", "staging"], Bag = SpanAttributeBag.Resource });

        Assert.Contains(expected, result.WhereSql);
        Assert.DoesNotContain("mapContains", result.WhereSql);
    }

    [Theory]
    [InlineData(SpanAttributeFilterOperator.Exists)]
    [InlineData(SpanAttributeFilterOperator.Absent)]
    [InlineData(SpanAttributeFilterOperator.Regex)]
    [InlineData(SpanAttributeFilterOperator.NotRegex)]
    public void BuildSpans_PresenceSensitiveOperators_KeepMapForm(SpanAttributeFilterOperator op)
    {
        var result = BuildSpansWith(new SpanAttributeFilter { Key = "http.route", Value = ".*", Operator = op });

        Assert.Contains("mapContains(SpanAttributes, {attrKey0:String})", result.WhereSql);
        Assert.DoesNotContain("attr_span_http_route", result.WhereSql);
    }
}
