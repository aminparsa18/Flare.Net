using Flare.Api.Auth;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Prometheus;

/// <summary>One synthetic annotation metric (ADR-0252): its OTel name and what it reports.</summary>
internal sealed record AnnotationMetric(string OtelName, string Description, string[] AttributeKeys);

/// <summary>
/// The <c>flare.alert.firing</c> and <c>flare.release.deployed</c> gauges the Prometheus API serves so a Grafana annotation
/// query can mark alerts and deploys on any panel (ADR-0252). Both are 1 where the event is, absent elsewhere.
/// </summary>
internal static class AnnotationMetrics
{
    public const string AlertAttributeName = "alertname";
    public const string AlertAttributeRuleId = "rule_id";
    public const string ReleaseAttributeVersion = "service.version";
    public const string ReleaseAttributeCommit = "commit";
    public const string ReleaseAttributeUrl = "url";

    public static readonly AnnotationMetric AlertFiring = new(
        "flare.alert.firing",
        "1 for every step an alert rule is firing, from its fire until it recovers.",
        [AlertAttributeName, AlertAttributeRuleId]);

    public static readonly AnnotationMetric ReleaseDeployed = new(
        "flare.release.deployed",
        "1 at the step a marked release was deployed, per service and version.",
        [ReleaseAttributeVersion, ReleaseAttributeCommit, ReleaseAttributeUrl]);

    public static readonly IReadOnlyList<AnnotationMetric> All = [AlertFiring, ReleaseDeployed];

    /// <summary>Most points one query returns; beyond it the query fails and asks for a larger step or a shorter range.</summary>
    public const int MaxPoints = 50_000;

    public static AnnotationMetric? Find(string otelName) => All.FirstOrDefault(m => m.OtelName == otelName);
}

/// <summary>A rule's firing incident: it fired at <see cref="Start"/> and recovered at <see cref="End"/>, null while still firing.</summary>
internal sealed record AlertIncident(Guid RuleId, string RuleName, DateTimeOffset Start, DateTimeOffset? End);

/// <summary>Pure pairing of fires with the resolutions that end them. No I/O.</summary>
internal static class AlertIncidents
{
    /// <summary>
    /// Walks each rule's transitions in time order: a fire opens an incident unless one is open (a re-fire after the cooldown
    /// is the same incident), a resolution closes it, and a resolution with nothing open is ignored. Incidents that ended
    /// before <paramref name="from"/> are dropped; the rest keep their true start, which may precede the range.
    /// </summary>
    public static IReadOnlyList<AlertIncident> Build(IEnumerable<AlertTransition> transitions, DateTimeOffset from)
    {
        var incidents = new List<AlertIncident>();
        foreach (var rule in transitions.GroupBy(t => t.RuleId))
        {
            AlertTransition? open = null;
            foreach (var t in rule.OrderBy(t => t.At).ThenBy(t => t.Resolved))
            {
                if (!t.Resolved)
                {
                    open ??= t;
                }
                else if (open is not null)
                {
                    incidents.Add(new AlertIncident(rule.Key, open.RuleName, open.At, t.At));
                    open = null;
                }
            }

            if (open is not null)
            {
                incidents.Add(new AlertIncident(rule.Key, open.RuleName, open.At, null));
            }
        }

        return [.. incidents.Where(i => i.End is not { } end || end >= from)];
    }
}

