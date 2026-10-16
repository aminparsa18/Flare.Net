using System.Globalization;
using System.Text.RegularExpressions;

namespace Flare.Api.Loki;

/// <summary>A field Logs Drilldown can break down by (<c>detected_fields</c>).</summary>
/// <param name="Label">The label name, as a query would write it.</param>
/// <param name="Type"><c>string</c>, <c>int</c>, <c>float</c>, <c>boolean</c>, <c>duration</c> or <c>bytes</c>.</param>
/// <param name="Cardinality">Distinct values in the sampled lines (capped).</param>
/// <param name="Parsers"><c>json</c> and/or <c>logfmt</c> for a field found in the line; null for structured metadata, which needs no parser.</param>
/// <param name="JsonPath">The key path of a JSON field, so a query can write <c>| json label="a.b"</c>.</param>
internal sealed record LokiDetectedField(string Label, string Type, ulong Cardinality, IReadOnlyList<string>? Parsers, IReadOnlyList<string>? JsonPath);

/// <summary>
/// Works out which fields a set of sampled log lines has (ADR-0237): the structured metadata of each entry,
/// and the labels <c>| json</c> and <c>| logfmt</c> would extract, with a type guessed from the values and
/// the distinct-value count Drilldown uses to choose how to chart them. A parsed label that collides with a
/// stream or metadata label is reported as <c>name_extracted</c>, as the response labels are. Pure.
/// </summary>
internal static class LokiDetectedFields
{
    /// <summary>Distinct values remembered per field; past it the cardinality stops growing.</summary>
    public const int MaxDistinctValues = 5000;

    public const int DefaultFieldLimit = 100;

    public const int DefaultLineLimit = 1000;

    private static readonly Regex DurationPattern = new(
        @"^(?:[0-9]+(?:\.[0-9]+)?(?:ns|us|µs|ms|s|m|h))+$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex BytesPattern = new(
        @"^[0-9]+(?:\.[0-9]+)?[ ]?(?:[kmgtpe]i?)?b?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<LokiDetectedField> Analyze(IReadOnlyList<LokiLogRow> rows, int fieldLimit)
    {
        var fields = new Dictionary<string, Accumulator>(StringComparer.Ordinal);

        Accumulator For(string name) => fields.TryGetValue(name, out var existing) ? existing : fields[name] = new Accumulator();

        foreach (var row in rows)
        {
            var (stream, metadata) = LokiEntryLabels.Labels(row);
            foreach (var (name, value) in metadata)
            {
                For(name).Observe(value, parser: null, path: null);
            }

            if (LokiEntryLabels.TryFlattenJson(row.Body) is { } json)
            {
                foreach (var field in json)
                {
                    var name = Collides(field.Name, stream, metadata) ? field.Name + "_extracted" : field.Name;
                    For(name).Observe(field.Value, "json", field.Path);
                }
            }

            foreach (var (key, value) in LokiLogfmt.Parse(row.Body))
            {
                var name = Collides(key, stream, metadata) ? key + "_extracted" : key;
                For(name).Observe(value, "logfmt", path: null);
            }
        }

        return fields
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(Math.Max(1, fieldLimit))
            .Select(kv => kv.Value.ToField(kv.Key))
            .ToList();
    }

    /// <summary>The type of a single value.</summary>
    internal static string TypeOf(string value)
    {
        if (value is "true" or "false")
        {
            return "boolean";
        }

        if (long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
        {
            return "int";
        }

        if (value.Any(char.IsAsciiDigit)
            && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number))
        {
            return "float";
        }

        if (DurationPattern.IsMatch(value))
        {
            return "duration";
        }

        return value.Any(char.IsAsciiLetter) && BytesPattern.IsMatch(value) ? "bytes" : "string";
    }

    private static bool Collides(string name, IReadOnlyDictionary<string, string> stream, IReadOnlyDictionary<string, string> metadata) =>
        stream.ContainsKey(name) || metadata.ContainsKey(name);

    private sealed class Accumulator
    {
        private readonly HashSet<string> _values = new(StringComparer.Ordinal);
        private readonly SortedSet<string> _parsers = new(StringComparer.Ordinal);
        private readonly HashSet<string> _types = new(StringComparer.Ordinal);
        private IReadOnlyList<string>? _path;

        public void Observe(string value, string? parser, IReadOnlyList<string>? path)
        {
            if (parser is not null)
            {
                _parsers.Add(parser);
            }

            _path ??= path;
            if (value.Length == 0)
            {
                return;
            }

            _types.Add(TypeOf(value));
            if (_values.Count < MaxDistinctValues)
            {
                _values.Add(value);
            }
        }

        public LokiDetectedField ToField(string label)
        {
            var type = _types.Count switch
            {
                0 => "string",
                1 => _types.First(),
                2 when _types.Contains("int") && _types.Contains("float") => "float",
                _ => "string",
            };
            return new LokiDetectedField(label, type, (ulong)_values.Count, _parsers.Count == 0 ? null : [.. _parsers], _path);
        }
    }
}

/// <summary>One bucket of one Drain pattern, as read from ClickHouse.</summary>
internal sealed record LokiPatternRow(string PatternId, string Level, string Template, long Bucket, long Count);

/// <summary>A pattern's line counts over time for <c>patterns</c>: <see cref="Samples"/> are (unix seconds, count).</summary>
internal sealed record LokiPatternSeries(string Pattern, string Level, IReadOnlyList<(long Seconds, long Count)> Samples)
{
    public long Total => Samples.Sum(s => s.Count);
}

/// <summary>
/// Turns Drain clusters (ADR-0007) into Loki's pattern series (ADR-0237): the template uses Loki's
/// <c>&lt;_&gt;</c> placeholder instead of Flare's <c>&lt;*&gt;</c>; the template of a cluster is the latest one seen,
/// since Drain generalises it as lines arrive; there is one series per (cluster, level) because Drilldown
/// filters patterns by level. Pure.
/// </summary>
internal static class LokiPatternAssembler
{
    public static List<LokiPatternSeries> Assemble(IReadOnlyList<LokiPatternRow> rows, long originMs, long stepMs, int limit)
    {
        var latestTemplate = rows
            .GroupBy(r => r.PatternId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Bucket).First().Template, StringComparer.Ordinal);

        return rows
            .GroupBy(r => (r.PatternId, r.Level))
            .Select(g => new LokiPatternSeries(
                latestTemplate[g.Key.PatternId].Replace("<*>", "<_>", StringComparison.Ordinal),
                g.Key.Level,
                g.GroupBy(r => r.Bucket)
                    .OrderBy(b => b.Key)
                    .Select(b => ((originMs + b.Key * stepMs) / 1000, b.Sum(r => r.Count)))
                    .ToList()))
            .OrderByDescending(s => s.Total)
            .ThenBy(s => s.Pattern, StringComparer.Ordinal)
            .Take(Math.Max(1, limit))
            .ToList();
    }
}
