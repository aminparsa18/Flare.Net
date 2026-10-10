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

    [Fact]
    public void BuildTimeline_FiltersOnSessionIdParameter_OrderedByStart()
    {
        var built = AppSessionQueryBuilder.BuildTimeline("abc", End.AddHours(-1), End);

        Assert.Contains("SpanAttributes['session.id'] = {sessionId:String}", built.Sql);
        Assert.Contains("mapContains(SpanAttributes, 'session.id')", built.Sql);
        Assert.Contains("ORDER BY StartTime, SpanId", built.Sql);
        Assert.Contains("LIMIT {limit:UInt32}", built.Sql);
        Assert.DoesNotContain("abc", built.Sql);
    }

    [Fact]
    public void BuildScreenshot_ParametrisesSessionAndSpan_NewestFirst()
    {
        var built = AppSessionQueryBuilder.BuildScreenshot("abc", "0123456789abcdef", End.AddHours(-1), End);

        Assert.Contains("FROM app_screenshots", built.Sql);
        Assert.Contains("SessionId = {sessionId:String}", built.Sql);
        Assert.Contains("SpanId = {spanId:String}", built.Sql);
        Assert.Contains("ORDER BY StartTime DESC", built.Sql);
        Assert.DoesNotContain("abc", built.Sql);
    }

    [Fact]
    public void BuildScreenshotSpans_ListsDistinctSpanIdsForTheSession()
    {
        var built = AppSessionQueryBuilder.BuildScreenshotSpans("abc", End.AddHours(-1), End);

        Assert.StartsWith("SELECT DISTINCT SpanId", built.Sql);
        Assert.DoesNotContain("spanId", built.Sql);
    }

    [Fact]
    public void ResolveTimelineWindow_DefaultsToADayBeforeNow()
    {
        var (from, to) = AppSessionQueryBuilder.ResolveTimelineWindow(new AppSessionTimelineRequest(), End);

        Assert.Equal(End, to);
        Assert.Equal(End.AddMinutes(-AppSessionQueryBuilder.DefaultTimelineLookbackMinutes), from);
    }

    [Fact]
    public void ResolveTimelineWindow_UsesGivenBounds()
    {
        var f = End.AddHours(-2).ToUnixTimeMilliseconds();
        var t = End.AddHours(-1).ToUnixTimeMilliseconds();

        var (from, to) = AppSessionQueryBuilder.ResolveTimelineWindow(new AppSessionTimelineRequest { FromUnixMs = f, ToUnixMs = t }, End);

        Assert.Equal(f, from.ToUnixTimeMilliseconds());
        Assert.Equal(t, to.ToUnixTimeMilliseconds());
    }

    [Fact]
    public void ResolveTimelineWindow_InvertedRange_KeepsEndAndWidensBackwards()
    {
        var t = End.AddHours(-1).ToUnixTimeMilliseconds();

        var (from, to) = AppSessionQueryBuilder.ResolveTimelineWindow(
            new AppSessionTimelineRequest { FromUnixMs = End.ToUnixTimeMilliseconds(), ToUnixMs = t }, End);

        Assert.True(from < to);
        Assert.Equal(t, to.ToUnixTimeMilliseconds());
    }

    [Fact]
    public void ResolveTimelineWindow_CapsWidthKeepingTheEnd()
    {
        var f = End.AddDays(-60).ToUnixTimeMilliseconds();

        var (from, to) = AppSessionQueryBuilder.ResolveTimelineWindow(
            new AppSessionTimelineRequest { FromUnixMs = f, ToUnixMs = End.ToUnixTimeMilliseconds() }, End);

        Assert.Equal(End, to);
        Assert.Equal(TimeSpan.FromMinutes(AppSessionQueryBuilder.MaxTimelineWindowMinutes), to - from);
    }
}
