using Flare.Ingest.Model;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Common.V1;

namespace Flare.Ingest.Otlp;

/// <summary>
/// Maps parsed OTLP <see cref="ExportLogsServiceRequest"/> messages into Flare's internal
/// <see cref="LogEvent"/> model. Shared by both the gRPC and HTTP receivers so every
/// transport converges on identical output.
/// </summary>
public static class OtlpLogMapper
{
    /// <param name="request">The parsed OTLP export request.</param>
    /// <param name="ingestedAt">
    /// <c>Flare.Ingest</c>'s own wall-clock read at the moment this request was
    /// received (see <see cref="LogEvent.IngestedAt"/>'s remarks) - passed in rather
    /// than read from a clock here so this mapper stays pure/deterministic and
    /// unit-testable without mocking time, same as every other field it derives from
    /// <paramref name="request"/> alone.
    /// </param>
    public static IEnumerable<LogEvent> Map(ExportLogsServiceRequest request, DateTimeOffset ingestedAt)
    {
        foreach (var resourceLogs in request.ResourceLogs)
        {
            var resourceAttributes = OtlpAnyValue.Flatten(resourceLogs.Resource?.Attributes);
            var serviceName = resourceAttributes.GetValueOrDefault("service.name");
            var resourceSchemaUrl = EmptyToNull(resourceLogs.SchemaUrl);

            foreach (var scopeLogs in resourceLogs.ScopeLogs)
            {
                var scopeAttributes = OtlpAnyValue.Flatten(scopeLogs.Scope?.Attributes);
                var scopeSchemaUrl = EmptyToNull(scopeLogs.SchemaUrl);

                foreach (var record in scopeLogs.LogRecords)
                {
                    yield return new LogEvent
                    {
                        EventId = Guid.NewGuid(),
                        // Event time, else observed time, else receipt time - the OTLP logs
                        // data model has receivers treat an unset observed time as "now".
                        // Without the last fallback a record with neither stamped 1970, where
                        // search's default lookback never finds it and ClockSkew saw ~56 years.
                        Timestamp = record.TimeUnixNano != 0 ? FromUnixNano(record.TimeUnixNano)
                            : record.ObservedTimeUnixNano != 0 ? FromUnixNano(record.ObservedTimeUnixNano)
                            : ingestedAt,
                        ObservedTimestamp = record.ObservedTimeUnixNano != 0 ? FromUnixNano(record.ObservedTimeUnixNano) : null,
                        SeverityNumber = (int)record.SeverityNumber,
                        SeverityText = EmptyToNull(record.SeverityText),
                        Body = OtlpAnyValue.ToFlatString(record.Body),
                        TraceId = record.TraceId.IsEmpty ? null : Convert.ToHexStringLower(record.TraceId.Span),
                        SpanId = record.SpanId.IsEmpty ? null : Convert.ToHexStringLower(record.SpanId.Span),
                        TraceFlags = (byte)(record.Flags & 0xFF),
                        ServiceName = serviceName,
                        ResourceSchemaUrl = resourceSchemaUrl,
                        ResourceAttributes = resourceAttributes,
                        ScopeSchemaUrl = scopeSchemaUrl,
                        ScopeName = EmptyToNull(scopeLogs.Scope?.Name),
                        ScopeVersion = EmptyToNull(scopeLogs.Scope?.Version),
                        ScopeAttributes = scopeAttributes,
                        LogAttributes = OtlpAnyValue.Flatten(record.Attributes),
                        EventName = EmptyToNull(record.EventName),
                        IngestedAt = ingestedAt,
                    };
                }
            }
        }
    }

    private static DateTimeOffset FromUnixNano(ulong unixNano) =>
        DateTimeOffset.UnixEpoch.AddTicks((long)(unixNano / 100));

    /// <summary>
    /// Proto3 string fields default to <c>""</c> when unset on the wire - there's no way
    /// to distinguish "unset" from "explicitly empty" at the protobuf level. Flare treats
    /// both as "absent" for every nullable string field on <see cref="LogEvent"/>.
    /// </summary>
    private static string? EmptyToNull(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
