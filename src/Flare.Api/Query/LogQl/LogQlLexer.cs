using System.Globalization;
using System.Text;

namespace Flare.Api.Query.LogQl;

public enum LogQlTokenKind
{
    Identifier, // includes keywords - LogQlParser decides which identifiers are keywords in context, so "service"/"level" can be both a WHERE column and a GROUP BY column name without two token kinds
    String,
    Duration, // a digit-led run like "1h", "15m" - only meaningful inside time(...), see LogQlLexer's own remarks
    Star,
    LParen,
    RParen,
    Comma,
    Op, // = != <> < <= > >=
    Eof,
}

public readonly record struct LogQlToken(LogQlTokenKind Kind, string Text, int Position);

/// <summary>
/// Hand-rolled tokenizer for the SQL-query-row grammar - small enough (a handful of
/// keywords, one operator family, one string-literal form) that a lexer generator/library
/// would be more ceremony than the thing it replaces. <see cref="LogQlParser"/> is the
/// only caller.
/// </summary>
public static class LogQlLexer
{
    public static List<LogQlToken> Tokenize(string text)
    {
        var tokens = new List<LogQlToken>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            var start = i;
            switch (c)
            {
                case '*':
                    tokens.Add(new LogQlToken(LogQlTokenKind.Star, "*", start));
                    i++;
                    continue;
                case '(':
                    tokens.Add(new LogQlToken(LogQlTokenKind.LParen, "(", start));
                    i++;
                    continue;
                case ')':
                    tokens.Add(new LogQlToken(LogQlTokenKind.RParen, ")", start));
                    i++;
                    continue;
                case ',':
                    tokens.Add(new LogQlToken(LogQlTokenKind.Comma, ",", start));
                    i++;
                    continue;
                case '\'':
                    tokens.Add(ReadString(text, ref i));
                    continue;
            }

            if (c is '=' or '!' or '<' or '>')
            {
                tokens.Add(ReadOperator(text, ref i));
                continue;
            }

            // A digit-led token is only ever a time(...) duration in this grammar (see
            // this feature's plan doc: "no bare/numeric literals" elsewhere) - lexed
            // uniformly here rather than context-sensitively in the parser.
            if (char.IsDigit(c))
            {
                while (i < text.Length && (char.IsLetterOrDigit(text[i])))
                {
                    i++;
                }

                tokens.Add(new LogQlToken(LogQlTokenKind.Duration, text[start..i], start));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                {
                    i++;
                }

                tokens.Add(new LogQlToken(LogQlTokenKind.Identifier, text[start..i], start));
                continue;
            }

            throw new LogQlParseException($"Unexpected character '{c}' at position {start}.");
        }

        tokens.Add(new LogQlToken(LogQlTokenKind.Eof, string.Empty, i));
        return tokens;
    }

    private static LogQlToken ReadString(string text, ref int i)
    {
        var start = i;
        i++; // opening '
        var sb = new StringBuilder();
        while (true)
        {
            if (i >= text.Length)
            {
                throw new LogQlParseException($"Unterminated string literal starting at position {start}.");
            }

            var c = text[i];
            if (c == '\'')
            {
                // '' is the SQL-standard escaped single quote; a lone ' closes the literal.
                if (i + 1 < text.Length && text[i + 1] == '\'')
                {
                    sb.Append('\'');
                    i += 2;
                    continue;
                }

                i++; // closing '
                break;
            }

            sb.Append(c);
            i++;
        }

        return new LogQlToken(LogQlTokenKind.String, sb.ToString(), start);
    }

    private static LogQlToken ReadOperator(string text, ref int i)
    {
        var start = i;
        var c = text[i];
        var next = i + 1 < text.Length ? text[i + 1] : '\0';

        string op;
        if (c == '!' && next == '=')
        {
            op = "!=";
            i += 2;
        }
        else if (c == '<' && next == '>')
        {
            op = "<>";
            i += 2;
        }
        else if (c == '<' && next == '=')
        {
            op = "<=";
            i += 2;
        }
        else if (c == '>' && next == '=')
        {
            op = ">=";
            i += 2;
        }
        else if (c is '=' or '<' or '>')
        {
            op = c.ToString(CultureInfo.InvariantCulture);
            i += 1;
        }
        else
        {
            throw new LogQlParseException($"Unexpected character '{c}' at position {start}.");
        }

        return new LogQlToken(LogQlTokenKind.Op, op, start);
    }
}
