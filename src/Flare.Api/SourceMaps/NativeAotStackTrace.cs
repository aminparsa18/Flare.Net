using System.Text;
using System.Text.RegularExpressions;

namespace Flare.Api.SourceMaps;

/// <summary>One Native AOT frame: <c>at Ns.Type.Method(String) + 0x48</c>, an offset from the function's entry.</summary>
public sealed record NativeAotFrame(string Method, string Arguments, int Offset);

/// <summary>
/// Finds and rewrites Native AOT frames (iOS/macOS builds with <c>PublishAot</c>), which carry no file or line.
/// A resolved frame is written in the CoreCLR form <c>at Ns.Type.Method(args) in File.cs:line 3</c>. See ADR-0168.
/// </summary>
public static partial class NativeAotStackTrace
{
    [GeneratedRegex(@"^(?<indent>\s*)at (?<name>.+?)\((?<args>[^()]*)\) \+ 0x(?<off>[0-9a-fA-F]+)\s*$")]
    private static partial Regex Frame();

    public static bool TryParseFrame(string line, out string indent, out NativeAotFrame frame)
    {
        var match = Frame().Match(line);
        if (!match.Success || !int.TryParse(match.Groups["off"].ValueSpan, System.Globalization.NumberStyles.HexNumber, null, out var offset))
        {
            indent = "";
            frame = null!;
            return false;
        }

        indent = match.Groups["indent"].Value;
        frame = new NativeAotFrame(match.Groups["name"].Value, match.Groups["args"].Value, offset);
        return true;
    }

    public static bool HasFrames(string stacktrace) =>
        stacktrace.Split('\n').Any(line => TryParseFrame(line.TrimEnd('\r'), out _, out _));

    public static (string Text, bool Changed) Rewrite(string stacktrace, Func<NativeAotFrame, (string File, int Line)?> resolve)
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
