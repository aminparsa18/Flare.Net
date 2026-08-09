using Flare.Api.Model;

namespace Flare.Api.Alerting;

/// <summary>Outcome of one notification attempt, recorded verbatim into an <see cref="AlertHistoryEntry"/>.</summary>
public sealed record NotificationResult(bool Success, int StatusCode, string? Error);

public interface IAlertNotifier
{
    Task<NotificationResult> SendAsync(AlertRule rule, ulong observedCount, DateTimeOffset firedAt, CancellationToken cancellationToken);
}
