using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Prometheus;

/// <summary>One synthetic release-health metric: its OTel name and type, and which count of a bucket it reports.</summary>
internal sealed record ReleaseHealthMetric(string OtelName, MetricPointType Type, string Description, Func<ReleaseHealthSeriesPoint, double> Value);

/// <summary>
/// The <c>flare.release.*</c> metrics the Prometheus API serves on top of the stored ones (ADR-0250). They are derived from
/// <c>spans</c> at query time like the Sessions page's release health (ADR-0175), never stored. The Prometheus names follow
/// <see cref="PromNames"/>: the two counters get <c>_total</c>.
/// </summary>
internal static class ReleaseHealthMetrics
{
    public const string NamePrefix = "flare.release.";

    /// <summary>The one data point attribute: becomes the <c>service_version</c> label.</summary>
    public const string VersionAttribute = "service.version";

    public static readonly IReadOnlyList<ReleaseHealthMetric> All =
    [
        new("flare.release.sessions", MetricPointType.Sum, "Sessions that started, per service and app version.", p => p.Sessions),
        new("flare.release.crashed_sessions", MetricPointType.Sum, "Sessions that ended in a fatal unhandled exception, per service and app version.", p => p.CrashedSessions),
        new("flare.release.users", MetricPointType.Gauge, "Distinct users with a session that started in the step, per service and app version.", p => p.Users),
        new("flare.release.crashed_users", MetricPointType.Gauge, "Distinct users with a crashed session that started in the step, per service and app version.", p => p.CrashedUsers),
    ];

    public static ReleaseHealthMetric? Find(string otelName) =>
        otelName.StartsWith(NamePrefix, StringComparison.Ordinal) ? All.FirstOrDefault(m => m.OtelName == otelName) : null;
}

/// <summary>
/// Adds the <see cref="ReleaseHealthMetrics"/> to what <see cref="PromEvaluator"/> sees, so Grafana panels and alerts can
/// query release health with plain PromQL (ADR-0250). Everything else passes straight through. Used by the Prometheus API
/// only, not registered as <see cref="IMetricQueryService"/>, so the Metrics page, dashboards and metric alerts never list
/// or chart these.
/// </summary>
/// <remarks>
/// Counters hold the sessions that started in each bucket, which is what Flare stores for any counter (a per-interval
/// increase), so <c>increase(flare_release_sessions_total[1h])</c> needs no special case. A crashed series is emitted for
/// every bucket that has sessions, with 0 where nothing crashed, so <c>crashed / sessions</c> never loses its numerator.
/// </remarks>
internal sealed class ReleaseHealthMetricQueryService(IMetricQueryService inner, IAppSessionQueryService sessions) : IMetricQueryService
{
    public async Task<MetricNamesResponse> GetNamesAsync(MetricNamesRequest request, CancellationToken cancellationToken)
    {
        var real = await inner.GetNamesAsync(request, cancellationToken);
        // No service: which services have sessions is a span scan the name lookup should not pay for. The label
        // browser finds them through the series endpoint instead.
        var synthetic = ReleaseHealthMetrics.All.Select(m => new MetricNameInfo
        {
            MetricName = m.OtelName,
            ServiceName = string.Empty,
            Type = m.Type,
            Description = m.Description,
            SeriesCount = 0,
        });
        return new MetricNamesResponse { Metrics = [.. real.Metrics, .. synthetic] };
    }

    public Task<MetricAttributeKeysResponse> GetAttributeKeysAsync(MetricAttributeKeysRequest request, CancellationToken cancellationToken) =>
        ReleaseHealthMetrics.Find(request.MetricName) is null
            ? inner.GetAttributeKeysAsync(request, cancellationToken)
            : Task.FromResult(new MetricAttributeKeysResponse
            {
                Keys = [new MetricAttributeKeyInfo { Key = ReleaseHealthMetrics.VersionAttribute, DistinctValueCount = 0 }],
            });

    public async Task<MetricQueryResponse> QueryAsync(MetricQueryRequest request, CancellationToken cancellationToken)
    {
        if (ReleaseHealthMetrics.Find(request.MetricName) is not { } metric)
        {
            return await inner.QueryAsync(request, cancellationToken);
        }

        var to = request.Filter.To ?? throw new PromQlException("a release-health query needs an end time.");
        var from = request.Filter.From ?? throw new PromQlException("a release-health query needs a start time.");
        var services = request.Filter.Services is { Count: > 0 } s ? s : null;
        var versions = (request.Filter.Attributes ?? []).Where(a => a.Key == ReleaseHealthMetrics.VersionAttribute).Select(a => a.Value).ToList();
        var otherAttribute = (request.Filter.Attributes ?? []).Any(a => a.Key != ReleaseHealthMetrics.VersionAttribute);

        var (points, truncated) = await sessions.GetReleaseHealthSeriesAsync(
            from, to, request.BucketWidthSeconds, services is { Count: 1 } ? services[0] : null, cancellationToken);
        if (truncated)
        {
            throw new PromQlException(
                $"release health has more than {AppSessionQueryBuilder.MaxSeriesRows} service, version and bucket combinations in this range. " +
                "Narrow it with service_name or service_version matchers, a shorter range or a larger step.");
        }

        if (otherAttribute)
        {
            return new MetricQueryResponse { Series = [] };
        }

        var series = points
            .Where(p => services is null || services.Contains(p.ServiceName))
            .Where(p => versions.All(v => v == p.Version))
            .GroupBy(p => (p.ServiceName, p.Version))
            .Select(g => (Group: g, Total: g.Sum(p => (double)p.Sessions)))
            .OrderByDescending(g => g.Total)
            .ThenBy(g => g.Group.Key.ServiceName, StringComparer.Ordinal)
            .ThenBy(g => g.Group.Key.Version, StringComparer.Ordinal)
            .Take(Math.Clamp(request.TopN ?? PromEvaluator.MaxSeries, 1, PromEvaluator.MaxSeries))
            .Select(g => new MetricSeries
            {
                ServiceName = g.Group.Key.ServiceName,
                Attributes = g.Group.Key.Version.Length == 0
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string> { [ReleaseHealthMetrics.VersionAttribute] = g.Group.Key.Version },
                Points = [.. g.Group.OrderBy(p => p.BucketUnixMs).Select(p => new MetricSeriesPoint
                {
                    BucketStart = DateTimeOffset.FromUnixTimeMilliseconds(p.BucketUnixMs),
                    Value = metric.Value(p),
                })],
            })
            .ToList();
        return new MetricQueryResponse { Series = series };
    }
}
