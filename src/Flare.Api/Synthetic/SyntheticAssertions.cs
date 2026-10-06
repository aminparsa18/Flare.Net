using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Flare.Api.Synthetic;

/// <summary>
/// Regex and JSON-path response assertions for HTTP monitors - pure. Regexes use the non-backtracking engine
/// (linear time, so a pattern cannot stall the alert worker); lookarounds and backreferences are rejected.
/// See <c>docs-internal/adr/0133-synthetic-regex-jsonpath-assertions.md</c>.
/// </summary>
public static class SyntheticAssertions
{
    public static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    /// <summary>The error for an unusable regex <paramref name="pattern"/>, else null.</summary>
    public static string? ValidateRegex(string? pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return null;
        }

        try
        {
            _ = new Regex(pattern, RegexOptions.NonBacktracking, RegexTimeout);
            return null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return $"bodyMatchesRegex is not a supported pattern: {ex.Message}";
        }
    }

    /// <summary>The error for an unusable JSON <paramref name="path"/>, else null.</summary>
    public static string? ValidateJsonPath(string? path) =>
        string.IsNullOrEmpty(path) || TryParsePath(path, out _) ? null
            : "jsonPath must look like $.data.items[0].status or $['key'].";

    /// <summary>The failure reason when <paramref name="body"/> breaks the regex or JSON-path assertion, else null.</summary>
    public static string? Error(string body, string regex, string jsonPath, string jsonPathEquals)
    {
        if (regex.Length > 0)
        {
            try
            {
                if (!new Regex(regex, RegexOptions.NonBacktracking, RegexTimeout).IsMatch(body))
                {
                    return "response body does not match the expected pattern";
                }
            }
            catch (RegexMatchTimeoutException)
            {
                return "response body pattern match timed out";
            }
        }

        return jsonPath.Length > 0 ? JsonPathError(body, jsonPath, jsonPathEquals) : null;
    }

    private static string? JsonPathError(string body, string path, string expected)
    {
        if (!TryParsePath(path, out var steps))
        {
            return "jsonPath is invalid";
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return "response body is not valid JSON";
        }

        using (doc)
        {
            var node = doc.RootElement;
            foreach (var step in steps)
            {
                if (step.Index is { } i)
                {
                    if (node.ValueKind != JsonValueKind.Array || i >= node.GetArrayLength())
                    {
                        return $"jsonPath {path} was not found";
                    }

                    node = node[i];
                }
                else if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(step.Name!, out node))
                {
                    return $"jsonPath {path} was not found";
                }
            }

            if (expected.Length == 0)
            {
                return null;
            }

            var actual = node.ValueKind == JsonValueKind.String ? node.GetString() : node.GetRawText();
            return string.Equals(actual, expected, StringComparison.Ordinal)
                ? null
                : $"jsonPath {path} is '{Truncate(actual)}', expected '{Truncate(expected)}'";
        }
    }

    private static string Truncate(string? s) => s is { Length: > 60 } ? s[..60] + "..." : s ?? "";

    private readonly record struct Step(string? Name, int? Index);

    /// <summary>Parses <c>$.a.b[0]['c d']</c> (the leading <c>$</c> is optional) into steps.</summary>
    private static bool TryParsePath(string path, out List<Step> steps)
    {
        steps = [];
        var i = path.StartsWith('$') ? 1 : 0;
        while (i < path.Length)
        {
            if (path[i] == '.')
            {
                var start = ++i;
                while (i < path.Length && path[i] is not ('.' or '['))
                {
                    i++;
                }

                if (i == start)
                {
                    return false;
                }

                steps.Add(new Step(path[start..i], null));
            }
            else if (path[i] == '[')
            {
                var close = path.IndexOf(']', i);
                if (close < 0)
                {
                    return false;
                }

                var inner = path[(i + 1)..close];
                if (int.TryParse(inner, out var index) && index >= 0)
                {
                    steps.Add(new Step(null, index));
                }
                else if (inner.Length >= 2 && inner[0] == inner[^1] && inner[0] is '\'' or '"')
                {
                    steps.Add(new Step(inner[1..^1], null));
                }
                else
                {
                    return false;
                }

                i = close + 1;
            }
            else if (steps.Count == 0 && i == (path.StartsWith('$') ? 1 : 0))
            {
                var start = i;
                while (i < path.Length && path[i] is not ('.' or '['))
                {
                    i++;
                }

                steps.Add(new Step(path[start..i], null));
            }
            else
            {
                return false;
            }
        }

        return steps.Count > 0;
    }
}
