namespace Flare.Api.Slos;

/// <summary>
/// The latency thresholds a latency SLO can use. <c>span_sli_minute</c> (migration 0049) stores
/// one "requests at or under T" counter per rung - a fraction-under-threshold can't be derived
/// from a pre-aggregated quantile - so a threshold off the ladder has no column to read.
/// </summary>
public static class SloLatencyLadder
{
    /// <summary>Rung thresholds in milliseconds, ascending.</summary>
    public static readonly IReadOnlyList<int> ThresholdsMs = [50, 100, 250, 500, 1000, 2500, 5000, 10000];

    public static bool IsRung(int thresholdMs) => ThresholdsMs.Contains(thresholdMs);

    /// <summary>The <c>span_sli_minute</c> column counting requests at or under <paramref name="thresholdMs"/>, or null when it isn't a rung.</summary>
    public static string? Column(int thresholdMs) => IsRung(thresholdMs) ? $"Under{thresholdMs}ms" : null;
}
