using Flare.Api.Model;

namespace Flare.Api.LiveTail;

/// <summary>
/// Pure <see cref="BufferedLogEvent"/> → <see cref="LogEventDto"/> mapping for the
/// live-tail endpoint. Deliberately has no I/O dependency so it's unit-testable on its
/// own, same style as <c>Flare.Ingest.Pipeline.ClickHouseRowMapper</c>.
/// </summary>
/// <remarks>
/// Applies the identical null-coalescing/fallback conventions
/// <c>ClickHouseRowMapper.ToRow</c> uses when writing the same event to ClickHouse
/// (every nullable string → <see cref="string.Empty"/>,
/// <see cref="BufferedLogEvent.ObservedTimestamp"/> falls back to
/// <see cref="BufferedLogEvent.Timestamp"/>), so a live-tailed event and the same event
/// later returned by <c>/api/logs/search</c> render identically.
/// </remarks>
public static class BufferedLogEventMapper
{
    public static LogEventDto ToDto(BufferedLogEvent bufferedLogEvent) => new()
    {
        EventId = bufferedLogEvent.EventId,
        Timestamp = bufferedLogEvent.Timestamp,
        ObservedTimestamp = bufferedLogEvent.ObservedTimestamp ?? bufferedLogEvent.Timestamp,
        TraceId = bufferedLogEvent.TraceId ?? string.Empty,
        SpanId = bufferedLogEvent.SpanId ?? string.Empty,
        TraceFlags = bufferedLogEvent.TraceFlags,
        SeverityText = bufferedLogEvent.SeverityText ?? string.Empty,
        SeverityNumber = (byte)bufferedLogEvent.SeverityNumber,
        ServiceName = bufferedLogEvent.ServiceName ?? string.Empty,
        Body = bufferedLogEvent.Body ?? string.Empty,
        ResourceSchemaUrl = bufferedLogEvent.ResourceSchemaUrl ?? string.Empty,
        ResourceAttributes = bufferedLogEvent.ResourceAttributes,
        ScopeSchemaUrl = bufferedLogEvent.ScopeSchemaUrl ?? string.Empty,
        ScopeName = bufferedLogEvent.ScopeName ?? string.Empty,
        ScopeVersion = bufferedLogEvent.ScopeVersion ?? string.Empty,
        ScopeAttributes = bufferedLogEvent.ScopeAttributes,
        LogAttributes = bufferedLogEvent.LogAttributes,
        EventName = bufferedLogEvent.EventName ?? string.Empty,
        PatternId = bufferedLogEvent.PatternId ?? string.Empty,
        PatternTemplate = bufferedLogEvent.PatternTemplate ?? string.Empty,
    };
}
