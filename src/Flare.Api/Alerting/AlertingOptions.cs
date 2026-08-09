namespace Flare.Api.Alerting;

/// <summary>
/// Tuning knobs for <see cref="AlertEvaluationWorker"/>. Bound from the
/// <c>Alerting</c> configuration section.
/// </summary>
/// <remarks>
/// Per-rule defaults (window, cooldown) live on <see cref="Model.AlertRuleRequest"/>
/// itself, not here - there's no separate "default window" config to keep in sync with
/// the model's own defaults.
/// </remarks>
public sealed class AlertingOptions
{
    public const string SectionName = "Alerting";

    /// <summary>How often every enabled rule is re-evaluated.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Safety cap on rules evaluated per tick - each rule costs one ClickHouse count
    /// query (and, on a breach, one last-fired lookup), so this bounds how long a single
    /// tick can take rather than letting an unbounded rule count stall the loop.
    /// </summary>
    public int MaxRulesPerTick { get; set; } = 500;
}
