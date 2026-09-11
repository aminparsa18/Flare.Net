using Flare.Api.Model;

namespace Flare.Api.Alerting;

/// <summary>Outcome of one notification attempt, recorded verbatim into an <see cref="AlertHistoryEntry"/>.</summary>
public sealed record NotificationResult(bool Success, int StatusCode, string? Error);

public interface IAlertNotifier
{
    /// <param name="observedValue">
    /// The evaluated condition's result - a log-filter row count for
    /// <see cref="AlertConditionKind.LogCount"/> rules, a metric-query result (e.g. p99
    /// latency) for <see cref="AlertConditionKind.MetricThreshold"/> ones. <see cref="double"/>
    /// rather than the log-count-only <see cref="ulong"/> this used to be: every real log
    /// count is a whole number well within <see cref="double"/>'s exact-integer range, so
    /// this widening changes nothing for existing <see cref="AlertConditionKind.LogCount"/>
    /// rules - see <see cref="AlertMessageFormatter.BuildText"/> for how each implementation
    /// formats it per <see cref="AlertRule.ConditionKind"/>.
    /// </param>
    /// <param name="isTest">
    /// True for the "send test alert" endpoints (<c>AlertEndpoints.HandleSendTest*Async</c>) -
    /// every implementation swaps in <see cref="AlertMessageFormatter"/>'s test wording
    /// instead of treating <paramref name="observedValue"/> as a real breach, so a channel
    /// can be verified without anyone reading it mistaking it for a real incident.
    /// </param>
    Task<NotificationResult> SendAsync(AlertRule rule, double observedValue, DateTimeOffset firedAt, CancellationToken cancellationToken, bool isTest = false);
}
