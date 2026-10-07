using MemoryPack;

namespace Flare.Api.Retention;

/// <summary>Lifecycle of one retention change; stored in <c>retention_operations.Status</c>.</summary>
public static class RetentionStatus
{
    public const string Pending = "pending";
    public const string Success = "success";
    public const string Failed = "failed";
}

/// <summary>One signal's retention: what ClickHouse actually has versus what was last asked for.</summary>
[MemoryPackable]
public sealed partial record SignalRetention
{
    public required string Signal { get; init; }

    public required IReadOnlyList<string> Tables { get; init; }

    /// <summary>The TTL parsed live from ClickHouse, in days. Null when there is none (kept forever) or <see cref="ActualState"/> is not <c>days</c>.</summary>
    public int? ActualDays { get; init; }

    /// <summary><c>none</c> (no TTL), <c>days</c>, or <c>custom</c> (a TTL Flare didn't write, or tables of one signal disagreeing).</summary>
    public required string ActualState { get; init; }

    /// <summary>What the last request asked for (0 = keep forever). Null if retention was never set through Flare.</summary>
    public int? ExpectedDays { get; init; }

    /// <summary><c>pending</c>, <c>success</c> or <c>failed</c> for the last request; null if there never was one.</summary>
    public string? Status { get; init; }

    public string? Error { get; init; }

    public Guid? TransactionId { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}

[MemoryPackable]
public sealed partial record RetentionResponse
{
    public required IReadOnlyList<SignalRetention> Signals { get; init; }
}

/// <summary>Body of <c>PUT /api/retention</c>: signal name to days (0 = keep forever). Signals left out are untouched.</summary>
[MemoryPackable]
public sealed partial record SetRetentionRequest
{
    public IReadOnlyDictionary<string, int>? Signals { get; init; }

    public string? Validate()
    {
        if (Signals is not { Count: > 0 })
        {
            return "signals is required and must name at least one signal.";
        }

        foreach (var (name, days) in Signals)
        {
            if (RetentionSignal.Find(name) is null)
            {
                return $"Unknown signal '{name}'. Expected one of: {string.Join(", ", RetentionSignal.All.Select(s => s.Name))}.";
            }

            if (days is < 0 or > RetentionSql.MaxDays)
            {
                return $"{name}: days must be between 0 (keep forever) and {RetentionSql.MaxDays}.";
            }
        }

        return null;
    }
}

[MemoryPackable]
public sealed partial record SetRetentionResponse
{
    public required Guid TransactionId { get; init; }
}
