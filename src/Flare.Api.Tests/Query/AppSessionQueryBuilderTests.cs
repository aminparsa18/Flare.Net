using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class AppSessionQueryBuilderTests
{
    private static readonly DateTimeOffset End = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null, AppSessionQueryBuilder.DefaultWindowMinutes)]
    [InlineData(0, AppSessionQueryBuilder.DefaultWindowMinutes)]
    [InlineData(1, AppSessionQueryBuilder.MinWindowMinutes)]
    [InlineData(90, 90)]
    [InlineData(1_000_000, AppSessionQueryBuilder.MaxWindowMinutes)]
    public void ClampWindowMinutes_ClampsAndDefaults(int? requested, int expected)
    {
        Assert.Equal(expected, AppSessionQueryBuilder.ClampWindowMinutes(requested));
    }

    [Fact]
    public void BuildSessions_GroupsBySessionId_GuardedByMapContains()
    {
        var built = AppSessionQueryBuilder.BuildSessions(new AppSessionsRequest(), 60, End);

        Assert.Contains("GROUP BY SessionId", built.Sql);
        Assert.Contains("mapContains(SpanAttributes, 'session.id')", built.Sql);
        Assert.DoesNotContain("HAVING", built.Sql);
        Assert.DoesNotContain("ServiceName = {service:String}", built.Sql);
        Assert.DoesNotContain("{version:String}", built.Sql);
    }

    [Fact]
    public void BuildSessions_AppliesServiceVersionAndErrorFilters()
    {
        var built = AppSessionQueryBuilder.BuildSessions(
            new AppSessionsRequest { Service = "shop-app", Version = "2.3.0", ErrorsOnly = true }, 60, End);

        Assert.Contains("ServiceName = {service:String}", built.Sql);
        Assert.Contains("ResourceAttributes['service.version'] = {version:String}", built.Sql);
        Assert.Contains("HAVING ErrorCount > 0", built.Sql);
    }

    [Fact]
    public void BuildSessions_FetchesOneRowPastTheCap()
    {
        var built = AppSessionQueryBuilder.BuildSessions(new AppSessionsRequest(), 60, End);

        Assert.Contains("LIMIT {limit:UInt32}", built.Sql);
    }

    [Fact]
    public void BuildFacets_IgnoresServiceAndVersionFilters()
    {
        var built = AppSessionQueryBuilder.BuildFacets(60, End);

        Assert.Contains("groupUniqArray(ServiceName)", built.Sql);
        Assert.DoesNotContain("{service:String}", built.Sql);
        Assert.DoesNotContain("{version:String}", built.Sql);
    }
}
