using System.Globalization;
using System.Text;

namespace Flare.Api.Prometheus;

/// <summary>Raised for PromQL the subset parser rejects - syntax errors and valid-but-unsupported constructs alike. Surfaced as a Prometheus <c>bad_data</c> error.</summary>
public sealed class PromQlException(string message) : Exception(message);

internal enum PromMatchOp
{
    Equal,
    NotEqual,
    Regex,
    NotRegex,
}

internal sealed record PromMatcher(string Name, PromMatchOp Op, string Value);

/// <summary>Root of the PromQL subset's AST. See <see cref="PromQlParser"/> for exactly what is accepted.</summary>
internal abstract record PromExpr;

internal sealed record PromNumber(double Value) : PromExpr;

/// <summary>A vector selector. <see cref="Range"/> is set only for the <c>[5m]</c> form (a range selector), which is valid solely as a range function's argument.</summary>
internal sealed record PromSelector(string? MetricName, IReadOnlyList<PromMatcher> Matchers, TimeSpan? Range) : PromExpr
{
    /// <summary>The metric name, from the bare name or a <c>__name__="..."</c> matcher.</summary>
    public string? EffectiveMetricName =>
        MetricName ?? Matchers.FirstOrDefault(m => m is { Name: "__name__", Op: PromMatchOp.Equal })?.Value;

    /// <summary>Matchers other than <c>__name__</c>.</summary>
    public IEnumerable<PromMatcher> LabelMatchers => Matchers.Where(m => m.Name != "__name__");
}

/// <summary><c>rate</c>/<c>increase</c> over a range selector.</summary>
internal sealed record PromRangeFunction(string Name, PromSelector Selector) : PromExpr;

/// <summary><c>sum|avg|min|max|count [by|without (labels)] (inner)</c>.</summary>
internal sealed record PromAggregation(string Op, bool Without, IReadOnlyList<string> Labels, PromExpr Inner) : PromExpr;

internal sealed record PromHistogramQuantile(double Quantile, PromExpr Inner) : PromExpr;

/// <summary>
/// Hand-written recursive-descent parser for the PromQL subset Flare serves (ADR-0109): vector
/// selectors with <c>= != =~ !~</c> matchers, <c>rate</c>/<c>increase</c> over a range selector,
/// <c>sum|avg|min|max|count</c> with <c>by</c>/<c>without</c>, <c>histogram_quantile</c>, and
/// arithmetic between scalar literals (Grafana's data-source test is <c>1+1</c>). Everything else -
/// vector/vector operators, <c>offset</c>, <c>@</c>, subqueries, other functions - throws a
/// <see cref="PromQlException"/> naming the construct, rather than evaluating a partial answer.
/// Pure and I/O-free.
/// </summary>
internal sealed class PromQlParser
{
    private static readonly string[] AggregationOps = ["sum", "avg", "min", "max", "count"];
    private static readonly string[] RangeFunctions = ["rate", "increase"];

    private readonly string _text;
    private int _pos;

    private PromQlParser(string text) => _text = text;

    public static PromExpr Parse(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new PromQlException("query must not be empty.");
        }

        var parser = new PromQlParser(query);
        var expr = parser.ParseBinary(0);
        parser.SkipWhitespace();
        if (!parser.AtEnd)
        {
            throw new PromQlException($"unexpected '{parser.Peek()}' at position {parser._pos}.");
        }

        if (expr is PromSelector { Range: not null })
        {
            throw new PromQlException("a range selector is only valid inside rate() or increase().");
        }