/// <summary>
/// Adds the <see cref="AnnotationMetrics"/> to what <see cref="PromEvaluator"/> sees (ADR-0252), the way
/// <see cref="ReleaseHealthMetricQueryService"/> adds release health. Everything else passes through. Used by the Prometheus
/// API only, so the Metrics page, dashboards and metric alerts never list these.
/// </summary>
/// <remarks>
/// A point is the start of an epoch-aligned bucket of the query's step. An alert is 1 in every bucket that overlaps an
/// incident, so a Grafana annotation reads one region per incident; a rule still firing runs to the end of the range, capped
/// at now. A release is 1 in the bucket of its deploy time. Alert visibility follows the caller's project access
/// (<paramref name="access"/>, with <paramref name="ruleProjects"/> saying which project each rule belongs to), releases follow the service scope the stored queries already apply.
/// </remarks>
internal sealed class AnnotationMetricQueryService(
    IMetricQueryService inner,
    Func<CancellationToken, Task<IReadOnlyList<(Guid RuleId, Guid? ProjectId)>>> ruleProjects,
    IAlertFiringHistoryService history,
    IReleaseQueryService releases,
    Func<ProjectAccess> access,
    TimeProvider timeProvider) : IMetricQueryService
{
    public async Task<MetricNamesResponse> GetNamesAsync(MetricNamesRequest request, CancellationToken cancellationToken)
    {
        var real = await inner.GetNamesAsync(request, cancellationToken);
        var synthetic = AnnotationMetrics.All.Select(m => new MetricNameInfo
        {
            MetricName = m.OtelName,
            ServiceName = string.Empty,
            Type = MetricPointType.Gauge,
            Description = m.Description,
            SeriesCount = 0,
        });
        return new MetricNamesResponse { Metrics = [.. real.Metrics, .. synthetic] };
    }

    public Task<MetricAttributeKeysResponse> GetAttributeKeysAsync(MetricAttributeKeysRequest request, CancellationToken cancellationToken) =>
        AnnotationMetrics.Find(request.MetricName) is { } metric
            ? Task.FromResult(new MetricAttributeKeysResponse
            {
                Keys = [.. metric.AttributeKeys.Select(k => new MetricAttributeKeyInfo { Key = k, DistinctValueCount = 0 })],
            })
            : inner.GetAttributeKeysAsync(request, cancellationToken);

    public async Task<MetricQueryResponse> QueryAsync(MetricQueryRequest request, CancellationToken cancellationToken)
    {
        if (AnnotationMetrics.Find(request.MetricName) is not { } metric)
        {
            return await inner.QueryAsync(request, cancellationToken);
        }

        var to = request.Filter.To ?? throw new PromQlException("an annotation query needs an end time.");
        var from = request.Filter.From ?? throw new PromQlException("an annotation query needs a start time.");
        var width = TimeSpan.FromSeconds(Math.Max(1, request.BucketWidthSeconds));
        var series = metric == AnnotationMetrics.AlertFiring
            ? await AlertSeriesAsync(from, to, width, cancellationToken)
            : await ReleaseSeriesAsync(from, to, width, request.Filter.Services, cancellationToken);

        var attributes = request.Filter.Attributes ?? [];
        var wanted = series
            .Where(s => attributes.All(a => s.Attributes.TryGetValue(a.Key, out var v) && v == a.Value))
            .Take(Math.Clamp(request.TopN ?? PromEvaluator.MaxSeries, 1, PromEvaluator.MaxSeries))
            .ToList();
        return new MetricQueryResponse { Series = wanted };
    }

    private async Task<IReadOnlyList<MetricSeries>> AlertSeriesAsync(DateTimeOffset from, DateTimeOffset to, TimeSpan width, CancellationToken cancellationToken)
    {
        var projectAccess = access();
        IReadOnlyCollection<Guid>? visible = null;
        if (!projectAccess.IsUnrestricted)
        {
            visible = [.. projectAccess.Filter(await ruleProjects(cancellationToken), r => r.ProjectId).Select(r => r.RuleId)];
        }

        var incidents = AlertIncidents.Build(await history.GetTransitionsAsync(from, to, visible, cancellationToken), from);
        var now = timeProvider.GetUtcNow();
        var budget = AnnotationMetrics.MaxPoints;
        var result = new List<MetricSeries>();
        foreach (var rule in incidents.GroupBy(i => i.RuleId))
        {
            var points = new List<MetricSeriesPoint>();
            foreach (var incident in rule.OrderBy(i => i.Start))
            {
                var first = Floor(incident.Start < from ? from : incident.Start, width);
                var last = Floor(Min(incident.End ?? now, Min(to, now)), width);
                for (var bucket = first; bucket <= last && bucket < to; bucket += width)
                {
                    // Two incidents can share a bucket (one recovers and the next fires inside it): one point, not two.
                    if (points.Count > 0 && points[^1].BucketStart >= bucket)
                    {
                        continue;
                    }

                    points.Add(new MetricSeriesPoint { BucketStart = bucket, Value = 1 });
                    if (--budget < 0)
                    {
                        throw TooManyPoints();
                    }
                }
            }

            if (points.Count > 0)
            {
                result.Add(new MetricSeries
                {
                    ServiceName = string.Empty,
                    Attributes = new Dictionary<string, string>
                    {
                        [AnnotationMetrics.AlertAttributeName] = rule.First().RuleName,
                        [AnnotationMetrics.AlertAttributeRuleId] = rule.Key.ToString(),
                    },
                    Points = points,
                });
            }
        }

        return [.. result.OrderBy(s => s.Attributes[AnnotationMetrics.AlertAttributeName], StringComparer.Ordinal).ThenBy(s => s.Attributes[AnnotationMetrics.AlertAttributeRuleId], StringComparer.Ordinal)];
    }

    private async Task<IReadOnlyList<MetricSeries>> ReleaseSeriesAsync(DateTimeOffset from, DateTimeOffset to, TimeSpan width, IReadOnlyList<string>? services, CancellationToken cancellationToken)
    {
        var marked = (await releases.ListAsync(null, cancellationToken))
            .Where(r => r.DeployedAt >= from && r.DeployedAt < to)
            .Where(r => services is not { Count: > 0 } || services.Contains(r.Service))
            .OrderBy(r => r.Service, StringComparer.Ordinal).ThenBy(r => r.Version, StringComparer.Ordinal)
            .ToList();
        if (marked.Count > AnnotationMetrics.MaxPoints)
        {
            throw TooManyPoints();
        }

        // One series per release, so a deploy keeps its own commit and link as labels; a release has exactly one point.
        return [.. marked.Select(r => new MetricSeries
        {
            ServiceName = r.Service,
            Attributes = ReleaseAttributes(r),
            Points = [new MetricSeriesPoint { BucketStart = Floor(r.DeployedAt, width), Value = 1 }],
        })];
    }

    private static Dictionary<string, string> ReleaseAttributes(Release r)
    {
        var attributes = new Dictionary<string, string> { [AnnotationMetrics.ReleaseAttributeVersion] = r.Version };
        if (r.Commit.Length > 0)
        {
            attributes[AnnotationMetrics.ReleaseAttributeCommit] = r.Commit;
        }

        if (r.Url.Length > 0)
        {
            attributes[AnnotationMetrics.ReleaseAttributeUrl] = r.Url;
        }

        return attributes;
    }

    private static PromQlException TooManyPoints() =>
        new($"annotations have more than {AnnotationMetrics.MaxPoints} points in this range. Use a larger step or a shorter range.");

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    private static DateTimeOffset Floor(DateTimeOffset t, TimeSpan width) =>
        DateTimeOffset.FromUnixTimeMilliseconds(t.ToUnixTimeMilliseconds() / (long)width.TotalMilliseconds * (long)width.TotalMilliseconds);
}
