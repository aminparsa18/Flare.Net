using Flare.Cli.Commands;
using Xunit;

namespace Flare.Cli.Tests;

public class RetentionRuleParseTests
{
    [Fact]
    public void Parses_AttributeValueDays()
    {
        Assert.True(RetentionSetCommand.TryParseRule("deployment.environment=dev:7", out var rule));
        Assert.Equal(new RetentionRuleWire("deployment.environment", "dev", 7), rule);
    }

    [Fact]
    public void Value_MayContainColons_AndDaysZeroIsForever()
    {
        Assert.True(RetentionSetCommand.TryParseRule("k8s.namespace=team:a:0", out var rule));
        Assert.Equal(new RetentionRuleWire("k8s.namespace", "team:a", 0), rule);
    }

    [Theory]
    [InlineData("nodays")]
    [InlineData("a=b")]
    [InlineData("a=b:")]
    [InlineData("a=:5")]
    [InlineData("=b:5")]
    [InlineData("a=b:-1")]
    [InlineData("a=b:x")]
    public void Rejects_Malformed(string text) =>
        Assert.False(RetentionSetCommand.TryParseRule(text, out _));
}
