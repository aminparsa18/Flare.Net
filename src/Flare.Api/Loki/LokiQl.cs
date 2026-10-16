using System.Globalization;
using System.Text;

namespace Flare.Api.Loki;

/// <summary>Raised for LogQL the subset parser rejects - syntax errors and valid-but-unsupported constructs alike. Surfaced as a Loki-style plain-text 400.</summary>
public sealed class LokiException(string message) : Exception(message);

internal enum LokiMatchOp
{
    Equal,
    NotEqual,
    Regex,
    NotRegex,
}

/// <summary>One <c>name op "value"</c> matcher of a stream selector.</summary>
internal sealed record LokiMatcher(string Name, LokiMatchOp Op, string Value);

/// <summary>Root of the LogQL subset's AST. See <see cref="LokiQlParser"/> for exactly what is accepted.</summary>
internal abstract record LokiExpr;

/// <summary>A stream selector plus its pipeline - the whole of a log query, or the argument of a range aggregation.</summary>
internal sealed record LokiLogSelector(IReadOnlyList<LokiMatcher> Matchers, IReadOnlyList<LokiStage> Stages) : LokiExpr;

/// <summary><c>count_over_time|rate|bytes_over_time|bytes_rate</c> over a log selector with a <c>[range]</c>.</summary>
internal sealed record LokiRangeAggregation(string Function, LokiLogSelector Selector, TimeSpan Range) : LokiExpr;

/// <summary><c>sum|avg|min|max|count|topk|bottomk [by|without (labels)] (inner)</c>. <see cref="K"/> is set for topk/bottomk.</summary>
internal sealed record LokiVectorAggregation(string Function, bool Without, IReadOnlyList<string> Labels, int? K, LokiExpr Inner) : LokiExpr;

internal sealed record LokiBinary(char Op, LokiExpr Left, LokiExpr Right) : LokiExpr;

internal sealed record LokiScalar(double Value) : LokiExpr;

/// <summary><c>vector(n)</c>: one series with no labels and the constant value <c>n</c>.</summary>
internal sealed record LokiVectorLiteral(double Value) : LokiExpr;

/// <summary>One stage of a log pipeline, after the stream selector.</summary>
internal abstract record LokiStage;

/// <summary><c>|= "a" or "b"</c>, <c>!= "a"</c>, <c>|~ "re"</c>, <c>!~ "re"</c>. The patterns are alternatives; a negated filter excludes all of them.</summary>
internal sealed record LokiLineFilter(LokiMatchOp Op, IReadOnlyList<string> Patterns) : LokiStage;

/// <summary><c>| json</c> or <c>| json label="path.to.field", ...</c>.</summary>
internal sealed record LokiJsonStage(IReadOnlyList<(string Label, string Path)> Expressions) : LokiStage;

/// <summary><c>| drop __error__</c>. Only the parser-error labels can be dropped, which never exist here, so it is a no-op kept for compatibility with Grafana's generated queries.</summary>
internal sealed record LokiDropStage : LokiStage;

/// <summary><c>| label op value</c> filters, combined with <c>and</c>/<c>,</c>/<c>or</c> and parentheses.</summary>
internal sealed record LokiLabelFilterStage(LokiPredicate Predicate) : LokiStage;

internal abstract record LokiPredicate;

internal enum LokiCompareOp
{
    Equal,
    NotEqual,
    Regex,
    NotRegex,
    Greater,
    GreaterOrEqual,
    Less,
    LessOrEqual,
}

/// <summary>A label comparison. <see cref="Number"/> is set when the value was written as a bare number (numeric comparison) rather than a quoted string.</summary>
internal sealed record LokiComparison(string Label, LokiCompareOp Op, string Text, double? Number) : LokiPredicate;

internal sealed record LokiAnd(LokiPredicate Left, LokiPredicate Right) : LokiPredicate;

internal sealed record LokiOr(LokiPredicate Left, LokiPredicate Right) : LokiPredicate;

