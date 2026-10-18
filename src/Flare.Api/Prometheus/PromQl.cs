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

internal enum PromAtKind
{
    /// <summary><c>@ 1700000000</c>: a fixed unix time in seconds.</summary>
    Timestamp,

    /// <summary><c>@ start()</c>: the start of the query range (the evaluation time of an instant query).</summary>
    Start,

    /// <summary><c>@ end()</c>: the end of the query range (the evaluation time of an instant query).</summary>
    End,
}

/// <summary>The <c>@</c> modifier: the expression is evaluated at this one time for every step instead of at the step itself.</summary>
internal sealed record PromAt(PromAtKind Kind, long TimestampMs = 0);

/// <summary>A vector selector. <see cref="Range"/> is set only for the <c>[5m]</c> form (a range selector), which is valid solely as a range function's argument. <see cref="Offset"/> is the <c>offset 1h</c> modifier (zero when absent) and <see cref="At"/> the <c>@</c> modifier.</summary>
internal sealed record PromSelector(string? MetricName, IReadOnlyList<PromMatcher> Matchers, TimeSpan? Range, TimeSpan Offset = default, PromAt? At = null) : PromExpr
{
    /// <summary>The metric name, from the bare name or a <c>__name__="..."</c> matcher.</summary>
    public string? EffectiveMetricName =>
        MetricName ?? Matchers.FirstOrDefault(m => m is { Name: "__name__", Op: PromMatchOp.Equal })?.Value;

    /// <summary>Matchers other than <c>__name__</c>.</summary>
    public IEnumerable<PromMatcher> LabelMatchers => Matchers.Where(m => m.Name != "__name__");
}

/// <summary><c>rate</c>/<c>increase</c>/<c>irate</c> (counters) or <c>avg|min|max|sum|count|last_over_time</c> (gauges) over a range selector.</summary>
internal sealed record PromRangeFunction(string Name, PromSelector Selector) : PromExpr;

/// <summary>
/// A subquery <c>expr[range:resolution]</c>: <see cref="Inner"/> evaluated every <see cref="Resolution"/> (the query
/// step when null) over the last <see cref="Range"/>. Valid only as the argument of an <c>*_over_time</c> function.
/// </summary>
internal sealed record PromSubquery(PromExpr Inner, TimeSpan Range, TimeSpan? Resolution, TimeSpan Offset, PromAt? At) : PromExpr;

/// <summary><c>avg|min|max|sum|count|last_over_time</c> over a <see cref="PromSubquery"/>.</summary>
internal sealed record PromSubqueryFunction(string Name, PromSubquery Subquery) : PromExpr;

/// <summary>
/// <c>sum|avg|min|max|count|group|stddev|stdvar [by|without (labels)] (inner)</c>, plus the parameterised
/// <c>topk|bottomk|quantile [by|without (labels)] (param, inner)</c>; <see cref="Parameter"/> is k or the quantile.
/// </summary>
internal sealed record PromAggregation(string Op, bool Without, IReadOnlyList<string> Labels, PromExpr Inner, double? Parameter = null) : PromExpr;

/// <summary>An instant-vector function applied sample by sample: <c>abs</c>, <c>ceil</c>, <c>floor</c>, <c>round</c>, <c>sqrt</c>, <c>exp</c>, <c>ln</c>, <c>log2</c>, <c>log10</c>, <c>sgn</c>, <c>clamp*</c>, and <c>vector(s)</c>.</summary>
internal sealed record PromFunction(string Name, PromExpr Argument, IReadOnlyList<double> Parameters) : PromExpr;

/// <summary><c>label_replace(v, dst, replacement, src, regex)</c> or <c>label_join(v, dst, separator, src...)</c>; <see cref="Arguments"/> are the string arguments after the vector.</summary>
internal sealed record PromLabelFunction(string Name, PromExpr Inner, IReadOnlyList<string> Arguments) : PromExpr;

