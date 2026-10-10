using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class ReleaseQueryBuilderTests
{
    private static readonly DateTimeOffset Deploy = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ComputeId_IsStablePerServiceAndVersion()
    {
        var id = ReleaseQueryBuilder.ComputeId("api", "1.2.0");

        Assert.Equal(32, id.Length);
        Assert.Equal(id, ReleaseQueryBuilder.ComputeId("api", "1.2.0"));
        Assert.NotEqual(id, ReleaseQueryBuilder.ComputeId("api", "1.2.1"));
        Assert.NotEqual(ReleaseQueryBuilder.ComputeId("ab", "c"), ReleaseQueryBuilder.ComputeId("a", "bc"));
    }

    [Fact]
    public void BuildNewErrors_AttributesGroupsToTheirEarliestVersion()
    {
        var built = ReleaseQueryBuilder.BuildNewErrors("api", "1.2.0", Deploy);

        Assert.Contains("argMin(ResourceAttributes['service.version'], EventTime) AS FirstVersion", built.Sql);
        Assert.Contains("WHERE FirstVersion = {version:String}", built.Sql);
        Assert.Contains("ServiceName = {service:String}", built.Sql);
    }

    [Fact]
    public void BuildNewErrors_LooksBackFromTheDeployTime()
    {
        var built = ReleaseQueryBuilder.BuildNewErrors("api", "1.2.0", Deploy);

        Assert.Contains("{from:DateTime64(9)}", built.Sql);
    }

    [Fact]
    public void BuildNewErrorCounts_OnlyReturnsVersionedGroups()
    {
        var built = ReleaseQueryBuilder.BuildNewErrorCounts("api", Deploy);

        Assert.Contains("WHERE FirstVersion != ''", built.Sql);
        Assert.Contains("GROUP BY FirstVersion", built.Sql);
    }

    [Fact]
    public void Validate_RequiresServiceAndVersion()
    {
        Assert.NotNull(new ReleaseRequest { Service = " ", Version = "1" }.Validate(Deploy));
        Assert.NotNull(new ReleaseRequest { Service = "api", Version = "" }.Validate(Deploy));
        Assert.Null(new ReleaseRequest { Service = "api", Version = "1.2.0" }.Validate(Deploy));
    }

    [Fact]
    public void Validate_RejectsNonHttpUrlAndFarFutureDeploy()
    {
        Assert.NotNull(new ReleaseRequest { Service = "api", Version = "1", Url = "javascript:alert(1)" }.Validate(Deploy));
        Assert.Null(new ReleaseRequest { Service = "api", Version = "1", Url = "https://github.com/o/r/commit/abc" }.Validate(Deploy));
        Assert.NotNull(new ReleaseRequest { Service = "api", Version = "1", DeployedAt = Deploy.AddDays(3) }.Validate(Deploy));
    }
}
