using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class ActiveServicesQueryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_SelectsServiceNameAndMaxTimestamp_GroupedByService()
    {
        var result = ActiveServicesQueryBuilder.Build(TimeSpan.FromMinutes(5), Now);

        Assert.Contains("SELECT ServiceName, max(Timestamp) AS LastSeenAt", result.Sql);
        Assert.Contains("GROUP BY ServiceName", result.Sql);
        Assert.Contains("WHERE Timestamp >= {since:DateTime64(9)}", result.Sql);
    }

    [Fact]
    public void Build_BindsSinceAsNowMinusWindow()
    {
        var result = ActiveServicesQueryBuilder.Build(TimeSpan.FromMinutes(5), Now);

        Assert.Equal(Now.AddMinutes(-5).UtcDateTime, result.Parameters.ToDictionary()["since"]);
    }
}
