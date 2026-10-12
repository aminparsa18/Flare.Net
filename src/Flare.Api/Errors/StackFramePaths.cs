using System.Text.RegularExpressions;

namespace Flare.Api.Errors;

/// <summary>
/// Pulls the source-file paths out of an <c>exception.stacktrace</c> so ownership rules can match
/// them (ADR-0196). Recognizes .NET (<c>in /src/File.cs:line 12</c>), Node (<c>(/src/f.js:10:5)</c>),
/// Python (<c>File "/src/f.py", line 3</c>) and Java/Kotlin (<c>(File.java:42)</c>, file name only).
/// A frame with no file information (framework code without symbols) contributes nothing.
/// </summary>
public static partial class StackFramePaths
{
    public const int MaxPaths = 40;

    [GeneratedRegex(@" in (?<p>.+?):line \d+", RegexOptions.CultureInvariant)]
    private static partial Regex DotNetFrame();

    [GeneratedRegex(@"File ""(?<p>[^""]+)"", line \d+", RegexOptions.CultureInvariant)]
    private static partial Regex PythonFrame();

    [GeneratedRegex(@"\(?(?<p>(?:[A-Za-z]:)?[^\s()]+\.[A-Za-z0-9]+):\d+:\d+\)?\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex NodeFrame();

    [GeneratedRegex(@"\((?<p>[\w$.-]+\.(?:java|kt|scala|groovy)):\d+\)", RegexOptions.CultureInvariant)]
    private static partial Regex JvmFrame();

    /// <summary>The distinct paths in frame order (top of the stack first), with <c>\</c> turned into <c>/</c>.</summary>
    public static IReadOnlyList<string> Extract(string? stackTrace)
    {
        if (string.IsNullOrWhiteSpace(stackTrace))
        {
            return [];
        }

        var paths = new List<string>();
        foreach (var line in stackTrace.Split('\n'))
        {
            var match = DotNetFrame().Match(line);
            if (!match.Success)
            {
                match = PythonFrame().Match(line);
            }

            if (!match.Success)
            {
                match = JvmFrame().Match(line);
            }

            if (!match.Success)
            {
                match = NodeFrame().Match(line.TrimEnd('\r'));
            }

            if (!match.Success)
            {
                continue;
            }

            var path = match.Groups["p"].Value.Trim().Replace('\\', '/');
            if (path.Length > 0 && !path.Contains("://", StringComparison.Ordinal) && !paths.Contains(path))
            {
                paths.Add(path);
                if (paths.Count == MaxPaths)
                {
                    break;
                }
            }
        }

        return paths;
    }
}
