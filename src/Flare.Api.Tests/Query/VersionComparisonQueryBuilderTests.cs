using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class VersionComparisonQueryBuilderTests
{
    private static readonly DateTimeOffset To = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    private static ServiceVersionInfo V(string version, int firstSeenHour) => new()
    {
        Version = version,
        FirstSeenUnixMs = To.AddHours(-firstSeenHour).ToUnixTimeMilliseconds(),
        LastSeenUnixMs = To.ToUnixTimeMilliseconds(),
        SpanCount = 10,
    };

    // Newest first-seen first, as BuildVersions orders them.
    private static readonly ServiceVersionInfo[] Versions = [V("2.0", 1), V("1.1", 24), V("1.0", 72)];

    [Fact]
    public void ResolvePair_DefaultsToNewestVersusTheOneBeforeIt()
    {
        var (baseline, current) = VersionComparisonQueryBuilder.ResolvePair(Versions, null, null);

        Assert.Equal("1.1", baseline);
        Assert.Equal("2.0", current);
    }

    [Fact]
    public void ResolvePair_PicksPreviousRelativeToAnExplicitCurrent()
    {
        var (baseline, current) = VersionComparisonQueryBuilder.ResolvePair(Versions, null, "1.1");

        Assert.Equal("1.0", baseline);
        Assert.Equal("1.1", current);
    }

    [Fact]
    public void ResolvePair_HonoursAnExplicitBaseline_ButNotOneEqualToCurrent()
    {
        Assert.Equal(("1.0", "2.0"), VersionComparisonQueryBuilder.ResolvePair(Versions, "1.0", "2.0"));
        Assert.Equal(("1.1", "2.0"), VersionComparisonQueryBuilder.ResolvePair(Versions, "2.0", "2.0"));
    }

    [Fact]
    public void ResolvePair_IgnoresUnknownVersions()
    {
        Assert.Equal(("1.1", "2.0"), VersionComparisonQueryBuilder.ResolvePair(Versions, "nope", "also-nope"));
    }

    [Fact]
    public void ResolvePair_NeedsTwoVersions()
    {
        Assert.Equal((null, null), VersionComparisonQueryBuilder.ResolvePair([V("1.0", 1)], null, null));
        Assert.Equal((null, null), VersionComparisonQueryBuilder.ResolvePair([], null, null));
        Assert.Equal((null, null), VersionComparisonQueryBuilder.ResolvePair(Versions, null, "1.0"));
    }

    [Fact]
    public void BuildVersions_GroupsByVersionNewestFirst()
    {
        var result = VersionComparisonQueryBuilder.BuildVersions("orders-api", To.AddHours(-168), To);

        Assert.Contains("ResourceAttributes['service.version'] AS Version", result.Sql);
        Assert.Contains("ServiceName = {service:String}", result.Sql);
        Assert.Contains("ORDER BY FirstSeen DESC", result.Sql);
        Assert.Equal("orders-api", result.Parameters.ToDictionary()["service"]);
    }

    [Fact]
    public void Build_NewThingsAreDecidedInSql_AndBothVersionsAreBound()
    {
        var q = VersionComparisonQueryBuilder.Build("orders-api", "1.1", "2.0", To.AddHours(-168), To);

        foreach (var sql in new[] { q.NewExceptions.Sql, q.NewDependencies.Sql, q.NewLogPatterns.Sql })
        {
            Assert.Contains("> 0 AND countIf(ResourceAttributes['service.version'] = {baseline:String}) = 0", sql);
        }

        Assert.Contains("Kind IN (2, 5)", q.Endpoints.Sql);
        Assert.Contains("EventName = 'exception'", q.NewExceptions.Sql);
        Assert.Contains("UNION ALL", q.NewDependencies.Sql);
        // An output alias named Kind would shadow spans.Kind inside the outbound-call expression.
        Assert.DoesNotContain("AS Kind,", q.NewDependencies.Sql);
        Assert.Contains("FROM logs", q.NewLogPatterns.Sql);
        Assert.Contains("PatternId != ''", q.NewLogPatterns.Sql);

        var parameters = q.NewLogPatterns.Parameters.ToDictionary();
        Assert.Equal("1.1", parameters["baseline"]);
        Assert.Equal("2.0", parameters["current"]);
    }

    [Theory]
    [InlineData(null, 168)]
    [InlineData(0, 168)]
    [InlineData(6, 6)]
    [InlineData(99999, 720)]
    public void ClampLookbackHours_DefaultsAndClamps(int? requested, int expected) =>
        Assert.Equal(expected, VersionComparisonQueryBuilder.ClampLookbackHours(requested));
}
