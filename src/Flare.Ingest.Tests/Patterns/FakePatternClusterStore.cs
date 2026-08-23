using System.Collections.Concurrent;
using System.Globalization;
using Flare.Ingest.Patterns;

namespace Flare.Ingest.Tests.Patterns;

/// <summary>
/// Hand-written <see cref="IPatternClusterStore"/> test double simulating Redis-style
/// compare-and-swap over a shared <see cref="ConcurrentDictionary{TKey,TValue}"/> - lets
/// tests exercise multiple <see cref="DrainPatternMatcher"/> instances "sharing state"
/// (proving the actual cross-replica fix, see <see cref="DrainPatternMatcherTests"/>'s
/// convergence tests) without a real Redis. This repo has no Redis test fixture and
/// deliberately avoids Testcontainers (see <c>Flare.Cli.Internal.ComposeRunner</c>'s
/// remarks) - <see cref="IPatternClusterStore"/> is narrow enough that a fake is cheap to
/// write and keeps the test fast/deterministic, same no-mocking-framework convention as
/// <see cref="FakeLogPatternMatcher"/>. <see cref="RedisPatternClusterStore"/> itself is
/// verified manually (docs/clustering.md's "Verifying it yourself" section), not against
/// this fake.
/// </summary>
public sealed class FakePatternClusterStore : IPatternClusterStore
{
    private readonly ConcurrentDictionary<(int TokenCount, string FirstToken), Entry> buckets = new();

    public Task<(IReadOnlyList<ClusterRecord> Clusters, string? Version)> LoadAsync(
        int tokenCount, string firstToken, CancellationToken cancellationToken)
    {
        var key = (tokenCount, firstToken);
        if (buckets.TryGetValue(key, out var entry))
        {
            return Task.FromResult<(IReadOnlyList<ClusterRecord>, string?)>((entry.Clusters, Version(entry.Revision)));
        }
        return Task.FromResult<(IReadOnlyList<ClusterRecord>, string?)>(([], null));
    }

    public Task<bool> TrySaveAsync(
        int tokenCount,
        string firstToken,
        string? expectedVersion,
        IReadOnlyList<ClusterRecord> clusters,
        CancellationToken cancellationToken)
    {
        var key = (tokenCount, firstToken);

        if (expectedVersion is null)
        {
            return Task.FromResult(buckets.TryAdd(key, new Entry(clusters, 1)));
        }

        if (!buckets.TryGetValue(key, out var current) || Version(current.Revision) != expectedVersion)
        {
            return Task.FromResult(false);
        }

        var updated = new Entry(clusters, current.Revision + 1);
        return Task.FromResult(buckets.TryUpdate(key, updated, current));
    }

    private static string Version(int revision) => revision.ToString(CultureInfo.InvariantCulture);

    private sealed record Entry(IReadOnlyList<ClusterRecord> Clusters, int Revision);
}
