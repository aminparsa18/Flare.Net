using Flare.Api.Alerting;
using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Alerting;

/// <summary>
/// Covers <see cref="AnomalyEvaluator.EvaluateAsync"/>'s routing - which
/// <see cref="IAlertQueryService"/> call each source uses, and over which windows. Values come
/// from a canned fake, same split as <see cref="AlertNoDataEvaluatorTests"/>.
/// </summary>
public class AnomalyEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LogCountSource_QueriesCurrentThenEachBaselineWindow()
    {
        var alerts = new FakeAlertQueryService(from => from == Now.AddSeconds(-300) ? 10 : 100);
        var anomaly = new AnomalyCondition { Source = AlertConditionKind.LogCount, BaselinePeriods = 3, Direction = AnomalyDirection.Below, ZScoreThreshold = 3 };

        var result = await AnomalyEvaluator.EvaluateAsync(alerts, anomaly, new LogFilter(), null, null, 300, Now, default);

        Assert.NotNull(result);
        Assert.True(result.Value.Score.Breached);
        Assert.Equal(10, result.Value.Score.Current);
        Assert.Equal(100, result.Value.Score.BaselineMean);
        Assert.Equal(
            [
                ("logs", Now.AddSeconds(-300), Now),
                ("logs", Now.AddDays(-1).AddSeconds(-300), Now.AddDays(-1)),
                ("logs", Now.AddDays(-2).AddSeconds(-300), Now.AddDays(-2)),
                ("logs", Now.AddDays(-3).AddSeconds(-300), Now.AddDays(-3)),
            ],
            alerts.Calls);
    }

    [Fact]
    public async Task MetricSource_UsesTheMetricQueryAndReturnsItsUnit()
    {
        var alerts = new FakeAlertQueryService(_ => 0.25);
        var metric = new MetricAlertCondition { MetricName = "http.server.duration", Type = MetricPointType.Histogram, Aggregation = MetricAlertAggregation.P99 };
        var anomaly = new AnomalyCondition { Source = AlertConditionKind.MetricThreshold, BaselinePeriods = 3, ZScoreThreshold = 3 };

        var result = await AnomalyEvaluator.EvaluateAsync(alerts, anomaly, new LogFilter(), metric, null, 300, Now, default);

        Assert.Equal("s", result!.Value.MetricUnit);
        Assert.All(alerts.Calls, c => Assert.Equal("metric", c.Kind));
        Assert.Equal(4, alerts.Calls.Count);
    }

    [Fact]
    public async Task ExceptionSource_UsesTheExceptionCount()
    {
        var alerts = new FakeAlertQueryService(_ => 5);
        var anomaly = new AnomalyCondition { Source = AlertConditionKind.ExceptionCount, BaselinePeriods = 3, ZScoreThreshold = 3 };

        await AnomalyEvaluator.EvaluateAsync(alerts, anomaly, new LogFilter(), null, new ExceptionCountCondition { ExceptionType = "System.TimeoutException" }, 300, Now, default);

        Assert.All(alerts.Calls, c => Assert.Equal("exceptions", c.Kind));
    }

    [Theory]
    [InlineData(AlertConditionKind.MetricThreshold)]
    [InlineData(AlertConditionKind.ExceptionCount)]
    [InlineData(AlertConditionKind.Anomaly)]
    public async Task MissingSourceCondition_ReturnsNullWithoutQuerying(AlertConditionKind source)
    {
        var alerts = new FakeAlertQueryService(_ => 1);

        var result = await AnomalyEvaluator.EvaluateAsync(alerts, new AnomalyCondition { Source = source, BaselinePeriods = 7, ZScoreThreshold = 3 }, new LogFilter(), null, null, 300, Now, default);

        Assert.Null(result);
        Assert.Empty(alerts.Calls);
    }

    private sealed class FakeAlertQueryService(Func<DateTimeOffset, double> valueAt) : IAlertQueryService
    {
        public List<(string Kind, DateTimeOffset From, DateTimeOffset To)> Calls { get; } = [];

        public Task<ulong> CountMatchingLogsAsync(LogFilter condition, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
        {
            Calls.Add(("logs", from, to));
            return Task.FromResult((ulong)valueAt(from));
        }

        public Task<(double Value, string? Unit)> EvaluateMetricConditionAsync(MetricAlertCondition condition, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
        {
            Calls.Add(("metric", from, to));
            return Task.FromResult<(double, string?)>((valueAt(from), "s"));
        }

        public Task<ulong> CountMatchingExceptionsAsync(ExceptionCountCondition condition, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
        {
            Calls.Add(("exceptions", from, to));
            return Task.FromResult((ulong)valueAt(from));
        }

        public Task<ulong> CountMatchingMetricPointsAsync(MetricAlertCondition condition, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AlertRule> CreateAsync(AlertRuleRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<AlertRule>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AlertRule?> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AlertRule?> UpdateAsync(Guid id, AlertRuleRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<AlertRule>> GetEnabledRulesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DateTimeOffset?> GetLastFiredAsync(Guid ruleId, bool includeSuppressed, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task InsertEventAsync(AlertHistoryEntry entry, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<AlertHistoryEntry>> GetHistoryAsync(Guid ruleId, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
