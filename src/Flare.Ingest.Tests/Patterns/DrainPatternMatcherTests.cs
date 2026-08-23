using Flare.Ingest.Patterns;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flare.Ingest.Tests.Patterns;

public class DrainPatternMatcherTests
{
    [Fact]
    public async Task Match_WildcardsNumericTokensWithinAPath_ProducesSameTemplateAndPatternId()
    {
        var matcher = CreateMatcher();

        var first = await Match(matcher, "GET /api/orders/123");
        var second = await Match(matcher, "GET /api/orders/456");

        Assert.Equal("GET /api/orders/<*>", first.PatternTemplate);
        Assert.Equal(first.PatternTemplate, second.PatternTemplate);
        Assert.Equal(first.PatternId, second.PatternId);
    }

    [Fact]
    public async Task Match_WildcardsUuidTokens()
    {
        var matcher = CreateMatcher();

        var first = await Match(matcher, "user 3f2504e0-4f89-11d3-9a0c-0305e82c3301 logged in");
        var second = await Match(matcher, "user 7c9e6679-7425-40de-944b-e07fc1f90ae7 logged in");

        Assert.Equal("user <*> logged in", first.PatternTemplate);
        Assert.Equal(first.PatternId, second.PatternId);
    }

    [Fact]
    public async Task Match_WildcardsHexTokens_OnlyWhenDigitPresent()
    {
        // Two separate matchers: both bodies share the same token count and first token
        // ("checksum"), so matching them against one shared matcher would merge the
        // second into the first cluster's already-wildcarded position (correct Drain
        // behavior - a generalized position accepts anything - but not what this test is
        // isolating).
        var withDigit = await Match(CreateMatcher(), "checksum deadbeef1 ok");
        Assert.Equal("checksum <*> ok", withDigit.PatternTemplate);

        // Pure a-f letter words are real English words, not hex - spared, not wildcarded.
        var pureLetters = await Match(CreateMatcher(), "checksum cafefaced ok");
        Assert.Equal("checksum cafefaced ok", pureLetters.PatternTemplate);
    }

    [Fact]
    public async Task Match_KeepsDistinctTokenCountsInSeparateClusters()
    {
        var matcher = CreateMatcher();

        var first = await Match(matcher, "GET /api/orders/123");
        var second = await Match(matcher, "GET /api/orders/123 slow");

        Assert.NotEqual(first.PatternId, second.PatternId);
    }

    [Fact]
    public async Task Match_MergesClustersAboveSimilarityThreshold()
    {
        var matcher = CreateMatcher(similarityThreshold: 0.5);

        await Match(matcher, "connection to db-primary failed after 3 retries");
        var second = await Match(matcher, "connection to db-replica failed after 3 retries");
        var third = await Match(matcher, "connection to db-secondary failed after 3 retries");

        // 6/7 positions agree (>= 0.5) - merges into one cluster, wildcarding only the
        // differing position. PatternId is recomputed whenever the merge changes the
        // template text, so only calls made *after* a merge (second, third) share the
        // resulting id - the first call's already-returned id predates the merge and
        // isn't retroactively updated, a documented, accepted limitation (see
        // DrainPatternMatcher.ComputePatternId's remarks).
        Assert.Equal("connection to <*> failed after <*> retries", second.PatternTemplate);
        Assert.Equal(second.PatternId, third.PatternId);
    }

    [Fact]
    public async Task Match_KeepsClustersSeparateBelowThreshold()
    {
        var matcher = CreateMatcher(similarityThreshold: 0.9);

        // Same token count and same first token (so both land in the same tree bucket),
        // but only 1/7 positions agree - well under the 0.9 threshold, so this must stay
        // a distinct cluster rather than merge.
        var first = await Match(matcher, "alpha beta gamma delta epsilon zeta eta");
        var second = await Match(matcher, "alpha two three four five six seven");

        Assert.NotEqual(first.PatternId, second.PatternId);
        Assert.Equal("alpha two three four five six seven", second.PatternTemplate);
    }

    [Fact]
    public async Task Match_IsDeterministic_SamePatternIdAcrossRepeatedCalls()
    {
        var first = await Match(CreateMatcher(), "GET /api/orders/123");
        var second = await Match(CreateMatcher(), "GET /api/orders/999");

        // Two independent matchers, each seeing only one line - same finalized template
        // text ("GET /api/orders/<*>") must hash to the same id regardless of matcher
        // instance or arrival order.
        Assert.Equal(first.PatternId, second.PatternId);
    }