/// <summary>
/// Hand-written recursive-descent parser for the LogQL subset Flare serves (ADR-0236): stream selectors
/// with <c>= != =~ !~</c> matchers; line filters (<c>|= != |~ !~</c>, with <c>or</c> alternatives);
/// <c>| json</c>; label filters (<c>| status &gt;= 500 and level="error"</c>); <c>| drop __error__</c>;
/// <c>count_over_time</c>, <c>rate</c>, <c>bytes_over_time</c>, <c>bytes_rate</c> over a range;
/// <c>sum|avg|min|max|count|topk|bottomk</c> with <c>by</c>/<c>without</c>; <c>+ - * / %</c> between
/// metric expressions and numbers; and <c>vector(n)</c> (Grafana's data-source test is
/// <c>vector(1)+vector(1)</c>). Everything else - <c>logfmt</c>, <c>line_format</c>, <c>unwrap</c>,
/// <c>offset</c>, comparison and set operators - throws a <see cref="LokiException"/> naming the
/// construct, rather than evaluating a partial answer. Pure and I/O-free.
/// </summary>
internal sealed class LokiQlParser
{
    private static readonly string[] RangeFunctions = ["count_over_time", "rate", "bytes_over_time", "bytes_rate"];
    private static readonly string[] VectorAggregations = ["sum", "avg", "min", "max", "count", "topk", "bottomk"];

    private static readonly string[] UnsupportedStages =
        ["logfmt", "line_format", "label_format", "unwrap", "regexp", "pattern", "unpack", "decolorize", "keep", "distinct"];

    private static readonly string[] UnwrappedFunctions =
        ["sum_over_time", "avg_over_time", "min_over_time", "max_over_time", "first_over_time", "last_over_time", "stddev_over_time", "stdvar_over_time", "quantile_over_time", "rate_counter", "absent_over_time"];

    private readonly string _text;
    private int _pos;

    private LokiQlParser(string text) => _text = text;

    public static LokiExpr Parse(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new LokiException("query must not be empty.");
        }

