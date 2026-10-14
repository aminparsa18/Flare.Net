using System.Globalization;
using System.Text;

namespace Flare.Api.Tempo;

/// <summary>A TraceQL query this subset cannot read or does not support. The message names the construct.</summary>
public sealed class TraceQlException(string message) : Exception(message);

public enum TraceQlOp
{
    Eq,
    Neq,
    Re,
    Nre,
    Gt,
    Gte,
    Lt,
    Lte,
}

public enum TraceQlValueKind
{
    String,
    Number,

    /// <summary>A duration literal such as <c>250ms</c>; <see cref="TraceQlValue.Nanos"/> holds it.</summary>
    Duration,
    Bool,

    /// <summary>The bare words <c>error</c>, <c>ok</c> and <c>unset</c>.</summary>
    Status,

    /// <summary>The bare span kind words (<c>server</c>, <c>client</c>, ...). Parsed so <c>kind</c> can be rejected by name.</summary>
    Kind,
}

/// <param name="Text">The string's contents, or the literal exactly as written.</param>
/// <param name="Nanos">The value in nanoseconds for a <see cref="TraceQlValueKind.Duration"/>.</param>
public sealed record TraceQlValue(TraceQlValueKind Kind, string Text, ulong Nanos = 0);

/// <param name="Field">As written: an intrinsic (<c>name</c>), or <c>span.x</c> / <c>resource.x</c> / <c>.x</c>.</param>
public sealed record TraceQlPredicate(string Field, TraceQlOp Op, TraceQlValue Value);

/// <summary>One <c>{ ... }</c>: predicates that must all hold for the same span. Empty means any span.</summary>
public sealed record TraceQlSpanset(IReadOnlyList<TraceQlPredicate> Predicates);

/// <param name="Spansets">In order of appearance; spanset <c>i</c> is condition <c>A + i</c> in <see cref="Expression"/>.</param>
/// <param name="Expression">The structural expression over the spansets' letters (<c>A -&gt; B</c>, <c>A &amp;&amp; B</c>...), in <see cref="Query.TraceStructureExpression"/>'s grammar.</param>
public sealed record TraceQlQuery(IReadOnlyList<TraceQlSpanset> Spansets, string Expression);

/// <summary>
/// A hand-written parser for the TraceQL subset the Tempo-compatible API accepts (ADR-0217).
/// Pure, no ClickHouse dependency. Grammar, loosest to tightest:
/// <code>
/// query     := or
/// or        := and ("||" and)*
/// and       := relation ("&amp;&amp;" relation)*
/// relation  := operand ((">" | ">>") operand)?
/// operand   := spanset | "(" or ")"
/// spanset   := "{" [predicate ("&amp;&amp;" predicate)*] "}"
/// predicate := field op value
/// op        := "=" | "!=" | "=~" | "!~" | ">" | ">=" | "&lt;" | "&lt;="
/// value     := string | number | duration | "true" | "false" | "error" | "ok" | "unset"
/// </code>
/// <c>&gt;</c> between spansets is "has a direct child matching", <c>&gt;&gt;</c> is "has a descendant
/// matching" - the same relations the structural trace query builds (ADR-0069). Anything past a
/// spanset expression (a <c>|</c> pipeline: <c>count()</c>, <c>select()</c>, ...), a <c>||</c> inside a
/// spanset, sibling and negated structural operators and arithmetic are rejected with a message that
/// names them, never half-evaluated.
/// </summary>
public static class TraceQlParser
{
    public const int MaxLength = 4000;

    public static TraceQlQuery Parse(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new TraceQlException("the query is empty.");
        }

        if (query.Length > MaxLength)
        {
            throw new TraceQlException($"the query is longer than {MaxLength} characters.");
        }

        var parser = new Parser(Tokenize(query));
        var spansets = new List<TraceQlSpanset>();
        var expression = parser.ParseOr(spansets);
        if (!parser.AtEnd)
        {
            throw Unexpected(parser.Peek);
        }

        if (spansets.Count > Query.TraceStructureSqlBuilder.MaxConditions)
        {
            throw new TraceQlException($"a query can combine at most {Query.TraceStructureSqlBuilder.MaxConditions} spansets.");
        }

