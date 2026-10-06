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
}
