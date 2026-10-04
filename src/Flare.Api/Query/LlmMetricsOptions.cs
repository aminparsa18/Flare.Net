namespace Flare.Api.Query;

/// <summary>
/// Instant, config-only rollback valve for the flush-time-pre-aggregated <c>llm_model_calls</c>
/// table behind <c>/llm</c> - see ADR-0102. <c>false</c> makes <see cref="LlmQueryService"/> use
/// its live <c>spans</c> queries, exactly as before the rollup, no redeploy or migration
/// rollback needed. Same shape as <see cref="ServiceMetricsOptions.Enabled"/>.
/// </summary>
public sealed class LlmMetricsOptions
{
    public const string SectionName = "LlmMetrics";

    public bool Enabled { get; set; } = true;
}