/// <summary><c>absent(v)</c>: a single series of 1 at each step where <see cref="Inner"/> has no series. <see cref="Labels"/> are the selector's equality matchers.</summary>
internal sealed record PromAbsent(PromExpr Inner, IReadOnlyDictionary<string, string> Labels) : PromExpr;

internal enum PromCardinality
{
    OneToOne,

    /// <summary><c>group_left</c>: many left-hand series match one right-hand series.</summary>
    ManyToOne,

    /// <summary><c>group_right</c>: one left-hand series matches many right-hand series.</summary>
    OneToMany,
}

/// <summary>The <c>on</c>/<c>ignoring</c> and <c>group_left</c>/<c>group_right</c> clauses of a binary operator. The default matches on every label except <c>__name__</c>.</summary>
internal sealed record PromVectorMatching(bool On, IReadOnlyList<string> Labels, PromCardinality Cardinality, IReadOnlyList<string> Include)
{
    public static readonly PromVectorMatching Default = new(false, [], PromCardinality.OneToOne, []);
}

/// <summary>A binary operator where at least one side is a vector (scalar-scalar is folded at parse time).</summary>
internal sealed record PromBinary(string Op, bool ReturnBool, PromExpr Left, PromExpr Right, PromVectorMatching Matching) : PromExpr
{
    public static bool IsComparison(string op) => op is "==" or "!=" or ">" or "<" or ">=" or "<=";

    public static bool IsSetOperator(string op) => op is "and" or "or" or "unless";
}

internal sealed record PromHistogramQuantile(double Quantile, PromExpr Inner) : PromExpr;

/// <summary>Every vector selector in an expression, for endpoints that work on selectors rather than evaluated values (exemplars).</summary>
internal static class PromSelectors
{
    public static List<PromSelector> Collect(PromExpr expr)
    {
        var result = new List<PromSelector>();
        Walk(expr, result);
        return result;
    }

    private static void Walk(PromExpr expr, List<PromSelector> into)
    {
        switch (expr)
        {
            case PromSelector selector:
                into.Add(selector);
                break;
            case PromRangeFunction fn:
                into.Add(fn.Selector);
                break;
            case PromSubqueryFunction sf:
                Walk(sf.Subquery.Inner, into);
                break;
            case PromAggregation agg:
                Walk(agg.Inner, into);
                break;
            case PromFunction fn:
                Walk(fn.Argument, into);
                break;
            case PromLabelFunction lf:
                Walk(lf.Inner, into);
                break;
            case PromAbsent absent:
                Walk(absent.Inner, into);
                break;
            case PromHistogramQuantile hq:
                Walk(hq.Inner, into);
                break;
            case PromBinary binary:
                Walk(binary.Left, into);
                Walk(binary.Right, into);
                break;
        }
    }
}

/// <summary>
/// Hand-written recursive-descent parser for the PromQL subset Flare serves (ADR-0109, extended by
/// ADR-0239, ADR-0245): vector selectors with <c>= != =~ !~</c> matchers and <c>offset</c>/<c>@</c> modifiers, range functions
/// over a range selector, aggregations (<c>sum avg min max count group stddev stdvar</c>, and
/// <c>topk bottomk quantile</c> with a parameter) with <c>by</c>/<c>without</c>, <c>histogram_quantile</c>,
/// <c>*_over_time</c> over subqueries, instant-vector math functions, and binary operators (arithmetic, comparison with <c>bool</c>, and
/// <c>and or unless</c>) between scalars and vectors with <c>on</c>/<c>ignoring</c>/<c>group_left</c>/<c>group_right</c>.
/// Everything else - other functions - throws a <see cref="PromQlException"/>
/// naming the construct, rather than evaluating a partial answer. Pure and I/O-free.
/// </summary>
internal sealed class PromQlParser
{
    private static readonly string[] AggregationOps = ["sum", "avg", "min", "max", "count", "group", "stddev", "stdvar", "topk", "bottomk", "quantile"];
    private static readonly string[] ParameterisedAggregations = ["topk", "bottomk", "quantile"];
    private static readonly string[] RangeFunctions = ["rate", "increase", "irate", "avg_over_time", "last_over_time", "min_over_time", "max_over_time", "sum_over_time", "count_over_time"];

