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

    /// <summary>The TTL parsed live from ClickHouse, in days (0 = rows are never deleted, which with <see cref="ActualColdAfterDays"/> set means tier-only). Null when there is no TTL or <see cref="ActualState"/> is not <c>days</c>.</summary>
    public int? ActualDays { get; init; }

    /// <summary>Days until parts move to the cold volume, parsed live. Null when they don't.</summary>
    public int? ActualColdAfterDays { get; init; }

    /// <summary><c>none</c> (no TTL), <c>days</c>, or <c>custom</c> (a TTL Flare didn't write, or tables of one signal disagreeing).</summary>
    public required string ActualState { get; init; }

    /// <summary>What the last request asked for (0 = keep forever). Null if retention was never set through Flare.</summary>
    public int? ExpectedDays { get; init; }

    /// <summary>The cold-after value of the last request (0 = no tiering). Null if retention was never set through Flare.</summary>
    public int? ExpectedColdAfterDays { get; init; }

    /// <summary><c>pending</c>, <c>success</c> or <c>failed</c> for the last request; null if there never was one.</summary>
    public string? Status { get; init; }

    public string? Error { get; init; }

    public Guid? TransactionId { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}

/// <summary>One ClickHouse disk as the retention UI offers it.</summary>
[MemoryPackable]
public sealed partial record StorageDisk
{
    public required string Name { get; init; }

    /// <summary><c>Local</c> or <c>ObjectStorage</c> (S3-compatible, e.g. RustFS).</summary>
    public required string Type { get; init; }

    public long FreeBytes { get; init; }

    public long TotalBytes { get; init; }
}

/// <summary>Whether the cold tier is configured, read from <c>system.storage_policies</c> / <c>system.disks</c> so the UI only offers what exists.</summary>
[MemoryPackable]
public sealed partial record ColdStorageInfo
{
    /// <summary>True when ClickHouse has the <c>flare_tiered</c> policy with a <c>cold</c> volume.</summary>
    public required bool Available { get; init; }

    public required IReadOnlyList<StorageDisk> Disks { get; init; }
}

[MemoryPackable]
public sealed partial record RetentionResponse
{
    public required IReadOnlyList<SignalRetention> Signals { get; init; }

    public required ColdStorageInfo ColdStorage { get; init; }
}

/// <summary>Body of <c>PUT /api/retention</c>: signal name to days (0 = keep forever). Signals left out are untouched.</summary>
[MemoryPackable]
public sealed partial record SetRetentionRequest
{
    public IReadOnlyDictionary<string, int>? Signals { get; init; }

    /// <summary>Signal name to the age in days at which its data moves to cold storage. Optional; a signal named here must also be in <see cref="Signals"/>, and the value must be less than its retention unless that is 0 (tier-only, keep forever).</summary>
    public IReadOnlyDictionary<string, int>? ColdAfterDays { get; init; }

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

        foreach (var (name, coldDays) in ColdAfterDays ?? new Dictionary<string, int>())
        {
            var requested = Signals.Where(kv => string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (requested.Length == 0)
            {
                return $"coldAfterDays names '{name}', which is not in signals.";
            }

            var days = requested[0].Value;
            if (coldDays is < 0 or > RetentionSql.MaxDays)
            {
                return $"{name}: coldAfterDays must be between 0 (no tiering) and {RetentionSql.MaxDays}.";
            }

            if (coldDays > 0 && days > 0 && coldDays >= days)
            {
                return $"{name}: coldAfterDays ({coldDays}) must be less than its retention ({days}), or the data would be deleted before it moves.";
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
