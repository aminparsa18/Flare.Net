using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Alerting;

/// <summary>Why <see cref="AlertAckResolver"/> found nothing to acknowledge.</summary>
public enum AlertAckFailure
{
    None,

    /// <summary>The signed token is malformed, tampered with or expired.</summary>
    InvalidToken,

    /// <summary>The rule is gone, not firing, or the token belongs to an earlier incident.</summary>
    NotFiring,
}

/// <summary>The rule and live incident an ack request points at, or why there is none.</summary>
public sealed record AlertAckResolution(AlertRule? Rule, AlertFiringState? State, AlertAckFailure Failure);

/// <summary>
/// Finds the firing incident an inbound ack refers to, shared by the signed link (ADR-0127), the
/// Slack button and PagerDuty sync (ADR-0138) so all three apply the same "only the live incident" rule.
/// </summary>
public static class AlertAckResolver
{
    /// <summary>
    /// The incident behind a signed <paramref name="token"/>. One issued before the rule's latest
    /// resolution is stale, so an old message can't acknowledge a later, unrelated incident.
    /// </summary>
    public static async Task<AlertAckResolution> ResolveTokenAsync(string? token, IAlertAckLinkSigner signer, IAlertQueryService alerts, CancellationToken cancellationToken)
    {
        if (signer.Validate(token) is not { } claims)
        {
            return new AlertAckResolution(null, null, AlertAckFailure.InvalidToken);
        }

        return await ResolveRuleAsync(claims.RuleId, claims.IssuedAt, alerts, cancellationToken);
    }

    /// <summary>The firing incident of <paramref name="ruleId"/>; with <paramref name="issuedAt"/>, only if it began no later than that moment's incident.</summary>
    public static async Task<AlertAckResolution> ResolveRuleAsync(Guid ruleId, DateTimeOffset? issuedAt, IAlertQueryService alerts, CancellationToken cancellationToken)
    {
        var rule = await alerts.GetAsync(ruleId, cancellationToken);
        var states = rule is null ? null : await alerts.GetFiringStatesAsync([rule.Id], cancellationToken);
        if (rule is null || states is null || !states.TryGetValue(rule.Id, out var state)
            || (issuedAt is { } issued && state.LastResolvedAt is { } resolvedAt && issued <= resolvedAt))
        {
            return new AlertAckResolution(null, null, AlertAckFailure.NotFiring);
        }

        return new AlertAckResolution(rule, state, AlertAckFailure.None);
    }
}
