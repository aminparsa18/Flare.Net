using MemoryPack;

namespace Flare.Ingest.Patterns;

/// <summary>
/// One Drain cluster's persisted shape - the unit <see cref="IPatternClusterStore"/> loads/saves per bucket.
/// <see cref="MemoryPackableAttribute"/>: the wire format <see cref="RedisPatternClusterStore"/>
/// uses for this record (ADR-0017) - <see cref="InMemoryPatternClusterStore"/> never
/// serializes it at all.
/// </summary>
[MemoryPackable]
public sealed partial record ClusterRecord(string Id, string[] TemplateTokens, string PatternId, long LastUsedTicks);

/// <summary>
/// Storage seam for <see cref="DrainPatternMatcher"/>'s cluster tree - split out so the
/// matcher's masking/tokenizing/similarity/generalization logic (pure, unchanged) can run
/// against either a per-process dictionary (<see cref="InMemoryPatternClusterStore"/>,
/// today's default/single-node behavior) or a Redis-backed store shared across
/// <c>Flare.Ingest</c> replicas (<see cref="RedisPatternClusterStore"/>, opt-in via
/// <see cref="LogPatternOptions.SharedStore"/>) without duplicating that logic anywhere
/// else. One bucket = one <c>(tokenCount, firstToken)</c> pair, matching the matcher's own
/// tree keying - see <see cref="PatternClusterKeys"/> for how a bucket becomes a key.
/// </summary>
public interface IPatternClusterStore
{
    /// <summary>
    /// Loads every cluster currently in a bucket, plus an opaque <c>Version</c> token
    /// identifying the exact state read - pass it back to <see cref="TrySaveAsync"/> for
    /// optimistic concurrency. <see langword="null"/> version means the bucket doesn't
    /// exist yet.
    /// </summary>
    Task<(IReadOnlyList<ClusterRecord> Clusters, string? Version)> LoadAsync(
        int tokenCount, string firstToken, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically replaces a bucket's entire cluster list, succeeding only if the store's
    /// current state still matches <paramref name="expectedVersion"/> (the value
    /// <see cref="LoadAsync"/> returned) - a compare-and-swap, not a blind overwrite, so
    /// two replicas racing to update the same bucket can't silently clobber each other.
    /// Returns <see langword="false"/> on conflict; callers should reload and retry.
    /// </summary>
    Task<bool> TrySaveAsync(
        int tokenCount,
        string firstToken,
        string? expectedVersion,
        IReadOnlyList<ClusterRecord> clusters,
        CancellationToken cancellationToken);
}
