using Flare.Api.Alerting;
using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Alerting;

/// <summary>
/// Covers <see cref="AlertNoDataEvaluator.IsAbsentAsync"/>'s branching - which count it asks
/// <see cref="IAlertQueryService"/> for, over which window, and when it doesn't ask at all.
/// The counts themselves come from a canned fake; the real ClickHouse queries behind them
/// are covered by <see cref="Query.MetricAlertConditionQueryBuilderTests"/> and end-to-end
/// runs, same split as the rest of this project.
/// </summary>
public class AlertNoDataEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private static readonly MetricAlertCondition Metric = new() { MetricName = "process.threads", Type = MetricPointType.Gauge };

    [Fact]
    public async Task Disabled_NeverQueries()
    {
        var alerts = new FakeAlertQueryService();

        var absent = await AlertNoDataEvaluator.IsAbsentAsync(alerts, AlertConditionKind.LogCount, new LogFilter(), null, 0, Now, default);

        Assert.False(absent);
        Assert.Empty(alerts.Calls);
    }

    [Theory]
    [InlineData(0UL, true)]
    [InlineData(1UL, false)]
    public async Task LogCount_AbsentOnlyWhenZeroLogsInTheNoDataWindow(ulong logs, bool expected)
    {
        var alerts = new FakeAlertQueryService { LogCount = logs };

        var absent = await AlertNoDataEvaluator.IsAbsentAsync(alerts, AlertConditionKind.LogCount, new LogFilter(), null, 900, Now, default);

        Assert.Equal(expected, absent);
        var call = Assert.Single(alerts.Calls);
        Assert.Equal(("logs", Now.AddSeconds(-900), Now), call);
    }

    [Theory]
    [InlineData(0UL, true)]
    [InlineData(3UL, false)]
    public async Task MetricThreshold_CountsRawPoints(ulong points, bool expected)
    {
        var alerts = new FakeAlertQueryService { MetricPointCount = points };

        var absent = await AlertNoDataEvaluator.IsAbsentAsync(alerts, AlertConditionKind.MetricThreshold, new LogFilter(), Metric, 600, Now, default);

        Assert.Equal(expected, absent);
        Assert.Equal(("metricPoints", Now.AddSeconds(-600), Now), Assert.Single(alerts.Calls));
    }

    [Fact]
    public async Task MetricThreshold_WithoutCondition_IsNotAbsent()
    {
        var alerts = new FakeAlertQueryService();

        Assert.False(await AlertNoDataEvaluator.IsAbsentAsync(alerts, AlertConditionKind.MetricThreshold, new LogFilter(), null, 600, Now, default));
        Assert.Empty(alerts.Calls);
    }

    [Fact]
    public async Task ExceptionCount_IsNeverAbsent()
    {
        var alerts = new FakeAlertQueryService();

        Assert.False(await AlertNoDataEvaluator.IsAbsentAsync(alerts, AlertConditionKind.ExceptionCount, new LogFilter(), null, 600, Now, default));
        Assert.Empty(alerts.Calls);
    }

    private sealed class FakeAlertQueryService : IAlertQueryService
    {
        public ulong LogCount { get; init; }

        public ulong MetricPointCount { get; init; }

        public List<(string Kind, DateTimeOffset From, DateTimeOffset To)> Calls { get; } = [];

        public Task<ulong> CountMatchingLogsAsync(LogFilter condition, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
        {
            Calls.Add(("logs", from, to));
            return Task.FromResult(LogCount);
        }

        public Task<ulong> CountMatchingMetricPointsAsync(MetricAlertCondition condition, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
        {
            Calls.Add(("metricPoints", from, to));
            return Task.FromResult(MetricPointCount);
        }

        public Task<AlertRule> CreateAsync(AlertRuleRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<AlertRule>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AlertRule?> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AlertRule?> UpdateAsync(Guid id, AlertRuleRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<AlertRule>> GetEnabledRulesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<(double Value, string? Unit)> EvaluateMetricConditionAsync(MetricAlertCondition condition, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ulong> CountMatchingExceptionsAsync(ExceptionCountCondition condition, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DateTimeOffset?> GetLastFiredAsync(Guid ruleId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task InsertEventAsync(AlertHistoryEntry entry, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<AlertHistoryEntry>> GetHistoryAsync(Guid ruleId, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
