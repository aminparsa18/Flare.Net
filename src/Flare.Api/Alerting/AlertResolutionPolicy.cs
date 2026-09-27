using Flare.Api.Model;

namespace Flare.Api.Alerting;

/// <summary>What <c>AlertEvaluationWorker</c> does when a rule evaluates as not breached - see <see cref="AlertResolutionPolicy.Decide"/>.</summary>
public enum AlertResolutionAction
{
    /// <summary>The rule wasn't firing - nothing to resolve.</summary>
    None,

    /// <summary>Someone was paged, but a maintenance window is active: resolve on the first ok evaluation after it ends.</summary>
    Defer,

    /// <summary>Record the resolution without sending anything - every fire since the last resolution was suppressed, so nobody was paged.</summary>
    RecordOnly,

    /// <summary>Send "Resolved" to the rule's opted-in channels and record the resolution.</summary>
    Notify,
}

/// <summary>
/// The firing→ok transition rule for "Resolved" notifications - pure, so it's unit-testable
/// without ClickHouse. See <c>docs-internal/adr/0064-alert-resolved-notifications.md</c>.
/// </summary>
public static class AlertResolutionPolicy
{
    /// <param name="state">The rule's firing state (<c>IAlertQueryService.GetFiringStatesAsync</c>); null when it's ok.</param>
    /// <param name="maintenanceWindowActive">Whether a maintenance window covering the rule is active right now.</param>
    /// <remarks>
    /// Only called for a real "not breached" result - an evaluation that couldn't decide
    /// (insufficient data, a misconfigured rule) never resolves. A maintenance window defers
    /// rather than suppresses: it silences every notification while it's active, and a
    /// resolution recorded silently would leave a paged PagerDuty incident open for good.
    /// A window never defers a <see cref="AlertResolutionAction.RecordOnly"/>, which sends nothing anyway.
    /// </remarks>
    public static AlertResolutionAction Decide(AlertFiringState? state, bool maintenanceWindowActive) => state switch
    {
        null => AlertResolutionAction.None,
        { Notified: false } => AlertResolutionAction.RecordOnly,
        _ when maintenanceWindowActive => AlertResolutionAction.Defer,
        _ => AlertResolutionAction.Notify,
    };
}
