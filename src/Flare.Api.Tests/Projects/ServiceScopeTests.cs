using Flare.Api.Model;
using Flare.Api.Query;
using Flare.Identity.Projects;
using Flare.Identity.Users;
using Xunit;

namespace Flare.Api.Tests.Projects;

// ServiceScope is ambient (AsyncLocal), so every test restores it.
public class ServiceScopeTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    public void Dispose() => ServiceScope.Current = null;

    [Fact]
    public void Unrestricted_AddsNoClause()
    {
        Assert.DoesNotContain("scope", LogFilterSqlBuilder.Build(new LogFilter(), Now).WhereSql);
    }

    [Fact]
    public void Restricted_AddsExactAndPrefixClauses_ToEveryFilterBuilder()
    {
        ServiceScope.Current = ["billing", "checkout-*"];

        var sqls = new[]
        {
            LogFilterSqlBuilder.Build(new LogFilter(), Now).WhereSql,
            SpanFilterSqlBuilder.Build(new SpanFilter(), Now).WhereSql,
            MetricFilterSqlBuilder.Build(new MetricFilter(), Now).WhereSql,
            ExceptionFilterSqlBuilder.Build(new ExceptionFilter(), Now).WhereSql,
        };

        foreach (var sql in sqls)
        {
            Assert.Contains("ServiceName IN {scopeExact:Array(String)}", sql);
            Assert.Contains("startsWith(ServiceName, p)", sql);
        }
    }

    [Fact]
    public void Restricted_ComposesWithTheUsersOwnServiceFilter()
    {
        ServiceScope.Current = ["a"];

        var sql = LogFilterSqlBuilder.Build(new LogFilter { Services = ["b"] }, Now).WhereSql;

        Assert.Contains("ServiceName IN {services:Array(String)}", sql);
        Assert.Contains("{scopeExact:Array(String)}", sql);
    }

    [Fact]
    public void Restricted_ScopesEveryServiceKeyedAndTraceBuilder()
    {
        ServiceScope.Current = ["checkout-*"];
        var now = Now;
        var sqls = new Dictionary<string, string>
        {
            ["trace-by-id"] = TraceByIdQueryBuilder.Build("t").Sql,
            ["trace-roots"] = TraceLevelQueryBuilder.BuildRoots("t").Sql,
            ["trace-children"] = TraceLevelQueryBuilder.BuildChildren("t", ["p"]).Sql,
            ["span-duration"] = SpanDurationQueryBuilder.Build([("t", "s")]).Sql,
            ["span-rollup"] = SpanRollupQueryBuilder.Build(["t"]).Sql,
            ["services-active"] = ActiveServicesQueryBuilder.Build(TimeSpan.FromHours(1), now).Sql,
            ["service-metrics"] = ServiceMetricsQueryBuilder.Build(TimeSpan.FromHours(1), now).Sql,
            ["dependency-metrics"] = ServiceDependencyMetricsQueryBuilder.Build(TimeSpan.FromHours(1), now).Sql,
            ["breakdown"] = ServiceCallBreakdownQueryBuilder.Build("checkout-api", TimeSpan.FromHours(1), now).ExternalCallsSql,
            ["error-known-versions"] = ErrorIssueEvidenceQueryBuilder.BuildKnownVersions("T", "m", now).Sql,
        };

        foreach (var (name, sql) in sqls)
        {
            Assert.True(sql.Contains("scopePrefixes", StringComparison.Ordinal), $"{name} is not scoped");
        }
    }

    [Fact]
    public void DependencyGraph_RequiresBothEdgeEndsInScope()
    {
        ServiceScope.Current = ["a"];

        var graph = ServiceDependencyQueryBuilder.Build(TimeSpan.FromHours(1), Now);

        Assert.Contains("scopeExactParent", graph.EdgesSql);
        Assert.Contains("scopeExactChild", graph.EdgesSql);
        Assert.Contains("scopeExact", graph.NodesSql);
        Assert.Contains("scopeExact", graph.ExternalLeavesSql);
    }

    [Fact]
    public void EmptyAllowList_MatchesNothing()
    {
        ServiceScope.Current = [];

        Assert.Contains(" AND 0", LogFilterSqlBuilder.Build(new LogFilter(), Now).WhereSql);
    }

    [Fact]
    public void LiveTailMatcher_HonoursAllowList()
    {
        var dto = Event("checkout-api");

        Assert.True(LogFilterMatcher.Matches(dto, new LogFilter(), ["checkout-*"]));
        Assert.False(LogFilterMatcher.Matches(dto, new LogFilter(), ["billing"]));
        Assert.True(LogFilterMatcher.Matches(dto, new LogFilter(), null));
    }

    [Fact]
    public void Evaluator_AdminOrNoProjects_Unrestricted_OtherwiseUnionOfMemberships()
    {
        var a = new Project(Guid.NewGuid(), "A", "", Now, ["a-*"]);
        var b = new Project(Guid.NewGuid(), "B", "", Now, ["b", "a-*"]);
        var c = new Project(Guid.NewGuid(), "C", "", Now, ["c"]);
        var memberships = new[] { new ProjectMembership(a.Id, UserRole.Viewer), new ProjectMembership(b.Id, UserRole.Member) };

        Assert.Null(ProjectScopeEvaluator.Resolve(true, [a, b, c], []));
        Assert.Null(ProjectScopeEvaluator.Resolve(false, [], []));
        Assert.Equal(["a-*", "b"], ProjectScopeEvaluator.Resolve(false, [a, b, c], memberships)!.Order());
        Assert.Empty(ProjectScopeEvaluator.Resolve(false, [a, b, c], [])!);
    }

    private static LogEventDto Event(string service) => new()
    {
        EventId = Guid.NewGuid(),
        Timestamp = DateTimeOffset.UnixEpoch,
        ObservedTimestamp = DateTimeOffset.UnixEpoch,
        IngestedAt = DateTimeOffset.UnixEpoch,
        TraceId = "",
        SpanId = "",
        TraceFlags = 0,
        SeverityText = "",
        SeverityNumber = 9,
        ServiceName = service,
        Body = "",
        ResourceSchemaUrl = "",
        ResourceAttributes = new Dictionary<string, string>(),
        ScopeSchemaUrl = "",
        ScopeName = "",
        ScopeVersion = "",
        ScopeAttributes = new Dictionary<string, string>(),
        LogAttributes = new Dictionary<string, string>(),
        EventName = "",
        PatternId = "",
        PatternTemplate = "",
    };
}
