using MemoryPack;

namespace Flare.Api.Model;

/// <summary>What an <see cref="Slo"/> counts as a good event. See <c>docs-internal/adr/0108-slo-error-budgets.md</c>.</summary>
/// <remarks><see cref="Availability"/> is first so an omitted JSON <c>kind</c> (which deserializes as 0) means the common default.</remarks>
public enum SloKind
{
    /// <summary>Good = an entry span whose status isn't an error.</summary>
    Availability,

    /// <summary>Good = an entry span that finished within <see cref="Slo.LatencyThresholdMs"/>.</summary>
    Latency,
}

/// <summary>
/// A service level objective over entry-span data: <see cref="TargetPercent"/> of events must be
/// good over a rolling <see cref="WindowDays"/>-day window. Evaluated from the pre-aggregated
/// <c>span_sli_minute</c> table; burn-rate alerting is an <see cref="AlertConditionKind.SloBurnRate"/>
/// rule pointing at one of these.
/// </summary>
[MemoryPackable]
public sealed partial record Slo
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = "";

    public SloKind Kind { get; init; }

    /// <summary>Exact <c>ServiceName</c>.</summary>
    public required string ServiceName { get; init; }

    /// <summary>Entry-span name (the endpoint) to scope to; empty = every entry span of the service.</summary>
    public string OperationName { get; init; } = "";

    /// <summary>Percent of events that must be good, e.g. 99.5.</summary>
    public required double TargetPercent { get; init; }

    /// <summary>Latency SLOs only: a rung of <see cref="Slos.SloLatencyLadder"/>. 0 for availability.</summary>
    public int LatencyThresholdMs { get; init; }

    public required int WindowDays { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Create/update request body for <c>/api/slos</c>.</summary>
[MemoryPackable]
public sealed partial record SloRequest
{
    public const int MaxNameLength = 200;
    public const int MaxOperationLength = 500;
    public const int MinWindowDays = 1;
    public const int MaxWindowDays = 90;
    public const double MinTargetPercent = 1;
    public const double MaxTargetPercent = 99.999;

    public required string Name { get; init; }

    public string? Description { get; init; }

    public SloKind Kind { get; init; }

    public required string ServiceName { get; init; }

    public string? OperationName { get; init; }

    public required double TargetPercent { get; init; }

    public int? LatencyThresholdMs { get; init; }

    /// <summary>Omitted/null means 28.</summary>
    public int? WindowDays { get; init; }

    /// <summary>Returns an error message, or null when this request is valid.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return "name is required.";
        }

        if (Name.Length > MaxNameLength)
        {
            return $"name must be at most {MaxNameLength} characters.";
        }

        if (string.IsNullOrWhiteSpace(ServiceName))
        {
            return "serviceName is required.";
        }

        if ((OperationName?.Length ?? 0) > MaxOperationLength)
        {
            return $"operationName must be at most {MaxOperationLength} characters.";
        }

        if (!Enum.IsDefined(Kind))
        {
            return "Unknown kind.";
        }

        if (!(TargetPercent >= MinTargetPercent && TargetPercent <= MaxTargetPercent))
        {
            return $"targetPercent must be between {MinTargetPercent} and {MaxTargetPercent}.";
        }

        if (WindowDays is { } days && (days < MinWindowDays || days > MaxWindowDays))
        {
            return $"windowDays must be between {MinWindowDays} and {MaxWindowDays}.";
        }

        if (Kind == SloKind.Latency)
        {
            if (LatencyThresholdMs is not { } threshold || !Slos.SloLatencyLadder.IsRung(threshold))
            {
                return $"A latency SLO's latencyThresholdMs must be one of {string.Join(", ", Slos.SloLatencyLadder.ThresholdsMs)}.";
            }
        }
        else if (LatencyThresholdMs is > 0)
        {
            return "latencyThresholdMs applies only to a Latency SLO.";
        }

        return null;
    }
}

/// <summary>Response body for <c>GET /api/slos</c>.</summary>
[MemoryPackable]
public sealed partial record SloListResponse
{
    public required IReadOnlyList<Slo> Slos { get; init; }
}

/// <summary>Good/bad event counts over one trailing window, and the burn rate they imply.</summary>
[MemoryPackable]
public sealed partial record SloWindowStats
{
    public required int WindowSeconds { get; init; }

    public required long Total { get; init; }

    public required long Bad { get; init; }

    /// <summary>How fast the error budget is being spent over this window (1 = exactly the budget); null with no events.</summary>
    public double? BurnRate { get; init; }
}

/// <summary>One hourly bucket of the SLO window, for the budget burn-down chart.</summary>
[MemoryPackable]
public sealed partial record SloSeriesPoint
{
    public required long TimeUnixMs { get; init; }

    public required long Total { get; init; }

    public required long Bad { get; init; }
}

/// <summary>Response body for <c>GET /api/slos/{id}/status</c>.</summary>
[MemoryPackable]
public sealed partial record SloStatus
{
    public required Slo Slo { get; init; }

    /// <summary>Events and bad events over the SLO's whole window.</summary>
    public required long Total { get; init; }

    public required long Bad { get; init; }

    /// <summary>Percent of good events over the window; null with no events.</summary>
    public double? Sli { get; init; }

    /// <summary>Fraction of the error budget unspent: 1 = untouched, 0 = spent, negative = overspent. Null with no events.</summary>
    public double? ErrorBudgetRemaining { get; init; }

    /// <summary>Burn rate over the standard alerting windows (5m, 30m, 1h, 6h, 24h), shortest first.</summary>
    public required IReadOnlyList<SloWindowStats> BurnRates { get; init; }

    /// <summary>Hourly good/bad counts across the SLO window, oldest first.</summary>
    public required IReadOnlyList<SloSeriesPoint> Series { get; init; }
}

/// <summary>
/// The <see cref="AlertConditionKind.SloBurnRate"/> condition: breached when the burn rate of
/// <see cref="SloId"/> is at or above <see cref="BurnRateThreshold"/> over <i>both</i> the long
/// and the short window - the long window shows the burn is real, the short one that it's still
/// happening, so the alert resolves quickly once it stops. Standard pairings: 1h/5m at 14.4
/// (page: 2% of a 30-day budget gone in an hour) and 6h/30m at 6 (ticket).
/// <see cref="MemoryPackableAttribute"/> only, with a hand-written TypeScript companion
/// (<c>$lib/memorypack/SloBurnRateCondition.ts</c>), like <see cref="AnomalyCondition"/>.
/// </summary>
[MemoryPackable]
public sealed partial record SloBurnRateCondition
{
    public const int MinWindowSeconds = 60;
    public const int MaxWindowSeconds = 7 * 24 * 3600;
    public const double MaxBurnRateThreshold = 1000;

    public required Guid SloId { get; init; }

    public required int LongWindowSeconds { get; init; }

    public required int ShortWindowSeconds { get; init; }

    /// <summary>Greater than 0 and at most <see cref="MaxBurnRateThreshold"/>.</summary>
    public required double BurnRateThreshold { get; init; }
}
