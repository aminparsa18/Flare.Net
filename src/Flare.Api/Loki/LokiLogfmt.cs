using System.Text.RegularExpressions;

namespace Flare.Api.Loki;

/// <summary>
/// <c>| logfmt</c> (ADR-0237): <c>key=value</c> pairs separated by whitespace, a value being a bare word or a
/// double-quoted string with <c>\"</c> and <c>\\</c> escapes. One regular expression defines a pair, and both
/// the ClickHouse side (<see cref="MapSql"/>, which filters and groups on the labels) and the C# side
/// (<see cref="Parse"/>, which reports them in a response and drives live tail) use it, so a line cannot
/// match a filter on one side and show different labels on the other. A line that is a JSON object is not
/// logfmt (Loki's decoder rejects its quotes), so it has no logfmt labels. Keys are sanitised to
/// <c>[a-zA-Z0-9_]</c> as Loki does; a key with no value, or an empty value, is not a label. Pure.
/// </summary>
internal static class LokiLogfmt
{
    private const string Ws = @"[ \t\n\r\f]";

    /// <summary>A pair, in RE2 syntax (also valid .NET): group 1 the key, group 2 a quoted value, group 3 a bare one.</summary>
    public const string PairPattern = "(?:^|" + Ws + ")([^ \\t\\n\\r\\f=\"]+)=(?:\"((?:[^\"\\\\]|\\\\.)*)\"|([^ \\t\\n\\r\\f\"]*))";

    private const string UnescapePattern = "\\\\([\"\\\\])";

    private static readonly Regex Pair = new(PairPattern, RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly Regex Unescape = new(UnescapePattern, RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex NotLabelChar = new("[^a-zA-Z0-9_]", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>The ClickHouse <c>WITH</c> aliases that define <c>lkv</c>, the line's logfmt labels as a <c>Map(String, String)</c>.</summary>
    public static string MapSql =>
        $"extractAllGroups(if(startsWith(trimLeft(Body), '{{'), '', Body), {LokiSqlLiteral.Of(PairPattern)}) AS lkp, " +
        $"mapFromArrays(arrayMap(g -> replaceRegexpAll(g[1], '[^a-zA-Z0-9_]', '_'), lkp), " +
        $"arrayMap(g -> if(g[2] != '', replaceRegexpAll(g[2], {LokiSqlLiteral.Of(UnescapePattern)}, '\\\\1'), g[3]), lkp)) AS lkv";

    public static string Sanitize(string key) => NotLabelChar.Replace(key, "_");

    /// <summary>The labels of <paramref name="body"/>, in order of appearance; a repeated key keeps its last value.</summary>
    public static IReadOnlyList<(string Key, string Value)> Parse(string body)
    {
        var result = new List<(string, string)>();
        if (body.AsSpan().TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            return result;
        }

        try
        {
            foreach (Match match in Pair.Matches(body))
            {
                var value = match.Groups[2].Success ? Unescape.Replace(match.Groups[2].Value, "$1") : match.Groups[3].Value;
                if (value.Length > 0)
                {
                    result.Add((Sanitize(match.Groups[1].Value), value));
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // A pathological line yields the pairs found so far.
        }

        return result;
    }
}
