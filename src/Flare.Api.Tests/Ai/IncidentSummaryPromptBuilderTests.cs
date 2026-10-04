using Flare.Api.Ai;
using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Ai;

public class IncidentSummaryPromptBuilderTests
{
    private static AlertRule Rule(string name = "High error rate") => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Condition = new LogFilter { Services = ["checkout"] },
        Threshold = new AlertThreshold { Count = 10 },
        WindowSeconds = 300,
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    private static IncidentEvidence Evidence(AlertRule? rule = null) => new()
    {
        Rule = rule ?? Rule(),
        From = new DateTimeOffset(2026, 10, 4, 10, 0, 0, TimeSpan.Zero),
        To = new DateTimeOffset(2026, 10, 4, 10, 5, 0, TimeSpan.Zero),
        Observed = 42,
        Previous = 3,
    };

    [Fact]
    public void Build_LogRule_IncludesObservedPreviousAndEvidence()
    {
        var prompt = IncidentSummaryPromptBuilder.Build(
            Evidence() with
            {
                LogPatterns = [new IncidentLogPattern("Payment failed for order <*>", "checkout", 17, 40, "")],
                ErrorSpans = [new IncidentErrorSpan("payments", "POST /charge", "card declined", 812.5)],
            },
            12_000);

        Assert.Contains("Alert: High error rate", prompt);
        Assert.Contains("42 matching log events", prompt);
        Assert.Contains("Services: checkout", prompt);
        Assert.Contains("current: 42", prompt);
        Assert.Contains("40x [ERROR] checkout: Payment failed for order <*>", prompt);
        Assert.Contains("payments / POST /charge (812.5 ms): card declined", prompt);
    }

    [Fact]
    public void Build_RedactsEvidenceAndRuleText()
    {
        var prompt = IncidentSummaryPromptBuilder.Build(
            Evidence(Rule("alert for ops@example.com")) with
            {
                LogPatterns = [new IncidentLogPattern("login failed password=hunter2 from 10.1.2.3", "auth", 17, 5, "")],
            },
            12_000);

        Assert.DoesNotContain("ops@example.com", prompt);
        Assert.DoesNotContain("hunter2", prompt);
        Assert.DoesNotContain("10.1.2.3", prompt);
    }

    [Fact]
    public void Build_NoData_SaysSoAndSkipsEvidence()
    {
        var prompt = IncidentSummaryPromptBuilder.Build(Evidence() with { NoData = true }, 12_000);

        Assert.Contains("no data at all matched", prompt);
        Assert.DoesNotContain("Previous window", prompt);
    }

    [Fact]
    public void Build_OverBudget_DropsTrailingSectionsButKeepsTheAlert()
    {
        var patterns = Enumerable.Range(0, 8).Select(i => new IncidentLogPattern(new string('x', 250) + i, "svc", 17, 1, "")).ToList();
        var prompt = IncidentSummaryPromptBuilder.Build(
            Evidence() with { LogPatterns = patterns, ErrorSpans = [new IncidentErrorSpan("s", "span-name", "", 1)] },
            600);

        Assert.StartsWith("Alert: High error rate", prompt);
        Assert.True(prompt.Length <= 600);
        Assert.DoesNotContain("Failing spans", prompt);
    }

    [Fact]
    public void Build_TinyBudget_TruncatesHeader()
    {
        Assert.Equal(20, IncidentSummaryPromptBuilder.Build(Evidence(), 20).Length);
    }

    [Fact]
    public void Build_MetricRule_ShowsMetricThresholdAndUnit()
    {
        var rule = Rule() with
        {
            ConditionKind = AlertConditionKind.MetricThreshold,
            MetricCondition = new MetricAlertCondition { MetricName = "http.server.duration", Type = MetricPointType.Gauge },
            MetricThresholdValue = 500,
        };

        var prompt = IncidentSummaryPromptBuilder.Build(Evidence(rule) with { Observed = 910, MetricUnit = "ms", Previous = 120 }, 12_000);

        Assert.Contains("metric http.server.duration", prompt);
        Assert.Contains("= 910 ms", prompt);
        Assert.Contains("500 ms", prompt);
        Assert.Contains("previous window", prompt, StringComparison.OrdinalIgnoreCase);
    }
}
