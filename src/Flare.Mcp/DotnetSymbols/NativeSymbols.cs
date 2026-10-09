using System.Text.Json;
using System.Text.RegularExpressions;

namespace Flare.Mcp.DotnetSymbols;

/// <summary>
/// Function-to-source table for Native AOT stack frames (<c>at Ns.Type.Method(Args) + 0x48</c>, an offset from
/// the function's entry). Built from the image's debug symbols (an Apple <c>.dSYM</c>) by
/// <see cref="NativeSymbolsBuilder"/>; the mangled linkage name is stored as is and matched here, so the
/// mangling rules can evolve without re-uploading. See ADR-0168.
/// </summary>
public sealed partial class NativeSymbols
{
    public const string BundleSuffix = ".native.json";

    private readonly string[] _files;
    private readonly Function[] _functions;
    private readonly Dictionary<string, List<int>> _bySuffix = new(StringComparer.Ordinal);

    internal sealed record Function(string Symbol, int[] Offset, int[] Line, int[] File);

    private NativeSymbols(string uuid, string[] files, Function[] functions)
    {
        Uuid = uuid;
        _files = files;
        _functions = functions;
        Index();
    }

    /// <summary>The image UUID (Mach-O <c>LC_UUID</c>), lower-case with dashes.</summary>
    public string Uuid { get; }

    public static string BundleName(string uuid) => uuid + BundleSuffix;

    [GeneratedRegex(@"<[^<>]*>$")]
    private static partial Regex TrailingGenericArguments();

    [GeneratedRegex(@"\[[^\[\]]*\]")]
    private static partial Regex BracketedGenericParameters();

    // Every symbol is indexed under each suffix that starts after an underscore, because the
    // leading "{assembly}_" part of a linkage name cannot be told from the namespace.
    private void Index()
    {
        var names = new HashSet<string>(_functions.Select(f => Normalize(f.Symbol)), StringComparer.Ordinal);
        for (var i = 0; i < _functions.Length; i++)
        {
            var name = Normalize(_functions[i].Symbol);
            AddSuffixes(name, i);

            // "X_0", "X_1" next to an "X" are overloads of X: register them under X too, so X is ambiguous.
            var cut = name.LastIndexOf('_');
            if (cut > 0 && name[(cut + 1)..].All(char.IsAsciiDigit) && name.Length > cut + 1 && names.Contains(name[..cut]))
            {
                AddSuffixes(name[..cut], i);
            }
        }
    }

    private void AddSuffixes(string name, int function)
    {
        for (var at = name.IndexOf('_'); at >= 0; at = name.IndexOf('_', at + 1))
        {
            var suffix = name[(at + 1)..];
            if (!_bySuffix.TryGetValue(suffix, out var list))
            {
                _bySuffix[suffix] = list = [];
            }

            list.Add(function);
        }
    }

    private static string Normalize(string symbol) => TrailingGenericArguments().Replace(symbol.TrimStart('_'), "");

    /// <summary>
    /// The linkage-name form of a frame's method name: every character outside <c>[A-Za-z0-9]</c> becomes <c>_</c>,
    /// with <c>__</c> between the declaring type and the method.
    /// </summary>
    internal static string Mangle(string frameMethod)
    {
        var name = BracketedGenericParameters().Replace(frameMethod, "");
        var split = LastMemberDot(name);
        var type = split < 0 ? "" : name[..split];
        var method = split < 0 ? name : name[(split + 1)..];
        return Sanitize(type) + (type.Length > 0 ? "__" : "") + Sanitize(method);

        static string Sanitize(string s) => string.Create(s.Length, s, (span, src) =>
        {
            for (var i = 0; i < src.Length; i++)
            {
                span[i] = char.IsAsciiLetterOrDigit(src[i]) ? src[i] : '_';
            }
        });
    }

    /// <summary>Index of the dot that separates the method from its type: the last one outside <c>&lt;&gt;</c> (a ".ctor" keeps its dot).</summary>
    private static int LastMemberDot(string name)
    {
        var depth = 0;
        for (var i = name.Length - 1; i >= 0; i--)
        {
            if (name[i] == '>')
            {
                depth++;
            }
            else if (name[i] == '<')
            {
                depth--;
            }
            else if (name[i] == '.' && depth == 0)
            {
                return i > 0 && name[i - 1] == '.' ? i - 1 : i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Source position of the instruction at <paramref name="offset"/> bytes into <paramref name="frameMethod"/>.
    /// Null when no function matches, or when several do and they disagree (overloads).
    /// </summary>
    public (string File, int Line)? Lookup(string frameMethod, int offset)
    {
        if (!_bySuffix.TryGetValue(Mangle(frameMethod), out var candidates))
        {
            return null;
        }

        (string File, int Line)? found = null;
        foreach (var index in candidates.Distinct())
        {
            var hit = At(_functions[index], Math.Max(offset - 1, 0));
            if (hit is null || (found is not null && found != hit))
            {
                return null;
            }

            found = hit;
        }

        return found;
    }

    private (string File, int Line)? At(Function function, int offset)
    {
        var at = -1;
        for (var i = 0; i < function.Offset.Length && function.Offset[i] <= offset; i++)
        {
            at = i;
        }

        return at < 0 || function.Line[at] <= 0 ? null : (_files[function.File[at]], function.Line[at]);
    }

    public static NativeSymbols Parse(ReadOnlySpan<byte> json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json.ToArray());
            var root = doc.RootElement;
            if (root.GetProperty("version").GetInt32() != 1)
            {
                throw new FormatException("Unsupported native symbols version.");
            }

            var files = root.GetProperty("files").EnumerateArray().Select(f => f.GetString() ?? "").ToArray();
            var functions = new List<Function>();
            foreach (var f in root.GetProperty("functions").EnumerateArray())
            {
                var rows = f.GetProperty("r");
                var offset = new int[rows.GetArrayLength()];
                var line = new int[offset.Length];
                var file = new int[offset.Length];
                var i = 0;
                foreach (var r in rows.EnumerateArray())
                {
                    offset[i] = r[0].GetInt32();
                    line[i] = r[1].GetInt32();
                    file[i] = r[2].GetInt32();
                    if (file[i] < 0 || file[i] >= files.Length)
                    {
                        throw new FormatException("Row refers to an unknown file.");
                    }

                    i++;
                }

                functions.Add(new Function(f.GetProperty("n").GetString()!, offset, line, file));
            }

            return new NativeSymbols(root.GetProperty("uuid").GetString()!.ToLowerInvariant(), files, [.. functions]);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or IndexOutOfRangeException)
        {
            throw new FormatException("Not a valid Flare native symbols file: " + ex.Message, ex);
        }
    }

    internal static byte[] Serialize(string uuid, IReadOnlyList<string> files, IEnumerable<(string Symbol, (int Offset, int Line, int File)[] Rows)> functions)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteNumber("version", 1);
            w.WriteString("uuid", uuid);
            w.WriteStartArray("files");
            foreach (var f in files)
            {
                w.WriteStringValue(f);
            }

            w.WriteEndArray();
            w.WriteStartArray("functions");
            foreach (var (symbol, rows) in functions)
            {
                w.WriteStartObject();
                w.WriteString("n", symbol);
                w.WriteStartArray("r");
                foreach (var (offset, line, file) in rows)
                {
                    w.WriteStartArray();
                    w.WriteNumberValue(offset);
                    w.WriteNumberValue(line);
                    w.WriteNumberValue(file);
                    w.WriteEndArray();
                }

                w.WriteEndArray();
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return stream.ToArray();
    }
}
