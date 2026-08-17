using Flare.Ingest.Patterns;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flare.Ingest.Tests.Patterns;

public class DrainPatternMatcherTests
{
    [Fact]
    public void Match_WildcardsNumericTokensWithinAPath_ProducesSameTemplateAndPatternId()
    {
        var matcher = CreateMatcher();

        var first = matcher.Match("GET /api/orders/123");
        var second = matcher.Match("GET /api/orders/456");

        Assert.Equal("GET /api/orders/<*>", first.PatternTemplate);
        Assert.Equal(first.PatternTemplate, second.PatternTemplate);
        Assert.Equal(first.PatternId, second.PatternId);
    }

    [Fact]
    public void Match_WildcardsUuidTokens()
    {
        var matcher = CreateMatcher();

        var first = matcher.Match("user 3f2504e0-4f89-11d3-9a0c-0305e82c3301 logged in");
        var second = matcher.Match("user 7c9e6679-7425-40de-944b-e07fc1f90ae7 logged in");

        Assert.Equal("user <*> logged in", first.PatternTemplate);
        Assert.Equal(first.PatternId, second.PatternId);
    }

    [Fact]
    public void Match_WildcardsHexTokens_OnlyWhenDigitPresent()
    {
        // Two separate matchers: both bodies share the same token count and first token
        // ("checksum"), so matching them against one shared matcher would merge the
        // second into the first cluster's already-wildcarded position (correct Drain
        // behavior - a generalized position accepts anything - but not what this test is
        // isolating).
        var withDigit = CreateMatcher().Match("checksum deadbeef1 ok");
        Assert.Equal("checksum <*> ok", withDigit.PatternTemplate);

        // Pure a-f letter words are real English words, not hex - spared, not wildcarded.
        var pureLetters = CreateMatcher().Match("checksum cafefaced ok");
        Assert.Equal("checksum cafefaced ok", pureLetters.PatternTemplate);
    }

    [Fact]
    public void Match_KeepsDistinctTokenCountsInSeparateClusters()
    {
        var matcher = CreateMatcher();

        var first = matcher.Match("GET /api/orders/123");
        var second = matcher.Match("GET /api/orders/123 slow");

        Assert.NotEqual(first.PatternId, second.PatternId);
    }

    [Fact]
    public void Match_MergesClustersAboveSimilarityThreshold()
    {
        var matcher = CreateMatcher(similarityThreshold: 0.5);

        matcher.Match("connection to db-primary failed after 3 retries");
        var second = matcher.Match("connection to db-replica failed after 3 retries");
        var third = matcher.Match("connection to db-secondary failed after 3 retries");

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
    public void Match_KeepsClustersSeparateBelowThreshold()
    {
        var matcher = CreateMatcher(similarityThreshold: 0.9);

        // Same token count and same first token (so both land in the same tree bucket),
        // but only 1/7 positions agree - well under the 0.9 threshold, so this must stay
        // a distinct cluster rather than merge.
        var first = matcher.Match("alpha beta gamma delta epsilon zeta eta");
        var second = matcher.Match("alpha two three four five six seven");

        Assert.NotEqual(first.PatternId, second.PatternId);
        Assert.Equal("alpha two three four five six seven", second.PatternTemplate);
    }

    [Fact]
    public void Match_IsDeterministic_SamePatternIdAcrossRepeatedCalls()
    {
        var first = CreateMatcher().Match("GET /api/orders/123");
        var second = CreateMatcher().Match("GET /api/orders/999");

        // Two independent matchers, each seeing only one line - same finalized template
        // text ("GET /api/orders/<*>") must hash to the same id regardless of matcher
        // instance or arrival order.
        Assert.Equal(first.PatternId, second.PatternId);
    }

    [Fact]
    public void Match_EvictsLeastRecentlyUsedCluster_AtMaxTemplatesCap()
    {
        var matcher = CreateMatcher(maxTemplates: 2);

        var a = matcher.Match("alpha one");
        var b = matcher.Match("bravo two");
        // Touch "alpha one" again so "bravo two" becomes the least-recently-used cluster.
        matcher.Match("alpha one");
        matcher.Match("charlie three"); // pushes the cap - evicts "bravo two", not "alpha one".

        var alphaAgain = matcher.Match("alpha one");
        var bravoAgain = matcher.Match("bravo two");

        Assert.Equal(a.PatternId, alphaAgain.PatternId);
        // "bravo two" had to be re-created as a new cluster after eviction, but a fresh
        // cluster with identical template text still hashes to the same deterministic id.
        Assert.Equal(b.PatternId, bravoAgain.PatternId);
        Assert.Equal("bravo two", bravoAgain.PatternTemplate);
    }

    [Fact]
    public void Match_TruncatesAtMaxBodyLength()
    {
        var matcher = CreateMatcher(maxBodyLength: 11);

        var match = matcher.Match("hello world this is a very long log message");

        Assert.Equal("hello world", match.PatternTemplate);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Match_HandlesNullOrEmptyBody_WithoutThrowing(string? body)
    {
        var match = CreateMatcher().Match(body);

        Assert.Equal(string.Empty, match.PatternId);
        Assert.Equal(string.Empty, match.PatternTemplate);
    }

    private static DrainPatternMatcher CreateMatcher(
        double similarityThreshold = 0.5,
        int maxTemplates = 10_000,
        int maxBodyLength = 4096) =>
        new(Options.Create(new LogPatternOptions
        {
            SimilarityThreshold = similarityThreshold,
            MaxTemplates = maxTemplates,
            MaxBodyLength = maxBodyLength,
        }));
}
