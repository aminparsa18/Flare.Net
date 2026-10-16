using System.Text;

namespace Flare.Api.Loki;

/// <summary>
/// Loki's pattern line filter (<c>|&gt; "&lt;_&gt; level=&lt;_&gt;"</c>, ADR-0237) as a regular expression, shared by
/// the ClickHouse <c>match()</c> and the in-memory matcher of live tail. A <c>&lt;_&gt;</c> stands for any text;
/// as in Loki, a leading literal must start the line and the literals must then appear in order. Named
/// captures and two captures in a row are rejected, as Loki rejects them in a filter. Pure.
/// </summary>
internal static class LokiPatternMatcher
{
    /// <summary>Throws <see cref="LokiException"/> for a pattern Loki would reject.</summary>
    public static void Validate(string pattern) => _ = Tokenize(pattern);

    /// <summary>An RE2-compatible regular expression (also valid for .NET's non-backtracking engine) for <paramref name="pattern"/>.</summary>
    public static string ToRegex(string pattern)
    {
        var tokens = Tokenize(pattern);
        var sb = new StringBuilder("(?s)");
        if (tokens[0].IsLiteral)
        {
            sb.Append('^');
        }

        foreach (var (isLiteral, text) in tokens)
        {
            if (!isLiteral)
            {
                sb.Append(".*?");
                continue;
            }

            foreach (var c in text)
            {
                if (c < 128 && !char.IsAsciiLetterOrDigit(c) && !char.IsWhiteSpace(c) && c != '_' && !char.IsControl(c))
                {
                    sb.Append('\\');
                }

                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static List<(bool IsLiteral, string Text)> Tokenize(string pattern)
    {
        if (pattern.Length == 0)
        {
            throw new LokiException("a pattern filter needs a non-empty pattern.");
        }

        var tokens = new List<(bool IsLiteral, string Text)>();
        var literal = new StringBuilder();
        var pos = 0;
        var previousCapture = false;
        while (pos < pattern.Length)
        {
            if (pattern[pos] == '<')
            {
                var close = pattern.IndexOf('>', pos + 1);
                if (close > pos + 1 && IsName(pattern.AsSpan(pos + 1, close - pos - 1)))
                {
                    if (pattern[(pos + 1)..close] != "_")
                    {
                        throw new LokiException("named captures are not allowed in a pattern filter; use <_>.");
                    }

                    if (previousCapture)
                    {
                        throw new LokiException("a pattern filter cannot have two captures in a row.");
                    }

                    if (literal.Length > 0)
                    {
                        tokens.Add((true, literal.ToString()));
                        literal.Clear();
                    }

                    tokens.Add((false, "_"));
                    previousCapture = true;
                    pos = close + 1;
                    continue;
                }
            }

            literal.Append(pattern[pos++]);
            previousCapture = false;
        }

        if (literal.Length > 0)
        {
            tokens.Add((true, literal.ToString()));
        }

        return tokens;
    }

    private static bool IsName(ReadOnlySpan<char> name)
    {
        foreach (var c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }
}
