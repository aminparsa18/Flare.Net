using Flare.Ingest.Patterns;
using Xunit;

namespace Flare.Ingest.Tests.Patterns;

public class PatternClusterKeysTests
{
    [Fact]
    public void BucketKey_IsStable_ForTheSameTokenCountAndFirstToken()
    {
        Assert.Equal(PatternClusterKeys.BucketKey(3, "GET"), PatternClusterKeys.BucketKey(3, "GET"));
    }

    [Fact]
    public void BucketKey_DiffersByFirstToken()
    {
        Assert.NotEqual(PatternClusterKeys.BucketKey(3, "GET"), PatternClusterKeys.BucketKey(3, "POST"));
    }

    [Fact]
    public void BucketKey_DiffersByTokenCount()
    {
        Assert.NotEqual(PatternClusterKeys.BucketKey(3, "GET"), PatternClusterKeys.BucketKey(4, "GET"));
    }

    [Fact]
    public void BucketKey_IsPrefixedForRedisNamespacing()
    {
        Assert.StartsWith("flare:patterns:bucket:", PatternClusterKeys.BucketKey(1, "hello"));
    }

    [Fact]
    public void BucketKey_HandlesArbitraryFirstTokenContentWithoutThrowing()
    {
        // firstToken is raw, unbounded log content - must never blow up key generation.
        var key = PatternClusterKeys.BucketKey(1, new string('x', 10_000) + "\0\n\t:,{}");

        Assert.StartsWith("flare:patterns:bucket:", key);
    }
}
