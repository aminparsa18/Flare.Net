using Flare.Ingest.Model;
using Flare.Ingest.Pipeline.Rules;
using Xunit;

namespace Flare.Ingest.Tests.Pipeline.Rules;

public class PipelineRuleConditionMatcherTests
{
    [Fact]
    public void Matches_WithNoFields_ReturnsTrue()
    {
        // Deliberate: an empty condition matches every log - see
        // PipelineRuleConditionMatcher's remarks on why this isn't a bug, and
        // docs-internal/adr/0033-pipeline-rules-extraction-redaction.md for the
        // dashboard-side "matches all logs" notice this relies on.
        Assert.True(PipelineRuleConditionMatcher.Matches(MinimalLogEvent(), new PipelineRuleCondition()));
    }

    [Theory]
    [InlineData("flare-ingest", true)]
    [InlineData("payments-api", false)]
    public void Matches_Services_IsExactMatch(string serviceName, bool expected)
    {
        var logEvent = MinimalLogEvent() with { ServiceName = "flare-ingest" };
        var condition = new PipelineRuleCondition { Services = [serviceName] };

        Assert.Equal(expected, PipelineRuleConditionMatcher.Matches(logEvent, condition));
    }

    [Theory]
    [InlineData((byte)17, true)]
    [InlineData((byte)9, false)]
    public void Matches_SeverityNumbers_IsExactMatch(byte severityNumber, bool expected)
    {
        var logEvent = MinimalLogEvent() with { SeverityNumber = 17 };
        var condition = new PipelineRuleCondition { SeverityNumbers = [severityNumber] };

        Assert.Equal(expected, PipelineRuleConditionMatcher.Matches(logEvent, condition));
    }

    [Theory]
    [InlineData("went wrong", true)]
    [InlineData("WENT WRONG", true)]
    [InlineData("all good", false)]
    public void Matches_Search_IsCaseInsensitiveSubstring(string search, bool expected)
    {
        var logEvent = MinimalLogEvent() with { Body = "something went wrong" };
        var condition = new PipelineRuleCondition { Search = search };

        Assert.Equal(expected, PipelineRuleConditionMatcher.Matches(logEvent, condition));
    }

    [Fact]
    public void Matches_Search_AgainstNullBody_ReturnsFalse()
    {
        var logEvent = MinimalLogEvent() with { Body = null };
        var condition = new PipelineRuleCondition { Search = "anything" };

        Assert.False(PipelineRuleConditionMatcher.Matches(logEvent, condition));
    }

    [Theory]
    [InlineData(AttributeConditionOperator.Exists, "500", true)]
    [InlineData(AttributeConditionOperator.Absent, "500", false)]
    [InlineData(AttributeConditionOperator.Equals, "500", true)]
    [InlineData(AttributeConditionOperator.Equals, "404", false)]
    [InlineData(AttributeConditionOperator.NotEquals, "404", true)]
    public void Matches_Attributes_LogBag(AttributeConditionOperator op, string value, bool expected)
    {
        var logEvent = MinimalLogEvent() with { LogAttributes = new Dictionary<string, string> { ["http.status_code"] = "500" } };
        var condition = new PipelineRuleCondition
        {
            Attributes = [new AttributeCondition { Bag = AttributeBag.Log, Key = "http.status_code", Value = value, Operator = op }],
        };

        Assert.Equal(expected, PipelineRuleConditionMatcher.Matches(logEvent, condition));
    }

    [Fact]
    public void Matches_Attributes_ResourceBag_IsSeparateFromLogBag()
    {
        var logEvent = MinimalLogEvent() with
        {
            ResourceAttributes = new Dictionary<string, string> { ["deployment.environment"] = "production" },
            LogAttributes = new Dictionary<string, string>(),
        };
        var condition = new PipelineRuleCondition
        {
            Attributes = [new AttributeCondition { Bag = AttributeBag.Resource, Key = "deployment.environment", Value = "production" }],
        };

        Assert.True(PipelineRuleConditionMatcher.Matches(logEvent, condition));

        var wrongBag = new PipelineRuleCondition
        {
            Attributes = [new AttributeCondition { Bag = AttributeBag.Log, Key = "deployment.environment", Value = "production" }],
        };
        Assert.False(PipelineRuleConditionMatcher.Matches(logEvent, wrongBag));
    }

    [Fact]
    public void Matches_Attributes_InOperator()
    {
        var logEvent = MinimalLogEvent() with { LogAttributes = new Dictionary<string, string> { ["http.status_code"] = "500" } };
        var condition = new PipelineRuleCondition
        {
            Attributes = [new AttributeCondition { Key = "http.status_code", Operator = AttributeConditionOperator.In, Values = ["500", "503"] }],
        };

        Assert.True(PipelineRuleConditionMatcher.Matches(logEvent, condition));
    }

    [Fact]
    public void Matches_Attributes_RegexOperator_InvalidPattern_FailsClosed()
    {
        var logEvent = MinimalLogEvent() with { LogAttributes = new Dictionary<string, string> { ["k"] = "v" } };
        var condition = new PipelineRuleCondition
        {
            Attributes = [new AttributeCondition { Key = "k", Operator = AttributeConditionOperator.Regex, Value = "(unterminated" }],
        };

        Assert.False(PipelineRuleConditionMatcher.Matches(logEvent, condition));
    }

    [Fact]
    public void Matches_AllConditionsMustMatch()
    {
        var logEvent = MinimalLogEvent() with { ServiceName = "flare-ingest", SeverityNumber = 17 };
        var condition = new PipelineRuleCondition { Services = ["flare-ingest"], SeverityNumbers = [9] };

        Assert.False(PipelineRuleConditionMatcher.Matches(logEvent, condition));
    }

    private static LogEvent MinimalLogEvent() => new()
    {
        EventId = Guid.NewGuid(),
        Timestamp = new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero),
        IngestedAt = new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero),
        SeverityNumber = 9,
        ResourceAttributes = new Dictionary<string, string>(),
        ScopeAttributes = new Dictionary<string, string>(),
        LogAttributes = new Dictionary<string, string>(),
    };
}
