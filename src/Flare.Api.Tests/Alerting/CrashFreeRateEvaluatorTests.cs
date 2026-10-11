using Flare.Api.Alerting;
using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Alerting;

public class CrashFreeRateEvaluatorTests
{
    private static ReleaseHealthVersion Version(string version, ulong sessions, ulong crashed) => new()
    {
        Version = version,
        Sessions = sessions,
        CrashedSessions = crashed,
        ErroredSessions = crashed,
        Users = 0,
        CrashedUsers = 0,
    };

    [Fact]
    public void Tally_IsThePercentOfSessionsThatDidNotCrash() =>
        Assert.Equal(98.0, CrashFreeRateEvaluator.Tally(Version("2.0", 200, 4), 1)!.Rate, 6);

    [Theory]
    [InlineData(0UL, 0, true)]
    [InlineData(10UL, 11, true)]
    [InlineData(10UL, 10, false)]
    [InlineData(10UL, 0, false)]
    public void Tally_NeedsEnoughSessions_AndAtLeastOne(ulong sessions, int minSessions, bool expectNull) =>
        Assert.Equal(expectNull, CrashFreeRateEvaluator.Tally(Version("2.0", sessions, 0), minSessions) is null);

    [Fact]
    public async Task Evaluate_PicksTheWorstVersionWithEnoughSessions()
    {
        var sessions = new FakeSessions(null, [Version("2.2", 1000, 5), Version("2.3", 400, 20), Version("2.4", 3, 3)]);

        var tally = await CrashFreeRateEvaluator.EvaluateAsync(sessions, [], 3600, 50, CancellationToken.None);

        Assert.Equal("2.3", tally!.Version);
        Assert.Equal(95.0, tally.Rate, 6);
        Assert.Equal(60, sessions.Requests[0].WindowMinutes);
        Assert.Null(sessions.Requests[0].Service);
    }

    [Fact]
    public async Task Evaluate_AsksOncePerScopedService_AndKeepsTheWorst()
    {
        var sessions = new FakeSessions("b", [Version("1.0", 100, 10)], [Version("1.0", 100, 1)]);

        var tally = await CrashFreeRateEvaluator.EvaluateAsync(sessions, ["a", "b"], 90, 1, CancellationToken.None);

        Assert.Equal(["a", "b"], sessions.Requests.Select(r => r.Service));
        Assert.Equal(2, sessions.Requests[0].WindowMinutes);
        Assert.Equal(90.0, tally!.Rate, 6);
    }

    [Fact]
    public async Task Evaluate_NoVersionWithEnoughSessions_IsNull() =>
        Assert.Null(await CrashFreeRateEvaluator.EvaluateAsync(new FakeSessions(null, [Version("1.0", 5, 5)]), [], 3600, 50, CancellationToken.None));

    /// <summary>Returns the queued version lists in call order; only <c>GetReleaseHealthAsync</c> is exercised.</summary>
    private sealed class FakeSessions(object? unused, params IReadOnlyList<ReleaseHealthVersion>[] responses) : IAppSessionQueryService
    {
        private int _next;

        public List<ReleaseHealthRequest> Requests { get; } = [];

        public Task<ReleaseHealthResponse> GetReleaseHealthAsync(ReleaseHealthRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new ReleaseHealthResponse { WindowMinutes = request.WindowMinutes ?? 0, Versions = responses[Math.Min(_next++, responses.Length - 1)] });
        }

        public Task<AppSessionsResponse> GetSessionsAsync(AppSessionsRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ReleaseHealthTrendResponse> GetReleaseHealthTrendAsync(ReleaseHealthTrendRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AppPerformanceResponse> GetPerformanceAsync(AppPerformanceRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AppSessionTimelineResponse> GetTimelineAsync(string sessionId, AppSessionTimelineRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AppSessionScreenshotResponse?> GetScreenshotAsync(string sessionId, string spanId, AppSessionTimelineRequest window, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
