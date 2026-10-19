using Flare.Api.Auth;
using Flare.Api.Model;
using Flare.Api.Prometheus;
using Flare.Api.Query;
using Flare.Identity.Projects;
using Flare.Identity.Users;
using Xunit;

namespace Flare.Api.Tests.Prometheus;

public class AnnotationMetricQueryServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid RuleA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid RuleB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private static AlertTransition Fire(Guid rule, string name, DateTimeOffset at) => new(rule, name, at, false);

    private static AlertTransition Resolve(Guid rule, string name, DateTimeOffset at) => new(rule, name, at, true);

    private static Release Deploy(string service, string version, DateTimeOffset at, string commit = "", string url = "") =>
        new() { Id = service + version, Service = service, Version = version, DeployedAt = at, Commit = commit, Url = url };

    private static PromEvaluator Build(
        IReadOnlyList<AlertTransition>? transitions = null,
        IReadOnlyList<Release>? releases = null,
        ProjectAccess? access = null,
        IReadOnlyList<(Guid, Guid?)>? rules = null,
        FakeHistory? history = null,
        DateTimeOffset? now = null) =>
        new(new AnnotationMetricQueryService(
            new EmptyMetrics(),
            _ => Task.FromResult(rules ?? []),
            history ?? new FakeHistory(transitions ?? []),
            new FakeReleases(releases ?? []),
            () => access ?? ProjectAccess.Unrestricted,
            new FixedTime(now ?? T0.AddHours(1))));

    private static Task<PromEvalResult> Range(PromEvaluator evaluator, string query, int minutes = 10, int stepSeconds = 60) =>
        evaluator.EvaluateAsync(PromQlParser.Parse(query), T0, T0.AddMinutes(minutes), TimeSpan.FromSeconds(stepSeconds), default);

    [Fact]
    public void Incidents_PairAFireWithItsResolution_AndTreatARefireAsTheSameIncident()
    {
        var incidents = AlertIncidents.Build(
            [
                Fire(RuleA, "high latency", T0), Fire(RuleA, "high latency", T0.AddMinutes(5)), Resolve(RuleA, "high latency", T0.AddMinutes(8)),
                Fire(RuleA, "high latency", T0.AddMinutes(20)),
            ],
            T0.AddMinutes(-1));

        Assert.Equal(2, incidents.Count);
        Assert.Equal((T0, (DateTimeOffset?)T0.AddMinutes(8)), (incidents[0].Start, incidents[0].End));
        Assert.Equal((T0.AddMinutes(20), (DateTimeOffset?)null), (incidents[1].Start, incidents[1].End));
    }

    [Fact]
    public void Incidents_IgnoreAResolutionWithNothingOpen_AndDropOnesThatEndedBeforeTheRange()
    {
        var incidents = AlertIncidents.Build(
            [Resolve(RuleA, "a", T0), Fire(RuleB, "b", T0.AddHours(-3)), Resolve(RuleB, "b", T0.AddHours(-2))],
            T0.AddHours(-1));

        Assert.Empty(incidents);
    }

    [Fact]
    public async Task AlertFiring_IsOneFromTheFireToTheResolution_PerRule()
    {
        var evaluator = Build([Fire(RuleA, "high latency", T0.AddSeconds(90)), Resolve(RuleA, "high latency", T0.AddMinutes(4).AddSeconds(10))]);

        var result = await Range(evaluator, "flare_alert_firing");

        var series = Assert.Single(result.Series);
        Assert.Equal("high latency", series.Labels["alertname"]);
        Assert.Equal(RuleA.ToString(), series.Labels["rule_id"]);
        // Buckets 12:01 through 12:04 overlap the incident; 12:00 and 12:05 onward do not.
        Assert.Equal([1, 2, 3, 4], series.Samples.Where(s => s.Value == 1).Select(s => (int)((s.TimestampMs - T0.ToUnixTimeMilliseconds()) / 60_000)));
    }

    [Fact]
    public async Task AlertFiring_ARuleStillFiringRunsToNow_NotToTheEndOfTheRange()
    {
        var evaluator = Build([Fire(RuleA, "disk", T0.AddMinutes(2))], now: T0.AddMinutes(5));

        var result = await Range(evaluator, "flare_alert_firing", minutes: 10);

        var series = Assert.Single(result.Series);
        Assert.Equal([2, 3, 4, 5], series.Samples.Where(s => s.Value == 1).Select(s => (int)((s.TimestampMs - T0.ToUnixTimeMilliseconds()) / 60_000)));
    }

    [Fact]
    public async Task AlertFiring_ARuleAlreadyFiringWhenTheRangeOpens_IsOneFromTheFirstBucket()
    {
        var evaluator = Build([Fire(RuleA, "disk", T0.AddHours(-2)), Resolve(RuleA, "disk", T0.AddMinutes(2))]);

        var result = await Range(evaluator, "flare_alert_firing");

        Assert.Equal([0, 1, 2], Assert.Single(result.Series).Samples.Where(s => s.Value == 1).Select(s => (int)((s.TimestampMs - T0.ToUnixTimeMilliseconds()) / 60_000)));
    }

    [Fact]
    public async Task AlertFiring_RestrictedCallerOnlySeesRulesOfTheirProjects()
    {
        var mine = Guid.NewGuid();
        var other = Guid.NewGuid();
        var history = new FakeHistory([Fire(RuleA, "mine", T0), Fire(RuleB, "other", T0)]);
        var access = ProjectAccess.ForMember([new ProjectMembership(mine, UserRole.Viewer)]);

        await Range(Build(history: history, access: access, rules: [(RuleA, mine), (RuleB, other)]), "flare_alert_firing");

        Assert.Equal([RuleA], history.LastRuleIds);
    }

    [Fact]
    public async Task AlertFiring_UnrestrictedCallerAsksForEveryRule()
    {
        var history = new FakeHistory([Fire(RuleA, "a", T0)]);

        await Range(Build(history: history), "flare_alert_firing");

        Assert.Null(history.LastRuleIds);
    }

    [Fact]
    public async Task AlertFiring_PointCapHit_IsAnErrorNotAPartialAnswer()
    {
        // Called directly: the evaluator's own step cap would reject this range before the overlay saw it.
        var service = new AnnotationMetricQueryService(
            new EmptyMetrics(), _ => Task.FromResult<IReadOnlyList<(Guid, Guid?)>>([]), new FakeHistory([Fire(RuleA, "a", T0.AddDays(-2))]),
            new FakeReleases([]), () => ProjectAccess.Unrestricted, new FixedTime(T0));

        var ex = await Assert.ThrowsAsync<PromQlException>(() => service.QueryAsync(new MetricQueryRequest
        {
            MetricName = "flare.alert.firing",
            Type = MetricPointType.Gauge,
            BucketWidthSeconds = 1,
            Filter = new MetricFilter { From = T0.AddDays(-2), To = T0 },
        }, default));
        Assert.Contains("larger step", ex.Message);
    }

    [Fact]
    public async Task ReleaseDeployed_IsOneAtTheDeployBucket_WithVersionCommitAndUrlAsLabels()
    {
        var evaluator = Build(releases:
        [
            Deploy("shop", "2.1", T0.AddSeconds(150), commit: "abc123", url: "https://ci.example/run/9"),
            Deploy("shop", "2.0", T0.AddHours(-5)),
        ]);

        var result = await Range(evaluator, "flare_release_deployed");

        var series = Assert.Single(result.Series);
        Assert.Equal("shop", series.Labels["service_name"]);
        Assert.Equal("2.1", series.Labels["service_version"]);
        Assert.Equal("abc123", series.Labels["commit"]);
        Assert.Equal("https://ci.example/run/9", series.Labels["url"]);
        Assert.Equal([2], series.Samples.Where(s => s.Value == 1).Select(s => (int)((s.TimestampMs - T0.ToUnixTimeMilliseconds()) / 60_000)));
    }

    [Fact]
    public async Task ReleaseDeployed_ServiceAndVersionMatchersNarrowTheSeries()
    {
        var evaluator = Build(releases: [Deploy("shop", "2.1", T0), Deploy("shop", "2.2", T0), Deploy("admin", "2.1", T0)]);

        var result = await Range(evaluator, "flare_release_deployed{service_name=\"shop\", service_version=\"2.2\"}");

        Assert.Equal("2.2", Assert.Single(result.Series).Labels["service_version"]);
    }

    [Fact]
    public async Task Names_ListTheTwoMetrics_AlongsideTheStoredOnes()
    {
        var service = new AnnotationMetricQueryService(new EmptyMetrics(), _ => Task.FromResult<IReadOnlyList<(Guid, Guid?)>>([]), new FakeHistory([]), new FakeReleases([]), () => ProjectAccess.Unrestricted, new FixedTime(T0));

        var names = (await service.GetNamesAsync(new MetricNamesRequest(), default)).Metrics;

        Assert.Equal(
            ["flare_alert_firing", "flare_release_deployed"],
            names.SelectMany(n => PromNames.PromNamesFor(n.MetricName, n.Unit, n.Type)).Select(n => n.PromName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task UnrelatedMetrics_PassThroughToTheInnerService()
    {
        var inner = new EmptyMetrics();
        var service = new AnnotationMetricQueryService(inner, _ => Task.FromResult<IReadOnlyList<(Guid, Guid?)>>([]), new FakeHistory([]), new FakeReleases([]), () => ProjectAccess.Unrestricted, new FixedTime(T0));

        await service.QueryAsync(new MetricQueryRequest
        {
            MetricName = "http.requests",
            Type = MetricPointType.Sum,
            BucketWidthSeconds = 60,
            Filter = new MetricFilter { From = T0, To = T0.AddMinutes(1) },
        }, default);

        Assert.Equal("http.requests", inner.LastQuery?.MetricName);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeHistory(IReadOnlyList<AlertTransition> transitions) : IAlertFiringHistoryService
    {
        public IReadOnlyCollection<Guid>? LastRuleIds { get; private set; }

        public Task<IReadOnlyList<AlertTransition>> GetTransitionsAsync(DateTimeOffset from, DateTimeOffset to, IReadOnlyCollection<Guid>? ruleIds, CancellationToken cancellationToken)
        {
            LastRuleIds = ruleIds;
            return Task.FromResult<IReadOnlyList<AlertTransition>>([.. transitions.Where(t => ruleIds is null || ruleIds.Contains(t.RuleId))]);
        }
    }

    private sealed class FakeReleases(IReadOnlyList<Release> releases) : IReleaseQueryService
    {
        public Task<IReadOnlyList<Release>> ListAsync(string? service, CancellationToken cancellationToken) => Task.FromResult(releases);

        public Task<Release> UpsertAsync(ReleaseRequest request, string createdBy, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> DeleteAsync(string service, string version, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ReleaseNewError>?> ListNewErrorsAsync(string service, string version, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlySet<(string Version, string Type, string Message)>> FindGroupsSeenAsync(string service, DateTimeOffset since, IReadOnlyList<(string Type, string Message)> groups, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ReleaseErrorTally?> FindWorstRecentAsync(IReadOnlyList<string> services, TimeSpan within, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class EmptyMetrics : IMetricQueryService
    {
        public MetricQueryRequest? LastQuery { get; private set; }

        public Task<MetricNamesResponse> GetNamesAsync(MetricNamesRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new MetricNamesResponse { Metrics = [] });

        public Task<MetricQueryResponse> QueryAsync(MetricQueryRequest request, CancellationToken cancellationToken)
        {
            LastQuery = request;
            return Task.FromResult(new MetricQueryResponse { Series = [] });
        }

        public Task<MetricAttributeKeysResponse> GetAttributeKeysAsync(MetricAttributeKeysRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new MetricAttributeKeysResponse { Keys = [] });
    }
}