        return new TraceQlQuery(spansets, expression);
    }

    private static TraceQlException Unexpected(Token token) => token.Kind == TokenKind.End
        ? new TraceQlException("the query ends unexpectedly.")
        : new TraceQlException($"unexpected '{token.Text}' at position {token.Position}.");

    private enum TokenKind
    {
        LBrace,
        RBrace,
        LParen,
        RParen,
        Operator,
        Word,
        String,
        Pipe,
        End,
    }

    private readonly record struct Token(TokenKind Kind, string Text, int Position);

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            switch (c)
            {
                case '{':
                    tokens.Add(new Token(TokenKind.LBrace, "{", i++));
                    continue;
                case '}':
                    tokens.Add(new Token(TokenKind.RBrace, "}", i++));
                    continue;
                case '(':
                    tokens.Add(new Token(TokenKind.LParen, "(", i++));
                    continue;
                case ')':
                    tokens.Add(new Token(TokenKind.RParen, ")", i++));
                    continue;
                case '"':
                case '`':
                    tokens.Add(ReadString(text, ref i));
                    continue;
            }

            var two = i + 1 < text.Length ? text.AsSpan(i, 2) : default;
            if (two is "&&" or "||" or "!=" or "=~" or "!~" or ">=" or "<=" or ">>")
            {
                // "|" alone starts a pipeline stage; "||" is the boolean operator, so the two-char check comes first.
                tokens.Add(new Token(TokenKind.Operator, two.ToString(), i));
                i += 2;
                continue;
            }

            if (c is '=' or '>' or '<')
            {
                tokens.Add(new Token(TokenKind.Operator, c.ToString(), i++));
                continue;
            }

            if (c == '|')
            {
                tokens.Add(new Token(TokenKind.Pipe, "|", i++));
                continue;
            }

            if (IsWordChar(c))
            {
                var start = i;
                while (i < text.Length && IsWordChar(text[i]))
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.Word, text[start..i], start));
                continue;
            }

            throw new TraceQlException($"unexpected '{c}' at position {i}.");
        }

        tokens.Add(new Token(TokenKind.End, "", text.Length));
        return tokens;
    }

    /// <summary>Attribute names carry dots, dashes, colons and slashes; numbers and durations (<c>1.5s</c>, <c>250µs</c>) share the class.</summary>
    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '.' or '-' or ':' or '/' or 'µ';

    private static Token ReadString(string text, ref int i)
    {
        var start = i;
        var quote = text[i++];
        var sb = new StringBuilder();
        while (i < text.Length && text[i] != quote)
        {
            if (quote == '"' && text[i] == '\\' && i + 1 < text.Length)
            {
                i++;
                sb.Append(text[i] switch { 'n' => '\n', 't' => '\t', 'r' => '\r', var other => other });
                i++;
                continue;
            }

            sb.Append(text[i++]);
        }

        if (i >= text.Length)
        {
            throw new TraceQlException($"the string starting at position {start} is not closed.");
        }

        i++;
        return new Token(TokenKind.String, sb.ToString(), start);
    }

    private sealed class Parser(List<Token> tokens)
    {
        private int _pos;

        public bool AtEnd => Peek.Kind == TokenKind.End;

        public Token Peek => tokens[_pos];

        public string ParseOr(List<TraceQlSpanset> spansets)
        {
            var left = ParseAnd(spansets);
            while (IsOperator("||"))
            {
                _pos++;
                left = $"({left} || {ParseAnd(spansets)})";
            }

            return left;
        }

        private string ParseAnd(List<TraceQlSpanset> spansets)
        {
            var left = ParseRelation(spansets);
            while (IsOperator("&&"))
            {
                _pos++;
                left = $"({left} && {ParseRelation(spansets)})";
            }

            return left;
        }

        private string ParseRelation(List<TraceQlSpanset> spansets)
        {
            var left = ParseOperand(spansets);
            if (!IsOperator(">") && !IsOperator(">>"))
            {
                return left;
            }

            var direct = Peek.Text == ">";
            _pos++;
            var right = ParseOperand(spansets);
            // The structure expression only relates single conditions, so both sides must be spansets, not groups.
            if (left.Length != 1 || right.Length != 1)
            {
                throw new TraceQlException("a structural operator (> or >>) relates two spansets, not groups of them.");
            }

            return $"{left} {(direct ? "->" : "=>")} {right}";
        }

        private string ParseOperand(List<TraceQlSpanset> spansets)
        {
            if (Peek.Kind == TokenKind.LParen)
            {
                _pos++;
                var inner = ParseOr(spansets);
                Expect(TokenKind.RParen, ")");
                return inner;
            }

            if (Peek.Kind == TokenKind.Pipe)
            {
                throw PipelineNotSupported();
            }

            if (Peek.Kind != TokenKind.LBrace)
            {
                throw Unexpected(Peek);
            }

            _pos++;
            var predicates = new List<TraceQlPredicate>();
            if (Peek.Kind != TokenKind.RBrace)
            {
                predicates.Add(ParsePredicate());
                while (IsOperator("&&"))
                {
                    _pos++;
                    predicates.Add(ParsePredicate());
                }

                if (IsOperator("||"))
                {
                    throw new TraceQlException("'||' inside a spanset is not supported; combine whole spansets instead, e.g. { a } || { b }.");
                }
            }

            Expect(TokenKind.RBrace, "}");
            if (Peek.Kind == TokenKind.Pipe)
            {
                throw PipelineNotSupported();
            }

            spansets.Add(new TraceQlSpanset(predicates));
            // Letter 'A' + index; the caller bounds the count by MaxConditions (< 26).
            return ((char)('A' + spansets.Count - 1)).ToString();
        }

        private TraceQlPredicate ParsePredicate()
        {
            var fieldToken = Peek;
            if (fieldToken.Kind != TokenKind.Word)
            {
                throw Unexpected(fieldToken);
            }

            _pos++;
            var opToken = Peek;
            var op = opToken.Kind == TokenKind.Operator ? opToken.Text switch
            {
                "=" => (TraceQlOp?)TraceQlOp.Eq,
                "!=" => TraceQlOp.Neq,
                "=~" => TraceQlOp.Re,
                "!~" => TraceQlOp.Nre,
                ">" => TraceQlOp.Gt,
                ">=" => TraceQlOp.Gte,
                "<" => TraceQlOp.Lt,
                "<=" => TraceQlOp.Lte,
                _ => null,
            } : null;
            if (op is null)
            {
                throw opToken.Kind is TokenKind.RBrace or TokenKind.End
                    ? new TraceQlException($"'{fieldToken.Text}' needs a comparison, e.g. {fieldToken.Text} = \"x\".")
                    : Unexpected(opToken);
            }

            _pos++;
            return new TraceQlPredicate(fieldToken.Text, op.Value, ParseValue());
        }

        private TraceQlValue ParseValue()
        {
            var token = Peek;
            _pos++;
            switch (token.Kind)
            {
                case TokenKind.String:
                    return new TraceQlValue(TraceQlValueKind.String, token.Text);
                case TokenKind.Word:
                    return WordValue(token);
                default:
                    _pos--;
                    throw Unexpected(token);
            }
        }

        private static TraceQlValue WordValue(Token token)
        {
            var text = token.Text;
            switch (text)
            {
                case "true" or "false":
                    return new TraceQlValue(TraceQlValueKind.Bool, text);
                case "error" or "ok" or "unset":
                    return new TraceQlValue(TraceQlValueKind.Status, text);
                case "unspecified" or "internal" or "server" or "client" or "producer" or "consumer":
                    return new TraceQlValue(TraceQlValueKind.Kind, text);
                case "nil":
                    throw new TraceQlException("nil comparisons are not supported.");
            }

            if (!char.IsDigit(text[0]) && !(text[0] == '-' && text.Length > 1 && char.IsDigit(text[1])))
            {
                throw new TraceQlException($"'{text}' is not a value; quote strings, e.g. \"{text}\". Comparing two fields is not supported.");
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number))
            {
                return new TraceQlValue(TraceQlValueKind.Number, text);
            }

            return TryParseDuration(text, out var nanos)
                ? new TraceQlValue(TraceQlValueKind.Duration, text, nanos)
                : throw new TraceQlException($"'{text}' is not a valid number or duration (units: ns, us, ms, s, m, h).");
        }

        private bool IsOperator(string text) => Peek.Kind == TokenKind.Operator && Peek.Text == text;

        private void Expect(TokenKind kind, string text)
        {
            if (Peek.Kind != kind)
            {
                throw Peek.Kind == TokenKind.End ? new TraceQlException($"expected '{text}' but the query ends.") : Unexpected(Peek);
            }

            _pos++;
        }

        private static TraceQlException PipelineNotSupported() =>
            new("pipeline stages after a spanset (| count(), | avg(), | select(), | by(), ...) are not supported.");
    }

    /// <summary>A TraceQL / Tempo duration: a number with a unit (<c>ns us µs ms s m h</c>), or several, such as <c>1m30s</c>.</summary>
    public static bool TryParseDuration(string text, out ulong nanos)
    {
        nanos = 0;
        var i = 0;
        var total = 0.0;
        var any = false;
        while (i < text.Length)
        {
            var start = i;
            while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.'))
            {
                i++;
            }

            if (i == start || !double.TryParse(text.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
            {
                return false;
            }

            var unitStart = i;
            while (i < text.Length && !char.IsDigit(text[i]) && text[i] != '.')
            {
                i++;
            }

            double unit;
            switch (text[unitStart..i])
            {
                case "ns": unit = 1; break;
                case "us" or "µs": unit = 1e3; break;
                case "ms": unit = 1e6; break;
                case "s": unit = 1e9; break;
                case "m": unit = 60e9; break;
                case "h": unit = 3600e9; break;
                default: return false;
            }

            total += amount * unit;
            any = true;
        }

        if (!any || total < 0 || total >= ulong.MaxValue)
        {
            return false;
        }

        nanos = (ulong)Math.Round(total);
        return true;
    }
}
