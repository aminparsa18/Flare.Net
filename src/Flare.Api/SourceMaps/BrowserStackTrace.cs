using System.Text;
using System.Text.RegularExpressions;

namespace Flare.Api.SourceMaps;

/// <summary>One frame found in a browser stack trace: generated URL plus the 1-based line and column the engine printed.</summary>
public sealed record BrowserFrame(string FunctionName, string Url, int Line, int Column);

/// <summary>
/// Finds and rewrites frames in browser stack traces. Handles V8 (<c>at fn (url:line:col)</c>) and
/// the Firefox/Safari form (<c>fn@url:line:col</c>); every other line is left untouched. A
/// rewritten frame is always written in the V8 form, which Flare's stack-trace viewer already
/// understands. See ADR-0152.
/// </summary>
public static partial class BrowserStackTrace
{
    [GeneratedRegex(@"^(?<indent>\s*)at (?:(?<fn>.+?) \()?(?<url>[^\s()]+?):(?<line>\d+):(?<col>\d+)\)?\s*$")]
    private static partial Regex V8Frame();

    [GeneratedRegex(@"^(?<indent>\s*)(?<fn>[^@\s]*)@(?<url>\S+?):(?<line>\d+):(?<col>\d+)\s*$")]
    private static partial Regex GeckoFrame();

    public static bool TryParseFrame(string line, out string indent, out BrowserFrame frame)
    {
        var match = V8Frame().Match(line);
        if (!match.Success)
        {
            match = GeckoFrame().Match(line);
        }

        if (!match.Success || !int.TryParse(match.Groups["line"].ValueSpan, out var l) || !int.TryParse(match.Groups["col"].ValueSpan, out var c))
        {
            indent = "";
            frame = null!;
            return false;
        }

        indent = match.Groups["indent"].Value;
        frame = new BrowserFrame(match.Groups["fn"].Value, match.Groups["url"].Value, l, c);
        return true;
    }

    /// <summary>
    /// Rewrites every frame <paramref name="resolve"/> returns a position for. Returns the new text and
    /// whether any frame changed; the input is returned as-is when nothing resolved.
    /// </summary>
    /// <remarks>
    /// A map's <c>names</c> entry at a frame's position is the identifier written there. For every frame but
    /// the first that is a call site, so it names the function of the frame <em>above</em> (the callee). A
    /// frame's function name is therefore taken from the next frame's mapped name; without one it keeps the
    /// name the engine printed (usually minified).
    /// </remarks>
    public static (string Text, bool Changed) Rewrite(string stacktrace, Func<BrowserFrame, OriginalPosition?> resolve)
    {
        var lines = stacktrace.Split('\n');
        var parsed = new (string Indent, BrowserFrame Frame, OriginalPosition? Original)?[lines.Length];
        var frameIndexes = new List<int>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (BrowserStackTrace.TryParseFrame(lines[i].TrimEnd('\r'), out var indent, out var frame))
            {
                parsed[i] = (indent, frame, resolve(frame));
                frameIndexes.Add(i);
            }
        }

        var builder = new StringBuilder(stacktrace.Length);
        var changed = false;
        for (var i = 0; i < lines.Length; i++)
        {
            if (parsed[i] is { Original: { } original } entry)
            {
                var next = frameIndexes.IndexOf(i) is var at && at + 1 < frameIndexes.Count ? parsed[frameIndexes[at + 1]] : null;
                var name = next?.Original?.Name ?? (entry.Frame.FunctionName.Length > 0 ? entry.Frame.FunctionName : null);
                var location = $"{original.Source}:{original.Line + 1}:{original.Column + 1}";
                builder.Append(entry.Indent).Append("at ").Append(name is null ? location : $"{name} ({location})");
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
