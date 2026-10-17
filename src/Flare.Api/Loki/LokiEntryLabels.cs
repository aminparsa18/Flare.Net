using System.Text;
using System.Text.Json;

namespace Flare.Api.Loki;

/// <summary>The columns of one <c>logs</c> row that the Loki response needs.</summary>
internal sealed record LokiLogRow(
    long Ns,
    string Body,
    string ServiceName,
    string SeverityText,
    byte SeverityNumber,
    string TraceId,
    string SpanId,
    string ScopeName,
    string ScopeVersion,
    IReadOnlyDictionary<string, string> ResourceAttributes,
    IReadOnlyDictionary<string, string> LogAttributes);

/// <summary>
/// One log line in Loki's terms: <see cref="StreamLabels"/> identify the stream, <see cref="StructuredMetadata"/>
/// is the per-line key/value data Loki 3 attaches to an entry, and <see cref="Parsed"/> holds the labels the
/// <c>| json</c> and <c>| logfmt</c> stages extracted. <see cref="Line"/> is the line as returned: the body, or
/// the result of <c>| line_format</c>.
/// </summary>
internal sealed record LokiLogEntry(
    long Ns,
    string Line,
    IReadOnlyDictionary<string, string> StreamLabels,
    IReadOnlyDictionary<string, string> StructuredMetadata,
    IReadOnlyDictionary<string, string> Parsed);

/// <summary>
/// Turns a <c>logs</c> row into a Loki entry the way Loki's own OTLP ingestion does (ADR-0236): the stream
/// is <c>service_name</c> plus the indexed resource attributes (<see cref="LokiLabelResolver.IndexResourceKeys"/>),
/// and everything else - the level, the trace and span ids, the scope, log attributes and the remaining
/// resource attributes - is structured metadata. The parser stages of the pipeline add <c>parsed</c> labels
/// and a final <c>line_format</c> rewrites the line. Pure.
/// </summary>
internal static class LokiEntryLabels
{
    private const int MaxParsedLabels = 100;

    private const long MaxParsedBodyChars = 64 * 1024;

    public static LokiLogEntry Build(LokiLogRow row, LokiEntryPlan? plan = null)
    {
        plan ??= LokiEntryPlan.None;
        var (stream, metadata) = Labels(row);
        var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var parser in plan.Parsers)
        {
            Apply(parser, row.Body, parsed, stream, metadata);
        }

