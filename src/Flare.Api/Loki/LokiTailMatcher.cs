using System.Globalization;
using System.Text.RegularExpressions;
using Flare.Api.LiveTail;
using Flare.Api.Model;

namespace Flare.Api.Loki;

/// <summary>
/// Evaluates a LogQL log selector and its pipeline against one log in memory, for <c>GET /loki/api/v1/tail</c>
/// (ADR-0237). Live tail reads the ingest stream, not ClickHouse, so the SQL a query would run cannot be
/// used; instead every stage is a step over the same labels a response would carry
/// (<see cref="LokiEntryLabels"/>, <see cref="LokiLogfmt"/>, <see cref="LokiPatternMatcher"/>), so a line
/// that a query returns is a line tail delivers, with the same labels and the same <c>line_format</c> output.
/// Regular expressions run in .NET's linear-time engine (no lookarounds or backreferences), the limit
/// live tail's other filters already have; a query using anything else is refused at connect time with
/// an error naming the pattern, never approximated. Pure.
/// </summary>
internal sealed class LokiTailMatcher
{
    private readonly List<Func<State, bool>> _steps = [];
    private readonly LokiTemplate? _lineFormat;
    private readonly List<LokiParserSpec> _plan = [];

    private LokiTailMatcher(LiveTailOptions options, LokiLogSelector selector)
    {
        var regexCount = 0;

        Regex CompileRegex(string pattern)
        {
            if (++regexCount > options.MaxRegexPatterns)
            {
                throw new LokiException($"live tail supports at most {options.MaxRegexPatterns} regular expressions per query.");
            }

            if (pattern.Length > options.MaxRegexPatternLength)
            {
                throw new LokiException($"a regular expression in a live tail query may be at most {options.MaxRegexPatternLength} characters.");
            }

            try
            {
                return new Regex(pattern, RegexOptions.NonBacktracking | RegexOptions.CultureInvariant, options.RegexMatchTimeout);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                throw new LokiException($"live tail evaluates regular expressions in memory and supports linear-time patterns only (no lookarounds or backreferences): {pattern}");
            }
        }

        var services = new List<string>();
        byte[]? levelNumbers = null;
        foreach (var matcher in selector.Matchers)
        {
            _steps.Add(CompareStep(matcher.Name, ToCompareOp(matcher.Op), matcher.Value, null, CompileRegex));
            if (matcher.Op == LokiMatchOp.Equal && matcher.Name == "service_name")
            {
                services.Add(matcher.Value);
            }

            if (matcher.Op == LokiMatchOp.Equal && matcher.Name is "detected_level" or "level")
            {
                levelNumbers = SeverityNumbersOf(matcher.Value);
            }
        }

        // Equality on the service or the level is also handed to the broadcaster as an ordinary LogFilter, so
        // the shared fan-out loop does not queue lines this connection would discard.
        Prefilter = new LogFilter
        {
            Services = services.Count == 1 ? services : null,
            SeverityNumbers = levelNumbers,
        };

        foreach (var stage in selector.Stages)
        {
            switch (stage)
            {
                case LokiLineFilter line:
                    _steps.Add(LineFilterStep(line, CompileRegex));
                    break;
                case LokiPatternFilter pattern:
                {
                    var regex = CompileRegex(LokiPatternMatcher.ToRegex(pattern.Pattern));
                    _steps.Add(s => Matches(regex, s.Row.Body) != pattern.Negated);
                    break;
                }

                case LokiJsonStage json:
                    AddParser(new LokiParserSpec(LokiParserKind.Json, ToMap(json.Expressions)));
                    break;
                case LokiLogfmtStage logfmt:
                    AddParser(new LokiParserSpec(LokiParserKind.Logfmt, ToMap(logfmt.Expressions)));
                    break;
                case LokiLabelFilterStage filter:
                    _steps.Add(PredicateStep(filter.Predicate, CompileRegex));
                    break;
                case LokiLineFormatStage format:
                    _lineFormat = format.Template;
                    break;
                case LokiUnwrapStage:
                    throw new LokiException("live tail takes a log query; unwrap is only valid inside a range function.");
                case LokiDropStage:
                    break;
            }
        }
    }

    /// <summary>A coarse <see cref="LogFilter"/> that every line the query can match passes; the exact test is <see cref="TryMatch"/>.</summary>
    public LogFilter Prefilter { get; }

    /// <summary>Compiles <paramref name="selector"/>; throws <see cref="LokiException"/> for a query live tail cannot evaluate faithfully.</summary>
    public static LokiTailMatcher Compile(LokiLogSelector selector, LiveTailOptions options) => new(options, selector);

    /// <summary>The Loki view of a log stream entry.</summary>
    public static LokiLogRow ToRow(LogEventDto log) => new(
        (log.Timestamp.UtcTicks - DateTimeOffset.UnixEpoch.Ticks) * 100,
        log.Body,
        log.ServiceName,
        log.SeverityText,
        log.SeverityNumber,
        log.TraceId,
        log.SpanId,
        log.ScopeName,
        log.ScopeVersion,
        log.ResourceAttributes,
        log.LogAttributes);

    /// <summary>Runs the pipeline over <paramref name="row"/>; when it passes, <paramref name="entry"/> is what the query would return for it.</summary>
    public bool TryMatch(LokiLogRow row, out LokiLogEntry entry)
    {
        var (stream, metadata) = LokiEntryLabels.Labels(row);
        var state = new State(row, stream, metadata);
        foreach (var step in _steps)
        {
            if (!step(state))
            {
                entry = null!;
                return false;
            }
        }

        entry = LokiEntryLabels.Build(row, new LokiEntryPlan(_plan, _lineFormat));
        return true;
    }

