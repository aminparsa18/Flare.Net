using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class LatestVersionSqlTests
{
    [Fact]
    public void Select_PicksLatestVersionPerIdWithoutFinal()
    {
        var sql = LatestVersionSql.Select("alert_rules", "Id, Name, UpdatedAt");

        Assert.Equal(
            "SELECT Id, Name, UpdatedAt FROM (SELECT Id, Name, UpdatedAt, IsDeleted FROM alert_rules ORDER BY UpdatedAt DESC LIMIT 1 BY Id) WHERE IsDeleted = 0",
            sql);
        Assert.DoesNotContain("FINAL", sql);
    }

    [Fact]
    public void Select_IdWhere_IsPushedIntoTheInnerScan()
    {
        var sql = LatestVersionSql.Select("dashboards", "Id, UpdatedAt", idWhere: "Id = {id:UUID}");

        Assert.Contains("FROM dashboards WHERE Id = {id:UUID} ORDER BY UpdatedAt DESC LIMIT 1 BY Id)", sql);
    }

    [Fact]
    public void Select_LatestWhere_FiltersTheLatestVersionNotEveryVersion()
    {
        // A rule disabled in its latest version must not fall back to an older enabled one,
        // so Enabled = 1 has to sit outside the LIMIT 1 BY subquery.
        var sql = LatestVersionSql.Select("alert_rules", "Id, Enabled, UpdatedAt", latestWhere: "Enabled = 1", orderBy: "Name");

        Assert.EndsWith("LIMIT 1 BY Id) WHERE IsDeleted = 0 AND Enabled = 1 ORDER BY Name", sql);
    }
}
