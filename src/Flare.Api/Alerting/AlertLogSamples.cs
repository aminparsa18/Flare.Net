using System.Globalization;

namespace Flare.Api.Alerting;

/// <summary>
/// Formatting for the <c>{{log_samples}}</c> placeholder - the newest few events behind a
/// <see cref="Model.AlertConditionKind.LogCount"/> fire, one line each, so the notification
/// shows what happened and not only how many times. One line per event (newlines in a body
/// collapse to spaces) keeps every channel's rendering predictable.
/// </summary>
public static class AlertLogSamples
{
    /// <summary>How many events a LogCount notification samples.</summary>
    public const int Count = 5;

    public const int MaxBodyChars = 500;

    public static string FormatLine(DateTime timestamp, string severity, string service, string body)
    {
        var flat = string.Join(' ', body.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (flat.Length > MaxBodyChars)
        {
            flat = flat[..MaxBodyChars] + "...";
        }

        var time = DateTime.SpecifyKind(timestamp, DateTimeKind.Utc).ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        return $"{time} {severity} {service}: {flat}";
    }

    /// <summary>The lines as the placeholder/default-message block, or "" when there are none.</summary>
    public static string Join(IReadOnlyList<string>? samples) =>
        samples is { Count: > 0 } ? string.Join('\n', samples) : "";
}
