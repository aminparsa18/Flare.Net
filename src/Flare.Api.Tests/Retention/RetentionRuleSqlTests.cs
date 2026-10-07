using Flare.Api.Retention;
using Xunit;

namespace Flare.Api.Tests.Retention;

public class RetentionRuleSqlTests
{
    private static RetentionRule Rule(string attribute, string value, int days) => new() { Attribute = attribute, Value = value, Days = days };

    [Fact]
    public void BuildDefaultExpression_NoRules_IsTheNumber()
    {
        Assert.Equal("30", RetentionRuleSql.BuildDefaultExpression([], 30));
    }

    [Fact]
    public void BuildDefaultExpression_ForeverIsWrittenAsTheLargeNumberNeverZero()
    {
        Assert.Equal("20000", RetentionRuleSql.BuildDefaultExpression([], 0));
        Assert.Equal(
            "multiIf(ResourceAttributes['env'] = 'dev', 20000, 30)",
            RetentionRuleSql.BuildDefaultExpression([Rule("env", "dev", 0)], 30));
    }

    [Fact]
    public void BuildDefaultExpression_RulesAreOrderedAndFirstMatchWins()
    {
        var sql = RetentionRuleSql.BuildDefaultExpression(
            [Rule("deployment.environment", "dev", 7), Rule("service.namespace", "billing", 365)], 30);
        Assert.Equal("multiIf(ResourceAttributes['deployment.environment'] = 'dev', 7, ResourceAttributes['service.namespace'] = 'billing', 365, 30)", sql);
    }

    [Fact]
    public void BuildDefaultExpression_EscapesQuotesAndBackslashesInValues()
    {
        var sql = RetentionRuleSql.BuildDefaultExpression([Rule("k", @"a'b\c", 7)], 30);
        Assert.Equal(@"multiIf(ResourceAttributes['k'] = 'a\'b\\c', 7, 30)", sql);
    }

    [Fact]
    public void ParseDefaultExpression_ReadsClickHouseNormalisedForm()
    {
        // As system.columns.default_expression returns it: the map access is parenthesised.
        var parsed = RetentionRuleSql.ParseDefaultExpression("multiIf((ResourceAttributes['deployment.environment']) = 'dev', 7, (ResourceAttributes['team']) = 'a, b', 90, 30)");
        Assert.NotNull(parsed);
        Assert.Equal(30, parsed.Value.DefaultDays);
        Assert.Collection(
            parsed.Value.Rules,
            r => Assert.Equal(("deployment.environment", "dev", 7), (r.Attribute, r.Value, r.Days)),
            r => Assert.Equal(("team", "a, b", 90), (r.Attribute, r.Value, r.Days)));
    }

    [Fact]
    public void ParseDefaultExpression_RoundTripsWhatItBuilds()
    {
        RetentionRule[] rules = [Rule("env", "it's", 0), Rule("tier", "gold", 365)];
        var parsed = RetentionRuleSql.ParseDefaultExpression(RetentionRuleSql.BuildDefaultExpression(rules, 0));
        Assert.NotNull(parsed);
        Assert.Equal(0, parsed.Value.DefaultDays);
        Assert.Equal(rules.Select(r => (r.Attribute, r.Value, r.Days)), parsed.Value.Rules.Select(r => (r.Attribute, r.Value, r.Days)));
    }

    [Theory]
    [InlineData("20000", 0)]
    [InlineData("30", 30)]
    public void ParseDefaultExpression_PlainNumber_HasNoRules(string expression, int days)
    {
        var parsed = RetentionRuleSql.ParseDefaultExpression(expression);
        Assert.NotNull(parsed);
        Assert.Empty(parsed.Value.Rules);
        Assert.Equal(days, parsed.Value.DefaultDays);
    }

