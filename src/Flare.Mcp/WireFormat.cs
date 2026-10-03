using System.Globalization;

namespace Flare.Mcp;

internal static class WireFormat
{
    // Accepts any magnitude (15m, 36h, 7d) - mirrors dashboard time-range presets' grammar.
    internal static bool TryParseSince(string text, out TimeSpan span)
    {
        span = TimeSpan.Zero;
        var trimmed = text.Trim().ToLowerInvariant();
        if (trimmed.Length < 2)
        {
            return false;
        }

        var unit = trimmed[^1];
        var numberPart = trimmed[..^1];
        if (!double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            return false;
        }

        span = unit switch
        {
            's' => TimeSpan.FromSeconds(value),
            'm' => TimeSpan.FromMinutes(value),
            'h' => TimeSpan.FromHours(value),
            'd' => TimeSpan.FromDays(value),
            _ => TimeSpan.Zero,
        };

        return span > TimeSpan.Zero;
    }

    // Mirrors dashboard/src/lib/traces/duration.ts's formatDurationNano exactly.
    internal static string FormatDurationNano(ulong durationNano)
    {
        var ms = durationNano / 1_000_000d;
        if (ms < 1)
        {
            return $"{Math.Round(durationNano / 1_000d)}µs";
        }

        if (ms < 1_000)
        {
            return ms < 10 ? $"{ms:F2}ms" : $"{ms:F1}ms";
        }

        return $"{ms / 1_000:F2}s";
    }
}
