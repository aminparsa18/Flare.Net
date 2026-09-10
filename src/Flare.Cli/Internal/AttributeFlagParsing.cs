namespace Flare.Cli.Internal;

/// <summary>
/// Shared parsing for the repeatable <c>--attr</c>/<c>--attr-not</c>/<c>--attr-exists</c>/
/// <c>--attr-absent</c> flags <see cref="Flare.Cli.Commands.SearchCommand"/>/
/// <see cref="Flare.Cli.Commands.TracesCommand"/> (and their dashboard-terminal ports
/// <c>search.ts</c>/<c>traces.ts</c>) both expose - same "parse once, each command wraps
/// the result in its own wire DTO/default bag" split <see cref="SeverityLevels"/> already
/// establishes for <c>--level</c>. Always targets the filter's default/primary attribute
/// bag (<c>LogAttributes</c>/<c>SpanAttributes</c>) - no <c>--attr-bag</c> flag yet, same
/// scope the roadmap item that added this shipped with.
/// </summary>
internal static class AttributeFlagParsing
{
    internal readonly record struct ParsedAttr(string Key, string Value, string Operator);

    /// <summary>
    /// Parses <c>key=value</c> pairs (Equals/NotEquals, from <paramref name="equals"/>/
    /// <paramref name="notEquals"/>) plus bare keys (Exists/Absent, from
    /// <paramref name="exists"/>/<paramref name="absent"/>) into a flat list. Returns
    /// false with a human-readable <paramref name="error"/> on the first malformed entry
    /// (an <c>--attr</c>/<c>--attr-not</c> value missing <c>=</c> or with an empty key, or
    /// an empty <c>--attr-exists</c>/<c>--attr-absent</c> key) rather than skipping it
    /// silently.
    /// </summary>
    internal static bool TryParse(
        string[] equals,
        string[] notEquals,
        string[] exists,
        string[] absent,
        out List<ParsedAttr> parsed,
        out string error)
    {
        parsed = [];

        return TryParseKeyValue(equals, "--attr", "Equals", parsed, out error)
            && TryParseKeyValue(notEquals, "--attr-not", "NotEquals", parsed, out error)
            && TryParseBareKey(exists, "--attr-exists", "Exists", parsed, out error)
            && TryParseBareKey(absent, "--attr-absent", "Absent", parsed, out error);
    }

    private static bool TryParseKeyValue(string[] values, string flag, string operatorName, List<ParsedAttr> into, out string error)
    {
        foreach (var raw in values)
        {
            var separator = raw.IndexOf('=');
            if (separator <= 0)
            {
                error = $"{flag} expects KEY=VALUE, got '{raw}'.";
                return false;
            }

            into.Add(new ParsedAttr(raw[..separator], raw[(separator + 1)..], operatorName));
        }

        error = "";
        return true;
    }

    private static bool TryParseBareKey(string[] values, string flag, string operatorName, List<ParsedAttr> into, out string error)
    {
        foreach (var raw in values)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                error = $"{flag} expects a non-empty KEY.";
                return false;
            }

            into.Add(new ParsedAttr(raw, "", operatorName));
        }

        error = "";
        return true;
    }
}
