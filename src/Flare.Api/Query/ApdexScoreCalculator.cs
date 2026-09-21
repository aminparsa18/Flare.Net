namespace Flare.Api.Query;

/// <summary>
/// The standard Apdex formula - split out as a pure static, no <c>IClickHouseClient</c>
/// dependency, so the math itself is unit-testable directly (same "test the pure
/// function" precedent as <see cref="ServiceOverviewQueryService.BuildMetrics"/>).
/// </summary>
public static class ApdexScoreCalculator
{
    /// <summary>
    /// <c>(satisfied + tolerating / 2) / total</c>, the standard Apdex ratio - 1.0 all
    /// satisfied, 0.0 all frustrated. Null when <paramref name="requestCount"/> is 0: a
    /// service with no requests in the window has no meaningful score, not a 0.0 score.
    /// </summary>
    public static double? Calculate(ulong satisfiedCount, ulong toleratingCount, ulong requestCount) =>
        requestCount == 0 ? null : (satisfiedCount + toleratingCount / 2.0) / requestCount;
}