        return expr;
    }

    private bool AtEnd => _pos >= _text.Length;

    private char Peek() => AtEnd ? '\0' : _text[_pos];

    private void SkipWhitespace()
    {
        while (!AtEnd && char.IsWhiteSpace(_text[_pos]))
        {
            _pos++;
        }
    }

    private bool TryConsume(char c)
    {
        SkipWhitespace();
        if (Peek() == c)
        {
            _pos++;
            return true;
        }

        return false;
    }

    private void Expect(char c)
    {
        if (!TryConsume(c))
        {
            throw new PromQlException($"expected '{c}' at position {_pos}.");
        }
    }

    private static int Precedence(char op) => op is '*' or '/' or '%' ? 2 : 1;

    // Precedence climbing, but operands must be number literals: Flare evaluates scalar
    // arithmetic so Grafana's `1+1` health check works, and rejects series operands.
    private PromExpr ParseBinary(int minPrecedence)
    {
        var left = ParsePrimary();
        while (true)
        {
            SkipWhitespace();
            var op = Peek();
            if (op is not ('+' or '-' or '*' or '/' or '%') || Precedence(op) < minPrecedence)
            {
                return left;
            }

            _pos++;
            var right = ParseBinary(Precedence(op) + 1);
            if (left is not PromNumber l || right is not PromNumber r)
            {
                throw new PromQlException(
                    $"binary operator '{op}' between series is not supported; only arithmetic between number literals is.");
            }

            left = new PromNumber(op switch
            {
                '+' => l.Value + r.Value,
                '-' => l.Value - r.Value,
                '*' => l.Value * r.Value,
                '/' => l.Value / r.Value,
                _ => l.Value % r.Value,
            });
        }
    }

    private PromExpr ParsePrimary()
    {
        SkipWhitespace();
        if (TryConsume('('))
        {
            var inner = ParseBinary(0);
            Expect(')');
            return inner;
        }

        var c = Peek();
        if (char.IsDigit(c) || c == '.')
        {
            return new PromNumber(ParseNumber());
        }

        if (c == '-')
        {
            _pos++;
            return ParsePrimary() is PromNumber n
                ? new PromNumber(-n.Value)
                : throw new PromQlException("unary minus is only supported on number literals.");
        }

        if (c == '{')
        {
            return ParseSelectorTail(null);
        }

        if (!IsIdentStart(c))
        {
            throw new PromQlException(AtEnd ? "unexpected end of query." : $"unexpected '{c}' at position {_pos}.");
        }

        var ident = ParseIdentifier();
        var lower = ident.ToLowerInvariant();

        if (AggregationOps.Contains(lower))
        {
            return ParseAggregation(lower);
        }

        if (RangeFunctions.Contains(lower))
        {
            Expect('(');
            var arg = ParseBinary(0);
            Expect(')');
            return arg is PromSelector { Range: not null } selector
                ? new PromRangeFunction(lower, selector)
                : throw new PromQlException($"{lower}() requires a range selector, e.g. {lower}(metric[5m]).");
        }

        if (lower == "histogram_quantile")
        {
            Expect('(');
            var q = ParseBinary(0) as PromNumber
                ?? throw new PromQlException("histogram_quantile's first argument must be a number literal.");
            Expect(',');
            var inner = ParseBinary(0);
            Expect(')');
            return new PromHistogramQuantile(q.Value, inner);
        }

        SkipWhitespace();
        if (Peek() == '(')
        {
            throw new PromQlException(
                $"function '{ident}' is not supported (supported: rate, increase, histogram_quantile, sum, avg, min, max, count).");
        }

        return ParseSelectorTail(ident);
    }

    private PromExpr ParseAggregation(string op)
    {
        var without = false;
        var labels = new List<string>();
        // The clause may precede the parenthesised argument (`sum by (a) (x)`) or follow it (`sum(x) by (a)`).
        var grouped = TryParseGrouping(ref without, labels);
        Expect('(');
        var inner = ParseBinary(0);
        Expect(')');
        if (!grouped)
        {
            TryParseGrouping(ref without, labels);
        }

        return new PromAggregation(op, without, labels, inner);
    }

    private bool TryParseGrouping(ref bool without, List<string> labels)
    {
        SkipWhitespace();
        var save = _pos;
        if (!IsIdentStart(Peek()))
        {
            return false;
        }

        var word = ParseIdentifier().ToLowerInvariant();
        if (word is not ("by" or "without"))
        {
            _pos = save;
            return false;
        }

        without = word == "without";
        Expect('(');
        if (!TryConsume(')'))
        {
            do
            {
                SkipWhitespace();
                labels.Add(ParseIdentifier());
            }
            while (TryConsume(','));
            Expect(')');
        }

        return true;
    }

    private PromSelector ParseSelectorTail(string? name)
    {
        var matchers = new List<PromMatcher>();
        if (TryConsume('{'))
        {
            if (!TryConsume('}'))
            {
                do
                {
                    SkipWhitespace();
                    if (Peek() == '}')
                    {
                        break;
                    }

                    var label = ParseIdentifier();
                    SkipWhitespace();
                    var op = ParseMatchOp();
                    SkipWhitespace();
                    matchers.Add(new PromMatcher(label, op, ParseString()));
                }
                while (TryConsume(','));
                Expect('}');
            }
        }

        if (name is null && matchers.Count == 0)
        {
            throw new PromQlException("vector selector must contain at least one matcher or a metric name.");
        }

        if (name is null && !matchers.Any(m => m is { Name: "__name__", Op: PromMatchOp.Equal }))
        {
            throw new PromQlException("vector selector must name a metric (a bare name or __name__=\"...\").");
        }

        TimeSpan? range = null;
        SkipWhitespace();
        if (Peek() == '[')
        {
            _pos++;
            var end = _text.IndexOf(']', _pos);
            if (end < 0)
            {
                throw new PromQlException("unterminated range selector.");
            }

            var inner = _text[_pos..end].Trim();
            if (inner.Contains(':'))
            {
                throw new PromQlException("subqueries are not supported.");
            }

            range = ParseDuration(inner);
            _pos = end + 1;
        }

        SkipWhitespace();
        if (_pos + 6 <= _text.Length && _text.AsSpan(_pos).StartsWith("offset", StringComparison.OrdinalIgnoreCase))
        {
            throw new PromQlException("the offset modifier is not supported.");
        }

        if (Peek() == '@')
        {
            throw new PromQlException("the @ modifier is not supported.");
        }

        return new PromSelector(name, matchers, range);
    }

    private PromMatchOp ParseMatchOp()
    {
        var rest = _text.AsSpan(_pos);
        if (rest.StartsWith("=~", StringComparison.Ordinal)) { _pos += 2; return PromMatchOp.Regex; }
        if (rest.StartsWith("!~", StringComparison.Ordinal)) { _pos += 2; return PromMatchOp.NotRegex; }
        if (rest.StartsWith("!=", StringComparison.Ordinal)) { _pos += 2; return PromMatchOp.NotEqual; }
        if (rest.StartsWith("=", StringComparison.Ordinal)) { _pos += 1; return PromMatchOp.Equal; }
        throw new PromQlException($"expected a label matcher operator at position {_pos}.");
    }

    private static bool IsIdentStart(char c) => char.IsLetter(c) || c is '_' or ':';

    private string ParseIdentifier()
    {
        SkipWhitespace();
        var start = _pos;
        if (!IsIdentStart(Peek()))
        {
            throw new PromQlException(AtEnd ? "unexpected end of query." : $"expected an identifier at position {_pos}.");
        }

        while (!AtEnd && (char.IsLetterOrDigit(_text[_pos]) || _text[_pos] is '_' or ':'))
        {
            _pos++;
        }

        return _text[start.._pos];
    }

    private double ParseNumber()
    {
        var start = _pos;
        while (!AtEnd && (char.IsDigit(_text[_pos]) || _text[_pos] is '.' or 'e' or 'E'
            || (_text[_pos] is '+' or '-' && _pos > start && _text[_pos - 1] is 'e' or 'E')))
        {
            _pos++;
        }

        var token = _text[start.._pos];
        return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new PromQlException($"invalid number '{token}'.");
    }

    private string ParseString()
    {
        var quote = Peek();
        if (quote is not ('"' or '\'' or '`'))
        {
            throw new PromQlException($"expected a quoted string at position {_pos}.");
        }

        _pos++;
        var sb = new StringBuilder();
        while (!AtEnd && _text[_pos] != quote)
        {
            if (_text[_pos] == '\\' && quote != '`' && _pos + 1 < _text.Length)
            {
                _pos++;
                sb.Append(_text[_pos] switch { 'n' => '\n', 't' => '\t', var other => other });
            }
            else
            {
                sb.Append(_text[_pos]);
            }

            _pos++;
        }

        if (AtEnd)
        {
            throw new PromQlException("unterminated string.");
        }

        _pos++;
        return sb.ToString();
    }

    /// <summary>Parses a Prometheus duration such as <c>30s</c>, <c>5m</c>, <c>1h30m</c> (units ms, s, m, h, d, w, y).</summary>
    public static TimeSpan ParseDuration(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new PromQlException("duration must not be empty.");
        }

        var total = TimeSpan.Zero;
        var i = 0;
        while (i < text.Length)
        {
            var start = i;
            while (i < text.Length && char.IsDigit(text[i]))
            {
                i++;
            }

            if (i == start || !long.TryParse(text.AsSpan(start, i - start), out var amount))
            {
                throw new PromQlException($"invalid duration '{text}'.");
            }

            var unitStart = i;
            while (i < text.Length && char.IsLetter(text[i]))
            {
                i++;
            }

            total += text[unitStart..i] switch
            {
                "ms" => TimeSpan.FromMilliseconds(amount),
                "s" => TimeSpan.FromSeconds(amount),
                "m" => TimeSpan.FromMinutes(amount),
                "h" => TimeSpan.FromHours(amount),
                "d" => TimeSpan.FromDays(amount),
                "w" => TimeSpan.FromDays(7 * amount),
                "y" => TimeSpan.FromDays(365 * amount),
                _ => throw new PromQlException($"invalid duration '{text}'."),
            };
        }

        return total > TimeSpan.Zero ? total : throw new PromQlException("duration must be positive.");
    }
}
