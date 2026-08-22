namespace Flare.Ingest.Prometheus;

/// <summary>
/// Tuning knobs for <see cref="PrometheusScrapeWorker"/>. Bound from the
/// <c>PrometheusScrape</c> configuration section, same <c>IOptions&lt;T&gt;</c> convention
/// as <see cref="Pipeline.MetricEventPipelineOptions"/>/<see cref="Patterns.LogPatternOptions"/>.
/// </summary>
/// <remarks>
/// Deliberately no separate "Enabled" flag - an empty <see cref="Targets"/> list (the
/// default) is already a no-op, same as Prometheus's own <c>scrape_configs: []</c>
/// convention, rather than a second switch that could disagree with an empty list.
/// </remarks>
public sealed class PrometheusScrapeOptions
{
    public const string SectionName = "PrometheusScrape";

    /// <summary>Targets to periodically scrape. Empty by default - scraping stays off until configured.</summary>
    public List<PrometheusScrapeTargetOptions> Targets { get; set; } = [];
}

/// <summary>One Prometheus-style <c>/metrics</c> endpoint to scrape on its own interval.</summary>
public sealed class PrometheusScrapeTargetOptions
{
    /// <summary>
    /// Prometheus's own "job" concept - becomes <c>service.name</c> on every point scraped
    /// from this target (see <see cref="PrometheusMetricsMapper"/>'s remarks), same role
    /// the OTel Collector's <c>prometheusreceiver</c> gives it.
    /// </summary>
    public required string Job { get; set; }

    /// <summary>The target's scrape URL, e.g. <c>http://localhost:9100/metrics</c>.</summary>
    public required string Url { get; set; }

    /// <summary>How often to scrape this target. Matches Prometheus's own default.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Per-scrape HTTP timeout.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Extra resource attributes merged onto every point from this target, applied after
    /// (so able to override) the computed <c>service.name</c>/<c>service.instance.id</c> -
    /// see <see cref="PrometheusMetricsMapper"/>.
    /// </summary>
    public Dictionary<string, string> Labels { get; set; } = [];

    /// <summary>Extra HTTP request headers, e.g. <c>Authorization: Bearer &lt;token&gt;</c> for a protected endpoint.</summary>
    public Dictionary<string, string> Headers { get; set; } = [];
}
