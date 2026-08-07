using System.Globalization;
using System.Text;

namespace Flare.Api.Query;

/// <summary>
/// Keyset-pagination cursor for <c>/api/logs/search</c>: <c>(Timestamp, EventId)</c>,
/// the same tuple the query's <c>ORDER BY Timestamp DESC, EventId DESC</c> sorts by.
/// <c>EventId</c> (added by <c>db/clickhouse/0002_logs_event_id.sql</c>) is the
/// tiebreaker for rows that share an exact <see cref="Timestamp"/> - see that
/// migration's comments for why <c>TraceId</c> alone wasn't a safe enough tiebreaker.
/// </summary>
/// <remarks>
/// Opaque to callers by design (a base64-encoded internal format, not a stable public
/// contract) - encodes as <c>"{ticks}|{eventId:N}"</c>. Deliberately not JSON/a public
/// DTO: nothing outside this project needs to construct or inspect a cursor, only round-trip it.
/// </remarks>
public readonly record struct LogSearchCursor(DateTimeOffset Timestamp, Guid EventId)
{
    public string Encode() =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Timestamp.UtcTicks}|{EventId:N}"));

    /// <summary>Decodes a cursor previously returned by <see cref="Encode"/>. Returns <see langword="null"/> for a missing/malformed cursor.</summary>
    public static LogSearchCursor? TryDecode(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
        {
            return null;
        }

        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = raw.Split('|');
            if (parts.Length != 2)
            {
                return null;
            }

            var ticks = long.Parse(parts[0], CultureInfo.InvariantCulture);
            var eventId = Guid.ParseExact(parts[1], "N");
            return new LogSearchCursor(new DateTimeOffset(ticks, TimeSpan.Zero), eventId);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            // Malformed/tampered cursor from a caller - treat as "no cursor" (first
            // page) rather than failing the request; this is pagination convenience
            // state, not a security boundary.
            return null;
        }
    }
}
