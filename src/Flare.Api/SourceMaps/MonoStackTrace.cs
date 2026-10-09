using System.Text;
using System.Text.RegularExpressions;

namespace Flare.Api.SourceMaps;

/// <summary>One Mono-runtime frame without debug info: <c>at Ns.Type.Method (System.String s) [0x0001a] in &lt;mvid&gt;:0</c>.</summary>
public sealed record MonoFrame(string Method, string Arguments, IReadOnlyList<string> ParameterNames, int IlOffset, Guid Mvid);

/// <summary>
/// Finds and rewrites the frames a trimmed or AOT-compiled Mono build (MAUI on Android/iOS) prints
/// when no PDB is present. A resolved frame is written in the CoreCLR form
/// (<c>at Ns.Type.Method(args) in File.cs:line 23</c>), which the stack-trace viewer understands. See ADR-0168.
/// </summary>
public static partial class MonoStackTrace
{
    // The runtime may append "#aotid" to the MVID for AOT images; only the MVID matters.
    [GeneratedRegex(@"^(?<indent>\s*)at (?<name>\S+?) \((?<args>.*)\) \[0x(?<il>[0-9a-fA-F]+)\] in <(?<mvid>[0-9a-fA-F]{32})(?:#[0-9a-fA-F]+)?>:0\s*$")]
    private static partial Regex Frame();

    [GeneratedRegex(@"\[[^\[\]]*\]")]
    private static partial Regex GenericArguments();

    public static bool TryParseFrame(string line, out string indent, out MonoFrame frame)
    {
        var match = Frame().Match(line);
        if (!match.Success
            || !int.TryParse(match.Groups["il"].ValueSpan, System.Globalization.NumberStyles.HexNumber, null, out var il)
            || !Guid.TryParseExact(match.Groups["mvid"].Value, "N", out var mvid))
        {
            indent = "";
            frame = null!;
            return false;
        }

        indent = match.Groups["indent"].Value;
        var args = match.Groups["args"].Value;
        frame = new MonoFrame(GenericArguments().Replace(match.Groups["name"].Value, ""), args, ParameterNames(args), il, mvid);
        return true;
    }

    /// <summary>The trailing identifier of each top-level, comma-separated argument (commas inside <c>[]</c> belong to generic types).</summary>
    internal static IReadOnlyList<string> ParameterNames(string arguments)
    {
        var names = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i <= arguments.Length; i++)
        {
            if (i < arguments.Length)
            {
                var c = arguments[i];
                if (c is '[' or '<' or '(')
                {
                    depth++;
                }
                else if (c is ']' or '>' or ')')
                {
                    depth--;
                }

                if (c != ',' || depth > 0)
                {
                    continue;
                }
            }

            var part = arguments[start..i].Trim();
            if (part.Length > 0)
            {
                names.Add(part[(part.LastIndexOf(' ') + 1)..]);
            }

            start = i + 1;
        }

        return names;
    }

    public static IEnumerable<MonoFrame> Frames(string stacktrace)
    {
        foreach (var line in stacktrace.Split('\n'))
        {
            if (TryParseFrame(line.TrimEnd('\r'), out _, out var frame))
            {
                yield return frame;
            }
        }
    }

    public static (string Text, bool Changed) Rewrite(string stacktrace, Func<MonoFrame, (string File, int Line)?> resolve)
    {
        var lines = stacktrace.Split('\n');
        var builder = new StringBuilder(stacktrace.Length);
        var changed = false;
        for (var i = 0; i < lines.Length; i++)
        {
            if (TryParseFrame(lines[i].TrimEnd('\r'), out var indent, out var frame) && resolve(frame) is var (file, line))
            {
                builder.Append(indent).Append("at ").Append(frame.Method).Append('(').Append(frame.Arguments).Append(") in ")
                    .Append(file).Append(":line ").Append(line);
                changed = true;
            }
            else
            {
                builder.Append(lines[i]);
            }

            if (i < lines.Length - 1)
            {
                builder.Append('\n');
            }
        }

        return changed ? (builder.ToString(), true) : (stacktrace, false);
    }
}
