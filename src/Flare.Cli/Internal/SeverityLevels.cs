namespace Flare.Cli.Internal;

/// <summary>
/// OTel <c>SeverityNumber</c> (0-24) bucket boundaries for the CLI's <c>--level</c>
/// flag. Mirrors <c>src/dashboard/src/lib/logs/severity.ts</c> exactly - that file is
/// the single source of truth for "what counts as a warning" on the dashboard side;
/// this is the CLI-side duplicate of the same standard OTel bucket boundaries (TRACE
/// 1-4, DEBUG 5-8, INFO 9-12, WARN 13-16, ERROR 17-20, FATAL 21-24), not a shared
/// reference - the CLI never takes a compile-time dependency on the dashboard or API
/// projects, same boundary <see cref="ComposeRunner"/> draws around Flare's Docker
/// images (opaque wire contract, not shared code).
/// </summary>
internal static class SeverityLevels
{
    private static readonly Dictionary<string, (byte Min, byte Max)> Buckets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["trace"] = ((byte)1, (byte)4),
        ["debug"] = ((byte)5, (byte)8),
        ["info"] = ((byte)9, (byte)12),
        ["warn"] = ((byte)13, (byte)16),
        ["warning"] = ((byte)13, (byte)16),
        ["error"] = ((byte)17, (byte)20),
        ["fatal"] = ((byte)21, (byte)24),
    };

    public static bool TryExpand(string level, out IReadOnlyList<byte> severityNumbers)
    {
        if (!Buckets.TryGetValue(level.Trim(), out var range))
        {
            severityNumbers = [];
            return false;
        }

        var numbers = new List<byte>(range.Max - range.Min + 1);
        for (var n = range.Min; n <= range.Max; n++)
        {
            numbers.Add(n);
        }

        severityNumbers = numbers;
        return true;
    }

    public static string LabelFor(byte severityNumber) => severityNumber switch
    {
        >= 1 and <= 4 => "TRACE",
        >= 5 and <= 8 => "DEBUG",
        >= 9 and <= 12 => "INFO",
        >= 13 and <= 16 => "WARN",
        >= 17 and <= 20 => "ERROR",
        >= 21 and <= 24 => "FATAL",
        _ => "UNSPEC",
    };

    public static string ColorFor(byte severityNumber) => severityNumber switch
    {
        >= 13 and <= 16 => "yellow",
        >= 17 and <= 24 => "red",
        >= 9 and <= 12 => "white",
        _ => "grey",
    };
}
