using ClickHouse.Driver;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface IMetricAttributeRuleCoverageService
{
    Task<MetricAttributeRulePreviewResponse> PreviewAsync(MetricAttributeRulePreviewRequest request, CancellationToken cancellationToken);

    Task<MetricAttributeRuleUnmatchedResponse> FindUnmatchedAsync(int? windowMinutes, CancellationToken cancellationToken);
}

/// <summary>ClickHouse seam for the attribute-rule dry-run and unmatched-rule check - see <see cref="MetricAttributeRulePreviewQueryBuilder"/> for the SQL.</summary>
public sealed class MetricAttributeRuleCoverageService(
    IClickHouseClient client,
    IMetricAttributeRuleQueryService rules,
    IOptions<QueryLimitsOptions> queryLimits,
    TimeProvider timeProvider) : IMetricAttributeRuleCoverageService
{
    public async Task<MetricAttributeRulePreviewResponse> PreviewAsync(MetricAttributeRulePreviewRequest request, CancellationToken cancellationToken)
    {
        var windowMinutes = MetricCatalogQueryBuilder.ClampWindowMinutes(request.WindowMinutes);
        var built = MetricAttributeRulePreviewQueryBuilder.BuildPreview(request, windowMinutes, timeProvider.GetUtcNow());

        var metrics = new List<MetricAttributeRulePreviewMetric>();
        await using (var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken))
        {
            while (reader.Read())
            {
                metrics.Add(new MetricAttributeRulePreviewMetric
                {
                    MetricName = reader.GetString(0),
                    SeriesBefore = (long)reader.GetFieldValue<ulong>(1),
                    SeriesAfter = (long)reader.GetFieldValue<ulong>(2),
                });
            }
        }

        var truncated = metrics.Count > MetricAttributeRulePreviewQueryBuilder.MaxMetrics;
        if (truncated)
        {
            metrics.RemoveRange(MetricAttributeRulePreviewQueryBuilder.MaxMetrics, metrics.Count - MetricAttributeRulePreviewQueryBuilder.MaxMetrics);
        }

        return new MetricAttributeRulePreviewResponse
        {
            WindowMinutes = windowMinutes,
            Metrics = metrics,
            SeriesBefore = metrics.Sum(m => m.SeriesBefore),
            SeriesAfter = metrics.Sum(m => m.SeriesAfter),
            Truncated = truncated,
        };
    }

    public async Task<MetricAttributeRuleUnmatchedResponse> FindUnmatchedAsync(int? windowMinutes, CancellationToken cancellationToken)
    {
        var window = MetricCatalogQueryBuilder.ClampWindowMinutes(windowMinutes);
        var saved = await rules.ListAsync(cancellationToken);
        if (saved.Count == 0)
        {
            return new MetricAttributeRuleUnmatchedResponse { WindowMinutes = window, RuleIds = [] };
        }

        var built = MetricAttributeRulePreviewQueryBuilder.BuildMetricNames(window, timeProvider.GetUtcNow());
        var names = new List<string>();
        await using (var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken))
        {
            while (reader.Read())
            {
                names.Add(reader.GetString(0));
            }
        }

        return new MetricAttributeRuleUnmatchedResponse
        {
            WindowMinutes = window,
            RuleIds = saved.Where(r => !names.Any(n => MetricAttributeRulePreviewQueryBuilder.Matches(r.MetricName, n))).Select(r => r.Id).ToList(),
        };
    }

    private QueryOptions SafetyOptions() => QuerySafety.Full(queryLimits.Value);
}
