using System.Globalization;
using System.Text;

namespace Flare.Api.Query;

/// <summary>
/// Keyset-pagination cursor for <c>/api/spans/search</c>: <c>(StartTime, TraceId,
/// SpanId)</c>, the same tuple the query's <c>ORDER BY StartTime DESC, TraceId DESC,
/// SpanId DESC</c> sorts by. Unlike <see cref="LogSearchCursor"/> (which needed a
/// synthetic <c>EventId</c> tiebreaker because logs' <c>TraceId</c>/<c>SpanId</c> are
/// frequently absent), spans' own <c>(TraceId, SpanId)</c> is already spec-guaranteed
/// present and unique - no third synthetic column needed, just one more tuple element
/// than logs' cursor.
/// </summary>
/// <remarks>
/// Opaque to callers by design, same convention as <see cref="LogSearchCursor"/> -
/// encodes as <c>"{ticks}|{traceId}|{spanId}"</c>. <c>TraceId</c>/<c>SpanId</c> are
/// lower-hex and never contain <c>|</c>, so the naive <see cref="string.Split(char[])"/>
/// below is safe without escaping.
/// </remarks>
public readonly record struct SpanSearchCursor(DateTimeOffset StartTime, string TraceId, string SpanId)
{
    public string Encode() =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{StartTime.UtcTicks}|{TraceId}|{SpanId}"));

    /// <summary>Decodes a cursor previously returned by <see cref="Encode"/>. Returns <see langword="null"/> for a missing/malformed cursor.</summary>
    public static SpanSearchCursor? TryDecode(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
        {
            return null;
        }

        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = raw.Split('|');
            if (parts.Length != 3)
            {
                return null;
            }

            var ticks = long.Parse(parts[0], CultureInfo.InvariantCulture);
            return new SpanSearchCursor(new DateTimeOffset(ticks, TimeSpan.Zero), parts[1], parts[2]);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            // Malformed/tampered cursor from a caller - treat as "no cursor" (first
            // page) rather than failing the request, same as LogSearchCursor.
            return null;
        }
    }
}
