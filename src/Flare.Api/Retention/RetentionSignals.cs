namespace Flare.Api.Retention;

/// <summary>One retention-controlled telemetry signal: the data tables a retention setting applies to and the column their TTL is computed from.</summary>
public sealed record RetentionSignal(string Name, string TimeColumn, IReadOnlyList<string> Tables)
{
    public const string Logs = "logs";
    public const string Traces = "traces";
    public const string Metrics = "metrics";
    public const string Profiles = "profiles";

    /// <summary>
    /// The raw telemetry tables only. The pre-aggregated tables fed by materialized views
    /// (<c>service_metrics</c>, <c>outbound_calls</c>, ...) are small rollups and the config
    /// tables are not telemetry, so neither gets a TTL (docs-internal/adr/0143-retention-ttl.md).
    /// </summary>
    public static IReadOnlyList<RetentionSignal> All { get; } =
    [
        new(Logs, "Timestamp", ["logs"]),
        new(Traces, "StartTime", ["spans", "app_screenshots"]),
        new(Metrics, "Time", ["metrics_gauge", "metrics_sum", "metrics_histogram", "metrics_exponential_histogram"]),
        new(Profiles, "Timestamp", ["profile_samples"]),
    ];

    public static RetentionSignal? Find(string name) =>
        All.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
}
