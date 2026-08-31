using Flare.Ingest.Stats;
using Xunit;

namespace Flare.Ingest.Tests;

public class ServiceBreakdownTests
{
    [Fact]
    public void Build_SingleService_AllBytesAttributedToIt()
    {
        var result = ServiceBreakdown.Build(
            [("checkout-api", 0), ("checkout-api", 0), ("checkout-api", 0)],
            totalByteCount: 900);

        var entry = Assert.Single(result);
        Assert.Equal("checkout-api", entry.Key);
        Assert.Equal(3, entry.Value.RecordCount);
        Assert.Equal(900, entry.Value.ByteCount);
    }

    [Fact]
    public void Build_MultipleServices_SplitsBytesProportionallyToRecordShare()
    {
        var result = ServiceBreakdown.Build([("a", 0), ("a", 0), ("a", 0), ("b", 0)], totalByteCount: 400);

        Assert.Equal(3, result["a"].RecordCount);
        Assert.Equal(300, result["a"].ByteCount);
        Assert.Equal(1, result["b"].RecordCount);
        Assert.Equal(100, result["b"].ByteCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Build_NullOrEmptyServiceName_FallsBackToUnknownServiceLabel(string? serviceName)
    {
        var result = ServiceBreakdown.Build([(serviceName, 0L)], totalByteCount: 100);

        var entry = Assert.Single(result);
        Assert.Equal(ServiceBreakdown.UnknownServiceName, entry.Key);
    }

    [Fact]
    public void Build_EmptyInput_ReturnsEmptyDictionary()
    {
        var result = ServiceBreakdown.Build([], totalByteCount: 100);

        Assert.Empty(result);
    }

    [Fact]
    public void Build_SumsSkewNanos_PerService()
    {
        var result = ServiceBreakdown.Build(
            [("checkout-api", 100L), ("checkout-api", -50L), ("orders-api", 1_000L)],
            totalByteCount: 0);

        Assert.Equal(50, result["checkout-api"].SkewNanosSum);
        Assert.Equal(1_000, result["orders-api"].SkewNanosSum);
    }

    [Fact]
    public void Build_GroupsSkewNanos_UnderUnknownServiceLabel_WhenServiceNameIsAbsent()
    {
        var result = ServiceBreakdown.Build([(null, 200L), ("", 300L)], totalByteCount: 0);

        var entry = Assert.Single(result);
        Assert.Equal(500, entry.Value.SkewNanosSum);
    }
}