        var parser = new LokiQlParser(query);
        var expr = parser.ParseAdditive();
        parser.SkipWhitespace();
        return parser.AtEnd ? expr : throw parser.Error($"unexpected '{parser.Peek()}'");
    }

    /// <summary>Parses a stream selector with an optional pipeline, nothing else - the <c>query</c> parameter of the label-values endpoint and the <c>match[]</c> of <c>series</c>.</summary>
    public static LokiLogSelector ParseSelector(string query)
    {
        var expr = Parse(query);
        return expr as LokiLogSelector ?? throw new LokiException("expected a log stream selector such as {service_name=\"api\"}.");
    }

    /// <summary>A duration such as <c>5m</c>, <c>1h30m</c> or <c>500ms</c>.</summary>
    internal static TimeSpan ParseDuration(string text)
    {
        var parser = new LokiQlParser(text);
        var duration = parser.ReadDuration();
        return parser.AtEnd && duration > TimeSpan.Zero ? duration : throw new LokiException($"invalid duration \"{text}\".");
    }

    private bool AtEnd => _pos >= _text.Length;

    private char Peek() => AtEnd ? '\0' : _text[_pos];

    private LokiException Error(string message) => new($"parse error at position {_pos}: {message}.");

    private void SkipWhitespace()
    {
        while (!AtEnd && char.IsWhiteSpace(_text[_pos]))
        {
            _pos++;
        }
    }

    private bool TryConsume(string token)
    {
        SkipWhitespace();
        if (string.CompareOrdinal(_text, _pos, token, 0, token.Length) == 0)
        {
            _pos += token.Length;
            return true;
        }

        return false;
    }

    private void Expect(string token)
    {
        if (!TryConsume(token))
        {
            throw Error($"expected '{token}'");
        }
    }

    private static bool IsIdentStart(char c) => char.IsAsciiLetter(c) || c == '_';

    private static bool IsIdentPart(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';

    /// <summary>The identifier at the cursor, without consuming it.</summary>
    private string? PeekIdent()
    {
        SkipWhitespace();
        if (!IsIdentStart(Peek()))
        {
            return null;
        }

        var end = _pos;
        while (end < _text.Length && IsIdentPart(_text[end]))
        {
            end++;
        }

        return _text[_pos..end];
    }

    private string ReadIdent()
    {
        var ident = PeekIdent() ?? throw Error("expected an identifier");
        _pos += ident.Length;
        return ident;
    }

    private bool TryConsumeKeyword(string keyword)
    {
        if (PeekIdent() == keyword)
        {
            _pos += keyword.Length;
            return true;
        }

        return false;
    }

    // ---- metric expressions -------------------------------------------------------------

    private LokiExpr ParseAdditive()
    {
        var left = ParseMultiplicative();
        while (true)
        {
            SkipWhitespace();
            var op = Peek();
            if (op is not ('+' or '-'))
            {
                return left;
            }

            _pos++;
            left = new LokiBinary(op, left, ParseMultiplicative());
        }
    }

    private LokiExpr ParseMultiplicative()
    {
        var left = ParsePrimary();
        while (true)
        {
            SkipWhitespace();
            var op = Peek();
            if (op is not ('*' or '/' or '%'))
            {
                return left;
            }

            _pos++;
            left = new LokiBinary(op, left, ParsePrimary());
        }
    }

    private LokiExpr ParsePrimary()
    {
        SkipWhitespace();
        var c = Peek();

        if (c == '(')
        {
            _pos++;
            var inner = ParseAdditive();
            Expect(")");
            return inner;
        }

        if (c == '{')
        {
            return ParseLogSelector();
        }

        if (c == '-' || char.IsAsciiDigit(c))
        {
            var negative = c == '-';
            if (negative)
            {
                _pos++;
            }

            var value = ReadNumber();
            return new LokiScalar(negative ? -value : value);
        }

        var name = PeekIdent() ?? throw Error($"unexpected '{(AtEnd ? "end of query" : c.ToString())}'");
        _pos += name.Length;

        if (name == "vector")
        {
            Expect("(");
            SkipWhitespace();
            var value = ReadNumber();
            Expect(")");
            return new LokiVectorLiteral(value);
        }

        if (Array.IndexOf(RangeFunctions, name) >= 0)
        {
            return ParseRangeAggregation(name);
        }

        if (Array.IndexOf(VectorAggregations, name) >= 0)
        {
            return ParseVectorAggregation(name);
        }

        if (Array.IndexOf(UnwrappedFunctions, name) >= 0)
        {
            throw Error($"'{name}' is not supported (it needs unwrap); use count_over_time, rate, bytes_over_time or bytes_rate");
        }

        throw Error($"unknown function '{name}'");
    }

    private LokiRangeAggregation ParseRangeAggregation(string function)
    {
        Expect("(");
        SkipWhitespace();
        if (Peek() != '{')
        {
            throw Error($"{function}() takes a log stream selector");
        }

        var selector = ParseLogSelector();
        Expect("[");
        SkipWhitespace();
        var range = ReadDuration();
        Expect("]");
        if (PeekIdent() is "offset" or "by" or "without")
        {
            throw Error($"'{PeekIdent()}' is not supported here");
        }

        Expect(")");
        return new LokiRangeAggregation(function, selector, range);
    }

    private LokiVectorAggregation ParseVectorAggregation(string function)
    {
        var (without, labels, grouped) = ParseGrouping();
        Expect("(");
        int? k = null;
        if (function is "topk" or "bottomk")
        {
            SkipWhitespace();
            var kValue = ReadNumber();
            k = kValue >= 1 && kValue == Math.Floor(kValue) && kValue <= 10_000
                ? (int)kValue
                : throw Error($"{function}() needs a whole number of at least 1");
            Expect(",");
        }

        var inner = ParseAdditive();
        Expect(")");

        if (!grouped)
        {
            (without, labels, grouped) = ParseGrouping();
        }

        return new LokiVectorAggregation(function, without, labels, k, inner);
    }

    private (bool Without, IReadOnlyList<string> Labels, bool Present) ParseGrouping()
    {
        var keyword = PeekIdent();
        if (keyword is not ("by" or "without"))
        {
            return (false, [], false);
        }

        _pos += keyword.Length;
        Expect("(");
        var labels = new List<string>();
        SkipWhitespace();
        if (Peek() != ')')
        {
            do
            {
                labels.Add(ReadIdent());
            }
            while (TryConsume(","));
        }

        Expect(")");
        return (keyword == "without", labels, true);
    }

    // ---- log selectors and pipelines ----------------------------------------------------

    private LokiLogSelector ParseLogSelector()
    {
        Expect("{");
        var matchers = new List<LokiMatcher>();
        while (true)
        {
            SkipWhitespace();
            if (Peek() == '}')
            {
                break;
            }

            var name = ReadIdent();
            var op = ReadMatchOp();
            matchers.Add(new LokiMatcher(name, op, ReadString()));
            if (!TryConsume(","))
            {
                break;
            }
        }

        Expect("}");
        if (matchers.Count == 0)
        {
            throw Error("a stream selector needs at least one matcher");
        }

        var stages = new List<LokiStage>();
        while (TryParseStage(stages))
        {
        }

        return new LokiLogSelector(matchers, stages);
    }

    private LokiMatchOp ReadMatchOp()
    {
        SkipWhitespace();
        foreach (var (token, op) in new[]
        {
            ("=~", LokiMatchOp.Regex), ("!~", LokiMatchOp.NotRegex), ("!=", LokiMatchOp.NotEqual), ("=", LokiMatchOp.Equal),
        })
        {
            if (string.CompareOrdinal(_text, _pos, token, 0, token.Length) == 0)
            {
                _pos += token.Length;
                return op;
            }
        }

        throw Error("expected one of =, !=, =~, !~");
    }

    private bool TryParseStage(List<LokiStage> stages)
    {
        SkipWhitespace();

        foreach (var (token, op) in new[]
        {
            ("|=", LokiMatchOp.Equal), ("|~", LokiMatchOp.Regex), ("!=", LokiMatchOp.NotEqual), ("!~", LokiMatchOp.NotRegex),
        })
        {
            if (string.CompareOrdinal(_text, _pos, token, 0, token.Length) == 0)
            {
                _pos += token.Length;
                var patterns = new List<string> { ReadString() };
                while (TryConsumeKeyword("or"))
                {
                    patterns.Add(ReadString());
                }

                stages.Add(new LokiLineFilter(op, patterns));
                return true;
            }
        }

        if (string.CompareOrdinal(_text, _pos, "|>", 0, 2) == 0 || string.CompareOrdinal(_text, _pos, "!>", 0, 2) == 0)
        {
            throw Error("pattern line filters (|> and !>) are not supported");
        }

        if (Peek() != '|')
        {
            return false;
        }

        _pos++;
        SkipWhitespace();
        var word = PeekIdent();

        if (word == "json")
        {
            _pos += word.Length;
            stages.Add(ParseJsonStage());
            return true;
        }

        if (word == "drop")
        {
            _pos += word.Length;
            do
            {
                var label = ReadIdent();
                if (label is not ("__error__" or "__error_details__"))
                {
                    throw Error($"drop is only supported for __error__ and __error_details__, not '{label}'");
                }
            }
            while (TryConsume(","));

            stages.Add(new LokiDropStage());
            return true;
        }

        if (word is not null && Array.IndexOf(UnsupportedStages, word) >= 0)
        {
            throw Error($"the '{word}' stage is not supported");
        }

        stages.Add(new LokiLabelFilterStage(ParseOr()));
        return true;
    }

    private LokiJsonStage ParseJsonStage()
    {
        var expressions = new List<(string, string)>();
        SkipWhitespace();
        if (!IsIdentStart(Peek()))
        {
            return new LokiJsonStage(expressions);
        }

        do
        {
            var label = ReadIdent();
            Expect("=");
            expressions.Add((label, ReadString()));
        }
        while (TryConsume(","));

        return new LokiJsonStage(expressions);
    }

    private LokiPredicate ParseOr()
    {
        var left = ParseAnd();
        while (TryConsumeKeyword("or"))
        {
            left = new LokiOr(left, ParseAnd());
        }

        return left;
    }

    private LokiPredicate ParseAnd()
    {
        var left = ParsePredicateAtom();
        while (true)
        {
            if (TryConsumeKeyword("and") || TryConsume(","))
            {
                left = new LokiAnd(left, ParsePredicateAtom());
                continue;
            }

            return left;
        }
    }

    private LokiPredicate ParsePredicateAtom()
    {
        SkipWhitespace();
        if (TryConsume("("))
        {
            var inner = ParseOr();
            Expect(")");
            return inner;
        }

        var label = ReadIdent();
        SkipWhitespace();
        var op = ReadCompareOp();
        SkipWhitespace();

        if (Peek() is '"' or '`')
        {
            var text = ReadString();
            return op is LokiCompareOp.Greater or LokiCompareOp.GreaterOrEqual or LokiCompareOp.Less or LokiCompareOp.LessOrEqual
                ? throw Error("numeric comparison needs a number, not a string")
                : new LokiComparison(label, op, text, null);
        }

        if (op is LokiCompareOp.Regex or LokiCompareOp.NotRegex)
        {
            throw Error("a regular-expression label filter needs a quoted string");
        }

        var number = ReadNumber();
        return new LokiComparison(label, op, number.ToString("R", CultureInfo.InvariantCulture), number);
    }

    private LokiCompareOp ReadCompareOp()
    {
        foreach (var (token, op) in new[]
        {
            ("=~", LokiCompareOp.Regex), ("!~", LokiCompareOp.NotRegex), ("!=", LokiCompareOp.NotEqual), ("==", LokiCompareOp.Equal),
            (">=", LokiCompareOp.GreaterOrEqual), ("<=", LokiCompareOp.LessOrEqual), (">", LokiCompareOp.Greater),
            ("<", LokiCompareOp.Less), ("=", LokiCompareOp.Equal),
        })
        {
            if (string.CompareOrdinal(_text, _pos, token, 0, token.Length) == 0)
            {
                _pos += token.Length;
                return op;
            }
        }

        throw Error("expected a label comparison operator (=, !=, =~, !~, >, >=, <, <=)");
    }

    // ---- literals -----------------------------------------------------------------------

    private string ReadString()
    {
        SkipWhitespace();
        var quote = Peek();
        if (quote is not ('"' or '`'))
        {
            throw Error("expected a quoted string");
        }

        _pos++;
        var sb = new StringBuilder();
        while (true)
        {
            if (AtEnd)
            {
                throw Error("unterminated string");
            }

            var c = _text[_pos++];
            if (c == quote)
            {
                return sb.ToString();
            }

            if (c != '\\' || quote == '`')
            {
                sb.Append(c);
                continue;
            }

            if (AtEnd)
            {
                throw Error("unterminated string");
            }

            var escaped = _text[_pos++];
            sb.Append(escaped switch
            {
                'n' => "\n",
                't' => "\t",
                'r' => "\r",
                '"' => "\"",
                '\\' => "\\",
                // Go would reject an unknown escape; a regex such as "\d+" is more useful read as written.
                _ => "\\" + escaped,
            });
        }
    }

    private double ReadNumber()
    {
        SkipWhitespace();
        var start = _pos;
        while (!AtEnd && (char.IsAsciiDigit(_text[_pos]) || _text[_pos] == '.'))
        {
            _pos++;
        }

        if (!AtEnd && _text[_pos] is 'e' or 'E' && _pos + 1 < _text.Length && (char.IsAsciiDigit(_text[_pos + 1]) || _text[_pos + 1] is '+' or '-'))
        {
            _pos += 2;
            while (!AtEnd && char.IsAsciiDigit(_text[_pos]))
            {
                _pos++;
            }
        }

        if (!AtEnd && char.IsAsciiLetter(_text[_pos]))
        {
            throw Error("duration and byte-size literals are not supported here");
        }

        return double.TryParse(_text.AsSpan(start, _pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw Error("expected a number");
    }

    private TimeSpan ReadDuration()
    {
        SkipWhitespace();
        var total = TimeSpan.Zero;
        var any = false;
        while (!AtEnd && char.IsAsciiDigit(_text[_pos]))
        {
            var start = _pos;
            while (!AtEnd && (char.IsAsciiDigit(_text[_pos]) || _text[_pos] == '.'))
            {
                _pos++;
            }

            var amount = double.Parse(_text.AsSpan(start, _pos - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            var unitStart = _pos;
            while (!AtEnd && char.IsAsciiLetter(_text[_pos]))
            {
                _pos++;
            }

            total += _text[unitStart.._pos] switch
            {
                "ms" => TimeSpan.FromMilliseconds(amount),
                "s" => TimeSpan.FromSeconds(amount),
                "m" => TimeSpan.FromMinutes(amount),
                "h" => TimeSpan.FromHours(amount),
                "d" => TimeSpan.FromDays(amount),
                "w" => TimeSpan.FromDays(7 * amount),
                "y" => TimeSpan.FromDays(365 * amount),
                _ => throw Error("expected a duration unit (ms, s, m, h, d, w, y)"),
            };
            any = true;
        }

        return any ? total : throw Error("expected a duration such as 5m");
    }
}
