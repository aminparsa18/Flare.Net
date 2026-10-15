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

    /// <summary>The bare span kind words (<c>server</c>, <c>client</c>, ...). Compared with <c>kind</c>.</summary>
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

/// <summary>The metrics functions a TraceQL metrics query can apply to a spanset (ADR-0221).</summary>
public enum TraceQlMetricsFunction
{
    Rate,
    CountOverTime,
    QuantileOverTime,
    AvgOverTime,
    MinOverTime,
    MaxOverTime,
    SumOverTime,
    HistogramOverTime,
}

/// <summary>The second stage <c>| topk(n)</c> or <c>| bottomk(n)</c> (ADR-0224): at each step, keep the <paramref name="K"/> series with the largest (or smallest) value.</summary>
public sealed record TraceQlRank(bool Top, int K);

/// <summary>A TraceQL metrics query: <c>{ spanset } | function(...) [by (...)]</c>.</summary>
/// <param name="Quantiles">The quantiles of <see cref="TraceQlMetricsFunction.QuantileOverTime"/>, in order; empty for every other function.</param>
/// <param name="By">The <c>by (...)</c> fields as written: <c>name</c>, <c>status</c>, <c>kind</c>, <c>span.x</c>, <c>resource.x</c> or <c>.x</c>.</param>
/// <param name="Rank">The <c>| topk(n)</c> / <c>| bottomk(n)</c> stage, if any.</param>
/// <param name="Exemplars">Whether the query asked for exemplars with <c>with(exemplars=true)</c>.</param>
/// <param name="ValueField">What <c>avg_over_time</c> and the other value functions aggregate, as written: <c>span.x</c>, <c>resource.x</c> or <c>.x</c> for a numeric attribute (ADR-0222); null is <c>duration</c> (and for <c>rate</c> and <c>count_over_time</c>, which take no argument).</param>
public sealed record TraceQlMetricsQuery(TraceQlSpanset Spanset, TraceQlMetricsFunction Function, IReadOnlyList<double> Quantiles, IReadOnlyList<string> By, string? ValueField = null, TraceQlRank? Rank = null, bool Exemplars = false)
{
    /// <summary>The function's TraceQL name, which is also the <c>__name__</c> of its series.</summary>
    public string FunctionName => Function switch
    {
        TraceQlMetricsFunction.Rate => "rate",
        TraceQlMetricsFunction.CountOverTime => "count_over_time",
        TraceQlMetricsFunction.QuantileOverTime => "quantile_over_time",
        TraceQlMetricsFunction.AvgOverTime => "avg_over_time",
        TraceQlMetricsFunction.MinOverTime => "min_over_time",
        TraceQlMetricsFunction.MaxOverTime => "max_over_time",
        TraceQlMetricsFunction.HistogramOverTime => "histogram_over_time",
        _ => "sum_over_time",
    };
}

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
/// <see cref="ParseMetrics"/> reads the one pipeline stage a metrics query adds (ADR-0221):
/// <code>
/// metrics := spanset "|" function ["by" "(" field ("," field)* ")"] ["|" ("topk" | "bottomk") "(" integer ")"] ["with" "(" "exemplars" "=" bool ")"]
/// function := "rate" "(" ")" | "count_over_time" "(" ")" | "quantile_over_time" "(" value ("," number)+ ")"
///           | ("avg_over_time" | "min_over_time" | "max_over_time" | "sum_over_time" | "histogram_over_time") "(" value ")"
/// value    := "duration" | "span." name | "resource." name | "." name
/// </code>
/// </summary>
public static class TraceQlParser
{
    public const int MaxLength = 4000;

    /// <summary>The most quantiles one <c>quantile_over_time</c> asks for.</summary>
    public const int MaxQuantiles = 10;

    /// <summary>The most <c>by (...)</c> fields.</summary>
    public const int MaxGroupFields = 5;

    private static readonly string[] SupportedFunctions =
        ["rate", "count_over_time", "quantile_over_time", "avg_over_time", "min_over_time", "max_over_time", "sum_over_time", "histogram_over_time"];

    /// <summary>
    /// Parses <c>{ spanset } | function(...) [by (...)]</c>. The spanset is one <c>{ ... }</c>; several, structural
    /// operators and a second pipeline stage are rejected, naming the construct.
    /// </summary>
    public static TraceQlMetricsQuery ParseMetrics(string? query)
    {
        CheckLength(query);
        var parser = new Parser(Tokenize(query!), allowPipe: true);
        var spansets = new List<TraceQlSpanset>();
        var expression = parser.ParseOr(spansets);
        if (spansets.Count != 1 || expression != "A")
        {
            throw new TraceQlException("a metrics query takes one spanset, e.g. { status = error } | rate(); combining spansets is not supported.");
        }

        return parser.ParseMetricsStage(spansets[0]);
    }

