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
    public void BuildSessions_RestrictsToTheGivenSessionIdsAsABoundArray()
    {
        var built = AppSessionQueryBuilder.BuildSessions(new AppSessionsRequest(), 60, End, ["s-1", "s-2"]);

        Assert.Contains("AND SpanAttributes['session.id'] IN {onlySessionIds:Array(String)}", built.Sql);
        Assert.DoesNotContain("s-1", built.Sql);
    }

    [Fact]
    public void BuildSessions_WithoutAnIdRestrictionHasNoSessionIdPredicate()
    {
        var built = AppSessionQueryBuilder.BuildSessions(new AppSessionsRequest(), 60, End);

        Assert.DoesNotContain("onlySessionIds", built.Sql);
    }

    [Fact]
    public void BuildSessions_FetchesOneRowPastTheCap()
    {
        var built = AppSessionQueryBuilder.BuildSessions(new AppSessionsRequest(), 60, End);

        Assert.Contains("LIMIT {limit:UInt32}", built.Sql);
    }

    [Fact]
    public void BuildReleaseHealth_GroupsSessionsThenVersions_AndIgnoresVersionFilter()
    {
        var built = AppSessionQueryBuilder.BuildReleaseHealth(new ReleaseHealthRequest { Service = "shop-app" }, 60, End);

        Assert.Contains("GROUP BY SpanAttributes['session.id']", built.Sql);
        Assert.Contains("GROUP BY Version", built.Sql);
        Assert.Contains("Name = 'app.unhandled_exception'", built.Sql);
        Assert.Contains("exception.escaped", built.Sql);
        Assert.Contains("mapContains(SpanAttributes, 'session.id')", built.Sql);
        Assert.Contains("ServiceName = {service:String}", built.Sql);
        Assert.DoesNotContain("{version:String}", built.Sql);
        Assert.DoesNotContain("LIMIT", built.Sql);
    }

    [Fact]
    public void BuildReleaseHealthTrend_BucketsEachSessionByItsFirstSpan_PerVersion()
    {
        var built = AppSessionQueryBuilder.BuildReleaseHealthTrend(new ReleaseHealthTrendRequest { Service = "shop-app" }, 1440, 30, End);

        Assert.Contains("INTERVAL 30 MINUTE", built.Sql);
        Assert.Contains("min(StartTime) AS FirstSeen", built.Sql);
        Assert.Contains("GROUP BY SpanAttributes['session.id']", built.Sql);
        Assert.Contains("GROUP BY BucketUnixMs, Version", built.Sql);
        Assert.Contains("exception.escaped", built.Sql);
        Assert.Contains("ServiceName = {service:String}", built.Sql);
        Assert.DoesNotContain("{version:String}", built.Sql);
    }

    [Fact]
    public void BuildReleaseHealthSeries_AlignsBucketsToTheEpoch_AndGroupsByServiceAndVersion()
    {
        var built = AppSessionQueryBuilder.BuildReleaseHealthSeries(End.AddHours(-6), End, 300, "shop-app");

        Assert.Contains("intDiv(toUnixTimestamp64Milli(FirstSeen), 300000) * 300000", built.Sql);
        Assert.Contains("min(StartTime) AS FirstSeen", built.Sql);
        Assert.Contains("GROUP BY SpanAttributes['session.id']", built.Sql);
        Assert.Contains("GROUP BY BucketUnixMs, Svc, Version", built.Sql);
        Assert.Contains("uniqExactIf(UserId, UserId != '' AND Crashed) AS CrashedUsers", built.Sql);
        // The inner alias is Svc so it cannot shadow the ServiceName column the WHERE clause filters on.
        Assert.Contains("any(ServiceName) AS Svc", built.Sql);
        Assert.Contains("ServiceName = {service:String}", built.Sql);
        Assert.EndsWith($"LIMIT {AppSessionQueryBuilder.MaxSeriesRows + 1}", built.Sql);
    }

    [Fact]
    public void BuildRollingReleaseUsers_ScansAWindowBeforeFrom_AndExpandsRunsOfStepsClampedToTheRange()
    {
        var from = End.AddHours(-1);
        var built = AppSessionQueryBuilder.BuildRollingReleaseUsers(from, End, 60, 86400, "shop-app");

        // Scans a day before the range; the user's session steps become runs of covered steps (a step covers 1440 - 1 more).
        Assert.Equal(from.AddHours(-24).UtcDateTime, built.Parameters.OfType<ClickHouse.Driver.ADO.Parameters.ClickHouseDbParameter>().Single(p => p.ParameterName == "from").Value);
        Assert.Contains("arraySort(groupUniqArray(SessionStep)) AS Steps", built.Sql);
        Assert.Contains("b - Steps[i - 1] > 86400000", built.Sql);
        Assert.Contains("b + 86340000", built.Sql);
        Assert.Contains("HAVING UserId != ''", built.Sql);
        var first = from.ToUnixTimeMilliseconds() / 60000 * 60000;
        var last = (End.ToUnixTimeMilliseconds() - 1) / 60000 * 60000;
        Assert.Contains($"ARRAY JOIN range(greatest(toUInt64(Run.1), {first}), least(toUInt64(Run.2), {last}) + 1, 60000) AS Step", built.Sql);
        Assert.Contains("GROUP BY Step, Svc, Version", built.Sql);
        Assert.Contains("ServiceName = {service:String}", built.Sql);
        Assert.EndsWith($"LIMIT {AppSessionQueryBuilder.MaxSeriesRows + 1}", built.Sql);
    }

    [Fact]
    public void BuildRollingReleaseUsers_RoundsTheWindowDownToWholeSteps_AndAtLeastOne()
    {
        // 24h in 7h steps is 3 whole steps; a window shorter than a step is one step (it covers only itself).
        var wide = AppSessionQueryBuilder.BuildRollingReleaseUsers(End.AddDays(-2), End, 7 * 3600, 86400, null);
        Assert.Contains("> 75600000", wide.Sql);
        Assert.Contains("b + 50400000", wide.Sql);

        var narrow = AppSessionQueryBuilder.BuildRollingReleaseUsers(End.AddDays(-2), End, 3600, 60, null);
        Assert.Contains("> 3600000", narrow.Sql);
        Assert.Contains("b + 0", narrow.Sql);
    }

    [Theory]
    [InlineData(null, 60, 5)]
    [InlineData(null, 1440, 30)]
    [InlineData(null, 10080, 180)]
    [InlineData(15, 1440, 15)]
    [InlineData(1, 10080, 180)]
    [InlineData(0, 60, 5)]
    public void ResolveTrendBucketMinutes_HonoursARequestThatFitsTheCap_ElseChoosesFromTheWindow(int? requested, int windowMinutes, int expected) =>
        Assert.Equal(expected, AppSessionQueryBuilder.ResolveTrendBucketMinutes(requested, windowMinutes));

    [Fact]
    public void BuildAppStarts_GroupsByVersionAndType_WithScopeFilters()
    {
        var built = AppSessionQueryBuilder.BuildAppStarts(new AppPerformanceRequest { Service = "shop-app", Version = "2.0" }, 60, End);

        Assert.Contains("Name = 'app.start'", built.Sql);
        Assert.Contains("SpanAttributes['app.start.type']", built.Sql);
        Assert.Contains("GROUP BY Version, Type", built.Sql);
        Assert.Contains("quantile(0.95)", built.Sql);
        Assert.Contains("ServiceName = {service:String}", built.Sql);
        Assert.Contains("ResourceAttributes['service.version'] = {version:String}", built.Sql);
        Assert.Contains("LIMIT 200", built.Sql);
    }

    [Fact]
    public void BuildScreenPerformance_CombinesLoadsAndFrames_AndGuardsEmptyPercentiles()
    {
        var built = AppSessionQueryBuilder.BuildScreenPerformance(new AppPerformanceRequest(), 60, End);

        Assert.Contains("Name IN ('screen.load', 'screen.frames')", built.Sql);
        Assert.Contains("SpanAttributes['screen.name'] != ''", built.Sql);
        Assert.Contains("if(Loads = 0, 0, quantileIf(0.95)", built.Sql);
        Assert.Contains("sumIf(toUInt64OrZero(SpanAttributes['frames.frozen'])", built.Sql);
        Assert.Contains("GROUP BY Screen", built.Sql);
        Assert.DoesNotContain("{service:String}", built.Sql);
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
    public void BuildScreenshotSpans_IgnoresScheduledFramesWithoutASpan()
    {
        var built = AppSessionQueryBuilder.BuildScreenshotSpans("abc", End.AddHours(-1), End);

        Assert.Contains("SpanId != ''", built.Sql);
    }

    [Fact]
    public void BuildReplayFrames_ListsPeriodicTimesOldestFirstWithOneExtraRow()
    {
        var built = AppSessionQueryBuilder.BuildReplayFrames("abc", End.AddHours(-1), End);

        Assert.StartsWith("SELECT toUnixTimestamp64Milli(StartTime)", built.Sql);
        Assert.Contains("Kind = 'periodic'", built.Sql);
        Assert.Contains("SessionId = {sessionId:String}", built.Sql);
        Assert.Contains("ORDER BY StartTime\nLIMIT 721", built.Sql);
        Assert.DoesNotContain("abc", built.Sql);
    }

    [Fact]
    public void BuildReplayFrame_ReadsExactlyOneMillisecondOfPeriodicRows()
    {
        var built = AppSessionQueryBuilder.BuildReplayFrame("abc", 1_700_000_000_123);

        Assert.Contains("Kind = 'periodic'", built.Sql);
        Assert.Contains("StartTime >= {from:DateTime64(3)}", built.Sql);
        Assert.Contains("StartTime < {to:DateTime64(3)}", built.Sql);
        Assert.DoesNotContain("SpanId = ", built.Sql);
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

    [Fact]
    public void BuildFeedback_NewestFirst_ParametrisesFilters()
    {
        var built = AppSessionQueryBuilder.BuildFeedback(new AppFeedbackRequest { Service = "shop", SessionId = "abc" }, 60, End);

        Assert.Contains("FROM app_feedback", built.Sql);
        Assert.Contains("ServiceName = {service:String}", built.Sql);
        Assert.Contains("SessionId = {sessionId:String}", built.Sql);
        Assert.Contains("ORDER BY StartTime DESC", built.Sql);
        Assert.Contains($"LIMIT {AppSessionQueryBuilder.MaxFeedbackRows + 1}", built.Sql);
        Assert.DoesNotContain("shop", built.Sql);
    }

    [Fact]
    public void BuildFeedback_WithoutFilters_OnlyBoundsTheWindow()
    {
        var built = AppSessionQueryBuilder.BuildFeedback(new AppFeedbackRequest(), 60, End);

        Assert.DoesNotContain("{service:String}", built.Sql);
        Assert.DoesNotContain("{sessionId:String}", built.Sql);
    }
}
