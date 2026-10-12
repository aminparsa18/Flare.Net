using Flare.Api.Model;

namespace Flare.Api.Alerting;

/// <summary>What the expiry warning says and when it is due. Pure, so the thresholds and wording are unit-testable.</summary>
public static class CredentialExpiryPolicy
{
    /// <summary>
    /// The warning threshold (in days) a credential has reached, or null when it has already expired or is further out
    /// than the largest threshold. It is the smallest threshold that still covers the time left, so a credential first
    /// seen 2 days out gets the "3 days" warning once rather than both.
    /// </summary>
    public static int? DueThreshold(DateTimeOffset expiresAt, DateTimeOffset now, IReadOnlyCollection<int> warnDays)
    {
        if (expiresAt <= now)
        {
            return null;
        }

        var left = expiresAt - now;
        int? due = null;
        foreach (var days in warnDays.Where(d => d > 0))
        {
            if (left <= TimeSpan.FromDays(days) && (due is null || days < due))
            {
                due = days;
            }
        }

        return due;
    }

    /// <summary>Redis key that records a warning as sent. Includes the expiry, so extending a credential re-arms it.</summary>
    public static string DedupKey(string kind, Guid id, DateTimeOffset expiresAt, int threshold) =>
        $"flare:credential-expiry:{kind}:{id}:{expiresAt.ToUnixTimeMilliseconds()}:{threshold}";

    /// <summary>The synthetic rule carrying the warning, dressed up the way <see cref="Status.StatusIncidentNotifier"/> does.</summary>
    public static AlertRule BuildRule(string kindLabel, string name, string? owner, Guid id, DateTimeOffset expiresAt, DateTimeOffset now, string? publicUrl, string? page)
    {
        var daysLeft = Math.Max(1, (int)Math.Ceiling((expiresAt - now).TotalDays));
        var lines = new List<string>
        {
            Literal($"The {kindLabel} \"{name}\"{(owner is null ? "" : $" (owned by {owner})")} stops working on {expiresAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC, in {daysLeft} day{(daysLeft == 1 ? "" : "s")}."),
            "Replace it, or extend its expiry, before then.",
        };
        if (!string.IsNullOrWhiteSpace(publicUrl) && page is not null)
        {
            lines.Add($"{publicUrl.TrimEnd('/')}{page}");
        }

        return new AlertRule
        {
            Id = id,
            Name = $"{kindLabel} expiry",
            Condition = new LogFilter(),
            Threshold = new AlertThreshold { Count = 1 },
            WindowSeconds = 60,
            NotificationTitleTemplate = Literal($"{kindLabel} \"{name}\" expires in {daysLeft} day{(daysLeft == 1 ? "" : "s")}"),
            NotificationBodyTemplate = string.Join("\n\n", lines),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private static string Literal(string text) => text.Replace("{{", "{ {", StringComparison.Ordinal);
}
