using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Alerting;

/// <summary>The worst crash-free rate among the versions a <see cref="CrashFreeRateEvaluator"/> run considered.</summary>
/// <param name="Rate">Percent of that version's sessions that did not crash, 0-100.</param>
/// <param name="Version">The <c>service.version</c> it belongs to (empty when the app reports none).</param>
/// <param name="Sessions">How many sessions the rate is over.</param>
public sealed record CrashFreeTally(double Rate, string Version, ulong Sessions);

/// <summary>
/// Evaluates an <see cref="AlertConditionKind.CrashFreeRate"/> rule: the lowest crash-free session rate over the last
/// <see cref="AlertRule.WindowSeconds"/> among the versions with at least <see cref="AlertRule.MinDataPoints"/> sessions.
/// Shared by <c>AlertEvaluationWorker</c> and the dry-run test endpoints. See
/// <c>docs-internal/adr/0189-release-health-trends.md</c>.
/// </summary>
public static class CrashFreeRateEvaluator
{
    /// <summary>
    /// The lowest rate, or null when no version had enough sessions to judge. <paramref name="minSessions"/> below 1
    /// counts as 1, so a version with no sessions never yields a rate. A scope of several services asks once per service.
    /// </summary>
    public static async Task<CrashFreeTally?> EvaluateAsync(
        IAppSessionQueryService sessions,
        IReadOnlyList<string> services,
        int windowSeconds,
        int minSessions,
        CancellationToken cancellationToken)
    {
        var windowMinutes = (windowSeconds + 59) / 60;
        var scopes = services.Count == 0 ? [null] : services.Select(s => (string?)s).ToArray();

        CrashFreeTally? worst = null;
        foreach (var service in scopes)
        {
            var health = await sessions.GetReleaseHealthAsync(new ReleaseHealthRequest { WindowMinutes = windowMinutes, Service = service }, cancellationToken);
            foreach (var version in health.Versions)
            {
                var tally = Tally(version, minSessions);
                if (tally is not null && (worst is null || tally.Rate < worst.Rate))
                    worst = tally;
            }
        }

        return worst;
    }

    /// <summary>One version's rate, or null when it has fewer than <paramref name="minSessions"/> (at least 1) sessions.</summary>
    public static CrashFreeTally? Tally(ReleaseHealthVersion version, int minSessions)
    {
        if (version.Sessions == 0 || version.Sessions < (ulong)Math.Max(minSessions, 1))
            return null;
        return new CrashFreeTally(100.0 * (version.Sessions - version.CrashedSessions) / version.Sessions, version.Version, version.Sessions);
    }
}
