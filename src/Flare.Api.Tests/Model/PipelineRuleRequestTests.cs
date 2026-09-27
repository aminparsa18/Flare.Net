using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Model;

public class PipelineRuleRequestTests
{
    [Fact]
    public void Validate_ParseJson_WithDefaults_IsValid()
    {
        var request = RequestWith(new PipelineRuleAction { Kind = RuleActionKind.ParseJson, ParseJson = new ParseJsonAction() });

        Assert.Null(request.Validate());
    }

    [Fact]
    public void Validate_ParseJson_MissingGroup_IsRejected()
    {
        var request = RequestWith(new PipelineRuleAction { Kind = RuleActionKind.ParseJson });

        Assert.Contains("parseJson must be set", request.Validate());
    }

    [Fact]
    public void Validate_ParseJson_WithAnotherGroupAlsoSet_IsRejected()
    {
        var request = RequestWith(new PipelineRuleAction
        {
            Kind = RuleActionKind.ParseJson,
            ParseJson = new ParseJsonAction(),
            RedactRegex = new RedactRegexAction { Pattern = "x" },
        });

        Assert.Contains("parseJson must be set", request.Validate());
    }

    [Fact]
    public void Validate_ExtractRegex_WithParseJsonAlsoSet_IsRejected()
    {
        var request = RequestWith(new PipelineRuleAction
        {
            Kind = RuleActionKind.ExtractRegex,
            ExtractRegex = new ExtractRegexAction { Pattern = "(?<a>x)" },
            ParseJson = new ParseJsonAction(),
        });

        Assert.Contains("extractRegex must be set", request.Validate());
    }

    [Theory]
    [InlineData(0, null, "maxDepth")]
    [InlineData(ParseJsonAction.MaxDepthLimit + 1, null, "maxDepth")]
    [InlineData(null, 0, "maxKeys")]
    [InlineData(null, ParseJsonAction.MaxKeysLimit + 1, "maxKeys")]
    public void Validate_ParseJson_OutOfRangeLimits_AreRejected(int? maxDepth, int? maxKeys, string expected)
    {
        var request = RequestWith(new PipelineRuleAction
        {
            Kind = RuleActionKind.ParseJson,
            ParseJson = new ParseJsonAction { MaxDepth = maxDepth, MaxKeys = maxKeys },
        });

        Assert.Contains(expected, request.Validate());
    }

    private static PipelineRuleRequest RequestWith(PipelineRuleAction action) => new() { Name = "test-rule", Actions = [action] };
}
