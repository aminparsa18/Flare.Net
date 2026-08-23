using System.Text.Json;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Flare.Ingest.Patterns;

/// <summary>
/// Redis-backed <see cref="IPatternClusterStore"/> - the actual fix for
/// docs/clustering.md's "Drain log-pattern clustering state doesn't share across
/// Flare.Ingest replicas" limitation. One Redis string key per bucket
/// (<see cref="PatternClusterKeys.BucketKey"/>) holds the JSON-serialized cluster list;
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

        var json = (string)raw!;
        var clusters = JsonSerializer.Deserialize(json, PatternClusterRecordJsonContext.Default.ClusterRecordArray);
        return (clusters ?? [], json);
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
        var payload = JsonSerializer.Serialize(
            clusters as ClusterRecord[] ?? [.. clusters],
            PatternClusterRecordJsonContext.Default.ClusterRecordArray);

        var tran = db.CreateTransaction();
        tran.AddCondition(expectedVersion is null
            ? Condition.KeyNotExists(key)
            : Condition.StringEqual(key, expectedVersion));
        _ = tran.StringSetAsync(key, payload, options.Value.SharedTemplateTtl);
        return await tran.ExecuteAsync().WaitAsync(cancellationToken);
    }
}
