using Xunit;
using Flare.Api.Retention;

namespace Flare.Api.Tests.Retention;

public class RetentionSqlTests
{
    [Fact]
    public void BuildAlter_SingleNode_ModifiesTheTableItself()
    {
        Assert.Equal(
            "ALTER TABLE clickhousedb.logs MODIFY TTL toDateTime(Timestamp) + toIntervalDay(30)",
            RetentionSql.BuildAlter("logs", "Timestamp", 30, clusterMode: false));
    }

    [Fact]
    public void BuildAlter_ClusterMode_TargetsLocalTableOnCluster()
    {
        Assert.Equal(
            "ALTER TABLE clickhousedb.spans_local ON CLUSTER 'flare_cluster' MODIFY TTL toDateTime(StartTime) + toIntervalDay(7)",
            RetentionSql.BuildAlter("spans", "StartTime", 7, clusterMode: true));
    }

    [Theory]
    [InlineData(false, "ALTER TABLE clickhousedb.logs REMOVE TTL")]
    [InlineData(true, "ALTER TABLE clickhousedb.logs_local ON CLUSTER 'flare_cluster' REMOVE TTL")]
    public void BuildAlter_ZeroDays_RemovesTtl(bool clusterMode, string expected)
    {
        Assert.Equal(expected, RetentionSql.BuildAlter("logs", "Timestamp", 0, clusterMode));
    }

    [Fact]
    public void AlterSettings_SkipMaterializingExistingParts()
    {
        Assert.Equal(0, RetentionSql.AlterSettings["materialize_ttl_after_modify"]);
    }

    [Theory]
    [InlineData(false, "'logs'", "'logs_local'")]
    [InlineData(true, "'logs_local'", "'logs'")]
    public void BuildCreateQueriesSql_PicksTableNamesByMode(bool clusterMode, string present, string absent)
    {
        var sql = RetentionSql.BuildCreateQueriesSql(clusterMode);
        Assert.Contains(present, sql);
        Assert.DoesNotContain(absent + ",", sql);
        Assert.Contains(clusterMode ? "'metrics_exponential_histogram_local'" : "'metrics_exponential_histogram'", sql);
    }

    [Fact]
    public void ParseTtl_NoClause_IsNone()
    {
        const string ddl = "CREATE TABLE clickhousedb.logs (`Timestamp` DateTime64(9)) ENGINE = MergeTree PARTITION BY toStartOfMonth(Timestamp) ORDER BY Timestamp SETTINGS index_granularity = 8192";
        Assert.Equal(new TtlState(TtlKind.None, 0), RetentionSql.ParseTtl(ddl));
    }

    [Fact]
    public void ParseTtl_PlainDaysClause_ReturnsDays()
    {
        const string ddl = "CREATE TABLE clickhousedb.logs (`Timestamp` DateTime64(9)) ENGINE = MergeTree ORDER BY Timestamp TTL toDateTime(Timestamp) + toIntervalDay(30) SETTINGS index_granularity = 8192";
        Assert.Equal(new TtlState(TtlKind.Days, 30), RetentionSql.ParseTtl(ddl));
    }

    [Theory]
    [InlineData("TTL toDateTime(Timestamp) + toIntervalDay(30) TO VOLUME 'cold'")]
    [InlineData("TTL toDateTime(Timestamp) + toIntervalHour(12)")]
    [InlineData("TTL toDateTime(Timestamp) + toIntervalDay(7) DELETE WHERE ServiceName = 'x'")]
    public void ParseTtl_AnythingFlareDidNotWrite_IsCustom(string ttlClause)
    {
        var ddl = $"CREATE TABLE t (`Timestamp` DateTime) ENGINE = MergeTree ORDER BY Timestamp {ttlClause} SETTINGS index_granularity = 8192";
        Assert.Equal(TtlKind.Custom, RetentionSql.ParseTtl(ddl).Kind);
    }

    [Fact]
    public void ParseTtl_NoSettingsSuffix_StillReadsDays()
    {
        const string ddl = "CREATE TABLE t (`Timestamp` DateTime) ENGINE = MergeTree ORDER BY Timestamp TTL toDateTime(Timestamp) + toIntervalDay(90)";
        Assert.Equal(new TtlState(TtlKind.Days, 90), RetentionSql.ParseTtl(ddl));
    }

    [Fact]
    public void ParseTtl_ColumnLevelTtl_IsNotTheTableTtl()
    {
        const string ddl = "CREATE TABLE t (`Timestamp` DateTime, `Body` String TTL Timestamp + toIntervalDay(1)) ENGINE = MergeTree ORDER BY Timestamp SETTINGS index_granularity = 8192";
        Assert.Equal(TtlKind.None, RetentionSql.ParseTtl(ddl).Kind);
    }

    [Fact]
    public void Combine_AgreeingTables_KeepTheState()
    {
        var states = new[] { new TtlState(TtlKind.Days, 14), new TtlState(TtlKind.Days, 14) };
        Assert.Equal(new TtlState(TtlKind.Days, 14), RetentionSql.Combine(states));
    }

    [Fact]
    public void Combine_DisagreeingTables_IsCustom()
    {
        var states = new[] { new TtlState(TtlKind.Days, 14), new TtlState(TtlKind.None, 0) };
        Assert.Equal(TtlKind.Custom, RetentionSql.Combine(states).Kind);
    }

    [Fact]
    public void Signals_CoverEveryRawMetricsTable()
    {
        var metrics = RetentionSignal.Find("metrics");
        Assert.NotNull(metrics);
        Assert.Equal(4, metrics.Tables.Count);
        Assert.Equal(RetentionSignal.Find("METRICS"), metrics);
    }
}

public class SetRetentionRequestTests
{
    [Fact]
    public void Validate_AcceptsKnownSignalsIncludingZero()
    {
        var request = new SetRetentionRequest { Signals = new Dictionary<string, int> { ["logs"] = 30, ["Traces"] = 0 } };
        Assert.Null(request.Validate());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("empty")]
    public void Validate_RequiresAtLeastOneSignal(string? kind)
    {
        var request = new SetRetentionRequest { Signals = kind is null ? null : new Dictionary<string, int>() };
        Assert.NotNull(request.Validate());
    }

    [Fact]
    public void Validate_RejectsUnknownSignal()
    {
        var request = new SetRetentionRequest { Signals = new Dictionary<string, int> { ["alerts"] = 30 } };
        Assert.Contains("Unknown signal", request.Validate());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(RetentionSql.MaxDays + 1)]
    public void Validate_RejectsOutOfRangeDays(int days)
    {
        var request = new SetRetentionRequest { Signals = new Dictionary<string, int> { ["logs"] = days } };
        Assert.NotNull(request.Validate());
    }
}
