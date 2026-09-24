using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class QuerySafetyTests
{
    [Fact]
    public void Full_WithDefaults_KeepsThePreviouslyHardCodedCaps()
    {
        var settings = QuerySafety.Full(new QueryLimitsOptions()).CustomSettings!;

        Assert.Equal(30, settings["max_execution_time"]);
        Assert.Equal(0, settings["timeout_before_checking_execution_speed"]);
        Assert.Equal(1_000_000_000L, settings["max_rows_to_read"]);
        Assert.Equal(10_000L, settings["max_result_rows"]);
        Assert.Equal("break", settings["result_overflow_mode"]);
    }

    [Fact]
    public void Full_UsesConfiguredValues()
    {
        var limits = new QueryLimitsOptions { MaxExecutionSeconds = 120, MaxRowsToRead = 5_000_000_000, MaxResultRows = 50_000 };

        var settings = QuerySafety.Full(limits).CustomSettings!;

        Assert.Equal(120, settings["max_execution_time"]);
        Assert.Equal(5_000_000_000L, settings["max_rows_to_read"]);
        Assert.Equal(50_000L, settings["max_result_rows"]);
    }

    [Fact]
    public void ExecutionTimeOnly_SetsNoRowCaps()
    {
        var settings = QuerySafety.ExecutionTimeOnly(new QueryLimitsOptions { MaxExecutionSeconds = 90 }).CustomSettings!;

        Assert.Equal(90, settings["max_execution_time"]);
        Assert.False(settings.ContainsKey("max_rows_to_read"));
        Assert.False(settings.ContainsKey("max_result_rows"));
    }

    [Fact]
    public void AlertEvaluation_UsesItsOwnExecutionCap_AndNoResultRowCap()
    {
        var limits = new QueryLimitsOptions { MaxExecutionSeconds = 120, AlertEvaluationMaxExecutionSeconds = 15 };

        var settings = QuerySafety.AlertEvaluation(limits).CustomSettings!;

        Assert.Equal(15, settings["max_execution_time"]);
        Assert.Equal(1_000_000_000L, settings["max_rows_to_read"]);
        Assert.False(settings.ContainsKey("max_result_rows"));
    }

    [Fact]
    public void EachCall_ReturnsAFreshSettingsDictionary()
    {
        var limits = new QueryLimitsOptions();

        var first = QuerySafety.Full(limits);
        first.CustomSettings!["optimize_skip_unused_shards"] = 1;

        Assert.False(QuerySafety.Full(limits).CustomSettings!.ContainsKey("optimize_skip_unused_shards"));
    }
}
