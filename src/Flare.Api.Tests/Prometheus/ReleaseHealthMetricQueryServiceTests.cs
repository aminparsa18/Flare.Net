using Flare.Api.Model;
using Flare.Api.Prometheus;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Prometheus;

public class ReleaseHealthMetricQueryServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private static ReleaseHealthSeriesPoint Point(int minute, string service, string version, ulong sessions, ulong crashed, ulong users = 0, ulong crashedUsers = 0) =>
        new()
        {
            BucketUnixMs = T0.AddMinutes(minute).ToUnixTimeMilliseconds(),
            ServiceName = service,
            Version = version,
            Sessions = sessions,
            CrashedSessions = crashed,
            Users = users,
            CrashedUsers = crashedUsers,
        };

    private static (PromEvaluator Evaluator, FakeSessions Sessions) Build(params ReleaseHealthSeriesPoint[] points)
    {
        var sessions = new FakeSessions(points);
        return (new PromEvaluator(new ReleaseHealthMetricQueryService(new EmptyMetrics(), sessions)), sessions);
    }

    private static Task<PromEvalResult> Range(PromEvaluator evaluator, string query, int minutes = 3) =>
        evaluator.EvaluateAsync(PromQlParser.Parse(query), T0, T0.AddMinutes(minutes), TimeSpan.FromMinutes(1), default);

    [Fact]
    public async Task CrashFreeRate_IsOneMinusCrashedOverSessions_PerVersion()
    {
        var (evaluator, _) = Build(
            Point(0, "shop", "2.0", 10, 1), Point(1, "shop", "2.0", 10, 1),
            Point(0, "shop", "2.1", 20, 0), Point(1, "shop", "2.1", 20, 0));

        var result = await Range(evaluator,
            "1 - sum by (service_version) (increase(flare_release_crashed_sessions_total[2m])) / sum by (service_version) (increase(flare_release_sessions_total[2m]))");

        var byVersion = result.Series.ToDictionary(s => s.Labels["service_version"], s => s.Samples[^1].Value);
        Assert.Equal(0.9, byVersion["2.0"], 6);
        Assert.Equal(1.0, byVersion["2.1"], 6);
    }

    [Fact]
    public async Task Counters_AreSessionsThatStartedInEachBucket_LabelledByServiceAndVersion()
    {
        var (evaluator, _) = Build(Point(0, "shop", "2.0", 60, 6), Point(1, "shop", "2.0", 120, 0));

        var result = await Range(evaluator, "rate(flare_release_sessions_total[1m])", minutes: 1);

        var series = Assert.Single(result.Series);
        Assert.Equal("shop", series.Labels["service_name"]);
        Assert.Equal("2.0", series.Labels["service_version"]);
        Assert.Equal([1.0, 2.0], series.Samples.Select(s => s.Value));
    }

    [Fact]
    public async Task UserGauges_AreDistinctUsersPerStep()
    {
        var (evaluator, _) = Build(Point(0, "shop", "2.0", 10, 2, users: 8, crashedUsers: 2));

        var result = await Range(evaluator, "1 - flare_release_crashed_users / flare_release_users", minutes: 0);

        Assert.Equal(0.75, Assert.Single(result.Series).Samples[0].Value, 6);
    }

    [Fact]
    public async Task RollingUserGauges_AskForADayOfUsers_AndReadAsTheCrashFreeUsersRate()
    {
        var (evaluator, sessions) = Build(Point(0, "shop", "2.0", 0, 0, users: 200, crashedUsers: 10));

        var result = await Range(evaluator, "1 - flare_release_crashed_users_24h / flare_release_users_24h", minutes: 0);

        Assert.Equal(0.95, Assert.Single(result.Series).Samples[0].Value, 6);
        Assert.Equal(86400, sessions.LastWindowSeconds);
    }

    [Fact]
    public async Task StepGauges_DoNotUseTheRollingQuery()
    {
        var (evaluator, sessions) = Build(Point(0, "shop", "2.0", 10, 0, users: 8));

        await Range(evaluator, "flare_release_users", minutes: 0);

        Assert.Null(sessions.LastWindowSeconds);
    }

    [Fact]
    public async Task ServiceAndVersionMatchers_ReachTheQuery()
    {
        var (evaluator, sessions) = Build(
            Point(0, "shop", "2.0", 5, 0), Point(0, "shop", "2.1", 7, 0), Point(0, "admin", "2.1", 9, 0));

        var result = await Range(evaluator,
            "increase(flare_release_sessions_total{service_name=\"shop\", service_version=\"2.1\"}[1m])", minutes: 0);

        Assert.Equal("shop", sessions.LastService);
        var series = Assert.Single(result.Series);
        Assert.Equal("2.1", series.Labels["service_version"]);
    }

    [Fact]
    public async Task CounterOutsideRate_IsRejectedLikeAnyOtherCounter()
    {
        var (evaluator, _) = Build(Point(0, "shop", "2.0", 5, 0));

        await Assert.ThrowsAsync<PromQlException>(() => Range(evaluator, "flare_release_sessions_total"));
    }

    [Fact]
    public async Task UnrelatedMetrics_PassThroughToTheInnerService()
    {
        var inner = new EmptyMetrics();
        var service = new ReleaseHealthMetricQueryService(inner, new FakeSessions([]));

        await service.QueryAsync(new MetricQueryRequest
        {
            MetricName = "http.requests",
            Type = MetricPointType.Sum,
            BucketWidthSeconds = 60,
            Filter = new MetricFilter { From = T0, To = T0.AddMinutes(1) },
        }, default);

        Assert.Equal("http.requests", inner.LastQuery?.MetricName);
    }

    [Fact]
    public async Task Names_ListTheSixMetrics_AlongsideTheStoredOnes()
    {
        var service = new ReleaseHealthMetricQueryService(new EmptyMetrics(), new FakeSessions([]));

        var names = (await service.GetNamesAsync(new MetricNamesRequest(), default)).Metrics;

        Assert.Equal(
            [
                "flare_release_crashed_sessions_total", "flare_release_crashed_users", "flare_release_crashed_users_24h",
                "flare_release_sessions_total", "flare_release_users", "flare_release_users_24h",
            ],
            names.SelectMany(n => PromNames.PromNamesFor(n.MetricName, n.Unit, n.Type)).Select(n => n.PromName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ARowCapHit_IsAnErrorNotAPartialAnswer()
    {
        var sessions = new FakeSessions([Point(0, "shop", "2.0", 1, 0)]) { Truncated = true };
        var evaluator = new PromEvaluator(new ReleaseHealthMetricQueryService(new EmptyMetrics(), sessions));

        var ex = await Assert.ThrowsAsync<PromQlException>(() => Range(evaluator, "increase(flare_release_sessions_total[1m])"));
        Assert.Contains("Narrow it", ex.Message);
    }

    private sealed class FakeSessions(IReadOnlyList<ReleaseHealthSeriesPoint> points) : IAppSessionQueryService
    {
        public bool Truncated { get; init; }

        public string? LastService { get; private set; }

        public int? LastWindowSeconds { get; private set; }

        public Task<(IReadOnlyList<ReleaseHealthSeriesPoint> Points, bool Truncated)> GetRollingReleaseUsersAsync(
            DateTimeOffset from, DateTimeOffset to, int bucketSeconds, int windowSeconds, string? service, CancellationToken cancellationToken)
        {
            LastService = service;
            LastWindowSeconds = windowSeconds;
            return Task.FromResult((points, Truncated));
        }

        public Task<(IReadOnlyList<ReleaseHealthSeriesPoint> Points, bool Truncated)> GetReleaseHealthSeriesAsync(
            DateTimeOffset from, DateTimeOffset to, int bucketSeconds, string? service, CancellationToken cancellationToken)
        {
            LastService = service;
            return Task.FromResult((points, Truncated));
        }

        public Task<AppSessionsResponse> GetSessionsAsync(AppSessionsRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ReleaseHealthResponse> GetReleaseHealthAsync(ReleaseHealthRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ReleaseHealthTrendResponse> GetReleaseHealthTrendAsync(ReleaseHealthTrendRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AppPerformanceResponse> GetPerformanceAsync(AppPerformanceRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AppSessionTimelineResponse> GetTimelineAsync(string sessionId, AppSessionTimelineRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AppSessionScreenshotResponse?> GetScreenshotAsync(string sessionId, string spanId, AppSessionTimelineRequest window, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AppSessionReplayResponse> GetReplayAsync(string sessionId, AppSessionTimelineRequest window, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AppSessionScreenshotResponse?> GetReplayFrameAsync(string sessionId, long timeUnixMs, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AppFeedbackResponse> GetFeedbackAsync(AppFeedbackRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
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
