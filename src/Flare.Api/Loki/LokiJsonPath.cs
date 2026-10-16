using System.Globalization;
using System.Text;

namespace Flare.Api.Loki;

/// <summary>One step of a JSON path: an object key, or an array index (zero-based, as in LogQL).</summary>
internal readonly record struct LokiJsonSegment(string? Key, int Index)
{
    public static LokiJsonSegment OfKey(string key) => new(key, -1);

    public static LokiJsonSegment OfIndex(int index) => new(null, index);
}

/// <summary>
/// The path of <c>| json label="path"</c> (ADR-0237): keys separated by dots (<c>request.method</c>), quoted
/// keys in brackets (<c>["request"]["method"]</c>, which is what Logs Drilldown writes, and the only way to
/// spell a key that contains a dot or a bracket), and array indexes (<c>servers[0].name</c>). The forms mix:
/// <c>request["x.y"][2]</c>. Pure.
/// </summary>
internal static class LokiJsonPath
{
    public static IReadOnlyList<LokiJsonSegment> Parse(string path)
    {
        var segments = new List<LokiJsonSegment>();
        var pos = 0;
        while (pos < path.Length)
        {
            var c = path[pos];
            if (c == '.')
            {
                pos++;
            }
            else if (c == '[')
            {
                pos++;
                if (pos < path.Length && path[pos] == '"')
                {
                    segments.Add(LokiJsonSegment.OfKey(ReadQuotedKey(path, ref pos)));
                }
                else
                {
                    var start = pos;
                    while (pos < path.Length && char.IsAsciiDigit(path[pos]))
                    {
                        pos++;
                    }

                    segments.Add(pos > start && int.TryParse(path.AsSpan(start, pos - start), NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                        ? LokiJsonSegment.OfIndex(index)
                        : throw new LokiException($"invalid JSON path \"{path}\": expected a quoted key or an index in brackets."));
                }

                if (pos >= path.Length || path[pos] != ']')
                {
                    throw new LokiException($"invalid JSON path \"{path}\": expected ']'.");
                }

                pos++;
            }
            else
            {
                var start = pos;
                while (pos < path.Length && path[pos] is not ('.' or '['))
                {
                    pos++;
                }

                segments.Add(LokiJsonSegment.OfKey(path[start..pos]));
            }
        }

        return segments;
    }

    private static string ReadQuotedKey(string path, ref int pos)
    {
        pos++;
        var sb = new StringBuilder();
        while (pos < path.Length)
        {
            var c = path[pos++];
            if (c == '"')
            {
                return sb.ToString();
            }

            if (c == '\\' && pos < path.Length)
            {
                c = path[pos++];
            }

            sb.Append(c);
        }

        throw new LokiException($"invalid JSON path \"{path}\": unterminated quoted key.");
    }
}
