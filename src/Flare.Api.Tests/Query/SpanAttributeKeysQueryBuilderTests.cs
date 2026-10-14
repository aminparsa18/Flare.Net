using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class SpanAttributeKeysQueryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(SpanAttributeBag.Span, "SpanAttributes")]
    [InlineData(SpanAttributeBag.Resource, "ResourceAttributes")]
    [InlineData(SpanAttributeBag.Scope, "ScopeAttributes")]
    public void Build_ReadsTheBagsMapColumn_ThroughASampledSubquery(SpanAttributeBag bag, string column)
    {
        var built = SpanAttributeKeysQueryBuilder.Build(new SpanAttributeKeysRequest(bag, Now.AddHours(-1), Now), Now);

        Assert.Contains($"(SELECT {column} AS Attributes FROM spans WHERE StartTime >= {{from:DateTime64(9)}}", built.Sql);
        Assert.Contains("LIMIT {keysSample:UInt32})", built.Sql);
        Assert.Contains("arrayJoin(mapKeys(Attributes))", built.Sql);
        Assert.EndsWith("ORDER BY Cnt DESC, AttributeKey\nLIMIT {keysLimit:UInt32}", built.Sql);
        Assert.Equal(SpanAttributeKeysQueryBuilder.SampleSpans, built.Parameters["keysSample"].Value);
    }

    [Fact]
    public void Build_BindsTheWindow_AndCapsTheLimit()
    {
        var built = SpanAttributeKeysQueryBuilder.Build(new SpanAttributeKeysRequest(SpanAttributeBag.Span, Now.AddHours(-2), Now, Limit: 100_000), Now);

        Assert.Equal(Now.AddHours(-2).UtcDateTime, built.Parameters["from"].Value);
        Assert.Equal(Now.UtcDateTime, built.Parameters["to"].Value);
        Assert.Equal(SpanAttributeKeysQueryBuilder.MaxLimit, built.Parameters["keysLimit"].Value);
    }

    [Fact]
    public void Build_RejectsANonPositiveLimit() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SpanAttributeKeysQueryBuilder.Build(new SpanAttributeKeysRequest(SpanAttributeBag.Span, Now.AddHours(-1), Now, Limit: 0), Now));
}
