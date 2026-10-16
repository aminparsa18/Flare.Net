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
/// is the per-line key/value data Loki 3 attaches to an entry, and <see cref="Parsed"/> holds the labels a
/// <c>| json</c> stage extracted.
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
/// resource attributes - is structured metadata. Pure.
/// </summary>
internal static class LokiEntryLabels
{
    private const int MaxParsedLabels = 100;

    private const long MaxParsedBodyChars = 64 * 1024;

    public static LokiLogEntry Build(LokiLogRow row, LokiJsonSpec? json)
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

        var parsed = json is null ? new Dictionary<string, string>() : Parse(row.Body, json, stream, metadata);
        return new LokiLogEntry(row.Ns, row.Body, stream, metadata, parsed);
    }

    /// <summary>
    /// What <c>| json</c> extracts: with no expressions every scalar leaf, nested keys joined by <c>_</c>
    /// (<c>{"request":{"method":"GET"}}</c> is <c>request_method</c>); with expressions only those labels.
    /// A label that collides with a stream or metadata label gets Loki's <c>_extracted</c> suffix; a body
    /// that is not a JSON object sets <c>__error__</c>.
    /// </summary>
    private static Dictionary<string, string> Parse(
        string body, LokiJsonSpec json, IReadOnlyDictionary<string, string> stream, IReadOnlyDictionary<string, string> metadata)
    {
        var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
        if (body.Length > MaxParsedBodyChars)
        {
            parsed["__error__"] = "JSONParserErr";
            return parsed;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                parsed["__error__"] = "JSONParserErr";
                return parsed;
            }

            if (json.Expressions.Count > 0)
            {
                foreach (var (label, path) in json.Expressions)
                {
                    if (Navigate(document.RootElement, path) is { } leaf && Text(leaf) is { } text)
                    {
                        parsed[Unique(label, stream, metadata)] = text;
                    }
                }
            }
            else
            {
                Flatten(document.RootElement, string.Empty, parsed, stream, metadata);
            }
        }
        catch (JsonException)
        {
            parsed.Clear();
            parsed["__error__"] = "JSONParserErr";
        }

        return parsed;
    }

    private static JsonElement? Navigate(JsonElement element, string path)
    {
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(segment, out element))
            {
                return null;
            }
        }

        return element;
    }

    private static void Flatten(
        JsonElement element, string prefix, Dictionary<string, string> into, IReadOnlyDictionary<string, string> stream, IReadOnlyDictionary<string, string> metadata)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (into.Count >= MaxParsedLabels)
            {
                return;
            }

            var name = prefix.Length == 0 ? Sanitize(property.Name) : prefix + "_" + Sanitize(property.Name);
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                Flatten(property.Value, name, into, stream, metadata);
            }
            else if (Text(property.Value) is { } text)
            {
                into[Unique(name, stream, metadata)] = text;
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

    private static string Unique(string name, IReadOnlyDictionary<string, string> stream, IReadOnlyDictionary<string, string> metadata) =>
        stream.ContainsKey(name) || metadata.ContainsKey(name) ? name + "_extracted" : name;

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
