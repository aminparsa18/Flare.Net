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
/// In-memory only, singleton-scoped, no persistence across a <c>Flare.Ingest</c> restart -
/// matches this codebase's existing single-instance deployment model (see
/// <c>LogEventPipelineOptions.ConsumerName</c>'s own remarks on the same gap) rather than
/// solving a problem the rest of the pipeline doesn't solve yet. <see cref="Match"/> is
/// called from <see cref="LogPatternAnnotator"/>, itself only ever invoked from
/// <c>ClickHouseFlushWorker</c>'s single background loop today - the internal <c>lock</c>
/// is cheap insurance against that changing, not a response to measured contention.
/// </remarks>
public sealed partial class DrainPatternMatcher(IOptions<LogPatternOptions> options) : ILogPatternMatcher
{
    private const string Wildcard = "<*>";

    private static readonly PatternMatch EmptyMatch = new(string.Empty, string.Empty);

    private readonly Lock gate = new();
    private readonly Dictionary<int, Dictionary<string, List<Cluster>>> tree = [];
    private int clusterCount;
    private long clock;

    public PatternMatch Match(string? body)
    {
        var opts = options.Value;
        var text = Normalize(body, opts.MaxBodyLength);
        if (text.Length == 0)
        {
            return EmptyMatch;
        }

        var tokens = Mask(text).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return EmptyMatch;
        }

        lock (gate)
        {
            var tick = ++clock;
            var byFirstToken = GetOrAdd(tree, tokens.Length);
            var clusters = GetOrAdd(byFirstToken, tokens[0]);

            var best = FindBestMatch(clusters, tokens, out var bestSimilarity);
            if (best is not null && bestSimilarity >= opts.SimilarityThreshold)
            {
                if (Generalize(best.TemplateTokens, tokens))
                {
                    best.PatternId = ComputePatternId(best.TemplateTokens);
                }
                best.LastUsedTick = tick;
                return new PatternMatch(best.PatternId, string.Join(' ', best.TemplateTokens));
            }

            if (clusterCount >= opts.MaxTemplates)
            {
                EvictLeastRecentlyUsed();
            }

            var templateTokens = (string[])tokens.Clone();
            var created = new Cluster
            {
                TemplateTokens = templateTokens,
                PatternId = ComputePatternId(templateTokens),
                TokenCount = tokens.Length,
                FirstToken = tokens[0],
                LastUsedTick = tick,
            };
            clusters.Add(created);
            clusterCount++;
            return new PatternMatch(created.PatternId, string.Join(' ', created.TemplateTokens));
        }
    }

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

    /// <summary>Evicts the globally least-recently-used cluster to make room for a new one, pruning any bucket left empty by the removal.</summary>
    private void EvictLeastRecentlyUsed()
    {
        Cluster? oldest = null;
        foreach (var byFirstToken in tree.Values)
        {
            foreach (var clusters in byFirstToken.Values)
            {
                foreach (var candidate in clusters)
                {
                    if (oldest is null || candidate.LastUsedTick < oldest.LastUsedTick)
                    {
                        oldest = candidate;
                    }
                }
            }
        }

        if (oldest is null)
        {
            return;
        }

        var byToken = tree[oldest.TokenCount];
        var clusterList = byToken[oldest.FirstToken];
        clusterList.Remove(oldest);
        if (clusterList.Count == 0)
        {
            byToken.Remove(oldest.FirstToken);
            if (byToken.Count == 0)
            {
                tree.Remove(oldest.TokenCount);
            }
        }
        clusterCount--;
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
        // deterministic (the same template re-emerging after a restart gets the same id,
        // softening but not eliminating the "tree resets on restart" limitation) and, at
        // this matcher's MaxTemplates-bounded live cardinality, collision odds are
        // negligible (birthday-bound 50% risk needs ~2^32 distinct templates).
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private static TInner GetOrAdd<TValue, TInner>(Dictionary<TValue, TInner> dict, TValue key)
        where TValue : notnull
        where TInner : new()
    {
        if (!dict.TryGetValue(key, out var value))
        {
            value = new TInner();
            dict[key] = value;
        }
        return value;
    }

    private static List<Cluster> GetOrAdd(Dictionary<string, List<Cluster>> dict, string key)
    {
        if (!dict.TryGetValue(key, out var value))
        {
            value = [];
            dict[key] = value;
        }
        return value;
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
        public required string[] TemplateTokens { get; set; }
        public required string PatternId { get; set; }
        public required int TokenCount { get; init; }
        public required string FirstToken { get; init; }
        public long LastUsedTick { get; set; }
    }
}
