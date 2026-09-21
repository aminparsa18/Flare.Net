using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class PipelineRuleActionExecutorTests
{
    [Fact]
    public void Apply_ExtractRegex_FromBody_AddsNamedGroupsAsLogAttributes()
    {
        var logEvent = MinimalLogEvent() with { Body = "user_id=42 action=login" };
        var action = new PipelineRuleAction
        {
            Kind = RuleActionKind.ExtractRegex,
            ExtractRegex = new ExtractRegexAction { Pattern = @"user_id=(?<user_id>\d+) action=(?<action>\w+)" },
        };

        var result = PipelineRuleActionExecutor.Apply(logEvent, [action]);

        Assert.Equal("42", result.LogAttributes["user_id"]);
        Assert.Equal("login", result.LogAttributes["action"]);
        Assert.Equal("user_id=42 action=login", result.Body); // Extraction never mutates the source.
    }

    [Fact]
    public void Apply_ExtractRegex_FromAttribute_ReadsNamedSource()
    {
        var logEvent = MinimalLogEvent() with { LogAttributes = new Dictionary<string, string> { ["raw"] = "code=500" } };
        var action = new PipelineRuleAction
        {
            Kind = RuleActionKind.ExtractRegex,
            ExtractRegex = new ExtractRegexAction { SourceAttributeKey = "raw", Pattern = @"code=(?<status_code>\d+)" },
        };

        var result = PipelineRuleActionExecutor.Apply(logEvent, [action]);

        Assert.Equal("500", result.LogAttributes["status_code"]);
    }

    [Fact]
    public void Apply_ExtractRegex_NoMatch_LeavesEventUnchanged()
    {
        var logEvent = MinimalLogEvent() with { Body = "nothing to see here" };
        var action = new PipelineRuleAction
        {
            Kind = RuleActionKind.ExtractRegex,
            ExtractRegex = new ExtractRegexAction { Pattern = @"user_id=(?<user_id>\d+)" },
        };

        var result = PipelineRuleActionExecutor.Apply(logEvent, [action]);

        Assert.Empty(result.LogAttributes);
    }

    [Fact]
    public void Apply_RedactRegex_OnBody_ReplacesMatches()
    {
        var logEvent = MinimalLogEvent() with { Body = "card 4111111111111111 charged" };
        var action = new PipelineRuleAction
        {
            Kind = RuleActionKind.RedactRegex,
            RedactRegex = new RedactRegexAction { Pattern = @"\d{16}", Replacement = "****" },
        };

        var result = PipelineRuleActionExecutor.Apply(logEvent, [action]);

        Assert.Equal("card **** charged", result.Body);
    }

    [Fact]
    public void Apply_RedactRegex_OnAttribute_ReplacesInPlace()
    {
        var logEvent = MinimalLogEvent() with { LogAttributes = new Dictionary<string, string> { ["email"] = "user@example.com" } };
        var action = new PipelineRuleAction
        {
            Kind = RuleActionKind.RedactRegex,
            RedactRegex = new RedactRegexAction { SourceAttributeKey = "email", Pattern = @"@.+", Replacement = "@***" },
        };

        var result = PipelineRuleActionExecutor.Apply(logEvent, [action]);

        Assert.Equal("user@***", result.LogAttributes["email"]);
    }

    [Fact]
    public void Apply_RedactRegex_NoMatch_IsNoOp()
    {
        var logEvent = MinimalLogEvent() with { Body = "nothing sensitive here" };
        var action = new PipelineRuleAction
        {
            Kind = RuleActionKind.RedactRegex,
            RedactRegex = new RedactRegexAction { Pattern = @"\d{16}", Replacement = "****" },
        };

        var result = PipelineRuleActionExecutor.Apply(logEvent, [action]);

        Assert.Equal("nothing sensitive here", result.Body);
    }

    [Fact]
    public void Apply_InvalidPattern_IsSkippedNotThrown()
    {
        var logEvent = MinimalLogEvent() with { Body = "user_id=42" };
        var action = new PipelineRuleAction
        {
            Kind = RuleActionKind.ExtractRegex,
            ExtractRegex = new ExtractRegexAction { Pattern = "(unclosed" },
        };

        var result = PipelineRuleActionExecutor.Apply(logEvent, [action]);

        Assert.Equal("user_id=42", result.Body);
        Assert.Empty(result.LogAttributes);
    }

    [Fact]
    public void Apply_MultipleActions_RunInListOrder_LaterSeesEarlierOutput()
    {
        var logEvent = MinimalLogEvent() with { Body = "card 4111111111111111 for user_id=42" };
        var extractFirst = new PipelineRuleAction
        {
            Kind = RuleActionKind.ExtractRegex,
            ExtractRegex = new ExtractRegexAction { Pattern = @"user_id=(?<user_id>\d+)" },
        };
        var redactSecond = new PipelineRuleAction
        {
            Kind = RuleActionKind.RedactRegex,
            RedactRegex = new RedactRegexAction { Pattern = @"\d{16}", Replacement = "****" },
        };

        var result = PipelineRuleActionExecutor.Apply(logEvent, [extractFirst, redactSecond]);

        Assert.Equal("42", result.LogAttributes["user_id"]);
        Assert.Equal("card **** for user_id=42", result.Body);
    }

    private static LogEventDto MinimalLogEvent() => new()
    {
        EventId = Guid.NewGuid(),
        Timestamp = DateTimeOffset.UnixEpoch,
        ObservedTimestamp = DateTimeOffset.UnixEpoch,
        IngestedAt = DateTimeOffset.UnixEpoch,
        TraceId = string.Empty,
        SpanId = string.Empty,
        TraceFlags = 0,
        SeverityText = string.Empty,
        SeverityNumber = 9,
        ServiceName = "flare-ingest",
        Body = string.Empty,
        ResourceSchemaUrl = string.Empty,
        ResourceAttributes = new Dictionary<string, string>(),
        ScopeSchemaUrl = string.Empty,
        ScopeName = string.Empty,
        ScopeVersion = string.Empty,
        ScopeAttributes = new Dictionary<string, string>(),
        LogAttributes = new Dictionary<string, string>(),
        EventName = string.Empty,
        PatternId = string.Empty,
        PatternTemplate = string.Empty,
    };
}
