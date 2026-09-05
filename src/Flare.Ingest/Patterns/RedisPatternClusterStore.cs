using System.Text.Json;
using Flare.Ingest.Pipeline;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Flare.Ingest.Patterns;

/// <summary>
/// Redis-backed <see cref="IPatternClusterStore"/> - the actual fix for
/// docs/clustering.md's "Drain log-pattern clustering state doesn't share across
/// Flare.Ingest replicas" limitation. One Redis string key per bucket
/// (<see cref="PatternClusterKeys.BucketKey"/>) holds the serialized cluster list;
/// reads/writes go through a StackExchange.Redis conditional transaction for
/// compare-and-swap - no Lua needed, same "let the client library provide the primitive"
/// idiom as <c>Flare.Api.Alerting.AlertEvaluationWorker</c>'s <c>LockTakeAsync</c>. Opt-in
/// via <see cref="LogPatternOptions.SharedStore"/> - see <c>Program.cs</c> for the DI
/// selection between this and <see cref="InMemoryPatternClusterStore"/>.
/// </summary>
/// <remarks>
/// Eviction here is TTL-based, not the exact-count LRU cap
/// <see cref="InMemoryPatternClusterStore"/> enforces: every successful save refreshes
/// <see cref="LogPatternOptions.SharedTemplateTtl"/> on the bucket key, so a
/// rarely-touched template's key simply expires on its own. This trades exact cap parity
/// for zero cross-replica coordination cost on the save path (an exact global cap would
/// need a shared counter/sorted-set touched on every write) - the same "buys nothing
/// here" simplicity tradeoff <c>AlertEvaluationWorker</c>'s own remarks already made
/// about skipping Redlock. <see cref="LogPatternOptions.MaxTemplates"/> does not apply to
/// this store.
/// <para/>
/// Wire format (ADR-0017): the bucket's <see cref="ClusterRecord"/> list is MemoryPack-
/// encoded via <see cref="RedisEventPayload"/>, then base64-text-encoded on top of that -
/// unlike the Redis Stream buffers (<see cref="ClickHouseFlushWorker"/> and siblings),
/// this store's "version" *is* the literal Redis value (<c>Condition.StringEqual</c>
/// against the exact bytes <see cref="LoadAsync"/> read back), so it has to survive a
/// round trip through a C#
/// <see langword="string"/> losslessly - raw MemoryPack bytes are frequently invalid
/// UTF-8 and would get mangled (lossy replacement characters) by that round trip, while
/// base64 text is pure ASCII and round-trips exactly. A pre-upgrade bucket's value is
/// plain JSON text, which is never valid base64 (JSON's <c>{</c>/<c>"</c>/<c>:</c>/<c>,</c>
/// aren't in the base64 alphabet) - <see cref="DecodeClusters"/> uses exactly that to
/// distinguish the two without a separate marker, same one-time upgrade-seam idea as
/// <see cref="RedisEventPayload"/>'s leading tag byte.
/// </remarks>
public sealed class RedisPatternClusterStore(
    IConnectionMultiplexer connectionMultiplexer,
    IOptions<LogPatternOptions> options) : IPatternClusterStore
{
    public async Task<(IReadOnlyList<ClusterRecord> Clusters, string? Version)> LoadAsync(
        int tokenCount, string firstToken, CancellationToken cancellationToken)
    {
        var db = connectionMultiplexer.GetDatabase();
        var key = PatternClusterKeys.BucketKey(tokenCount, firstToken);
        RedisValue raw = await db.StringGetAsync(key).WaitAsync(cancellationToken);
        if (raw.IsNull)
        {
            return ([], null);
        }

        var text = (string)raw!;
        return (DecodeClusters(text), text);
    }

    public async Task<bool> TrySaveAsync(
        int tokenCount,
        string firstToken,
        string? expectedVersion,
        IReadOnlyList<ClusterRecord> clusters,
        CancellationToken cancellationToken)
    {
        var db = connectionMultiplexer.GetDatabase();
        var key = PatternClusterKeys.BucketKey(tokenCount, firstToken);
        var payload = Convert.ToBase64String(RedisEventPayload.Encode(clusters as ClusterRecord[] ?? [.. clusters]));

        var tran = db.CreateTransaction();
        tran.AddCondition(expectedVersion is null
            ? Condition.KeyNotExists(key)
            : Condition.StringEqual(key, expectedVersion));
        _ = tran.StringSetAsync(key, payload, options.Value.SharedTemplateTtl);
        return await tran.ExecuteAsync().WaitAsync(cancellationToken);
    }

    /// <summary>See the class remarks for why base64-vs-JSON, rather than a marker byte, is the format switch here.</summary>
    private static IReadOnlyList<ClusterRecord> DecodeClusters(string text)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(text);
        }
        catch (FormatException)
        {
            // Pre-ADR-0017 entry: plain JSON text, not base64 - see the class remarks.
            return JsonSerializer.Deserialize(text, PatternClusterRecordJsonContext.Default.ClusterRecordArray) ?? [];
        }

        return RedisEventPayload.Decode(bytes, PatternClusterRecordJsonContext.Default.ClusterRecordArray);
    }
}
