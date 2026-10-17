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
/// <remarks>
/// <c>| logfmt --strict</c> (ADR-0238) reports a malformed line as <c>__error__="LogfmtParserErr"</c> and keeps
/// only the labels found before the first fault, as Loki does. <see cref="StrictItemPattern"/> is the grammar of
/// Loki's decoder as one item: a key with an optional bare value, or a key with a quoted one. A line is valid
/// when it is whole items (<see cref="StrictValidPattern"/>), and the labels before the fault are those of the
/// longest run of items from the start (<see cref="StrictPrefixPattern"/>), found by the same expressions in
/// ClickHouse and in C#.
/// </remarks>
internal static class LokiLogfmt
{
    private const string Ws = @"[ \t\n\r\f]";

    /// <summary>A pair, in RE2 syntax (also valid .NET): group 1 the key, group 2 a quoted value, group 3 a bare one.</summary>
    public const string PairPattern = "(?:^|" + Ws + ")([^ \\t\\n\\r\\f=\"]+)=(?:\"((?:[^\"\\\\]|\\\\.)*)\"|([^ \\t\\n\\r\\f\"]*))";

    private const string Key = "[^ \\t\\n\\r\\f=\"]+";
    private const string Bare = "[^ \\t\\n\\r\\f=\"]*";
    private const string Quoted = "\"(?:[^\"\\\\]|\\\\(?:[\"\\\\/bfnrt]|u[0-9a-fA-F]{4}))*\"";

    /// <summary>One well-formed item of a strict logfmt line (RE2 syntax, also valid .NET): <c>key</c>, <c>key=</c>, <c>key=bare</c> or <c>key="quoted"</c>, after any whitespace.</summary>
    public const string StrictItemPattern = "(?:" + Ws + "*" + Key + "=" + Quoted + "|" + Ws + "*" + Key + "(?:=" + Bare + ")?(?:" + Ws + "|$))";

    /// <summary>The longest run of well-formed items from the start of the line: what a strict parse reads before it stops at a fault.</summary>
    public const string StrictPrefixPattern = "^(?:" + StrictItemPattern + ")*";

    /// <summary>A line that is nothing but well-formed items: the lines a strict parse accepts.</summary>
    public const string StrictValidPattern = StrictPrefixPattern + Ws + "*$";

    private const string UnescapePattern = "\\\\([\"\\\\])";

    private static readonly Regex Pair = new(PairPattern, RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly Regex StrictPrefix = new(StrictPrefixPattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1));
    private static readonly Regex StrictValid = new(StrictValidPattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1));
    private static readonly Regex Unescape = new(UnescapePattern, RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex NotLabelChar = new("[^a-zA-Z0-9_]", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>The ClickHouse <c>WITH</c> aliases that define <c>lkv</c>, the line's logfmt labels as a <c>Map(String, String)</c>.</summary>
    public static string MapSql =>
        $"extractAllGroups(if(startsWith(trimLeft(Body), '{{'), '', Body), {LokiSqlLiteral.Of(PairPattern)}) AS lkp, " +
        $"mapFromArrays(arrayMap(g -> replaceRegexpAll(g[1], '[^a-zA-Z0-9_]', '_'), lkp), " +
        $"arrayMap(g -> if(g[2] != '', replaceRegexpAll(g[2], {LokiSqlLiteral.Of(UnescapePattern)}, '\\\\1'), g[3]), lkp)) AS lkv";

    /// <summary>
    /// <see cref="MapSql"/> for <c>| logfmt --strict</c>: <c>lkv</c> is read from the well-formed prefix of the
    /// line only, and <c>lkok</c> says whether the whole line was well formed.
    /// </summary>
    public static string StrictMapSql =>
        $"extract(Body, {LokiSqlLiteral.Of(StrictPrefixPattern)}) AS lkb, " +
        $"match(Body, {LokiSqlLiteral.Of(StrictValidPattern)}) AS lkok, " +
        $"extractAllGroups(lkb, {LokiSqlLiteral.Of(PairPattern)}) AS lkp, " +
        $"mapFromArrays(arrayMap(g -> replaceRegexpAll(g[1], '[^a-zA-Z0-9_]', '_'), lkp), " +
        $"arrayMap(g -> if(g[2] != '', replaceRegexpAll(g[2], {LokiSqlLiteral.Of(UnescapePattern)}, '\\\\1'), g[3]), lkp)) AS lkv";

    public static string Sanitize(string key) => NotLabelChar.Replace(key, "_");

    /// <summary>The labels of <paramref name="body"/>, in order of appearance; a repeated key keeps its last value.</summary>
    public static IReadOnlyList<(string Key, string Value)> Parse(string body) =>
        body.AsSpan().TrimStart().StartsWith("{", StringComparison.Ordinal) ? [] : Extract(body);

    private static List<(string Key, string Value)> Extract(string text)
    {
        var result = new List<(string, string)>();
        try
        {
            foreach (Match match in Pair.Matches(text))
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

    /// <summary>
    /// The labels of <paramref name="body"/> under <c>--strict</c>: the pairs of the well-formed prefix, and
    /// whether the line had a fault after it (<c>__error__="LogfmtParserErr"</c>).
    /// </summary>
    public static (IReadOnlyList<(string Key, string Value)> Pairs, bool Failed) ParseStrict(string body)
    {
        try
        {
            var failed = !StrictValid.IsMatch(body);
            return (Extract(failed ? StrictPrefix.Match(body).Value : body), failed);
        }
        catch (RegexMatchTimeoutException)
        {
            return ([], true);
        }
    }
}
