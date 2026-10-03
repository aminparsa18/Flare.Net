using Flare.Api.Model;
using Flare.Api.Query;
using Flare.Identity.LlmPrices;
using Xunit;

namespace Flare.Api.Tests.Query;

public class LlmPricingTests
{
    private static readonly IReadOnlyDictionary<string, LlmModelPrice> NoOverrides = new Dictionary<string, LlmModelPrice>();

    [Theory]
    [InlineData("gpt-4o", 2.50, 10.00)]
    [InlineData("gpt-4o-mini", 0.15, 0.60)]
    [InlineData("gpt-4o-mini-2024-07-18", 0.15, 0.60)]
    [InlineData("GPT-4O-2024-08-06", 2.50, 10.00)]
    [InlineData("openai/gpt-4.1-nano", 0.10, 0.40)]
    [InlineData("claude-sonnet-4-5-20250929", 3.00, 15.00)]
    [InlineData("models/gemini-2.5-flash", 0.30, 2.50)]
    public void Resolve_MatchesTheLongestDefaultPrefix(string model, double input, double output)
    {
        var price = LlmPricing.Resolve(model, NoOverrides);

        Assert.NotNull(price);
        Assert.Equal(input, price.InputPerMillion);
        Assert.Equal(output, price.OutputPerMillion);
        Assert.False(price.IsCustom);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("some-local-llama")]
    public void Resolve_WithNoMatch_ReturnsNull(string model)
    {
        Assert.Null(LlmPricing.Resolve(model, NoOverrides));
    }

    [Fact]
    public void Resolve_PrefersAnExactCaseInsensitiveOverride()
    {
        var overrides = new Dictionary<string, LlmModelPrice>(StringComparer.OrdinalIgnoreCase)
        {
            ["gpt-4o"] = new("gpt-4o", 1, 2),
        };

        var price = LlmPricing.Resolve("GPT-4o", overrides);

        Assert.Equal(new ResolvedPrice(1, 2, IsCustom: true), price);
        // An override is for that exact name, not a prefix: the snapshot still takes the default.
        Assert.False(LlmPricing.Resolve("gpt-4o-2024-08-06", overrides)!.IsCustom);
    }

    [Fact]
    public void Cost_PricesInputAndOutputTokensPerMillion()
    {
        var cost = LlmPricing.Cost(new ResolvedPrice(2.50, 10.00, false), 2_000_000, 500_000);

        Assert.Equal(10.0, cost, 6);
    }

    [Fact]
    public void Apply_LeavesUnpricedRowsWithoutACost()
    {
        var priced = Row("gpt-4o", 1_000_000, 100_000);
        var unpriced = Row("mystery", 1_000_000, 100_000);

        var result = LlmPricing.Apply([priced, unpriced], NoOverrides);

        Assert.Equal(3.5, result[0].EstimatedCost!.Value, 6);
        Assert.Equal(2.50, result[0].InputPricePerMillion);
        Assert.Null(result[1].EstimatedCost);
        Assert.Null(result[1].InputPricePerMillion);
    }

    [Theory]
    [InlineData("m", 1, 2, true)]
    [InlineData("m", 0, 0, true)]
    [InlineData("", 1, 2, false)]
    [InlineData("m", -1, 2, false)]
    [InlineData("m", 1, double.NaN, false)]
    [InlineData("m", 1, LlmPricing.MaxPricePerMillion + 1, false)]
    public void Validate_RejectsMissingModelsAndOutOfRangePrices(string model, double input, double output, bool valid)
    {
        var error = LlmPricing.Validate(new SetLlmModelPriceRequest { Model = model, InputPerMillion = input, OutputPerMillion = output }, out _);

        Assert.Equal(valid, error is null);
    }

    private static LlmModel Row(string model, ulong input, ulong output) => new()
    {
        Provider = "openai",
        Model = model,
        CallCount = 1,
        ErrorCount = 0,
        PerSecond = 0,
        P50Ms = 0,
        P95Ms = 0,
        P99Ms = 0,
        InputTokens = input,
        OutputTokens = output,
        ServiceCount = 1,
        LastSeenUnixMs = 0,
    };
}
