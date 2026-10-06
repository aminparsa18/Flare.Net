using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class LogMetricPreviewQueryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_WithNoGroupBy_GroupsByServiceOnly()
    {
        var result = LogMetricPreviewQueryBuilder.Build(new LogMetricPreviewRequest(), Now);

        Assert.Contains("SELECT ServiceName, count() AS Cnt", result.Sql);
        Assert.Contains("GROUP BY 1", result.Sql);
        Assert.Equal(LogMetricPreviewQueryBuilder.MaxSeries, result.Parameters.ToDictionary()["seriesLimit"]);
    }

    [Fact]
    public void Build_ResolvesEachKeyFromLogThenResourceAttributes()
    {
        var result = LogMetricPreviewQueryBuilder.Build(new LogMetricPreviewRequest { GroupBy = ["http.route", "env"] }, Now);

        Assert.Contains("if(mapContains(LogAttributes, {gk0:String}), LogAttributes[{gk0:String}], ResourceAttributes[{gk0:String}])", result.Sql);
        Assert.Contains("{gk1:String}", result.Sql);
        Assert.Contains("GROUP BY 1, 2, 3", result.Sql);
        var parameters = result.Parameters.ToDictionary();
        Assert.Equal("http.route", parameters["gk0"]);
        Assert.Equal("env", parameters["gk1"]);
    }

    [Fact]
    public void Build_IgnoresTheFilterTimeRange_AndUsesTheHourWindow()
    {
        var filter = new LogFilter { From = Now.AddDays(-30), To = Now.AddDays(-29) };

        var result = LogMetricPreviewQueryBuilder.Build(new LogMetricPreviewRequest { Condition = filter }, Now);

        Assert.Contains(Now.AddHours(-1).UtcDateTime, result.Parameters.ToDictionary().Values.OfType<DateTime>());
        Assert.DoesNotContain(Now.AddDays(-30).UtcDateTime, result.Parameters.ToDictionary().Values.OfType<DateTime>());
    }

    [Fact]
    public void Validate_RejectsTooManyGroupByKeys()
    {
        var request = new LogMetricPreviewRequest { GroupBy = ["a", "b", "c", "d", "e", "f"] };

        Assert.NotNull(request.Validate());
    }
}