    /// <summary>The range functions that read a gauge's samples (and so also accept a subquery) rather than a counter's increases.</summary>
    internal static readonly string[] OverTimeFunctions = ["avg_over_time", "last_over_time", "min_over_time", "max_over_time", "sum_over_time", "count_over_time"];

    /// <summary>Instant-vector functions and how many number-literal parameters follow the vector (min, max).</summary>
    private static readonly Dictionary<string, (int Min, int Max)> InstantFunctions = new()
    {
        ["abs"] = (0, 0), ["ceil"] = (0, 0), ["floor"] = (0, 0), ["sqrt"] = (0, 0), ["exp"] = (0, 0),
        ["ln"] = (0, 0), ["log2"] = (0, 0), ["log10"] = (0, 0), ["sgn"] = (0, 0),
        ["round"] = (0, 1), ["clamp"] = (2, 2), ["clamp_min"] = (1, 1), ["clamp_max"] = (1, 1),
    };

    // Longest first so ">=" is not read as ">".
    private static readonly string[] SymbolOperators = ["==", "!=", ">=", "<=", "+", "-", "*", "/", "%", "^", ">", "<"];
    private static readonly string[] WordOperators = ["unless", "and", "or"];

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

        if (expr is PromSelector { Range: not null } or PromSubquery)
        {
            throw new PromQlException("a range selector or subquery is only valid inside rate(), increase() and the other range functions.");
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

    // PromQL precedence, lowest first: or / and unless / comparisons / + - / * / % / ^ (right-associative).
    private static int Precedence(string op) => op switch
    {
        "or" => 1,
        "and" or "unless" => 2,
        "==" or "!=" or ">" or "<" or ">=" or "<=" => 3,
        "+" or "-" => 4,
        "*" or "/" or "%" => 5,
        _ => 6,
    };

    private bool TryPeekBinaryOperator(out string op, out int length)
    {
        SkipWhitespace();
        var rest = _text.AsSpan(_pos);
        foreach (var symbol in SymbolOperators)
        {
            if (rest.StartsWith(symbol, StringComparison.Ordinal))
            {
                (op, length) = (symbol, symbol.Length);
                return true;
            }
        }

        foreach (var word in WordOperators)
        {
            if (rest.StartsWith(word, StringComparison.OrdinalIgnoreCase)
                && (rest.Length == word.Length || !IsIdentChar(rest[word.Length])))
            {
                (op, length) = (word, word.Length);
                return true;
            }
        }

        (op, length) = ("", 0);
        return false;
    }

    private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c is '_' or ':';

    // Precedence climbing. Scalar-scalar operands are folded here (Grafana's health check is `1+1`);
    // anything with a vector side becomes a PromBinary for the evaluator.
    private PromExpr ParseBinary(int minPrecedence)
    {
        var left = ParseUnary();
        while (TryPeekBinaryOperator(out var op, out var length) && Precedence(op) >= minPrecedence)
        {
            _pos += length;
            var returnBool = false;
            var matching = PromVectorMatching.Default;
            if (!PromBinary.IsSetOperator(op) && TryParseKeyword("bool"))
            {
                if (!PromBinary.IsComparison(op))
                {
                    throw new PromQlException($"the bool modifier is only valid on comparison operators, not '{op}'.");
                }

                returnBool = true;
            }

            matching = ParseMatching(op, matching);
            var precedence = Precedence(op);
            var right = ParseBinary(op == "^" ? precedence : precedence + 1);
            left = MakeBinary(op, returnBool, left, right, matching);
        }

        return left;
    }

    private bool TryParseKeyword(string keyword)
    {
        SkipWhitespace();
        var rest = _text.AsSpan(_pos);
        if (rest.StartsWith(keyword, StringComparison.OrdinalIgnoreCase)
            && (rest.Length == keyword.Length || !IsIdentChar(rest[keyword.Length])))
        {
            _pos += keyword.Length;
            return true;
        }

        return false;
    }

    private PromVectorMatching ParseMatching(string op, PromVectorMatching matching)
    {
        bool on;
        if (TryParseKeyword("on"))
        {
            on = true;
        }
        else if (TryParseKeyword("ignoring"))
        {
            on = false;
        }
        else
        {
            return matching;
        }

        var labels = ParseLabelList();
        var cardinality = PromCardinality.OneToOne;
        IReadOnlyList<string> include = [];
        if (TryParseKeyword("group_left"))
        {
            cardinality = PromCardinality.ManyToOne;
        }
        else if (TryParseKeyword("group_right"))
        {
            cardinality = PromCardinality.OneToMany;
        }

        if (cardinality != PromCardinality.OneToOne)
        {
            if (PromBinary.IsSetOperator(op))
            {
                throw new PromQlException($"no grouping allowed for '{op}' operation.");
            }

            SkipWhitespace();
            if (Peek() == '(')
            {
                include = ParseLabelList();
            }
        }

        return new PromVectorMatching(on, labels, cardinality, include);
    }

    private List<string> ParseLabelList()
    {
        var labels = new List<string>();
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

        return labels;
    }

    private static PromExpr MakeBinary(string op, bool returnBool, PromExpr left, PromExpr right, PromVectorMatching matching)
    {
        RequireInstant(left);
        RequireInstant(right);
        if (left is PromNumber l && right is PromNumber r)
        {
            if (PromBinary.IsSetOperator(op))
            {
                throw new PromQlException($"set operator '{op}' not allowed in binary scalar expression.");
            }

            if (PromBinary.IsComparison(op) && !returnBool)
            {
                throw new PromQlException("comparisons between scalars must use the bool modifier.");
            }

            return new PromNumber(PromBinaryOperations.Compute(op, l.Value, r.Value, returnBool).Value);
        }

        if (PromBinary.IsSetOperator(op) && (left is PromNumber || right is PromNumber))
        {
            throw new PromQlException($"set operator '{op}' not allowed in binary scalar expression.");
        }

        return new PromBinary(op, returnBool, left, right, matching);
    }

    /// <summary>Prometheus names the series <c>absent()</c> returns after the selector's equality matchers, unless a label is matched twice.</summary>
    private static Dictionary<string, string> AbsentLabels(PromExpr inner)
    {
        var labels = new Dictionary<string, string>();
        if (inner is not PromSelector selector)
        {
            return labels;
        }

        var seen = new HashSet<string>();
        var duplicated = new HashSet<string>();
        foreach (var m in selector.LabelMatchers)
        {
            if (!seen.Add(m.Name))
            {
                duplicated.Add(m.Name);
            }
        }

        foreach (var m in selector.LabelMatchers.Where(m => m.Op == PromMatchOp.Equal && !duplicated.Contains(m.Name)))
        {
            labels[m.Name] = m.Value;
        }

        return labels;
    }

    private static PromExpr RequireInstant(PromExpr expr) =>
        expr is PromSelector { Range: not null } or PromSubquery
            ? throw new PromQlException("a range selector is only valid inside rate(), increase() and the other range functions.")
            : expr;

    private PromExpr ParseUnary()
    {
        SkipWhitespace();
        var c = Peek();
        if (c is '-' or '+')
        {
            _pos++;
            // Binds tighter than * but looser than ^, so -2^2 is -4.
            var operand = ParseBinary(Precedence("^"));
            if (c == '+')
            {
                return operand;
            }

            return operand is PromNumber n
                ? new PromNumber(-n.Value)
                : new PromBinary("*", false, new PromNumber(-1), RequireInstant(operand), PromVectorMatching.Default);
        }

        return ParsePrimary();
    }

    private PromExpr ParsePrimary()
    {
        var expr = ParsePrimaryCore();
        SkipWhitespace();
        while (Peek() == '[')
        {
            expr = ParseSubquery(expr);
            SkipWhitespace();
        }

        return expr;
    }

    // expr[range:resolution] [offset d] [@ t]. A plain range selector (x[5m]) never reaches here: the selector parser consumes its own bracket.
    private PromSubquery ParseSubquery(PromExpr inner)
    {
        if (inner is PromSelector { Range: not null } or PromSubquery)
        {
            throw new PromQlException("a subquery is only allowed on an instant vector.");
        }

        if (inner is PromNumber)
        {
            throw new PromQlException("a subquery is only allowed on an instant vector; wrap the number in vector().");
        }

        _pos++;
        var end = _text.IndexOf(']', _pos);
        if (end < 0)
        {
            throw new PromQlException("unterminated subquery.");
        }

        var spec = _text[_pos..end];
        var colon = spec.IndexOf(':');
        if (colon < 0)
        {
            throw new PromQlException("a range selector is only valid on a metric selector; a subquery needs a resolution separator, e.g. [1h:1m].");
        }

        var range = ParseDuration(spec[..colon].Trim());
        var resolutionText = spec[(colon + 1)..].Trim();
        TimeSpan? resolution = resolutionText.Length == 0 ? null : ParseDuration(resolutionText);
        _pos = end + 1;

        var offset = TimeSpan.Zero;
        PromAt? at = null;
        ParseModifiers(ref offset, ref at);
        return new PromSubquery(inner, range, resolution, offset, at);
    }

    // `offset 1h` and `@ t` may appear in either order, each at most once.
    private void ParseModifiers(ref TimeSpan offset, ref PromAt? at)
    {
        var sawOffset = false;
        while (true)
        {
            if (!sawOffset && TryParseKeyword("offset"))
            {
                SkipWhitespace();
                if (Peek() == '-')
                {
                    throw new PromQlException("a negative offset is not supported.");
                }

                var start = _pos;
                while (!AtEnd && char.IsLetterOrDigit(_text[_pos]))
                {
                    _pos++;
                }

                offset = ParseDuration(_text[start.._pos]);
                sawOffset = true;
                continue;
            }

            SkipWhitespace();
            if (at is null && Peek() == '@')
            {
                _pos++;
                at = ParseAt();
                continue;
            }

            break;
        }
    }

    private PromAt ParseAt()
    {
        SkipWhitespace();
        if (IsIdentStart(Peek()))
        {
            var word = ParseIdentifier().ToLowerInvariant();
            if (word is not ("start" or "end"))
            {
                throw new PromQlException($"unexpected '{word}' after @; expected a unix timestamp, start() or end().");
            }

            Expect('(');
            Expect(')');
            return new PromAt(word == "start" ? PromAtKind.Start : PromAtKind.End);
        }

        var negative = Peek() == '-';
        if (negative || Peek() == '+')
        {
            _pos++;
        }

        if (!char.IsDigit(Peek()) && Peek() != '.')
        {
            throw new PromQlException($"expected a unix timestamp, start() or end() after @ at position {_pos}.");
        }

        var seconds = ParseNumber();
        return new PromAt(PromAtKind.Timestamp, (long)Math.Round((negative ? -seconds : seconds) * 1000));
    }

    private PromExpr ParsePrimaryCore()
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

        if (lower is "inf" or "nan" && NextNonSpace() != '(')
        {
            return new PromNumber(lower == "inf" ? double.PositiveInfinity : double.NaN);
        }

        if (AggregationOps.Contains(lower))
        {
            return ParseAggregation(lower);
        }

        if (RangeFunctions.Contains(lower))
        {
            Expect('(');
            var arg = ParseBinary(0);
            Expect(')');
            if (arg is PromSubquery subquery)
            {
                return OverTimeFunctions.Contains(lower)
                    ? new PromSubqueryFunction(lower, subquery)
                    : throw new PromQlException(
                        $"{lower}() over a subquery is not supported; apply rate() to the metric selector inside it, and use avg_over_time, min_over_time, max_over_time, sum_over_time, count_over_time or last_over_time over the subquery.");
            }

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
            var inner = RequireInstant(ParseBinary(0));
            Expect(')');
            return new PromHistogramQuantile(q.Value, inner);
        }

        if (lower == "absent")
        {
            Expect('(');
            var inner = RequireInstant(ParseBinary(0));
            Expect(')');
            return new PromAbsent(inner, AbsentLabels(inner));
        }

        if (lower is "label_replace" or "label_join")
        {
            Expect('(');
            var inner = RequireInstant(ParseBinary(0));
            var args = new List<string>();
            while (TryConsume(','))
            {
                SkipWhitespace();
                args.Add(ParseString());
            }

            Expect(')');
            var minArgs = lower == "label_replace" ? 4 : 2;
            if (lower == "label_replace" ? args.Count != minArgs : args.Count < minArgs)
            {
                throw new PromQlException(lower == "label_replace"
                    ? "label_replace() takes 5 arguments: label_replace(v, dst_label, replacement, src_label, regex)."
                    : "label_join() takes at least 3 arguments: label_join(v, dst_label, separator, src_label...).");
            }

            return new PromLabelFunction(lower, inner, args);
        }

        if (lower == "vector")
        {
            Expect('(');
            var scalar = ParseBinary(0) as PromNumber
                ?? throw new PromQlException("vector()'s argument must be a number literal.");
            Expect(')');
            return new PromFunction("vector", scalar, []);
        }

        if (InstantFunctions.TryGetValue(lower, out var arity))
        {
            Expect('(');
            var inner = RequireInstant(ParseBinary(0));
            if (inner is PromNumber)
            {
                throw new PromQlException($"{lower}() expects an instant vector, got a scalar; wrap the number in vector().");
            }

            var parameters = new List<double>();
            while (TryConsume(','))
            {
                parameters.Add(ParseBinary(0) is PromNumber p
                    ? p.Value
                    : throw new PromQlException($"{lower}()'s extra arguments must be number literals."));
            }

            Expect(')');
            if (parameters.Count < arity.Min || parameters.Count > arity.Max)
            {
                throw new PromQlException($"{lower}() takes {(arity.Min == arity.Max ? $"{arity.Min + 1}" : $"{arity.Min + 1} or {arity.Max + 1}")} argument(s), got {parameters.Count + 1}.");
            }

            return new PromFunction(lower, inner, parameters);
        }

        SkipWhitespace();
        if (Peek() == '(')
        {
            throw new PromQlException(
                $"function '{ident}' is not supported (supported: rate, increase, irate, avg_over_time, last_over_time, min_over_time, max_over_time, sum_over_time, count_over_time, histogram_quantile, "
                + "sum, avg, min, max, count, group, stddev, stdvar, topk, bottomk, quantile, vector, abs, ceil, floor, round, sqrt, exp, ln, log2, log10, sgn, clamp, clamp_min, clamp_max, absent, label_replace, label_join).");
        }

        return ParseSelectorTail(ident);
    }

