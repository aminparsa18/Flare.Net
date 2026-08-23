using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Flare.Ingest.Patterns;

/// <summary>
/// A simplified Drain (github.com/logpai/Drain3-style) log template miner: a fixed-depth
/// parse tree keyed on (token count, first token), token-by-token similarity matching
/// against candidate clusters within that bucket, and progressive generalization
/// (differing token positions become <c>&lt;*&gt;</c>) as new variants of a known shape
/// arrive. No ML/training step - pure string tokenization, run once per log body.
/// </summary>
/// <remarks>
/// Cluster storage lives behind <see cref="IPatternClusterStore"/>, not in this class -
/// this matcher only does the pure masking/tokenizing/similarity/generalization work and
/// orchestrates load-decide-save calls against whichever store is injected. Historically
/// (see docs/clustering.md's former "Known limitations" entry) this class owned an
/// in-memory-only tree directly, which meant independent <c>Flare.Ingest</c> replicas
/// each built their own clusters from whatever subset of logs they happened to consume,
/// fragmenting <c>PatternId</c>s for the same template across replicas.
/// <see cref="InMemoryPatternClusterStore"/> (the default) preserves that exact original
/// behavior for single-node deployments; <see cref="RedisPatternClusterStore"/> (opt-in
/// via <see cref="LogPatternOptions.SharedStore"/>) is the actual fix for the
/// multi-replica case - see its remarks.
/// <para>
/// <see cref="MatchBatchAsync"/> groups a whole flush batch by <c>(tokenCount,
/// firstToken)</c> bucket and does one store round trip per *distinct bucket*, not per
/// log line - real logs cluster heavily, so this keeps Redis round trips proportional to
/// template diversity rather than batch size. Distinct buckets are processed
/// concurrently (independent store keys); within one bucket, a store compare-and-swap
/// guards against another replica updating the same bucket concurrently, with a bounded
/// retry-then-proceed fallback (see <see cref="MatchBucketAsync"/>) - matching
/// <see cref="ILogPatternMatcher"/>'s documented "never throws, the flush path can't
/// afford to fail a whole batch" contract.
/// </para>
/// </remarks>
public sealed partial class DrainPatternMatcher(IPatternClusterStore store, IOptions<LogPatternOptions> options) : ILogPatternMatcher
{
    private const string Wildcard = "<*>";
    private const int MaxSaveAttempts = 3;

    private static readonly PatternMatch EmptyMatch = new(string.Empty, string.Empty);

    // Instance-scoped, thread-safe recency counter (Interlocked, since distinct buckets
    // are processed concurrently) - same role as the old private `clock` field, just
    // travelling inside each ClusterRecord.LastUsedTicks across store round trips instead
    // of staying local to an in-process Dictionary. Only ever compared within one store
    // instance's own eviction logic (see InMemoryPatternClusterStore), so it doesn't need
    // to be wall-clock or cross-instance-comparable.
    private long clock;

    public async Task<IReadOnlyList<PatternMatch>> MatchBatchAsync(IReadOnlyList<string?> bodies, CancellationToken cancellationToken)
    {
        var opts = options.Value;
        var results = new PatternMatch[bodies.Count];
        var tokensByIndex = new string[bodies.Count][];
        var groups = new Dictionary<(int TokenCount, string FirstToken), List<int>>();

        for (var i = 0; i < bodies.Count; i++)
        {
            var text = Normalize(bodies[i], opts.MaxBodyLength);
            if (text.Length == 0)
            {
                results[i] = EmptyMatch;
                continue;
            }

            var tokens = Mask(text).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                results[i] = EmptyMatch;
                continue;
            }

            tokensByIndex[i] = tokens;
            var key = (tokens.Length, tokens[0]);
            if (!groups.TryGetValue(key, out var indices))
            {
                indices = [];
                groups[key] = indices;
            }
            indices.Add(i);
        }

        if (groups.Count > 0)
        {
            await Parallel.ForEachAsync(
                groups,
                new ParallelOptions { CancellationToken = cancellationToken },
                (group, ct) => new ValueTask(MatchBucketAsync(group.Key.TokenCount, group.Key.FirstToken, group.Value, tokensByIndex, results, ct)));
        }