    [Theory]
    [InlineData("multiIf(Severity = 'x', 7, 30)")]
    [InlineData("now()")]
    [InlineData("")]
    public void ParseDefaultExpression_AnythingElse_IsNull(string expression)
    {
        Assert.Null(RetentionRuleSql.ParseDefaultExpression(expression));
    }

    [Fact]
    public void Validate_AcceptsReasonableRules()
    {
        Assert.Null(RetentionRuleSql.Validate("logs", [Rule("deployment.environment", "dev", 7)]));
    }

    [Theory]
    [InlineData("a'] OR 1=1 --")]
    [InlineData("")]
    [InlineData("has space")]
    public void Validate_RejectsAttributeKeysThatCouldBreakTheSql(string key)
    {
        Assert.NotNull(RetentionRuleSql.Validate("logs", [Rule(key, "v", 7)]));
    }

    [Fact]
    public void Validate_RejectsEmptyValuesBadDaysAndTooManyRules()
    {
        Assert.NotNull(RetentionRuleSql.Validate("logs", [Rule("k", "", 7)]));
        Assert.NotNull(RetentionRuleSql.Validate("logs", [Rule("k", "v", -1)]));
        Assert.NotNull(RetentionRuleSql.Validate("logs", [Rule("k", "v", RetentionSql.MaxDays + 1)]));
        Assert.NotNull(RetentionRuleSql.Validate("logs", Enumerable.Range(0, RetentionRuleSql.MaxRules + 1).Select(i => Rule("k", $"v{i}", 7)).ToArray()));
    }

    [Fact]
    public void BuildAlter_PerResource_ReadsTheRowColumn()
    {
        Assert.Equal(
            "ALTER TABLE clickhousedb.logs MODIFY TTL toDateTime(Timestamp) + toIntervalDay(7) TO VOLUME 'cold', toDateTime(Timestamp) + toIntervalDay(_retention_days)",
            RetentionSql.BuildAlter("logs", "Timestamp", 30, clusterMode: false, coldAfterDays: 7, perResource: true));
    }

    [Fact]
    public void ParseTtl_PerResourceRule_IsFlagged()
    {
        const string ddl = "CREATE TABLE t (`Timestamp` DateTime) ENGINE = MergeTree ORDER BY Timestamp TTL toDateTime(Timestamp) + toIntervalDay(_retention_days) SETTINGS index_granularity = 8192";
        Assert.Equal(new TtlState(TtlKind.Days, 0, 0, PerResource: true), RetentionSql.ParseTtl(ddl));
    }

    [Fact]
    public void BuildSetRetentionDefault_ClusterMode_AltersLocalAndDistributedTables()
    {
        var statements = RetentionSql.BuildSetRetentionDefault("logs", "30", clusterMode: true);
        Assert.Equal(2, statements.Count);
        Assert.Contains("clickhousedb.logs_local ON CLUSTER", statements[0]);
        Assert.Contains("clickhousedb.logs ON CLUSTER", statements[1]);
        Assert.Single(RetentionSql.BuildSetRetentionDefault("logs", "30", clusterMode: false));
    }

    [Fact]
    public void Request_ColdAfterIsCheckedAgainstTheShortestRule()
    {
        var request = new SetRetentionRequest
        {
            Signals = new Dictionary<string, int> { ["logs"] = 30 },
            ColdAfterDays = new Dictionary<string, int> { ["logs"] = 10 },
            Rules = new Dictionary<string, IReadOnlyList<RetentionRule>> { ["logs"] = [Rule("env", "dev", 7)] },
        };
        Assert.Contains("shortest retention (7)", request.Validate());
    }

    [Fact]
    public void Request_RulesForSignalNotInSignals_AreRejected()
    {
        var request = new SetRetentionRequest
        {
            Signals = new Dictionary<string, int> { ["logs"] = 30 },
            Rules = new Dictionary<string, IReadOnlyList<RetentionRule>> { ["traces"] = [Rule("env", "dev", 7)] },
        };
        Assert.Contains("not in signals", request.Validate());
    }
}
