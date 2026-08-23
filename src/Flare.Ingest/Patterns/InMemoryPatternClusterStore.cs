using System.Globalization;
using Microsoft.Extensions.Options;

namespace Flare.Ingest.Patterns;

/// <summary>
/// Default <see cref="IPatternClusterStore"/> - a per-process nested dictionary, the same
/// shape and tree-wide LRU eviction behavior <see cref="DrainPatternMatcher"/> used to own
/// directly before cluster storage was split out behind <see cref="IPatternClusterStore"/>
/// (see <see cref="RedisPatternClusterStore"/> for the cross-replica alternative).
/// Singleton lifetime, one instance per process - no persistence across a restart or
/// across replicas, same accepted limitation as before this split, just scoped to this
/// class instead of <c>DrainPatternMatcher</c> itself. This is the store every deployment
/// gets by default (<see cref="LogPatternOptions.SharedStore"/> = <see langword="false"/>) -
/// zero added overhead versus the matcher owning the tree itself, no network I/O.
/// </summary>
public sealed class InMemoryPatternClusterStore(IOptions<LogPatternOptions> options) : IPatternClusterStore
{
    private readonly Lock gate = new();
    private readonly Dictionary<(int TokenCount, string FirstToken), Bucket> buckets = [];
    private int clusterCount;

    public Task<(IReadOnlyList<ClusterRecord> Clusters, string? Version)> LoadAsync(
        int tokenCount, string firstToken, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (!buckets.TryGetValue((tokenCount, firstToken), out var bucket))
            {
                return Task.FromResult<(IReadOnlyList<ClusterRecord>, string?)>(([], null));
            }
            return Task.FromResult<(IReadOnlyList<ClusterRecord>, string?)>(
                ([.. bucket.Clusters], Version(bucket.Revision)));
        }
    }

    public Task<bool> TrySaveAsync(
        int tokenCount,
        string firstToken,
        string? expectedVersion,
        IReadOnlyList<ClusterRecord> clusters,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var key = (tokenCount, firstToken);
            var exists = buckets.TryGetValue(key, out var bucket);
            if (Version(exists ? bucket!.Revision : (int?)null) != expectedVersion)
            {
                return Task.FromResult(false);
            }

            var previousCount = exists ? bucket!.Clusters.Count : 0;
            if (!exists)
            {
                bucket = new Bucket();
                buckets[key] = bucket;
            }
            bucket!.Clusters.Clear();
            bucket.Clusters.AddRange(clusters);
            bucket.Revision++;
            clusterCount += clusters.Count - previousCount;

            EvictWhileOverCapacity();
            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// Evicts the globally least-recently-used cluster(s) - tree-wide, not scoped to the
    /// bucket that was just saved - until back at or under <see cref="LogPatternOptions.MaxTemplates"/>.
    /// A single <see cref="TrySaveAsync"/> call can add more than one cluster at once (a
    /// whole flush batch's worth for one bucket), unlike the original single-line-at-a-time
    /// <c>DrainPatternMatcher.Match</c>, so this loops rather than evicting exactly once.
    /// </summary>
    private void EvictWhileOverCapacity()
    {
        var cap = options.Value.MaxTemplates;
        while (clusterCount > cap)
        {
            (int TokenCount, string FirstToken)? oldestKey = null;
            ClusterRecord? oldest = null;
            foreach (var (key, bucket) in buckets)
            {
                foreach (var candidate in bucket.Clusters)
                {
                    if (oldest is null || candidate.LastUsedTicks < oldest.LastUsedTicks)
                    {
                        oldest = candidate;
                        oldestKey = key;
                    }
                }
            }

            if (oldest is null || oldestKey is null)
            {
                return;
            }

            var bucketToTrim = buckets[oldestKey.Value];
            bucketToTrim.Clusters.Remove(oldest);
            bucketToTrim.Revision++;
            clusterCount--;
            if (bucketToTrim.Clusters.Count == 0)
            {
                buckets.Remove(oldestKey.Value);
            }
        }
    }

    private static string? Version(int? revision) => revision?.ToString(CultureInfo.InvariantCulture);

    private sealed class Bucket
    {
        public List<ClusterRecord> Clusters { get; } = [];
        public int Revision { get; set; }
    }
}
