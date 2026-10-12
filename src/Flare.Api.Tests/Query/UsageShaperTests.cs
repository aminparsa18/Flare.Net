using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class UsageShaperTests
{
    [Theory]
    [InlineData(0, 7)]
    [InlineData(-3, 7)]
    [InlineData(1, 1)]
    [InlineData(30, 30)]
    [InlineData(365, 30)]
    public void ClampDays_DefaultsSevenWhenNonPositive_AndCapsAtThirty(int requested, int expected) =>
        Assert.Equal(expected, UsageShaper.ClampDays(requested));

    [Theory]
    [InlineData("logs_local", "logs")]
    [InlineData("logs", "logs")]
    [InlineData("metrics_exponential_histogram_local", "metrics_exponential_histogram")]
    public void NormalizeTableName_StripsClusterLocalSuffix(string input, string expected) =>
        Assert.Equal(expected, UsageShaper.NormalizeTableName(input));

    [Fact]
    public void BuildServiceRows_ApportionsBytesByEventShare_BiggestFirst()
    {
        var rows = UsageShaper.BuildServiceRows(
            "Logs",
            new Dictionary<string, long> { ["small"] = 100, ["big"] = 300, ["empty"] = 0 },
            signalCompressedBytes: 4000);

        Assert.Collection(
            rows,
            r => Assert.Equal(new UsageServiceRow("big", "Logs", 300, 3000), r),
            r => Assert.Equal(new UsageServiceRow("small", "Logs", 100, 1000), r));
    }

    [Fact]
    public void BuildServiceRows_NoEvents_ReturnsEmpty() =>
        Assert.Empty(UsageShaper.BuildServiceRows("Traces", new Dictionary<string, long>(), 1000));

    [Fact]
    public void BuildServiceRows_CapsToTopServicesPerSignal()
    {
        var events = Enumerable.Range(1, UsageShaper.TopServicesPerSignal + 10).ToDictionary(i => $"svc{i}", i => (long)i);
        var rows = UsageShaper.BuildServiceRows("Logs", events, 0);

        Assert.Equal(UsageShaper.TopServicesPerSignal, rows.Count);
        Assert.Equal(UsageShaper.TopServicesPerSignal + 10, rows[0].Events);
    }

    [Fact]
    public void TopAttributes_KeepsBiggestPerSignalAndScope()
    {
        var input = Enumerable.Range(1, UsageShaper.TopAttributesPerGroup + 5)
            .Select(i => new UsageAttributeRow("Logs", "Log", $"k{i}", i, 1))
            .Append(new UsageAttributeRow("Logs", "Resource", "r", 1, 1));

        var result = UsageShaper.TopAttributes(input);

        Assert.Equal(UsageShaper.TopAttributesPerGroup + 1, result.Count);
        Assert.DoesNotContain(result, r => r.Key == "k1");
        Assert.Contains(result, r => r.Key == "r");
    }
}
