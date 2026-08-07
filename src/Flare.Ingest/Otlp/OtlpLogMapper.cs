using System.Globalization;
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
    public static IEnumerable<LogEvent> Map(ExportLogsServiceRequest request)
    {
        foreach (var resourceLogs in request.ResourceLogs)
        {
            var resourceAttributes = Flatten(resourceLogs.Resource?.Attributes);
            var serviceName = resourceAttributes.GetValueOrDefault("service.name");
            var resourceSchemaUrl = EmptyToNull(resourceLogs.SchemaUrl);

            foreach (var scopeLogs in resourceLogs.ScopeLogs)
            {
                var scopeAttributes = Flatten(scopeLogs.Scope?.Attributes);
                var scopeSchemaUrl = EmptyToNull(scopeLogs.SchemaUrl);

                foreach (var record in scopeLogs.LogRecords)
                {
                    yield return new LogEvent
                    {
                        Timestamp = FromUnixNano(record.TimeUnixNano != 0 ? record.TimeUnixNano : record.ObservedTimeUnixNano),
                        ObservedTimestamp = record.ObservedTimeUnixNano != 0 ? FromUnixNano(record.ObservedTimeUnixNano) : null,
                        SeverityNumber = (int)record.SeverityNumber,
                        SeverityText = EmptyToNull(record.SeverityText),
                        Body = AnyValueToString(record.Body),
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
                        LogAttributes = Flatten(record.Attributes),
                        EventName = EmptyToNull(record.EventName),
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

    private static Dictionary<string, string> Flatten(IEnumerable<KeyValue>? attributes)
    {
        var result = new Dictionary<string, string>();
        if (attributes is null)
        {
            return result;
        }

        foreach (var kv in attributes)
        {
            var value = AnyValueToString(kv.Value);
            if (value is not null)
            {
                result[kv.Key] = value;
            }
        }

        return result;
    }

    private static string? AnyValueToString(AnyValue? value) => value?.ValueCase switch
    {
        AnyValue.ValueOneofCase.StringValue => value.StringValue,
        AnyValue.ValueOneofCase.BoolValue => value.BoolValue ? "true" : "false",
        AnyValue.ValueOneofCase.IntValue => value.IntValue.ToString(CultureInfo.InvariantCulture),
        AnyValue.ValueOneofCase.DoubleValue => value.DoubleValue.ToString(CultureInfo.InvariantCulture),
        AnyValue.ValueOneofCase.BytesValue => Convert.ToBase64String(value.BytesValue.Span),
        AnyValue.ValueOneofCase.ArrayValue => "[" + string.Join(",", value.ArrayValue.Values.Select(AnyValueToString)) + "]",
        AnyValue.ValueOneofCase.KvlistValue => "{" + string.Join(",", value.KvlistValue.Values.Select(kv => $"{kv.Key}={AnyValueToString(kv.Value)}")) + "}",
        _ => null,
    };
}