    private char NextNonSpace()
    {
        SkipWhitespace();
        return Peek();
    }

    private PromExpr ParseAggregation(string op)
    {
        var without = false;
        var labels = new List<string>();
        // The clause may precede the parenthesised argument (`sum by (a) (x)`) or follow it (`sum(x) by (a)`).
        var grouped = TryParseGrouping(ref without, labels);
        Expect('(');
        double? parameter = null;
        if (ParameterisedAggregations.Contains(op))
        {
            parameter = ParseBinary(0) is PromNumber p
                ? p.Value
                : throw new PromQlException($"{op}'s first argument must be a number literal.");
            Expect(',');
        }

        var inner = RequireInstant(ParseBinary(0));
        Expect(')');
        if (!grouped)
        {
            TryParseGrouping(ref without, labels);
        }

        return new PromAggregation(op, without, labels, inner, parameter);
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
            // `x[5m:1m]` is a subquery over the selector: leave the bracket for ParsePrimary.
            if (inner.Contains(':'))
            {
                _pos--;
            }
            else
            {
                range = ParseDuration(inner);
                _pos = end + 1;
            }
        }

        var offset = TimeSpan.Zero;
        PromAt? at = null;
        if (Peek() != '[')
        {
            ParseModifiers(ref offset, ref at);
        }

        return new PromSelector(name, matchers, range, offset, at);
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