    private void AddParser(LokiParserSpec spec)
    {
        _plan.Add(spec);
        _steps.Add(s =>
        {
            LokiEntryLabels.Apply(spec, s.Row.Body, s.Parsed, s.Stream, s.Metadata);
            s.AfterParser = true;
            return true;
        });
    }

    private static Func<State, bool> LineFilterStep(LokiLineFilter filter, Func<string, Regex> compile)
    {
        var tests = new List<Func<string, bool>>();
        foreach (var pattern in filter.Patterns)
        {
            if (filter.Op is LokiMatchOp.Equal or LokiMatchOp.NotEqual)
            {
                tests.Add(line => line.Contains(pattern, StringComparison.Ordinal));
            }
            else
            {
                var regex = compile(pattern);
                tests.Add(line => Matches(regex, line));
            }
        }

        var negated = filter.Op is LokiMatchOp.NotEqual or LokiMatchOp.NotRegex;
        return s => tests.Any(t => t(s.Row.Body)) != negated;
    }

    private Func<State, bool> PredicateStep(LokiPredicate predicate, Func<string, Regex> compile) => predicate switch
    {
        LokiAnd both => And(PredicateStep(both.Left, compile), PredicateStep(both.Right, compile)),
        LokiOr either => Or(PredicateStep(either.Left, compile), PredicateStep(either.Right, compile)),
        LokiComparison c => CompareStep(c.Label, c.Op, c.Text, c.Number, compile),
        _ => throw new LokiException("unsupported label filter."),
    };

    private static Func<State, bool> And(Func<State, bool> a, Func<State, bool> b) => s => a(s) && b(s);

    private static Func<State, bool> Or(Func<State, bool> a, Func<State, bool> b) => s => a(s) || b(s);

    /// <summary>A label comparison, with the semantics of <c>LokiSqlContext.Compare</c>: label regexes are anchored; an absent label is the empty string; a value that is not a number never satisfies a numeric comparison, except <c>!=</c>.</summary>
    private static Func<State, bool> CompareStep(string label, LokiCompareOp op, string text, double? number, Func<string, Regex> compile)
    {
        if (number is { } n)
        {
            return s =>
            {
                if (!double.TryParse(Value(label, s), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    return op == LokiCompareOp.NotEqual;
                }

                return op switch
                {
                    LokiCompareOp.Equal => value == n,
                    LokiCompareOp.NotEqual => value != n,
                    LokiCompareOp.Greater => value > n,
                    LokiCompareOp.GreaterOrEqual => value >= n,
                    LokiCompareOp.Less => value < n,
                    _ => value <= n,
                };
            };
        }

        switch (op)
        {
            case LokiCompareOp.Equal:
                return s => string.Equals(Value(label, s), text, StringComparison.Ordinal);
            case LokiCompareOp.NotEqual:
                return s => !string.Equals(Value(label, s), text, StringComparison.Ordinal);
            case LokiCompareOp.Regex:
            {
                var regex = compile($"^(?:{text})$");
                return s => Matches(regex, Value(label, s));
            }

            case LokiCompareOp.NotRegex:
            {
                var regex = compile($"^(?:{text})$");
                return s => !Matches(regex, Value(label, s));
            }

            default:
                throw new LokiException("a quoted value is required for this comparison.");
        }
    }

    /// <summary>A label's value at this point of the pipeline; the empty string when the label does not exist.</summary>
    private static string Value(string name, State s)
    {
        switch (name)
        {
            case "service_name":
                return s.Row.ServiceName;
            case "detected_level":
                return s.Metadata["detected_level"];
            case "level":
                return s.AfterParser && s.Parsed.TryGetValue("level", out var parsedLevel) ? parsedLevel : s.Metadata["detected_level"];
            case "__error__" or "__error_details__":
                return s.Parsed.TryGetValue(name, out var error) ? error : string.Empty;
        }

        if (s.AfterParser && s.Parsed.TryGetValue(name, out var parsed))
        {
            return parsed;
        }

        if (s.Metadata.TryGetValue(name, out var meta))
        {
            return meta;
        }

        return s.Stream.TryGetValue(name, out var labelValue) ? labelValue : string.Empty;
    }

    private static bool Matches(Regex regex, string text)
    {
        try
        {
            return regex.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static LokiCompareOp ToCompareOp(LokiMatchOp op) => op switch
    {
        LokiMatchOp.Equal => LokiCompareOp.Equal,
        LokiMatchOp.NotEqual => LokiCompareOp.NotEqual,
        LokiMatchOp.Regex => LokiCompareOp.Regex,
        _ => LokiCompareOp.NotRegex,
    };

    private static Dictionary<string, string> ToMap(IReadOnlyList<(string Label, string Target)> expressions)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (label, target) in expressions)
        {
            map[label] = target;
        }

        return map;
    }

    /// <summary>The OTel severity numbers whose bucketed level is <paramref name="level"/> (none match an unknown name).</summary>
    private static byte[] SeverityNumbersOf(string level) =>
        [.. Enumerable.Range(0, 25).Select(n => (byte)n).Where(n => LokiLabelResolver.LevelName(n) == level)];

    private sealed class State(LokiLogRow row, IReadOnlyDictionary<string, string> stream, IReadOnlyDictionary<string, string> metadata)
    {
        public LokiLogRow Row { get; } = row;

        public IReadOnlyDictionary<string, string> Stream { get; } = stream;

        public IReadOnlyDictionary<string, string> Metadata { get; } = metadata;

        public Dictionary<string, string> Parsed { get; } = new(StringComparer.Ordinal);

        public bool AfterParser { get; set; }
    }
}
