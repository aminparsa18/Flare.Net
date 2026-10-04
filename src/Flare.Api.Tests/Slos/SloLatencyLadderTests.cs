using Flare.Api.Slos;
using Xunit;

namespace Flare.Api.Tests.Slos;

public class SloLatencyLadderTests
{
    [Theory]
    [InlineData(50)]
    [InlineData(250)]
    [InlineData(10000)]
    public void Rung_MapsToItsColumn(int ms) => Assert.Equal($"Under{ms}ms", SloLatencyLadder.Column(ms));

    [Theory]
    [InlineData(0)]
    [InlineData(300)]
    [InlineData(-1)]
    public void OffLadder_HasNoColumn(int ms)
    {
        Assert.False(SloLatencyLadder.IsRung(ms));
        Assert.Null(SloLatencyLadder.Column(ms));
    }

    [Fact]
    public void EveryRung_HasAMigrationColumn()
    {
        // 0049_slos.sql's materialized view and table must carry exactly these columns.
        var sql = File.ReadAllText(FindMigration());
        foreach (var ms in SloLatencyLadder.ThresholdsMs)
        {
            Assert.Contains($"Under{ms}ms SimpleAggregateFunction(sum, UInt64)", sql);
            Assert.Contains($"AS Under{ms}ms", sql);
        }
    }

    private static string FindMigration()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "db", "clickhouse", "0049_slos.sql")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir!.FullName, "db", "clickhouse", "0049_slos.sql");
    }
}
