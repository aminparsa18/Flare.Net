using System.Text.Json;

namespace Flare.Mcp.DotnetSymbols;

/// <summary>
/// Compact per-assembly method-to-source table for symbolicating release .NET stack traces
/// (Mono-style frames from trimmed/AOT MAUI builds, which carry an IL offset and the assembly MVID
/// but no file or line). Built from a dll and its portable PDB by <see cref="DotnetSymbolsBuilder"/>
/// at upload time, so the server stores a small JSON file instead of the PDB and never needs the
/// assembly. See ADR-0168.
/// </summary>
public sealed class DotnetSymbols
{
    /// <summary>Suffix of the stored "bundle" name: <c>{mvid}.dotnet.json</c>.</summary>
    public const string BundleSuffix = ".dotnet.json";

    private readonly Dictionary<string, List<Method>> _methods;
    private readonly string[] _documents;

    private DotnetSymbols(Guid mvid, string[] documents, Dictionary<string, List<Method>> methods)
    {
        Mvid = mvid;
        _documents = documents;
        _methods = methods;
    }

    public Guid Mvid { get; }

    public static string BundleName(Guid mvid) => mvid.ToString("N") + BundleSuffix;

    internal sealed record Method(string[] Params, int[] Il, int[] Line, int[] Doc);

    /// <summary>
    /// Source position of <paramref name="ilOffset"/> in <paramref name="methodName"/> (<c>Ns.Type+Nested.Method</c>).
    /// Overloads are told apart by parameter names; when the names don't match, a method is still
    /// resolved if it is the only one of that name with that many parameters. Ambiguity returns null.
    /// </summary>
    public (string File, int Line)? Lookup(string methodName, IReadOnlyList<string> parameterNames, int ilOffset)
    {
        if (!_methods.TryGetValue(methodName, out var candidates))
        {
            return null;
        }

        var matches = candidates.Where(m => m.Params.AsSpan().SequenceEqual(parameterNames.ToArray())).ToList();
        if (matches.Count == 0)
        {
            matches = [.. candidates.Where(m => m.Params.Length == parameterNames.Count)];
        }

        if (matches.Count != 1)
        {
            return null;
        }

        var method = matches[0];
        var at = -1;
        for (var i = 0; i < method.Il.Length && method.Il[i] <= ilOffset; i++)
        {
            at = i;
        }

        return at < 0 ? null : (_documents[method.Doc[at]], method.Line[at]);
    }

    public static DotnetSymbols Parse(ReadOnlySpan<byte> json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json.ToArray());
            var root = doc.RootElement;
            if (root.GetProperty("version").GetInt32() != 1)
            {
                throw new FormatException("Unsupported .NET symbols version.");
            }

            var mvid = Guid.Parse(root.GetProperty("mvid").GetString()!);
            var documents = root.GetProperty("documents").EnumerateArray().Select(d => d.GetString() ?? "").ToArray();
            var methods = new Dictionary<string, List<Method>>(StringComparer.Ordinal);
            foreach (var m in root.GetProperty("methods").EnumerateArray())
            {
                var points = m.GetProperty("s");
                var count = points.GetArrayLength();
                var il = new int[count];
                var line = new int[count];
                var docIndex = new int[count];
                var i = 0;
                foreach (var p in points.EnumerateArray())
                {
                    il[i] = p[0].GetInt32();
                    line[i] = p[1].GetInt32();
                    docIndex[i] = p[2].GetInt32();
                    if (docIndex[i] < 0 || docIndex[i] >= documents.Length)
                    {
                        throw new FormatException("Sequence point refers to an unknown document.");
                    }

                    i++;
                }

                var name = m.GetProperty("n").GetString()!;
                var parameters = m.GetProperty("p").EnumerateArray().Select(p => p.GetString() ?? "").ToArray();
                if (!methods.TryGetValue(name, out var list))
                {
                    methods[name] = list = [];
                }

                list.Add(new Method(parameters, il, line, docIndex));
            }

            return new DotnetSymbols(mvid, documents, methods);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or IndexOutOfRangeException)
        {
            throw new FormatException("Not a valid Flare .NET symbols file: " + ex.Message, ex);
        }
    }

    internal static byte[] Serialize(Guid mvid, IReadOnlyList<string> documents, IEnumerable<(string Name, string[] Params, (int Il, int Line, int Doc)[] Points)> methods)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteNumber("version", 1);
            w.WriteString("mvid", mvid.ToString("D"));
            w.WriteStartArray("documents");
            foreach (var d in documents)
            {
                w.WriteStringValue(d);
            }

            w.WriteEndArray();
            w.WriteStartArray("methods");
            foreach (var (name, parameters, points) in methods)
            {
                w.WriteStartObject();
                w.WriteString("n", name);
                w.WriteStartArray("p");
                foreach (var p in parameters)
                {
                    w.WriteStringValue(p);
                }

                w.WriteEndArray();
                w.WriteStartArray("s");
                foreach (var (il, line, doc) in points)
                {
                    w.WriteStartArray();
                    w.WriteNumberValue(il);
                    w.WriteNumberValue(line);
                    w.WriteNumberValue(doc);
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
