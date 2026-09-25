using Flare.Api.Alerting;
using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Alerting;

/// <summary>
/// Covers <see cref="AlertTemplateRenderer"/>'s substitution/validation and
/// <see cref="AlertMessageFormatter.BuildMessage"/>'s choice between custom templates and the
/// built-in wording (ADR-0052).
/// </summary>
public class AlertTemplateRendererTests
{
    private static readonly Dictionary<string, string> NoLabels = [];

    private static AlertRule MakeRule() => new()
    {
        Id = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        Name = "High error rate",
        Condition = new LogFilter
        {
            Services = ["checkout"],
            Attributes =
            [
                new AttributeFilter { Key = "deployment.environment", Value = "prod" },
                new AttributeFilter { Key = "http.route", Value = "/pay", Operator = AttributeFilterOperator.NotEquals },
            ],
        },
        Threshold = new AlertThreshold { Count = 10 },
        WindowSeconds = 60,
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    [Fact]
    public void Render_SubstitutesKnownPlaceholders_ToleratingInnerWhitespace()
    {
        var text = AlertTemplateRenderer.Render("{{rule_name}} = {{ value }}", new Dictionary<string, string> { ["rule_name"] = "r", ["value"] = "5" }, NoLabels);

        Assert.Equal("r = 5", text);
    }

    [Fact]
    public void Render_DottedLabelKey_ResolvesAndMissingLabelIsEmpty()
    {
        var labels = new Dictionary<string, string> { ["service.name"] = "checkout" };

        var text = AlertTemplateRenderer.Render("[{{labels.service.name}}][{{labels.host.name}}]", NoLabels, labels);

        Assert.Equal("[checkout][]", text);
    }

    [Fact]
    public void Render_LeavesNonPlaceholderBracesAndUnknownNamesVerbatim()
    {
        var text = AlertTemplateRenderer.Render("{\"a\": 1} {{ }} {{nope}}", NoLabels, NoLabels);

        Assert.Equal("{\"a\": 1} {{ }} {{nope}}", text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{{rule_name}} fired - {{labels.service.name}} {{value}}")]
    public void Validate_KnownPlaceholdersOrEmpty_IsValid(string? template)
    {
        Assert.Null(AlertTemplateRenderer.Validate(template, "t", 100));
    }

    [Fact]
    public void Validate_UnknownPlaceholder_NamesItOnce()
    {
        var error = AlertTemplateRenderer.Validate("{{rule_nme}} {{rule_nme}} {{labels.}}", "notificationBodyTemplate", 100);

        Assert.NotNull(error);
        Assert.StartsWith("notificationBodyTemplate uses unknown placeholder(s): {{rule_nme}}, {{labels.}}.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_TooLong_IsRejected()
    {
        Assert.NotNull(AlertTemplateRenderer.Validate(new string('x', 11), "t", 10));
    }

    [Fact]
    public void BuildTemplateLabels_TakesServicesAndEqualityAttributesOnly()
    {
        var labels = AlertMessageFormatter.BuildTemplateLabels(MakeRule());

        Assert.Equal("checkout", labels["service.name"]);
        Assert.Equal("prod", labels["deployment.environment"]);
        Assert.False(labels.ContainsKey("http.route"));
    }

    [Fact]
    public void BuildMessage_NoTemplates_IsTheBuiltInText()
    {
        var rule = MakeRule();

        var message = AlertMessageFormatter.BuildMessage(rule, 42, isTest: false, "https://flare.example.com", null, DateTimeOffset.UnixEpoch, noData: false, anomaly: null);

        Assert.Null(message.Title);
        Assert.False(message.IsCustom);
        Assert.Equal(AlertMessageFormatter.BuildText(rule, 42, publicUrl: "https://flare.example.com", firedAt: DateTimeOffset.UnixEpoch), message.Text);
    }

    [Fact]
    public void BuildMessage_CustomTemplates_RenderValuesAndLinks()
    {
        var rule = MakeRule() with
        {
            NotificationTitleTemplate = "[{{status}}] {{rule_name}}",
            NotificationBodyTemplate = "{{labels.service.name}}: {{value}} {{comparator}} {{threshold}} in {{window}}\n{{rule_url}}",
        };

        var message = AlertMessageFormatter.BuildMessage(rule, 42, isTest: false, "https://flare.example.com", null, DateTimeOffset.UnixEpoch, noData: false, anomaly: null);

        Assert.True(message.IsCustom);
        Assert.Equal("[firing] High error rate", message.Title);
        Assert.Equal("checkout: 42 >= 10 in 60s\nhttps://flare.example.com/alerts?rule=11111111-2222-3333-4444-555555555555", message.Text);
        Assert.Equal($"{message.Title}\n{message.Text}", message.Combined);
    }

    [Fact]
    public void BuildMessage_TitleOnly_KeepsBuiltInBody()
    {
        var rule = MakeRule() with { NotificationTitleTemplate = "{{rule_name}}" };

        var message = AlertMessageFormatter.BuildMessage(rule, 42, isTest: false, null, null, DateTimeOffset.UnixEpoch, noData: false, anomaly: null);

        Assert.Equal("High error rate", message.Title);
        Assert.Equal(AlertMessageFormatter.BuildText(rule, 42, firedAt: DateTimeOffset.UnixEpoch), message.Text);
    }

    [Fact]
    public void BuildMessage_TestSend_PrefixesCustomWording()
    {
        var rule = MakeRule() with { NotificationTitleTemplate = "{{rule_name}}", NotificationBodyTemplate = "{{status}}" };

        var message = AlertMessageFormatter.BuildMessage(rule, 0, isTest: true, null, null, DateTimeOffset.UnixEpoch, noData: false, anomaly: null);

        Assert.Equal("[Test] High error rate", message.Title);
        Assert.Equal("test", message.Text);
    }

    [Fact]
    public void BuildMessage_TestSendWithoutTitle_PrefixesBody()
    {
        var rule = MakeRule() with { NotificationBodyTemplate = "{{status}}" };

        var message = AlertMessageFormatter.BuildMessage(rule, 0, isTest: true, null, null, DateTimeOffset.UnixEpoch, noData: false, anomaly: null);

        Assert.Null(message.Title);
        Assert.Equal("[Test] test", message.Text);
    }

    [Fact]
    public void BuildMessage_NoDataFire_ReportsNoDataWindowAndNoLogsLink()
    {
        var rule = MakeRule() with { NoDataWindowSeconds = 900, NotificationBodyTemplate = "{{status}}|{{value}}|{{window}}|{{logs_url}}" };

        var message = AlertMessageFormatter.BuildMessage(rule, 0, isTest: false, "https://flare.example.com", null, DateTimeOffset.UnixEpoch, noData: true, anomaly: null);

        Assert.Equal("no data|no data|900s|", message.Text);
    }

    [Fact]
    public void BuildMessage_MetricRule_FormatsValueWithUnit()
    {
        var rule = MakeRule() with
        {
            ConditionKind = AlertConditionKind.MetricThreshold,
            MetricCondition = new MetricAlertCondition { MetricName = "process.memory.usage", Type = MetricPointType.Gauge },
            MetricThresholdValue = 1_073_741_824,
            NotificationBodyTemplate = "{{metric}} {{value}} vs {{threshold}}",
        };

        var message = AlertMessageFormatter.BuildMessage(rule, 2_147_483_648, isTest: false, null, "By", DateTimeOffset.UnixEpoch, noData: false, anomaly: null);

        Assert.Equal("process.memory.usage 2 GB vs 1 GB", message.Text);
    }

    [Fact]
    public void BuildMessage_MessagePlaceholder_IsBuiltInTextWithoutLinks()
    {
        var rule = MakeRule() with { NotificationBodyTemplate = "{{message}} - runbook: https://wiki/x" };

        var message = AlertMessageFormatter.BuildMessage(rule, 42, isTest: false, "https://flare.example.com", null, DateTimeOffset.UnixEpoch, noData: false, anomaly: null);

        Assert.Equal($"{AlertMessageFormatter.BuildText(rule, 42)} - runbook: https://wiki/x", message.Text);
    }

    [Fact]
    public void ValidateCondition_RejectsUnknownTemplatePlaceholder()
    {
        var request = new AlertRuleRequest
        {
            Name = "r",
            Threshold = new AlertThreshold { Count = 1 },
            WindowSeconds = 60,
            NotificationBodyTemplate = "{{oops}}",
        };

        Assert.Contains("{{oops}}", request.ValidateCondition(), StringComparison.Ordinal);
    }
}
