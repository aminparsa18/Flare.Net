namespace Flare.Identity.IngestKeys;

/// <summary>
/// Redis key naming for per-ingest-key usage counters (ADR-0051). Written by
/// <c>Flare.Ingest</c> (on every accepted OTLP export), read by both <c>Flare.Ingest</c>
/// (limit enforcement) and <c>Flare.Api</c> (usage shown on the Ingest Keys page). Lives
/// here rather than mirrored per project the way <c>IngestionStatsKeys</c> is, because
/// both processes already reference this assembly for the ingest-key store itself - one
/// definition means the writer and the two readers can't drift apart. Pure string
/// helpers, no Redis dependency.
/// </summary>
/// <remarks>
/// Fixed windows aligned to the UTC minute and UTC day, not sliding windows: a counter
/// per window is one <c>HINCRBY</c> to write and one <c>HMGET</c> to read, and "today"
/// meaning the UTC calendar day is what an operator reading a daily cap expects.
/// </remarks>
public static class IngestApiKeyUsageKeys
{
    private const string Prefix = "flare:ingest-key-usage:";

    public const string EventsField = "events";
    public const string BytesField = "bytes";

    /// <summary>Only the current minute is ever read, so a short TTL - just long enough
    /// to outlive the window plus clock skew between replicas.</summary>
    public static readonly TimeSpan MinuteTtl = TimeSpan.FromMinutes(2);

    /// <summary>A full day plus a buffer for replica clock skew around UTC midnight.</summary>
    public static readonly TimeSpan DayTtl = TimeSpan.FromHours(26);

    public static long EpochMinute(DateTimeOffset timestamp) => timestamp.ToUnixTimeSeconds() / 60;

    public static long EpochDay(DateTimeOffset timestamp) => timestamp.ToUnixTimeSeconds() / 86_400;

    public static string MinuteKey(Guid keyId, DateTimeOffset timestamp) =>
        $"{Prefix}{keyId:N}:m:{EpochMinute(timestamp)}";

    public static string DayKey(Guid keyId, DateTimeOffset timestamp) =>
        $"{Prefix}{keyId:N}:d:{EpochDay(timestamp)}";
}
