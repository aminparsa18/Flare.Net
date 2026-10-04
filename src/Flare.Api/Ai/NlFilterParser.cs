using System.Text.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Ai;

/// <summary>
/// Turns the model's JSON reply into a validated <see cref="NlFilterResponse"/> (ADR-0105). Pure.
/// Everything is whitelisted: unknown properties are ignored, enums are matched against the real
/// ones, sizes are capped, and a structural query must pass <see cref="TraceStructureSqlBuilder.Validate"/>.
/// A piece that fails is dropped with a warning rather than failing the whole filter. Nothing the
/// model writes is ever executed as SQL; the result is only the filter shapes the explorers already take.
/// </summary>
public static class NlFilterParser
{
    public const string Logs = "Logs";
    public const string Traces = "Traces";

    private const int MaxServices = 20;
    private const int MaxAttributes = 8;
    private const int MaxValues = 50;
    private const int MaxTextLength = 200;
    private static readonly TimeSpan MaxCustomRange = TimeSpan.FromDays(365);

    private static readonly string[] LogPresets = ["5m", "15m", "1h", "6h", "24h", "7d", "30d", "90d", "365d", "all", "today", "thisWeek"];
    private static readonly string[] TracePresets = ["5m", "15m", "1h", "6h", "24h", "7d", "30d", "90d", "365d"];

    // OTel SeverityNumber buckets (TRACE 1-4 ... FATAL 21-24), same as the dashboard's severity.ts.
    private static readonly (string Name, int Min, int Max)[] Severities =
        [("trace", 1, 4), ("debug", 5, 8), ("info", 9, 12), ("warn", 13, 16), ("error", 17, 20), ("fatal", 21, 24)];

    public static bool IsTarget(string? target) => target is Logs or Traces;

    /// <returns>The filter, or null with a user-facing error when the reply is not a JSON object.</returns>
    public static (NlFilterResponse? Result, string? Error) Parse(string target, string modelText, string model, DateTimeOffset now)
    {
        if (!TryReadObject(modelText, out var doc))
        {
            return (null, "The model did not return a filter. Try rephrasing.");
        }

        using (doc)
        {
            var root = doc!.RootElement;
            var warnings = new List<string>();
            var logs = target == Logs;

            var (preset, customRange) = ParseTimeRange(root, logs, now, warnings);

            var search = "";
            if (GetString(root, "search") is { Length: > 0 } text)
            {
                if (logs)
                {
                    search = Trim(text);
                }
                else
                {
                    warnings.Add("Free-text search only applies to logs; it was ignored.");
                }
            }

            var severities = logs ? ParseSeverities(root, warnings) : [];
            if (!logs && (root.TryGetProperty("minSeverity", out _) || root.TryGetProperty("severities", out _)))
            {
                warnings.Add("Severity only applies to logs; it was ignored.");
            }

            var statusCodes = logs ? [] : ParseStatusCodes(root, warnings);
            TraceStructureFilter? structure = logs ? null : ParseStructure(root, warnings);

            return (new NlFilterResponse
            {
                Model = model,
                TimeRangePreset = preset,
                CustomRange = customRange,
                Services = ParseStrings(root, "services", MaxServices),
                SeverityNumbers = severities,
                Search = search,
                AttributeFilters = ParseAttributes(root, "attributes", logs, warnings),
                StatusCodes = statusCodes,
                Structure = structure,
                Warnings = warnings
            }, null);
        }
    }

    private static bool TryReadObject(string text, out JsonDocument? doc)
    {
        doc = null;
        var json = text.Trim();
        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            // Models often fence the JSON even when told not to.
            var firstNewline = json.IndexOf('\n');
            var lastFence = json.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline < 0 || lastFence <= firstNewline)
            {
                return false;
            }

