using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Model;

/// <summary>
/// Covers <see cref="AlertRuleRequest.ValidateCondition"/> - the condition-kind counterpart
/// to <see cref="AlertChannelValidationTests"/>'s channel validation, alongside the same
/// pure-logic set <see cref="AlertThresholdTests"/>'s doc comment already names.
/// </summary>
public class AlertConditionValidationTests
{
    [Fact]
    public void LogCount_NoMetricFieldsSet_IsValid()
    {
        var request = Build(AlertConditionKind.LogCount, metricCondition: null, metricThresholdValue: null);

        Assert.Null(request.ValidateCondition());
    }

    [Fact]
    public void ConditionKindOmitted_DefaultsToLogCount_IsValid()
    {
        var request = Build(conditionKind: null, metricCondition: null, metricThresholdValue: null);

        Assert.Null(request.ValidateCondition());
    }

    [Fact]
    public void MetricThreshold_WithConditionAndValue_IsValid()
    {
        var request = Build(AlertConditionKind.MetricThreshold, MakeCondition(), metricThresholdValue: 500.0);

        Assert.Null(request.ValidateCondition());
    }

    [Fact]
    public void MetricThreshold_MissingMetricCondition_IsInvalid()
    {
        var request = Build(AlertConditionKind.MetricThreshold, metricCondition: null, metricThresholdValue: 500.0);

        Assert.NotNull(request.ValidateCondition());
    }

    [Fact]
    public void MetricThreshold_MissingThresholdValue_IsInvalid()
    {
        var request = Build(AlertConditionKind.MetricThreshold, MakeCondition(), metricThresholdValue: null);

        Assert.NotNull(request.ValidateCondition());
    }

    [Fact]
    public void ExceptionCount_WithCondition_IsValid()
    {
        var request = Build(AlertConditionKind.ExceptionCount, exceptionCondition: MakeExceptionCondition());

        Assert.Null(request.ValidateCondition());
    }

    [Fact]
    public void ExceptionCount_MissingExceptionCondition_IsInvalid()
    {
        var request = Build(AlertConditionKind.ExceptionCount, exceptionCondition: null);

        Assert.NotNull(request.ValidateCondition());
    }

    private static MetricAlertCondition MakeCondition() => new() { MetricName = "process.threads", Type = MetricPointType.Gauge };

    private static ExceptionCountCondition MakeExceptionCondition() => new() { ExceptionType = "System.NullReferenceException" };

    private static AlertRuleRequest Build(
        AlertConditionKind? conditionKind,
        MetricAlertCondition? metricCondition = null,
        double? metricThresholdValue = null,
        ExceptionCountCondition? exceptionCondition = null) => new()
    {
        Name = "test",
        Threshold = new AlertThreshold { Count = 1 },
        WindowSeconds = 300,
        WebhookUrl = "https://hooks.slack.com/services/x",
        ConditionKind = conditionKind,
        MetricCondition = metricCondition,
        MetricThresholdValue = metricThresholdValue,
        ExceptionCondition = exceptionCondition,
    };
}
