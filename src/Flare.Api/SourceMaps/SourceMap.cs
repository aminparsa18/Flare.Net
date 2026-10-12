using System.Text.Json;

namespace Flare.Api.SourceMaps;

/// <summary>An original-source position a generated position maps back to. Line and column are 0-based.</summary>
public readonly record struct OriginalPosition(string Source, int Line, int Column, string? Name);

/// <summary>
/// A parsed JavaScript source map (v3): decodes the VLQ <c>mappings</c> once into per-line segment
/// arrays so a lookup is a binary search. Index maps (<c>sections</c>) are rejected. See ADR-0152.
/// </summary>
public sealed class SourceMap
{
    private readonly struct Segment(int genColumn, int source, int line, int column, int name)
    {
        public int GenColumn { get; } = genColumn;
        public int Source { get; } = source;
        public int Line { get; } = line;
        public int Column { get; } = column;
        public int Name { get; } = name;
    }

    private readonly string[] _sources;
    private readonly string[] _names;
    private readonly Segment[][] _lines;

    private SourceMap(string[] sources, string[] names, Segment[][] lines)
    {
        _sources = sources;
        _names = names;
        _lines = lines;
    }

    /// <summary>Parses a source map. Throws <see cref="FormatException"/> when it isn't a usable v3 map.</summary>
    public static SourceMap Parse(ReadOnlySpan<byte> json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json.ToArray());
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("A source map must be a JSON object.");
            }

            if (root.TryGetProperty("sections", out _))
            {
                throw new FormatException("Index source maps (sections) are not supported.");
            }

            if (!root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || version.GetInt32() != 3)
            {
                throw new FormatException("Only source map version 3 is supported.");
            }

            if (!root.TryGetProperty("mappings", out var mappingsElement) || mappingsElement.ValueKind != JsonValueKind.String)
            {
                throw new FormatException("The source map has no mappings string.");
            }

            var sourceRoot = root.TryGetProperty("sourceRoot", out var sr) && sr.ValueKind == JsonValueKind.String ? sr.GetString() ?? "" : "";
            var sources = ReadStrings(root, "sources").Select(s => JoinRoot(sourceRoot, s)).ToArray();
            var names = ReadStrings(root, "names").ToArray();
            return new SourceMap(sources, names, DecodeMappings(mappingsElement.GetString() ?? "", sources.Length, names.Length));
        }
        catch (JsonException ex)
        {
            throw new FormatException("The source map is not valid JSON: " + ex.Message, ex);
        }
    }

    /// <summary>Maps a generated 0-based line/column to the original position, or null when no mapping covers it.</summary>
    public OriginalPosition? Lookup(int line, int column)
    {
        if (line < 0 || line >= _lines.Length)
        {
            return null;
        }

        var segments = _lines[line];
        int lo = 0, hi = segments.Length - 1, found = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (segments[mid].GenColumn <= column)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        if (found < 0 || segments[found].Source < 0)
        {
            return null;
        }

        var s = segments[found];
        return new OriginalPosition(_sources[s.Source], s.Line, s.Column, s.Name >= 0 ? _names[s.Name] : null);
    }

    private static IEnumerable<string> ReadStrings(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in array.EnumerateArray())
        {
            yield return item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "";
        }
    }

    private static string JoinRoot(string root, string source) =>
        root.Length == 0 || source.Contains("://", StringComparison.Ordinal) ? source : root.EndsWith('/') ? root + source : root + "/" + source;

    private static Segment[][] DecodeMappings(string mappings, int sourceCount, int nameCount)
    {
        var lines = new List<Segment[]>();
        var current = new List<Segment>();
        int genColumn = 0, source = 0, line = 0, column = 0, name = 0;
        var fields = new int[5];
        var pos = 0;

        while (pos <= mappings.Length)
        {
            if (pos == mappings.Length || mappings[pos] == ';' || mappings[pos] == ',')
            {
                var atLineEnd = pos == mappings.Length || mappings[pos] == ';';
                if (atLineEnd)
                {
                    lines.Add([.. current.OrderBy(s => s.GenColumn)]);
                    current.Clear();
                    genColumn = 0;
                }

                pos++;
                continue;
            }

            var count = 0;
            while (pos < mappings.Length && mappings[pos] != ',' && mappings[pos] != ';')
            {
                if (count == fields.Length)
                {
                    throw new FormatException("A mapping segment has more than five fields.");
                }

                fields[count++] = ReadVlq(mappings, ref pos);
            }

            if (count is not (1 or 4 or 5))
            {
                throw new FormatException("A mapping segment must have 1, 4 or 5 fields.");
            }

            genColumn += fields[0];
            if (count == 1)
            {
                current.Add(new Segment(genColumn, -1, 0, 0, -1));
                continue;
            }

            source += fields[1];
            line += fields[2];
            column += fields[3];
            var nameIndex = -1;
            if (count == 5)
            {
                name += fields[4];
                nameIndex = name;
            }

            if (source < 0 || source >= sourceCount || (nameIndex >= nameCount) || line < 0 || column < 0 || genColumn < 0)
            {
                throw new FormatException("A mapping refers to a source, name or position that does not exist.");
            }

            current.Add(new Segment(genColumn, source, line, column, nameIndex));
        }

        return [.. lines];
    }

    private static int ReadVlq(string text, ref int pos)
    {
        long result = 0;
        var shift = 0;
        while (true)
        {
            if (pos >= text.Length)
            {
                throw new FormatException("A mapping segment ends in the middle of a value.");
            }

            var digit = Base64Value(text[pos++]);
            result |= (long)(digit & 31) << shift;
            if ((digit & 32) == 0)
            {
                break;
            }

            shift += 5;
            if (shift > 30)
            {
                throw new FormatException("A mapping value is too large.");
            }
        }

        var negative = (result & 1) == 1;
        var value = (int)(result >> 1);
        return negative ? -value : value;
    }

    private static int Base64Value(char c) => c switch
    {
        >= 'A' and <= 'Z' => c - 'A',
        >= 'a' and <= 'z' => c - 'a' + 26,
        >= '0' and <= '9' => c - '0' + 52,
        '+' => 62,
        '/' => 63,
        _ => throw new FormatException($"Invalid base64 VLQ character '{c}' in mappings."),
    };
}
