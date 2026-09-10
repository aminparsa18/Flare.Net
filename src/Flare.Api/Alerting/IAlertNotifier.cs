using Flare.Api.Model;

namespace Flare.Api.Alerting;

/// <summary>Outcome of one notification attempt, recorded verbatim into an <see cref="AlertHistoryEntry"/>.</summary>
public sealed record NotificationResult(bool Success, int StatusCode, string? Error);

public interface IAlertNotifier
{
    /// <param name="isTest">
    /// True for the "send test alert" endpoints (<c>AlertEndpoints.HandleSendTest*Async</c>) -
    /// every implementation swaps in <see cref="AlertMessageFormatter"/>'s test wording
    /// instead of treating <paramref name="observedCount"/> as a real breach, so a channel
    /// can be verified without anyone reading it mistaking it for a real incident.
    /// </param>
    Task<NotificationResult> SendAsync(AlertRule rule, ulong observedCount, DateTimeOffset firedAt, CancellationToken cancellationToken, bool isTest = false);
}