    [Fact]
    public async Task MatchBatchAsync_ConvergesAcrossInstances_WhenSharingAClusterStore()
    {
        // The actual regression test for docs/clustering.md's former "Drain log-pattern
        // clustering state doesn't share across Flare.Ingest replicas" limitation: two
        // DrainPatternMatcher instances (standing in for two replicas) sharing one store
        // must converge on the same PatternId for the same template, even when the merge
        // that generalizes the template happens on a *different* instance than the one
        // asking about a later variant.
        var options = Options.Create(new LogPatternOptions { SimilarityThreshold = 0.5 });
        var sharedStore = new FakePatternClusterStore();
        var replicaA = new DrainPatternMatcher(sharedStore, options);
        var replicaB = new DrainPatternMatcher(sharedStore, options);

        await Match(replicaA, "connection to db-primary failed after 3 retries");
        var fromA = await Match(replicaA, "connection to db-replica failed after 3 retries");
        var fromB = await Match(replicaB, "connection to db-secondary failed after 3 retries");

        // "3" is already wildcarded by Mask()'s numeric pass regardless of Drain, so the
        // only Drain-driven generalization is the db-* position - same template text the
        // existing (single-instance) Match_MergesClustersAboveSimilarityThreshold test
        // expects for its equivalent second call.
        Assert.Equal("connection to <*> failed after <*> retries", fromA.PatternTemplate);
        Assert.Equal(fromA.PatternId, fromB.PatternId);
    }

    [Fact]
    public async Task MatchBatchAsync_FragmentsAcrossInstances_WhenNotSharingAClusterStore()
    {
        // Contrast case, pinning the bug the test above fixes: same scenario, but each
        // instance gets its own independent InMemoryPatternClusterStore (today's default,
        // single-node-correct behavior) - each replica generalizes its own copy of the
        // template independently, so the same conceptual template lands under different
        // PatternIds depending which replica saw which variant first.
        var options = Options.Create(new LogPatternOptions { SimilarityThreshold = 0.5 });
        var replicaA = new DrainPatternMatcher(new InMemoryPatternClusterStore(options), options);
        var replicaB = new DrainPatternMatcher(new InMemoryPatternClusterStore(options), options);

        await Match(replicaA, "connection to db-primary failed after 3 retries");
        var fromA = await Match(replicaA, "connection to db-replica failed after 3 retries");
        var fromB = await Match(replicaB, "connection to db-secondary failed after 3 retries");

        Assert.NotEqual(fromA.PatternId, fromB.PatternId);
    }

    [Fact]
    public async Task Match_EvictsLeastRecentlyUsedCluster_AtMaxTemplatesCap()
    {
        var matcher = CreateMatcher(maxTemplates: 2);

        var a = await Match(matcher, "alpha one");
        var b = await Match(matcher, "bravo two");
        // Touch "alpha one" again so "bravo two" becomes the least-recently-used cluster.
        await Match(matcher, "alpha one");
        await Match(matcher, "charlie three"); // pushes the cap - evicts "bravo two", not "alpha one".

        var alphaAgain = await Match(matcher, "alpha one");
        var bravoAgain = await Match(matcher, "bravo two");

        Assert.Equal(a.PatternId, alphaAgain.PatternId);
        // "bravo two" had to be re-created as a new cluster after eviction, but a fresh
        // cluster with identical template text still hashes to the same deterministic id.
        Assert.Equal(b.PatternId, bravoAgain.PatternId);
        Assert.Equal("bravo two", bravoAgain.PatternTemplate);
    }

    [Fact]
    public async Task Match_TruncatesAtMaxBodyLength()
    {
        var matcher = CreateMatcher(maxBodyLength: 11);

        var match = await Match(matcher, "hello world this is a very long log message");

        Assert.Equal("hello world", match.PatternTemplate);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Match_HandlesNullOrEmptyBody_WithoutThrowing(string? body)
    {
        var match = await Match(CreateMatcher(), body);

        Assert.Equal(string.Empty, match.PatternId);
        Assert.Equal(string.Empty, match.PatternTemplate);
    }

    private static async Task<PatternMatch> Match(DrainPatternMatcher matcher, string? body) =>
        (await matcher.MatchBatchAsync([body], CancellationToken.None))[0];

    private static DrainPatternMatcher CreateMatcher(
        double similarityThreshold = 0.5,
        int maxTemplates = 10_000,
        int maxBodyLength = 4096)
    {
        var options = Options.Create(new LogPatternOptions
        {
            SimilarityThreshold = similarityThreshold,
            MaxTemplates = maxTemplates,
            MaxBodyLength = maxBodyLength,
        });
        return new DrainPatternMatcher(new InMemoryPatternClusterStore(options), options);
    }
}