        var line = plan.LineFormat is { } template
            ? template.Render(name => Lookup(name, parsed, metadata, stream), row.Body, DateTimeOffset.UnixEpoch.AddTicks(row.Ns / 100))
            : row.Body;
        return new LokiLogEntry(row.Ns, line, stream, metadata, parsed);
    }

    /// <summary>The stream labels and the structured metadata of a row, before any parser stage.</summary>
    public static (SortedDictionary<string, string> Stream, SortedDictionary<string, string> Metadata) Labels(LokiLogRow row)
    {
        var stream = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["service_name"] = row.ServiceName.Length > 0 ? row.ServiceName : "unknown_service",
        };

        foreach (var (key, value) in row.ResourceAttributes)
        {
            if (LokiLabelResolver.IndexResourceKeys.Contains(key) && value.Length > 0)
            {
                stream.TryAdd(LokiLabelResolver.LabelName(key), value);
            }
        }

        var metadata = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["detected_level"] = LokiLabelResolver.LevelName(row.SeverityNumber),
        };

        void Add(string name, string value)
        {
            if (value.Length > 0 && !stream.ContainsKey(name))
            {
                metadata.TryAdd(name, value);
            }
        }

        Add("severity_text", row.SeverityText);
        Add("trace_id", row.TraceId);
        Add("span_id", row.SpanId);
        Add("scope_name", row.ScopeName);
        Add("scope_version", row.ScopeVersion);
        foreach (var (key, value) in row.LogAttributes)
        {
            Add(LokiLabelResolver.LabelName(key), value);
        }

        foreach (var (key, value) in row.ResourceAttributes)
        {
            if (key != "service.name" && !LokiLabelResolver.IndexResourceKeys.Contains(key))
            {
                Add(LokiLabelResolver.LabelName(key), value);
            }
        }

        return (stream, metadata);
    }

    /// <summary>
    /// What a <c>line_format</c> template sees as <c>.name</c>: parsed labels first (the latest stage wins),
    /// then structured metadata, then the stream; <c>level</c> falls back to the severity-derived level, as
    /// it does in a selector.
    /// </summary>
    public static string? Lookup(
        string name, IReadOnlyDictionary<string, string> parsed, IReadOnlyDictionary<string, string> metadata, IReadOnlyDictionary<string, string> stream)
    {
        if (parsed.TryGetValue(name, out var value) || metadata.TryGetValue(name, out value) || stream.TryGetValue(name, out value))
        {
            return value;
        }

        return name == "level" && metadata.TryGetValue("detected_level", out var level) ? level : null;
    }

    /// <summary>
    /// Runs one parser stage over <paramref name="body"/> and adds what it extracts to <paramref name="parsed"/>.
    /// A label that collides with a stream, metadata or already-parsed label gets Loki's <c>_extracted</c>
    /// suffix; a body that is not a JSON object (or, under <c>logfmt --strict</c>, a malformed line) sets <c>__error__</c>.
    /// </summary>
    public static void Apply(
        LokiParserSpec parser, string body, Dictionary<string, string> parsed, IReadOnlyDictionary<string, string> stream, IReadOnlyDictionary<string, string> metadata)
    {
        if (parser.Kind == LokiParserKind.Logfmt)
        {
            IReadOnlyList<(string Key, string Value)> pairs;
            if (parser.Strict)
            {
                // A malformed line keeps the labels read before the fault and carries the error (ADR-0238).
                bool failed;
                (pairs, failed) = LokiLogfmt.ParseStrict(body);
                if (failed)
                {
                    parsed["__error__"] = "LogfmtParserErr";
                }
            }
            else
            {
                pairs = LokiLogfmt.Parse(body);
            }

            foreach (var (key, value) in pairs)
            {
                if (parser.Expressions.Count == 0)
                {
                    parsed[Unique(key, parsed, stream, metadata)] = value;
                }
                else
                {
                    foreach (var (label, wanted) in parser.Expressions)
                    {
                        if (LokiLogfmt.Sanitize(wanted) == key)
                        {
                            parsed[Unique(label, parsed, stream, metadata)] = value;
                        }
                    }
                }
            }

            return;
        }

        if (body.Length > MaxParsedBodyChars)
        {
            parsed["__error__"] = "JSONParserErr";
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                parsed["__error__"] = "JSONParserErr";
                return;
            }

            if (parser.Expressions.Count > 0)
            {
                foreach (var (label, path) in parser.Expressions)
                {
                    if (Navigate(document.RootElement, path) is { } leaf && Text(leaf) is { } text)
                    {
                        parsed[Unique(label, parsed, stream, metadata)] = text;
                    }
                }
            }
            else
            {
                foreach (var field in Flatten(document.RootElement))
                {
                    parsed[Unique(field.Name, parsed, stream, metadata)] = field.Value;
                }
            }
        }
        catch (JsonException)
        {
            parsed["__error__"] = "JSONParserErr";
        }
    }

    /// <summary>A JSON field a <c>| json</c> stage extracts: its label name (nested keys joined by <c>_</c>), value, and the key path it came from.</summary>
    internal sealed record JsonField(string Name, string Value, IReadOnlyList<string> Path);

    /// <summary>
    /// Every scalar leaf of a JSON object, nested keys joined by <c>_</c> (<c>{"request":{"method":"GET"}}</c> is
    /// <c>request_method</c>), at most <see cref="MaxParsedLabels"/> of them. Arrays are reported as their raw
    /// JSON; JSON null and empty objects have no label.
    /// </summary>
    internal static List<JsonField> Flatten(JsonElement root)
    {
        var fields = new List<JsonField>();
        Flatten(root, string.Empty, [], fields);
        return fields;
    }

    /// <summary>The leaf fields of <paramref name="body"/> if it is a JSON object; null when it is not.</summary>
    internal static List<JsonField>? TryFlattenJson(string body)
    {
        if (body.Length > MaxParsedBodyChars)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object ? Flatten(document.RootElement) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonElement? Navigate(JsonElement element, string path)
    {
        foreach (var segment in LokiJsonPath.Parse(path))
        {
            if (segment.Key is { } key)
            {
                if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(key, out element))
                {
                    return null;
                }
            }
            else if (element.ValueKind != JsonValueKind.Array || segment.Index >= element.GetArrayLength())
            {
                return null;
            }
            else
            {
                element = element[segment.Index];
            }
        }

        return element;
    }

    private static void Flatten(JsonElement element, string prefix, string[] path, List<JsonField> into)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (into.Count >= MaxParsedLabels)
            {
                return;
            }

            var name = prefix.Length == 0 ? Sanitize(property.Name) : prefix + "_" + Sanitize(property.Name);
            string[] here = [.. path, property.Name];
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                Flatten(property.Value, name, here, into);
            }
            else if (Text(property.Value) is { } text)
            {
                into.Add(new JsonField(name, text, here));
            }
        }
    }

    /// <summary>A scalar's text, an array's raw JSON; null for JSON null and objects.</summary>
    private static string? Text(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Array => element.GetRawText(),
        _ => null,
    };

    private static string Unique(
        string name, IReadOnlyDictionary<string, string> parsed, IReadOnlyDictionary<string, string> stream, IReadOnlyDictionary<string, string> metadata) =>
        parsed.ContainsKey(name) || stream.ContainsKey(name) || metadata.ContainsKey(name) ? name + "_extracted" : name;

    private static string Sanitize(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '_');
        }

        return sb.ToString();
    }
}