            json = json[(firstNewline + 1)..lastFence].Trim();
        }

        try
        {
            var parsed = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            {
                parsed.Dispose();
                return false;
            }

            doc = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static (string Preset, NlCustomRange? Custom) ParseTimeRange(JsonElement root, bool logs, DateTimeOffset now, List<string> warnings)
    {
        const string Default = "1h";
        if (!root.TryGetProperty("timeRange", out var range))
        {
            return (Default, null);
        }

        var allowed = logs ? LogPresets : TracePresets;
        if (range.ValueKind == JsonValueKind.String)
        {
            var preset = allowed.FirstOrDefault(p => string.Equals(p, range.GetString(), StringComparison.OrdinalIgnoreCase));
            if (preset is not null)
            {
                return (preset, null);
            }

            warnings.Add($"Time range \"{range.GetString()}\" is not supported here; using the last hour.");
            return (Default, null);
        }

        if (range.ValueKind == JsonValueKind.Object
            && DateTimeOffset.TryParse(GetString(range, "from"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var from)
            && DateTimeOffset.TryParse(GetString(range, "to"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var to)
            && from < to && to - from <= MaxCustomRange && to <= now.AddMinutes(5))
        {
            return ("custom", new NlCustomRange { From = from.ToUniversalTime(), To = to.ToUniversalTime() });
        }

        warnings.Add("The time range could not be understood; using the last hour.");
        return (Default, null);
    }

    private static List<int> ParseSeverities(JsonElement root, List<string> warnings)
    {
        var numbers = new SortedSet<int>();
        if (GetString(root, "minSeverity") is { Length: > 0 } min)
        {
            var index = Array.FindIndex(Severities, s => s.Name == min.Trim().ToLowerInvariant());
            if (index < 0)
            {
                warnings.Add($"Unknown severity \"{min}\" was ignored.");
            }
            else
            {
                for (var i = index; i < Severities.Length; i++)
                {
                    AddRange(numbers, Severities[i]);
                }
            }
        }

        foreach (var name in ParseStrings(root, "severities", Severities.Length))
        {
            var bucket = Array.FindIndex(Severities, s => s.Name == name.Trim().ToLowerInvariant());
            if (bucket < 0)
            {
                warnings.Add($"Unknown severity \"{name}\" was ignored.");
            }
            else
            {
                AddRange(numbers, Severities[bucket]);
            }
        }

        return [.. numbers];
    }

    private static void AddRange(SortedSet<int> set, (string Name, int Min, int Max) bucket)
    {
        for (var n = bucket.Min; n <= bucket.Max; n++)
        {
            set.Add(n);
        }
    }

    private static List<string> ParseStatusCodes(JsonElement root, List<string> warnings)
    {
        var result = new List<string>();
        foreach (var raw in ParseStrings(root, "statusCodes", 3))
        {
            var label = raw.Trim().ToUpperInvariant();
            label = label.StartsWith("STATUS_CODE_", StringComparison.Ordinal) ? label : "STATUS_CODE_" + label;
            if (label is "STATUS_CODE_ERROR" or "STATUS_CODE_OK" or "STATUS_CODE_UNSET")
            {
                if (!result.Contains(label))
                {
                    result.Add(label);
                }
            }
            else
            {
                warnings.Add($"Unknown span status \"{raw}\" was ignored.");
            }
        }

        return result;
    }

    private static List<NlAttributeFilter> ParseAttributes(JsonElement parent, string property, bool logs, List<string> warnings)
    {
        var result = new List<NlAttributeFilter>();
        if (!parent.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in array.EnumerateArray())
        {
            if (result.Count >= MaxAttributes)
            {
                warnings.Add($"Only the first {MaxAttributes} attribute conditions were kept.");
                break;
            }

            if (item.ValueKind != JsonValueKind.Object || GetString(item, "key") is not { Length: > 0 } key || key.Length > MaxTextLength
                || key.Any(char.IsControl))
            {
                warnings.Add("An attribute condition without a valid key was ignored.");
                continue;
            }

            if (!TryParseBag(GetString(item, "bag"), logs, out var bag))
            {
                warnings.Add($"Attribute \"{key}\" had an unknown attribute group and was ignored.");
                continue;
            }

            var op = ParseOperator(GetString(item, "operator") ?? "Equals", logs);
            if (op is null)
            {
                warnings.Add($"Attribute \"{key}\" had an unknown operator and was ignored.");
                continue;
            }

            var values = ParseStrings(item, "values", MaxValues);
            var value = Trim(GetString(item, "value") ?? "");
            if (op is "In" or "NotIn" && values.Count == 0)
            {
                warnings.Add($"Attribute \"{key}\" had no values to match and was ignored.");
                continue;
            }

            if (op is not ("Exists" or "Absent" or "In" or "NotIn") && value.Length == 0)
            {
                warnings.Add($"Attribute \"{key}\" had no value to compare and was ignored.");
                continue;
            }

            if (op is "GreaterThan" or "GreaterThanOrEqual" or "LessThan" or "LessThanOrEqual"
                && !double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                warnings.Add($"Attribute \"{key}\" needs a number to compare against and was ignored.");
                continue;
            }

            result.Add(new NlAttributeFilter
            {
                Bag = bag!,
                Key = key,
                Value = op is "In" or "NotIn" or "Exists" or "Absent" ? "" : value,
                Operator = op,
                Values = op is "In" or "NotIn" ? values : null
            });
        }

        return result;
    }

    private static bool TryParseBag(string? raw, bool logs, out string? bag)
    {
        bag = (raw ?? (logs ? "log" : "span")).Trim().ToLowerInvariant() switch
        {
            "log" or "span" or "attributes" => logs ? "Log" : "Span",
            "resource" => "Resource",
            "scope" => "Scope",
            _ => null
        };
        return bag is not null;
    }

    private static string? ParseOperator(string raw, bool logs)
    {
        var name = raw.Trim();
        if (logs)
        {
            return Enum.TryParse<AttributeFilterOperator>(name, ignoreCase: true, out var op) && Enum.IsDefined(op) ? op.ToString() : null;
        }

        return Enum.TryParse<SpanAttributeFilterOperator>(name, ignoreCase: true, out var spanOp) && Enum.IsDefined(spanOp) ? spanOp.ToString() : null;
    }

    private static TraceStructureFilter? ParseStructure(JsonElement root, List<string> warnings)
    {
        if (!root.TryGetProperty("structure", out var node) || node.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var conditions = new List<TraceSpanCondition>();
        if (node.TryGetProperty("conditions", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray().Take(TraceStructureSqlBuilder.MaxConditions + 1))
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var attributes = ParseAttributes(item, "attributes", logs: false, warnings);
                ulong? minDuration = item.TryGetProperty("minDurationMs", out var ms) && ms.ValueKind == JsonValueKind.Number
                    && ms.TryGetDouble(out var millis) && millis > 0 && millis < 86_400_000
                    ? (ulong)(millis * 1_000_000)
                    : null;
                var status = GetString(item, "statusCode")?.Trim().ToUpperInvariant();
                if (status is { Length: > 0 } && !status.StartsWith("STATUS_CODE_", StringComparison.Ordinal))
                {
                    status = "STATUS_CODE_" + status;
                }

                conditions.Add(new TraceSpanCondition
                {
                    Name = (GetString(item, "name") ?? "").Trim().ToUpperInvariant(),
                    ServiceName = NullIfEmpty(GetString(item, "serviceName")),
                    SpanName = NullIfEmpty(GetString(item, "spanName")),
                    StatusCode = NullIfEmpty(status),
                    MinDurationNano = minDuration,
                    Attributes = attributes.Count == 0
                        ? null
                        : [.. attributes.Select(a => new SpanAttributeFilter
                        {
                            Bag = Enum.Parse<SpanAttributeBag>(a.Bag),
                            Key = a.Key,
                            Value = a.Value,
                            Operator = Enum.Parse<SpanAttributeFilterOperator>(a.Operator),
                            Values = a.Values
                        })]
                });
            }
        }

        var structure = new TraceStructureFilter { Conditions = conditions, Expression = Trim(GetString(node, "expression") ?? "") };
        try
        {
            TraceStructureSqlBuilder.Validate(structure);
            return structure;
        }
        catch (ArgumentException ex)
        {
            warnings.Add($"The span-structure part was dropped: {ex.Message}");
            return null;
        }
    }

    private static List<string> ParseStrings(JsonElement parent, string property, int max)
    {
        var result = new List<string>();
        if (!parent.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } s && result.Count < max)
            {
                result.Add(Trim(s));
            }
        }

        return result;
    }

    private static string? GetString(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : Trim(s);

    private static string Trim(string s) => s.Length > MaxTextLength ? s[..MaxTextLength] : s;
}
