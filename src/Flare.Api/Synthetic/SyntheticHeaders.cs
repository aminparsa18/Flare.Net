namespace Flare.Api.Synthetic;

/// <summary>Parsing for a monitor's <c>RequestHeaders</c> text (one <c>Name: value</c> per line) - pure.</summary>
public static class SyntheticHeaders
{
    public const int MaxHeaders = 20;
    public const int MaxLength = 4_000;

    /// <summary>The parsed headers, or an <see cref="Error"/> message when the text is invalid.</summary>
    public sealed record Result(IReadOnlyList<(string Name, string Value)> Headers, string? Error);

    public static Result Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new Result([], null);
        }

        if (text.Length > MaxLength)
        {
            return new Result([], $"requestHeaders must be at most {MaxLength} characters.");
        }

        var headers = new List<(string, string)>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var colon = line.IndexOf(':');
            var name = colon > 0 ? line[..colon].Trim() : "";
            if (name.Length == 0 || !name.All(IsTokenChar))
            {
                return new Result([], $"requestHeaders: '{line.Trim()}' is not a 'Name: value' header.");
            }

            var value = line[(colon + 1)..].Trim();
            if (value.Any(char.IsControl))
            {
                return new Result([], $"requestHeaders: the value of '{name}' contains a control character.");
            }

            headers.Add((name, value));
        }

        return headers.Count > MaxHeaders
            ? new Result([], $"requestHeaders allows at most {MaxHeaders} headers.")
            : new Result(headers, null);
    }

    // RFC 9110 token characters.
    private static bool IsTokenChar(char c) =>
        c is > ' ' and < (char)127 && "()<>@,;:\\\"/[]?={}".IndexOf(c) < 0;

    /// <summary>What every header value is replaced with in API responses.</summary>
    public const string Mask = "********";

    /// <summary>The text with every header value replaced by <see cref="Mask"/>, so secrets never leave the API.</summary>
    public static string MaskValues(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var lines = new List<string>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var colon = line.IndexOf(':');
            if (colon > 0)
            {
                lines.Add($"{line[..colon].Trim()}: {Mask}");
            }
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// Replaces each submitted <c>Name: ********</c> line with the stored value of the same header name (case-insensitive,
    /// in order), so editing a monitor without retyping its secrets keeps them.
    /// </summary>
    public static string RestoreMasked(string? submitted, string? stored)
    {
        if (string.IsNullOrWhiteSpace(submitted) || !submitted.Contains(Mask, StringComparison.Ordinal))
        {
            return submitted ?? "";
        }

        var remaining = Parse(stored).Headers.ToList();
        var lines = new List<string>();
        foreach (var raw in submitted.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var colon = line.IndexOf(':');
            if (colon > 0 && line[(colon + 1)..].Trim() == Mask)
            {
                var name = line[..colon].Trim();
                var index = remaining.FindIndex(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    line = $"{name}: {remaining[index].Value}";
                    remaining.RemoveAt(index);
                }
            }

            lines.Add(line);
        }

        return string.Join('\n', lines);
    }
}
