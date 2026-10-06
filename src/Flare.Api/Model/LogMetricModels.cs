using System.Text.RegularExpressions;
using MemoryPack;

namespace Flare.Api.Model;

/// <summary>
/// A saved log-based metric: the logs a <see cref="Condition"/> matches are counted at ingest
/// (per service, per <see cref="GroupBy"/> attribute values) and written to <c>metrics_sum</c>
/// as a delta sum named <see cref="MetricName"/>, so charting or alerting on "count of X" reads
/// a metric instead of scanning logs. See docs-internal/adr/0140-log-based-metrics.md.
/// </summary>
[MemoryPackable]
public sealed partial record LogMetric
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = "";

    public bool Enabled { get; init; } = true;

    /// <summary>The emitted metric's name, e.g. <c>logs.checkout.errors</c>.</summary>
    public required string MetricName { get; init; }

    /// <summary>Which logs are counted. An empty filter counts every log.</summary>
    public required LogFilter Condition { get; init; }

    /// <summary>Attribute keys that become data-point attributes of the metric. Each is a series dimension, so keep them low-cardinality.</summary>
    public required IReadOnlyList<string> GroupBy { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

[MemoryPackable]
public sealed partial record LogMetricListResponse
{
    public required IReadOnlyList<LogMetric> Metrics { get; init; }
}

/// <summary>Create/update request body for <c>/api/log-metrics</c>. <see cref="Enabled"/> is nullable for the same omitted-vs-false reason as <see cref="PipelineRuleRequest"/>.</summary>
[MemoryPackable]
public sealed partial record LogMetricRequest
{
    public const int MaxGroupByKeys = 5;

    private static readonly Regex MetricNamePattern = new(@"^[A-Za-z][A-Za-z0-9_.\-]{0,199}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public required string Name { get; init; }

    public string? Description { get; init; }

    public bool? Enabled { get; init; }

    public required string MetricName { get; init; }

    public LogFilter? Condition { get; init; }

    public IReadOnlyList<string>? GroupBy { get; init; }

    /// <summary>
    /// A non-blank name, a metric name that is a plain identifier (letters, digits, <c>_ . -</c>,
    /// starting with a letter, up to 200 characters), and at most <see cref="MaxGroupByKeys"/>
    /// non-blank group-by keys. Returns an error message, or null when valid.
    /// </summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return "Name is required.";
        }

        if (string.IsNullOrWhiteSpace(MetricName) || !MetricNamePattern.IsMatch(MetricName.Trim()))
        {
            return "Metric name must start with a letter and use only letters, digits, '_', '.' and '-' (up to 200 characters).";
        }

        if (GroupBy is { } groupBy)
        {
            if (groupBy.Any(string.IsNullOrWhiteSpace))
            {
                return "Group-by keys must not be blank.";
            }

            if (groupBy.Select(k => k.Trim()).Distinct(StringComparer.Ordinal).Count() > MaxGroupByKeys)
            {
                return $"At most {MaxGroupByKeys} group-by keys are allowed.";
            }
        }

        return null;
    }
}

/// <summary>Body of <c>POST /api/log-metrics/preview</c>: a draft's condition and group-by keys, no name needed.</summary>
[MemoryPackable]
public sealed partial record LogMetricPreviewRequest
{
    public LogFilter? Condition { get; init; }

    public IReadOnlyList<string>? GroupBy { get; init; }

    /// <summary>Same group-by rules as <see cref="LogMetricRequest.Validate"/>; returns an error message, or null when valid.</summary>
    public string? Validate() =>
        GroupBy is null
            ? null
            : new LogMetricRequest { Name = "preview", MetricName = "preview", GroupBy = GroupBy }.Validate();
}

/// <summary>One series the draft would emit: the group-by values (aligned with the request's keys; empty when a log has no value) and how many logs carried them.</summary>
[MemoryPackable]
public sealed partial record LogMetricPreviewSeries
{
    public required string ServiceName { get; init; }

    public required IReadOnlyList<string> Values { get; init; }

    public required long Count { get; init; }
}

/// <summary>
/// Dry-run of a log metric over the last <see cref="WindowMinutes"/> minutes of stored logs.
/// <see cref="SeriesCount"/> is capped at <see cref="Query.LogMetricPreviewQueryBuilder.MaxSeries"/>
/// (<see cref="SeriesCapped"/> says it was hit); <see cref="TotalLogs"/> counts only the series read.
/// </summary>
[MemoryPackable]
public sealed partial record LogMetricPreviewResponse
{
    public required int WindowMinutes { get; init; }

    public required long TotalLogs { get; init; }

    public required int SeriesCount { get; init; }

    public required bool SeriesCapped { get; init; }

    /// <summary>Highest-count series first, at most <see cref="Query.LogMetricPreviewQueryBuilder.TopSeries"/>.</summary>
    public required IReadOnlyList<LogMetricPreviewSeries> Top { get; init; }
}