    public static TraceQlQuery Parse(string? query)
    {
        CheckLength(query);
        var parser = new Parser(Tokenize(query!));
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

    private static void CheckLength(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new TraceQlException("the query is empty.");
        }

        if (query.Length > MaxLength)
        {
            throw new TraceQlException($"the query is longer than {MaxLength} characters.");
        }
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
        Comma,
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
                case ',':
                    tokens.Add(new Token(TokenKind.Comma, ",", i++));
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

    private sealed class Parser(List<Token> tokens, bool allowPipe = false)
    {
        private int _pos;

        public bool AtEnd => Peek.Kind == TokenKind.End;

        /// <summary>The <c>| function(...) [by (...)] [with (...)]</c> after the spanset of a metrics query.</summary>
        public TraceQlMetricsQuery ParseMetricsStage(TraceQlSpanset spanset)
        {
            if (Peek.Kind != TokenKind.Pipe)
            {
                throw Peek.Kind == TokenKind.End
                    ? new TraceQlException("a metrics query needs a function after the spanset, e.g. { status = error } | rate().")
                    : Unexpected(Peek);
            }

            _pos++;
            var nameToken = Peek;
            if (nameToken.Kind != TokenKind.Word)
            {
                throw Unexpected(nameToken);
            }

            _pos++;
            var name = nameToken.Text;
            Expect(TokenKind.LParen, "(");
            var quantiles = new List<double>();
            string? valueField = null;
            TraceQlMetricsFunction function;
            switch (name)
            {
                case "rate" or "count_over_time":
                    function = name == "rate" ? TraceQlMetricsFunction.Rate : TraceQlMetricsFunction.CountOverTime;
                    break;
                case "quantile_over_time":
                    function = TraceQlMetricsFunction.QuantileOverTime;
                    valueField = ParseValueArgument(name);
                    while (Peek.Kind == TokenKind.Comma)
                    {
                        _pos++;
                        quantiles.Add(ParseQuantile());
                    }

                    if (quantiles.Count is 0 or > MaxQuantiles)
                    {
                        throw new TraceQlException($"quantile_over_time(duration, q, ...) takes 1 to {MaxQuantiles} quantiles between 0 and 1, e.g. quantile_over_time(duration, .5, .95).");
                    }

                    break;
                case "avg_over_time" or "min_over_time" or "max_over_time" or "sum_over_time" or "histogram_over_time":
                    function = name switch
                    {
                        "histogram_over_time" => TraceQlMetricsFunction.HistogramOverTime,
                        "avg_over_time" => TraceQlMetricsFunction.AvgOverTime,
                        "min_over_time" => TraceQlMetricsFunction.MinOverTime,
                        "max_over_time" => TraceQlMetricsFunction.MaxOverTime,
                        _ => TraceQlMetricsFunction.SumOverTime,
                    };
                    valueField = ParseValueArgument(name);
                    break;
                default:
                    throw new TraceQlException($"the metrics function '{name}' is not supported; use {string.Join(", ", SupportedFunctions.Select(f => f + "()"))}.");
            }

            Expect(TokenKind.RParen, ")");
            var by = new List<string>();
            if (Peek.Kind == TokenKind.Word && Peek.Text == "by")
            {
                _pos++;
                Expect(TokenKind.LParen, "(");
                by.Add(ParseGroupField());
                while (Peek.Kind == TokenKind.Comma)
                {
                    _pos++;
                    by.Add(ParseGroupField());
                }

                Expect(TokenKind.RParen, ")");
                if (by.Count > MaxGroupFields)
                {
                    throw new TraceQlException($"by (...) takes at most {MaxGroupFields} fields.");
                }
            }

            var exemplars = false;
            if (Peek.Kind == TokenKind.Word && Peek.Text == "with")
            {
                exemplars = ParseHints();
            }

            TraceQlRank? rank = null;
            if (Peek.Kind == TokenKind.Pipe)
            {
                rank = ParseRankStage();
                if (Peek.Kind == TokenKind.Word && Peek.Text == "with")
                {
                    exemplars = ParseHints();
                }
            }

            if (Peek.Kind == TokenKind.Pipe)
            {
                throw new TraceQlException("only one second pipeline stage, | topk(n) or | bottomk(n), is supported after the metrics function.");
            }

            if (!AtEnd)
            {
                throw Unexpected(Peek);
            }

            return new TraceQlMetricsQuery(spanset, function, quantiles, by, valueField, rank, exemplars);
        }

        /// <summary>
        /// The value a function aggregates: <c>duration</c> (null) or an attribute (<c>span.x</c>, <c>resource.x</c>,
        /// <c>.x</c>, returned as written). The other intrinsics are not numbers a metric can take, and say so.
        /// </summary>
        private string? ParseValueArgument(string function)
        {
            var token = Peek;
            if (token.Kind != TokenKind.Word)
            {
                throw new TraceQlException($"{function}(...) needs the field to aggregate: duration, or a numeric attribute such as span.size.");
            }

            _pos++;
            var field = token.Text;
            if (field == "duration")
            {
                return null;
            }

            var attribute = (field.StartsWith("span.", StringComparison.Ordinal) && field.Length > "span.".Length)
                || (field.StartsWith("resource.", StringComparison.Ordinal) && field.Length > "resource.".Length)
                || (field.StartsWith('.') && field.Length > 1);
            return attribute
                ? field
                : throw new TraceQlException($"{function}({field}) is not supported; aggregate duration or a numeric attribute (span.x, resource.x or .x).");
        }

        /// <summary><c>| topk(n)</c> or <c>| bottomk(n)</c>, the only second stage; anything else is rejected naming it.</summary>
        private TraceQlRank ParseRankStage()
        {
            _pos++;
            var token = Peek;
            if (token.Kind != TokenKind.Word || token.Text is not ("topk" or "bottomk"))
            {
                throw new TraceQlException(
                    $"the second pipeline stage '{(token.Kind == TokenKind.Word ? token.Text : token.Kind.ToString())}' is not supported; only | topk(n) and | bottomk(n) are.");
            }

            _pos++;
            Expect(TokenKind.LParen, "(");
            var count = Peek;
            if (count.Kind != TokenKind.Word || !int.TryParse(count.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var k) || k < 1)
            {
                throw new TraceQlException($"{token.Text}(n) takes a whole number of series to keep, 1 or more, e.g. {token.Text}(5).");
            }

            _pos++;
            Expect(TokenKind.RParen, ")");
            return new TraceQlRank(token.Text == "topk", k);
        }

        private double ParseQuantile()
        {
            var token = Peek;
            if (token.Kind != TokenKind.Word || !double.TryParse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var q) || q is < 0 or > 1 || !double.IsFinite(q))
            {
                throw token.Kind == TokenKind.End
                    ? new TraceQlException("the query ends unexpectedly.")
                    : new TraceQlException($"'{token.Text}' is not a quantile; use a number between 0 and 1, e.g. .95.");
            }

            _pos++;
            return q;
        }

        private string ParseGroupField()
        {
            var token = Peek;
            if (token.Kind != TokenKind.Word)
            {
                throw Unexpected(token);
            }

            _pos++;
            var field = token.Text;
            var ok = field is "name" or "status" or "kind"
                || (field.StartsWith("span.", StringComparison.Ordinal) && field.Length > "span.".Length)
                || (field.StartsWith("resource.", StringComparison.Ordinal) && field.Length > "resource.".Length)
                || (field.StartsWith('.') && field.Length > 1);
            return ok
                ? field
                : throw new TraceQlException($"'{field}' cannot be grouped by; use name, status, kind, span.x, resource.x or .x.");
        }

        /// <summary><c>with(exemplars = true)</c>: whether exemplars were asked for (ADR-0223). Any other hint is an error.</summary>
        private bool ParseHints()
        {
            _pos++;
            Expect(TokenKind.LParen, "(");
            var hint = Peek;
            if (hint.Kind != TokenKind.Word || hint.Text != "exemplars")
            {
                throw new TraceQlException($"the query hint '{(hint.Kind == TokenKind.Word ? hint.Text : hint.Kind.ToString())}' is not supported; only with(exemplars=...) is accepted.");
            }

            _pos++;
            if (!IsOperator("="))
            {
                throw Unexpected(Peek);
            }

            _pos++;
            if (Peek.Kind != TokenKind.Word || Peek.Text is not ("true" or "false"))
            {
                throw Unexpected(Peek);
            }

            var enabled = Peek.Text == "true";
            _pos++;
            Expect(TokenKind.RParen, ")");
            return enabled;
        }

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
            if (Peek.Kind == TokenKind.Pipe && !allowPipe)
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
