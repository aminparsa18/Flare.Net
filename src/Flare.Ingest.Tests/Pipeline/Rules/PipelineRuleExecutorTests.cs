using Flare.Ingest.Model;
using Flare.Ingest.Pipeline.Rules;
using Xunit;

namespace Flare.Ingest.Tests.Pipeline.Rules;

public class PipelineRuleExecutorTests
{
    [Fact]
    public void Apply_ExtractRegex_FromBody_AddsNamedGroupsAsLogAttributes()
    {
        var logEvent = MinimalLogEvent() with { Body = "user_id=42 action=login" };
        var rule = RuleWith(new PipelineRuleAction
        {
            Kind = RuleActionKind.ExtractRegex,
            ExtractRegex = new ExtractRegexAction { Pattern = @"user_id=(?<user_id>\d+) action=(?<action>\w+)" },
        });

        var result = PipelineRuleExecutor.Apply(logEvent, [rule]);

        Assert.Equal("42", result.LogAttributes["user_id"]);
        Assert.Equal("login", result.LogAttributes["action"]);
        Assert.Equal("user_id=42 action=login", result.Body); // Extraction never mutates the source.
    }

    [Fact]
    public void Apply_ExtractRegex_FromAttribute_ReadsNamedSource()
    {
        var logEvent = MinimalLogEvent() with { LogAttributes = new Dictionary<string, string> { ["raw"] = "code=500" } };
        var rule = RuleWith(new PipelineRuleAction
        {
            Kind = RuleActionKind.ExtractRegex,
            ExtractRegex = new ExtractRegexAction { SourceAttributeKey = "raw", Pattern = @"code=(?<status_code>\d+)" },
        });

        var result = PipelineRuleExecutor.Apply(logEvent, [rule]);

        Assert.Equal("500", result.LogAttributes["status_code"]);
    }

    [Fact]
    public void Apply_ExtractRegex_NoMatch_LeavesEventUnchanged()
    {
        var logEvent = MinimalLogEvent() with { Body = "nothing to see here" };
        var rule = RuleWith(new PipelineRuleAction
        {
            Kind = RuleActionKind.ExtractRegex,
            ExtractRegex = new ExtractRegexAction { Pattern = @"user_id=(?<user_id>\d+)" },
        });

        var result = PipelineRuleExecutor.Apply(logEvent, [rule]);

        Assert.Empty(result.LogAttributes);
    }

    [Fact]
    public void Apply_RedactRegex_OnBody_ReplacesMatches()
    {
        var logEvent = MinimalLogEvent() with { Body = "card 4111111111111111 charged" };
        var rule = RuleWith(new PipelineRuleAction
        {
            Kind = RuleActionKind.RedactRegex,
            RedactRegex = new RedactRegexAction { Pattern = @"\d{16}", Replacement = "****" },
        });

        var result = PipelineRuleExecutor.Apply(logEvent, [rule]);

        Assert.Equal("card **** charged", result.Body);
    }

    [Fact]
    public void Apply_RedactRegex_OnAttribute_ReplacesInPlace()
    {
        var logEvent = MinimalLogEvent() with { LogAttributes = new Dictionary<string, string> { ["email"] = "user@example.com" } };
        var rule = RuleWith(new PipelineRuleAction
        {
            Kind = RuleActionKind.RedactRegex,
            RedactRegex = new RedactRegexAction { SourceAttributeKey = "email", Pattern = @"@.+", Replacement = "@***" },
        });

        var result = PipelineRuleExecutor.Apply(logEvent, [rule]);

        Assert.Equal("user@***", result.LogAttributes["email"]);
    }

    [Fact]
    public void Apply_RedactRegex_MissingAttributeKey_IsNoOp()
    {
        var logEvent = MinimalLogEvent() with { LogAttributes = new Dictionary<string, string>() };
        var rule = RuleWith(new PipelineRuleAction
        {
            Kind = RuleActionKind.RedactRegex,
            RedactRegex = new RedactRegexAction { SourceAttributeKey = "missing", Pattern = @".+", Replacement = "x" },
        });

        var result = PipelineRuleExecutor.Apply(logEvent, [rule]);

        Assert.Empty(result.LogAttributes);
    }

    [Fact]
    public void Apply_NonMatchingRule_IsSkippedEntirely()
    {
        var logEvent = MinimalLogEvent() with { ServiceName = "other-service", Body = "user_id=42" };
        var rule = RuleWith(
            new PipelineRuleAction { Kind = RuleActionKind.ExtractRegex, ExtractRegex = new ExtractRegexAction { Pattern = @"user_id=(?<user_id>\d+)" } },
            condition: new PipelineRuleCondition { Services = ["flare-ingest"] });

        var result = PipelineRuleExecutor.Apply(logEvent, [rule]);

        Assert.Empty(result.LogAttributes);
    }

    [Fact]
    public void Apply_MultipleRules_RunInListOrder_LaterSeesEarlierOutput()
    {
        var logEvent = MinimalLogEvent() with { Body = "card 4111111111111111 for user_id=42" };
        var extractFirst = RuleWith(new PipelineRuleAction
        {
            Kind = RuleActionKind.ExtractRegex,
            ExtractRegex = new ExtractRegexAction { Pattern = @"user_id=(?<user_id>\d+)" },
        });
        var redactSecond = RuleWith(new PipelineRuleAction
        {
            Kind = RuleActionKind.RedactRegex,
            RedactRegex = new RedactRegexAction { Pattern = @"\d{16}", Replacement = "****" },
        });

        var result = PipelineRuleExecutor.Apply(logEvent, [extractFirst, redactSecond]);

        Assert.Equal("42", result.LogAttributes["user_id"]);
        Assert.Equal("card **** for user_id=42", result.Body);
    }

    private static PipelineRule RuleWith(PipelineRuleAction action, PipelineRuleCondition? condition = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = "test-rule",
        Condition = condition ?? new PipelineRuleCondition(),
        Actions = [action],
    };

    private static LogEvent MinimalLogEvent() => new()
    {
        EventId = Guid.NewGuid(),
        Timestamp = new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero),
        IngestedAt = new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero),
        SeverityNumber = 9,
        ServiceName = "flare-ingest",
        ResourceAttributes = new Dictionary<string, string>(),
        ScopeAttributes = new Dictionary<string, string>(),
        LogAttributes = new Dictionary<string, string>(),
    };
}
