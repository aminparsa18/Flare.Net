using Flare.Api.Model;

namespace Flare.Api.Alerting;

/// <summary>Outcome of one notification attempt, recorded verbatim into an <see cref="AlertHistoryEntry"/>.</summary>
public sealed record NotificationResult(bool Success, int StatusCode, string? Error);

public interface IAlertNotifier
{
    /// <param name="rule">
    /// The firing/tested rule - used for message formatting (name, id, condition kind)
    /// only. The actual destination comes from <paramref name="channel"/>, not from
    /// <paramref name="rule"/>'s own legacy inline channel fields - see
    /// <c>NotificationChannelResolver</c> for how a rule's legacy fields or
    /// <see cref="AlertRule.ChannelIds"/> become the <see cref="NotificationChannel"/>(s)
    /// passed here.
    /// </param>
    /// <param name="channel">
    /// The destination to send through - either a real, saved <see cref="NotificationChannel"/>
    /// (from <see cref="AlertRule.ChannelIds"/>) or one synthesized on the fly from a
    /// rule's legacy inline fields (never persisted in that case). Every implementation
    /// reads its destination field(s) (<see cref="NotificationChannel.WebhookUrl"/>, etc.)
    /// from here, never from <paramref name="rule"/>.
    /// </param>
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
    /// <param name="metricUnit">
    /// Passed straight through to <see cref="AlertMessageFormatter.BuildText"/> - see that
    /// parameter's own doc comment. Null for non-<see cref="AlertConditionKind.MetricThreshold"/>
    /// rules and every <paramref name="isTest"/> send (test wording never formats <paramref name="observedValue"/>).
    /// </param>
    /// <param name="noData">
    /// True when this fire comes from absent-data alerting (<see cref="AlertRule.NoDataWindowSeconds"/>) -
    /// the condition matched nothing at all, so <paramref name="observedValue"/> is a meaningless
    /// 0 and every implementation swaps in <see cref="AlertMessageFormatter"/>'s "no data"
    /// wording instead of reporting a threshold breach.
    /// </param>
    /// <param name="anomaly">
    /// Set only for an <see cref="AlertConditionKind.Anomaly"/> fire - the baseline the current
    /// value (<paramref name="observedValue"/>) was scored against. Every implementation swaps
    /// in <see cref="AlertMessageFormatter"/>'s anomaly wording; the structured payloads also
    /// carry its mean/z-score.
    /// </param>
    Task<NotificationResult> SendAsync(AlertRule rule, NotificationChannel channel, double observedValue, DateTimeOffset firedAt, CancellationToken cancellationToken, bool isTest = false, string? metricUnit = null, bool noData = false, AnomalyScore? anomaly = null);
}
