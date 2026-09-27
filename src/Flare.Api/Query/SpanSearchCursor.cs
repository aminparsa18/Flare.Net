using System.Globalization;
using System.Text;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>
/// Keyset-pagination cursor for <c>/api/spans/search</c>: <c>(sort key, TraceId,
/// SpanId)</c>, the same tuple the query's <c>ORDER BY</c> sorts by - <c>StartTime</c> by
/// default, or <c>DurationNano</c>/the trace's span count under
/// <see cref="SpanSearchRequest.SortBy"/>. Unlike <see cref="LogSearchCursor"/> (which
/// needed a synthetic <c>EventId</c> tiebreaker because logs' <c>TraceId</c>/<c>SpanId</c>
/// are frequently absent), spans' own <c>(TraceId, SpanId)</c> is already spec-guaranteed
/// present and unique - no third synthetic column needed.
/// </summary>
/// <remarks>
/// Opaque to callers by design, same convention as <see cref="LogSearchCursor"/> -
/// encodes as <c>"{sortBy}|{a|d}|{value}|{traceId}|{spanId}"</c>, where <c>value</c> is
/// UTC ticks for <see cref="SpanSortKey.StartTime"/>, nanoseconds for
/// <see cref="SpanSortKey.Duration"/>, and a count for <see cref="SpanSortKey.SpanCount"/>.
/// The sort key and direction are carried so <see cref="SpanSearchQueryBuilder"/> can
/// ignore a cursor minted under a different sort than the request's (a stale page-2 token
/// after the user re-sorted) rather than compare a duration against a timestamp.
/// <c>TraceId</c>/<c>SpanId</c> are lower-hex and never contain <c>|</c>, so the naive
/// <see cref="string.Split(char[])"/> below is safe without escaping.
/// </remarks>
public readonly record struct SpanSearchCursor(SpanSortKey SortBy, bool Ascending, ulong Value, string TraceId, string SpanId)
{
    /// <summary>A default-sort (<c>StartTime DESC</c>) cursor.</summary>
    public SpanSearchCursor(DateTimeOffset startTime, string traceId, string spanId)
        : this(SpanSortKey.StartTime, false, (ulong)startTime.UtcTicks, traceId, spanId)
    {
    }

    /// <summary><see cref="Value"/> read back as a timestamp - only meaningful for <see cref="SpanSortKey.StartTime"/>.</summary>
    public DateTimeOffset StartTime => new((long)Value, TimeSpan.Zero);

    public string Encode() =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{(int)SortBy}|{(Ascending ? 'a' : 'd')}|{Value.ToString(CultureInfo.InvariantCulture)}|{TraceId}|{SpanId}"));

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
            if (parts.Length != 5 || parts[1] is not ("a" or "d"))
            {
                return null;
            }

            var sortBy = (SpanSortKey)int.Parse(parts[0], CultureInfo.InvariantCulture);
            if (!Enum.IsDefined(sortBy))
            {
                return null;
            }

            var value = ulong.Parse(parts[2], CultureInfo.InvariantCulture);
            return new SpanSearchCursor(sortBy, parts[1] == "a", value, parts[3], parts[4]);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            // Malformed/tampered cursor from a caller - treat as "no cursor" (first
            // page) rather than failing the request, same as LogSearchCursor.
            return null;
        }
    }
}
