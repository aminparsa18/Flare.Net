using System.Text.Json;
using Flare.Api.Alerting;
using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Alerting;

/// <summary>
/// Covers <see cref="AlertMessageFormatter"/>'s deep-link behavior - null/blank
/// <c>publicUrl</c> omits the link entirely (today's behavior, unchanged for anyone who
/// hasn't set <see cref="AlertLinkOptions.PublicUrl"/>), a configured one produces
/// <c>{publicUrl}/alerts?rule={id}</c> with a trailing slash trimmed, appended as the last
/// line of <see cref="AlertMessageFormatter.BuildText"/>'s text - see that method's own
/// remarks for why a bare URL rather than channel-specific link markup.
/// </summary>
public class AlertMessageFormatterTests
{
    private static AlertRule MakeRule(string name = "High error rate") => new()
    {
        Id = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        Name = name,
        Condition = new LogFilter(),
        Threshold = new AlertThreshold { Count = 10 },
        WindowSeconds = 60,
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    [Fact]
    public void BuildRuleUrl_NullPublicUrl_ReturnsNull()
    {
        Assert.Null(AlertMessageFormatter.BuildRuleUrl(MakeRule(), null));
    }

    [Fact]
    public void BuildRuleUrl_BlankPublicUrl_ReturnsNull()
    {
        Assert.Null(AlertMessageFormatter.BuildRuleUrl(MakeRule(), "   "));
    }

    [Fact]
    public void BuildRuleUrl_ConfiguredPublicUrl_BuildsDeepLink()
    {
        var url = AlertMessageFormatter.BuildRuleUrl(MakeRule(), "https://flare.example.com");

        Assert.Equal("https://flare.example.com/alerts?rule=11111111-2222-3333-4444-555555555555", url);
    }

    [Fact]
    public void BuildRuleUrl_TrailingSlashOnPublicUrl_IsTrimmed()
    {
        var url = AlertMessageFormatter.BuildRuleUrl(MakeRule(), "https://flare.example.com/");

        Assert.Equal("https://flare.example.com/alerts?rule=11111111-2222-3333-4444-555555555555", url);
    }

    [Fact]
    public void BuildText_NoPublicUrl_HasNoLink()
    {
        var text = AlertMessageFormatter.BuildText(MakeRule(), observedValue: 42);

        Assert.DoesNotContain("http", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildText_WithPublicUrl_AppendsLinkOnItsOwnLine()
    {
        var text = AlertMessageFormatter.BuildText(MakeRule(), observedValue: 42, publicUrl: "https://flare.example.com");

        var lines = text.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Equal("https://flare.example.com/alerts?rule=11111111-2222-3333-4444-555555555555", lines[1]);
        Assert.Contains("High error rate", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void BuildText_ExceptionCountRule_NamesTheExceptionType()
    {
        var rule = MakeRule() with
        {
            ConditionKind = AlertConditionKind.ExceptionCount,
            ExceptionCondition = new ExceptionCountCondition { ExceptionType = "System.NullReferenceException" },
        };

        var text = AlertMessageFormatter.BuildText(rule, observedValue: 7);

        Assert.Contains("System.NullReferenceException occurred 7 times", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildText_MetricThresholdRule_NoUnit_FormatsAsPlainNumbers()
    {
        var rule = MakeRule() with
        {
            ConditionKind = AlertConditionKind.MetricThreshold,
            MetricCondition = new MetricAlertCondition { MetricName = "process.threads", Type = MetricPointType.Gauge },
            MetricThresholdValue = 500,
        };

        var text = AlertMessageFormatter.BuildText(rule, observedValue: 620);

        Assert.Contains("process.threads = 620 (>= 500)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildText_MetricThresholdRule_WithUnit_FormatsBothValuesAtSameScale()
    {
        var rule = MakeRule() with
        {
            ConditionKind = AlertConditionKind.MetricThreshold,
            MetricCondition = new MetricAlertCondition { MetricName = "process.memory.usage", Type = MetricPointType.Gauge },
            MetricThresholdValue = 500d * 1024 * 1024,
        };

        var text = AlertMessageFormatter.BuildText(rule, observedValue: 1000d * 1024 * 1024, metricUnit: "By");

        Assert.Contains("process.memory.usage = 1000 MB (>= 500 MB)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildText_MetricThresholdRule_NullThresholdValue_OmitsThresholdNumber()
    {
        var rule = MakeRule() with
        {
            ConditionKind = AlertConditionKind.MetricThreshold,
            MetricCondition = new MetricAlertCondition { MetricName = "process.threads", Type = MetricPointType.Gauge },
            MetricThresholdValue = null,
        };

        var text = AlertMessageFormatter.BuildText(rule, observedValue: 620, metricUnit: "By");

        Assert.Contains("process.threads = 620 B (>= )", text, StringComparison.Ordinal);
    }

    private static readonly DateTimeOffset FiredAt = new(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Reverses <see cref="AlertMessageFormatter.BuildMatchingLogsUrl"/>'s encoding the same way the dashboard's <c>parseLogsStateDeepLinkParam</c> does.</summary>
    private static JsonElement DecodeState(string url)
    {
        var query = new Uri(url).Query;
        Assert.StartsWith("?state=", query, StringComparison.Ordinal);
        var base64 = Uri.UnescapeDataString(query["?state=".Length..]);
        return JsonDocument.Parse(Convert.FromBase64String(base64)).RootElement;
    }

    [Fact]
    public void BuildMatchingLogsUrl_NullPublicUrl_ReturnsNull()
    {
        Assert.Null(AlertMessageFormatter.BuildMatchingLogsUrl(MakeRule(), null, FiredAt));
    }

    [Fact]
    public void BuildMatchingLogsUrl_LogCountRule_EncodesFilterAndEvaluatedWindow()
    {
        var rule = MakeRule() with
        {
            WindowSeconds = 300,
            Condition = new LogFilter
            {
                Services = ["checkout"],
                SeverityNumbers = [17, 21],
                Search = "timeout",
                Attributes = [new AttributeFilter { Bag = AttributeBag.Resource, Key = "deployment.environment", Value = "prod" }],
                BodyJsonFilters = [new BodyJsonFilter { Path = "user.id", Value = "", Operator = BodyJsonFilterOperator.In, Values = ["1", "2"] }],
            },
        };

        var url = AlertMessageFormatter.BuildMatchingLogsUrl(rule, "https://flare.example.com/", FiredAt);

        Assert.NotNull(url);
        Assert.StartsWith("https://flare.example.com/?state=", url, StringComparison.Ordinal);
        var state = DecodeState(url);
        Assert.Equal("custom", state.GetProperty("timeRangePreset").GetString());
        Assert.Equal("2026-09-24T09:55:00.000Z", state.GetProperty("customRange").GetProperty("from").GetString());
        Assert.Equal("2026-09-24T10:00:00.000Z", state.GetProperty("customRange").GetProperty("to").GetString());
        Assert.Equal("checkout", state.GetProperty("services")[0].GetString());
        Assert.Equal([17, 21], state.GetProperty("severityNumbers").EnumerateArray().Select(e => e.GetInt32()));
        Assert.Equal("timeout", state.GetProperty("search").GetString());

        var attribute = state.GetProperty("attributeFilters")[0];
        Assert.Equal("Resource", attribute.GetProperty("bag").GetString());
        Assert.Equal("deployment.environment", attribute.GetProperty("key").GetString());
        Assert.Equal("Equals", attribute.GetProperty("operator").GetString());
        Assert.False(attribute.TryGetProperty("values", out _));

        var bodyJson = state.GetProperty("bodyJsonFilters")[0];
        Assert.Equal("In", bodyJson.GetProperty("operator").GetString());
        Assert.Equal(["1", "2"], bodyJson.GetProperty("values").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void BuildMatchingLogsUrl_HasNoTelegramMarkdownMarkers()
    {
        // Enough varied bytes that base64url would almost certainly emit a '_' - standard
        // base64 + percent-escaping must never produce one.
        var rule = MakeRule() with { Condition = new LogFilter { Search = "??>>~~ü??>>~~ü" + new string('?', 40) } };

        var url = AlertMessageFormatter.BuildMatchingLogsUrl(rule, "https://flare.example.com", FiredAt)!;

        Assert.DoesNotContain('_', url);
        Assert.DoesNotContain('*', url);
        Assert.DoesNotContain('`', url);
        Assert.DoesNotContain('[', url);
        Assert.Equal("??>>~~ü??>>~~ü" + new string('?', 40), DecodeState(url).GetProperty("search").GetString());
    }

    [Theory]
    [InlineData(AlertConditionKind.MetricThreshold)]
    [InlineData(AlertConditionKind.ExceptionCount)]
    public void BuildMatchingLogsUrl_NonLogCountRule_ReturnsNull(AlertConditionKind kind)
    {
        var rule = MakeRule() with { ConditionKind = kind };

        Assert.Null(AlertMessageFormatter.BuildMatchingLogsUrl(rule, "https://flare.example.com", FiredAt));
    }

    [Fact]
    public void BuildMatchingLogsUrl_ConditionWithUnrepresentableField_ReturnsNull()
    {
        Assert.Null(AlertMessageFormatter.BuildMatchingLogsUrl(MakeRule() with { Condition = new LogFilter { TraceId = "abc" } }, "https://flare.example.com", FiredAt));
        Assert.Null(AlertMessageFormatter.BuildMatchingLogsUrl(MakeRule() with { Condition = new LogFilter { SpanId = "abc" } }, "https://flare.example.com", FiredAt));
        Assert.Null(AlertMessageFormatter.BuildMatchingLogsUrl(MakeRule() with { Condition = new LogFilter { PatternId = "abc" } }, "https://flare.example.com", FiredAt));
    }

    [Fact]
    public void BuildText_WithFiredAt_PutsMatchingLogsLineBeforeRuleLink()
    {
        var text = AlertMessageFormatter.BuildText(MakeRule(), observedValue: 42, publicUrl: "https://flare.example.com", firedAt: FiredAt);

        var lines = text.Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("Matching logs: https://flare.example.com/?state=", lines[1], StringComparison.Ordinal);
        Assert.Equal("https://flare.example.com/alerts?rule=11111111-2222-3333-4444-555555555555", lines[2]);
    }

    [Fact]
    public void BuildText_MetricRuleWithFiredAt_KeepsOnlyRuleLink()
    {
        var rule = MakeRule() with
        {
            ConditionKind = AlertConditionKind.MetricThreshold,
            MetricCondition = new MetricAlertCondition { MetricName = "process.threads", Type = MetricPointType.Gauge },
            MetricThresholdValue = 500,
        };

        var text = AlertMessageFormatter.BuildText(rule, observedValue: 620, publicUrl: "https://flare.example.com", firedAt: FiredAt);

        Assert.DoesNotContain("Matching logs", text, StringComparison.Ordinal);
        Assert.Equal(2, text.Split('\n').Length);
    }

    [Fact]
    public void BuildText_TestNotification_StillAppendsLink()
    {
        var text = AlertMessageFormatter.BuildText(MakeRule(), observedValue: 0, isTest: true, publicUrl: "https://flare.example.com");

        Assert.Contains("Test notification", text, StringComparison.Ordinal);
        Assert.EndsWith("https://flare.example.com/alerts?rule=11111111-2222-3333-4444-555555555555", text, StringComparison.Ordinal);
    }
}
