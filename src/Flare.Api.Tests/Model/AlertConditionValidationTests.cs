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

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(60)]
    [InlineData(3600)]
    public void NoDataWindow_DisabledOrAtLeastMinimum_IsValid(int? noDataWindowSeconds)
    {
        var request = Build(AlertConditionKind.LogCount, noDataWindowSeconds: noDataWindowSeconds);

        Assert.Null(request.ValidateCondition());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(59)]
    public void NoDataWindow_NegativeOrBelowMinimum_IsInvalid(int noDataWindowSeconds)
    {
        var request = Build(AlertConditionKind.LogCount, noDataWindowSeconds: noDataWindowSeconds);

        Assert.NotNull(request.ValidateCondition());
    }

    [Fact]
    public void NoDataWindow_OnMetricThreshold_IsValid()
    {
        var request = Build(AlertConditionKind.MetricThreshold, MakeCondition(), metricThresholdValue: 500.0, noDataWindowSeconds: 600);

        Assert.Null(request.ValidateCondition());
    }

    [Fact]
    public void NoDataWindow_OnExceptionCount_IsInvalid()
    {
        var request = Build(
            AlertConditionKind.ExceptionCount,
            exceptionCondition: new ExceptionCountCondition { ExceptionType = "System.InvalidOperationException" },
            noDataWindowSeconds: 600);

        Assert.Contains("ExceptionCount", request.ValidateCondition());
    }

    [Fact]
    public void NoDataWindow_Zero_OnExceptionCount_IsValid()
    {
        var request = Build(
            AlertConditionKind.ExceptionCount,
            exceptionCondition: new ExceptionCountCondition { ExceptionType = "System.InvalidOperationException" },
            noDataWindowSeconds: 0);

        Assert.Null(request.ValidateCondition());
    }

    [Fact]
    public void MissingCondition_ReportedBeforeNoDataWindowError()
    {
        var request = Build(AlertConditionKind.MetricThreshold, metricCondition: null, metricThresholdValue: null, noDataWindowSeconds: 1);

        Assert.Contains("metricCondition", request.ValidateCondition());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(60)]
    [InlineData(300)]
    public void EvaluationInterval_DisabledOrWithinWindow_IsValid(int? evaluationIntervalSeconds)
    {
        var request = Build(AlertConditionKind.LogCount, evaluationIntervalSeconds: evaluationIntervalSeconds);

        Assert.Null(request.ValidateCondition());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(30)]
    [InlineData(86_401)]
    public void EvaluationInterval_NegativeOrOutOfRange_IsInvalid(int evaluationIntervalSeconds)
    {
        var request = Build(AlertConditionKind.LogCount, evaluationIntervalSeconds: evaluationIntervalSeconds, windowSeconds: 100_000);

        Assert.Contains("evaluationIntervalSeconds", request.ValidateCondition());
    }

    [Fact]
    public void EvaluationInterval_LongerThanWindow_IsInvalid()
    {
        var request = Build(AlertConditionKind.LogCount, evaluationIntervalSeconds: 900, windowSeconds: 300);

        Assert.Contains("windowSeconds", request.ValidateCondition());
    }

    [Fact]
    public void EvaluationInterval_AtMaximumWithWideWindow_IsValid()
    {
        var request = Build(AlertConditionKind.LogCount, evaluationIntervalSeconds: AlertRuleRequest.MaxEvaluationIntervalSeconds, windowSeconds: AlertRuleRequest.MaxEvaluationIntervalSeconds);

        Assert.Null(request.ValidateCondition());
    }

    [Fact]
    public void Anomaly_LogCountSource_IsValid()
    {
        var request = Build(AlertConditionKind.Anomaly, anomalyCondition: new AnomalyCondition { BaselinePeriods = 7, ZScoreThreshold = 3 });

        Assert.Null(request.ValidateCondition());
    }

    [Fact]
    public void Anomaly_MissingAnomalyCondition_IsInvalid()
    {
        Assert.NotNull(Build(AlertConditionKind.Anomaly).ValidateCondition());
    }

    [Fact]
    public void Anomaly_MetricSource_NeedsMetricConditionButNoThresholdValue()
    {
        var anomaly = new AnomalyCondition { Source = AlertConditionKind.MetricThreshold, BaselinePeriods = 7, ZScoreThreshold = 3 };

        Assert.NotNull(Build(AlertConditionKind.Anomaly, anomalyCondition: anomaly).ValidateCondition());
        Assert.Null(Build(AlertConditionKind.Anomaly, MakeCondition(), anomalyCondition: anomaly).ValidateCondition());
    }

    [Fact]
    public void Anomaly_ExceptionSource_NeedsExceptionCondition()
    {
        var anomaly = new AnomalyCondition { Source = AlertConditionKind.ExceptionCount, BaselinePeriods = 7, ZScoreThreshold = 3 };

        Assert.NotNull(Build(AlertConditionKind.Anomaly, anomalyCondition: anomaly).ValidateCondition());
        Assert.Null(Build(AlertConditionKind.Anomaly, exceptionCondition: new ExceptionCountCondition { ExceptionType = "X" }, anomalyCondition: anomaly).ValidateCondition());
    }

    [Fact]
    public void Anomaly_AnomalySource_IsInvalid()
    {
        Assert.NotNull(Build(AlertConditionKind.Anomaly, anomalyCondition: new AnomalyCondition { Source = AlertConditionKind.Anomaly, BaselinePeriods = 7, ZScoreThreshold = 3 }).ValidateCondition());
    }

    [Theory]
    [InlineData(2, 3.0)]
    [InlineData(13, 3.0)]
    [InlineData(7, 0.0)]
    [InlineData(7, -1.0)]
    [InlineData(7, 10.5)]
    [InlineData(7, double.NaN)]
    public void Anomaly_OutOfRangeScoringParameters_AreInvalid(int periods, double z)
    {
        var anomaly = new AnomalyCondition { BaselinePeriods = periods, ZScoreThreshold = z };

        Assert.NotNull(Build(AlertConditionKind.Anomaly, anomalyCondition: anomaly).ValidateCondition());
    }

    [Theory]
    [InlineData(AnomalySeasonality.Daily, 86_400, false)]
    [InlineData(AnomalySeasonality.Daily, 86_399, true)]
    [InlineData(AnomalySeasonality.Weekly, 86_400, true)]
    public void Anomaly_WindowMustBeShorterThanThePeriod(AnomalySeasonality seasonality, int windowSeconds, bool valid)
    {
        var request = Build(AlertConditionKind.Anomaly, windowSeconds: windowSeconds, anomalyCondition: new AnomalyCondition { Seasonality = seasonality, BaselinePeriods = 7, ZScoreThreshold = 3 });

        Assert.Equal(valid, request.ValidateCondition() is null);
    }

    [Theory]
    [InlineData(AlertConditionKind.LogCount, true)]
    [InlineData(AlertConditionKind.ExceptionCount, false)]
    public void Anomaly_NoDataWindow_FollowsTheSourceKind(AlertConditionKind source, bool valid)
    {
        var request = Build(
            AlertConditionKind.Anomaly,
            exceptionCondition: new ExceptionCountCondition { ExceptionType = "X" },
            noDataWindowSeconds: 600,
            anomalyCondition: new AnomalyCondition { Source = source, BaselinePeriods = 7, ZScoreThreshold = 3 });

        Assert.Equal(valid, request.ValidateCondition() is null);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(AlertRuleRequest.MaxMinDataPoints)]
    public void MinDataPoints_DisabledOrInRange_IsValidForMetricThreshold(int? minDataPoints)
    {
        var request = Build(AlertConditionKind.MetricThreshold, MakeCondition(), 1.0, minDataPoints: minDataPoints);

        Assert.Null(request.ValidateCondition());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(AlertRuleRequest.MaxMinDataPoints + 1)]
    public void MinDataPoints_NegativeOrTooLarge_IsInvalid(int minDataPoints)
    {
        var request = Build(AlertConditionKind.MetricThreshold, MakeCondition(), 1.0, minDataPoints: minDataPoints);

        Assert.Contains("minDataPoints", request.ValidateCondition());
    }

    [Theory]
    [InlineData(AlertConditionKind.LogCount)]
    [InlineData(AlertConditionKind.ExceptionCount)]
    [InlineData(AlertConditionKind.Anomaly)]
    public void MinDataPoints_OnNonMetricKind_IsInvalid(AlertConditionKind kind)
    {
        var request = Build(
            kind,
            MakeCondition(),
            exceptionCondition: MakeExceptionCondition(),
            anomalyCondition: new AnomalyCondition { Source = AlertConditionKind.MetricThreshold, BaselinePeriods = 7, ZScoreThreshold = 3 },
            minDataPoints: 3);

        Assert.Contains("minDataPoints", request.ValidateCondition());
    }

    private static AlertRuleRequest Build(
        AlertConditionKind? conditionKind,
        MetricAlertCondition? metricCondition = null,
        double? metricThresholdValue = null,
        ExceptionCountCondition? exceptionCondition = null,
        int? noDataWindowSeconds = null,
        int? evaluationIntervalSeconds = null,
        int windowSeconds = 300,
        AnomalyCondition? anomalyCondition = null,
        int? minDataPoints = null) => new()
    {
        Name = "test",
        Threshold = new AlertThreshold { Count = 1 },
        WindowSeconds = windowSeconds,
        EvaluationIntervalSeconds = evaluationIntervalSeconds,
        WebhookUrl = "https://hooks.slack.com/services/x",
        ConditionKind = conditionKind,
        MetricCondition = metricCondition,
        MetricThresholdValue = metricThresholdValue,
        ExceptionCondition = exceptionCondition,
        NoDataWindowSeconds = noDataWindowSeconds,
        AnomalyCondition = anomalyCondition,
        MinDataPoints = minDataPoints,
    };
}