        return results;
    }

    /// <summary>
    /// Loads one bucket, runs the existing match/generalize decision loop across every
    /// line in this batch that belongs to it, and saves the result back with a
    /// compare-and-swap - retrying on conflict up to <see cref="MaxSaveAttempts"/> times.
    /// Repeated conflict is expected to be rare (whole-bucket batching already collapses
    /// most contention) - on the final attempt's failure, the locally-computed decisions
    /// are kept as-is rather than failing the batch.
    /// </summary>
    private async Task MatchBucketAsync(
        int tokenCount,
        string firstToken,
        List<int> indices,
        string[][] tokensByIndex,
        PatternMatch[] results,
        CancellationToken cancellationToken)
    {
        var opts = options.Value;

        for (var attempt = 1; attempt <= MaxSaveAttempts; attempt++)
        {
            var (loaded, version) = await store.LoadAsync(tokenCount, firstToken, cancellationToken);
            var clusters = loaded.Select(ToMutable).ToList();

            foreach (var index in indices)
            {
                var tokens = tokensByIndex[index];
                var tick = Interlocked.Increment(ref clock);

                var best = FindBestMatch(clusters, tokens, out var bestSimilarity);
                if (best is not null && bestSimilarity >= opts.SimilarityThreshold)
                {
                    if (Generalize(best.TemplateTokens, tokens))
                    {
                        best.PatternId = ComputePatternId(best.TemplateTokens);
                    }
                    best.LastUsedTicks = tick;
                    results[index] = new PatternMatch(best.PatternId, string.Join(' ', best.TemplateTokens));
                    continue;
                }

                var templateTokens = (string[])tokens.Clone();
                var created = new Cluster
                {
                    Id = Guid.NewGuid().ToString("N"),
                    TemplateTokens = templateTokens,
                    PatternId = ComputePatternId(templateTokens),
                    LastUsedTicks = tick,
                };
                clusters.Add(created);
                results[index] = new PatternMatch(created.PatternId, string.Join(' ', created.TemplateTokens));
            }

            var updated = clusters.Select(c => new ClusterRecord(c.Id, c.TemplateTokens, c.PatternId, c.LastUsedTicks)).ToArray();
            if (await store.TrySaveAsync(tokenCount, firstToken, version, updated, cancellationToken))
            {
                return;
            }
        }
    }

    private static Cluster ToMutable(ClusterRecord record) => new()
    {
        Id = record.Id,
        TemplateTokens = (string[])record.TemplateTokens.Clone(),
        PatternId = record.PatternId,
        LastUsedTicks = record.LastUsedTicks,
    };

    private static Cluster? FindBestMatch(List<Cluster> clusters, string[] tokens, out double bestSimilarity)
    {
        Cluster? best = null;
        bestSimilarity = -1d;
        foreach (var candidate in clusters)
        {
            var similarity = Similarity(candidate.TemplateTokens, tokens);
            if (similarity > bestSimilarity)
            {
                bestSimilarity = similarity;
                best = candidate;
            }
        }
        return best;
    }

    /// <summary>Fraction of positions where <paramref name="template"/> already matches or wildcards <paramref name="tokens"/>. Both arrays are always the same length - callers only compare within the same token-count bucket.</summary>
    private static double Similarity(string[] template, string[] tokens)
    {
        var matches = 0;
        for (var i = 0; i < template.Length; i++)
        {
            if (template[i] == Wildcard || template[i] == tokens[i])
            {
                matches++;
            }
        }
        return (double)matches / template.Length;
    }

    /// <summary>Widens <paramref name="template"/> in place to <c>&lt;*&gt;</c> at every position that disagrees with <paramref name="tokens"/>. Returns whether the template text actually changed (callers use this to decide whether the cached PatternId needs recomputing).</summary>
    private static bool Generalize(string[] template, string[] tokens)
    {
        var changed = false;
        for (var i = 0; i < template.Length; i++)
        {
            if (template[i] != Wildcard && template[i] != tokens[i])
            {
                template[i] = Wildcard;
                changed = true;
            }
        }
        return changed;
    }

    private static string Normalize(string? body, int maxLength) =>
        string.IsNullOrEmpty(body) ? string.Empty : body.Length > maxLength ? body[..maxLength] : body;

    /// <summary>
    /// Wildcards UUID, hex, and decimal-number substrings before whitespace tokenization -
    /// run in this order (UUID, then hex, then numeric) so a whole UUID collapses to one
    /// <c>&lt;*&gt;</c> before the hex/numeric passes can fragment it, and so a path segment
    /// like "/api/orders/123" collapses correctly even though it's one whitespace-delimited
    /// token, not three.
    /// </summary>
    private static string Mask(string text)
    {
        text = UuidPattern().Replace(text, Wildcard);
        text = HexPattern().Replace(text, Wildcard);
        text = NumericPattern().Replace(text, Wildcard);
        return text;
    }

    private static string ComputePatternId(string[] templateTokens)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(' ', templateTokens)));
        // First 16 hex chars (64 bits) of SHA-256 over the finalized template text -
        // deterministic (the same template re-emerging after a restart, or independently
        // on another replica, gets the same id) and, at this matcher's MaxTemplates-bounded
        // live cardinality, collision odds are negligible (birthday-bound 50% risk needs
        // ~2^32 distinct templates).
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    // Digit-presence lookahead means a pure-letter hex-charset word ("cafe", "deaf") is
    // deliberately spared - reduces, but doesn't eliminate, false positives on real words
    // that happen to be spelled entirely with a-f. Positive-only (no leading sign), so a
    // negative number like "-42" masks to "-<*>" rather than "<*>" - accepted, not fixed.
    [GeneratedRegex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b")]
    private static partial Regex UuidPattern();

    [GeneratedRegex(@"\b(?=[0-9a-fA-F]*\d)[0-9a-fA-F]{6,}\b")]
    private static partial Regex HexPattern();

    [GeneratedRegex(@"\b\d+(\.\d+)?\b")]
    private static partial Regex NumericPattern();

    private sealed class Cluster
    {
        public required string Id { get; init; }
        public required string[] TemplateTokens { get; set; }
        public required string PatternId { get; set; }
        public long LastUsedTicks { get; set; }
    }
}
