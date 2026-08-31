using System.Globalization;
using Flare.Ingest.Model;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Trace.V1;

namespace Flare.Ingest.Otlp;

/// <summary>
/// Maps parsed OTLP <see cref="ExportTraceServiceRequest"/> messages into Flare's
/// internal <see cref="SpanRecord"/> model. Shared by both the gRPC and HTTP receivers,
/// same shape as <see cref="OtlpLogMapper"/>.
/// </summary>
/// <remarks>
/// Deliberately duplicates <see cref="OtlpLogMapper"/>'s attribute-flattening helpers
/// (<c>EmptyToNull</c>/<c>AnyValueToString</c>/<c>Flatten</c>) rather than extracting a
/// shared utility - same "duplicate now, extract only on a third instance" call made for
/// the flush-worker pipeline (see <c>Pipeline/SpanFlushWorker.cs</c>'s remarks).
/// </remarks>
public static class OtlpTraceMapper
{
    /// <param name="request">The parsed OTLP export request.</param>
    /// <param name="ingestedAt">
    /// <c>Flare.Ingest</c>'s own wall-clock read at the moment this request was
    /// received - see <see cref="OtlpLogMapper.Map"/>'s remarks for why this is a
    /// parameter rather than read from a clock here.
    /// </param>
    public static IEnumerable<SpanRecord> Map(ExportTraceServiceRequest request, DateTimeOffset ingestedAt)
    {
        foreach (var resourceSpans in request.ResourceSpans)
        {
            var resourceAttributes = Flatten(resourceSpans.Resource?.Attributes);
            var serviceName = resourceAttributes.GetValueOrDefault("service.name");
            var resourceSchemaUrl = EmptyToNull(resourceSpans.SchemaUrl);

            foreach (var scopeSpans in resourceSpans.ScopeSpans)
            {
                var scopeAttributes = Flatten(scopeSpans.Scope?.Attributes);
                var scopeSchemaUrl = EmptyToNull(scopeSpans.SchemaUrl);

                foreach (var span in scopeSpans.Spans)
                {
                    yield return new SpanRecord
                    {
                        TraceId = Convert.ToHexStringLower(span.TraceId.Span),
                        SpanId = Convert.ToHexStringLower(span.SpanId.Span),
                        ParentSpanId = span.ParentSpanId.IsEmpty ? null : Convert.ToHexStringLower(span.ParentSpanId.Span),
                        TraceState = EmptyToNull(span.TraceState),
                        Name = EmptyToNull(span.Name),
                        Kind = (int)span.Kind,
                        StartTime = FromUnixNano(span.StartTimeUnixNano),
                        EndTime = FromUnixNano(span.EndTimeUnixNano),
                        // Computed from the raw wire nanoseconds, not from StartTime/EndTime -
                        // those round-trip through DateTimeOffset's 100ns tick resolution
                        // (see FromUnixNano), which would introduce avoidable rounding into a
                        // duration figure the UI sorts/filters by.
                        DurationNano = span.EndTimeUnixNano - span.StartTimeUnixNano,
                        StatusCode = (int)(span.Status?.Code ?? Status.Types.StatusCode.Unset),
                        StatusMessage = EmptyToNull(span.Status?.Message),
                        ServiceName = serviceName,
                        ResourceSchemaUrl = resourceSchemaUrl,
                        ResourceAttributes = resourceAttributes,
                        ScopeSchemaUrl = scopeSchemaUrl,
                        ScopeName = EmptyToNull(scopeSpans.Scope?.Name),
                        ScopeVersion = EmptyToNull(scopeSpans.Scope?.Version),
                        ScopeAttributes = scopeAttributes,
                        SpanAttributes = Flatten(span.Attributes),
                        Events = [.. span.Events.Select(MapEvent)],
                        IngestedAt = ingestedAt,
                    };
                }
            }
        }
    }

    private static SpanEvent MapEvent(Span.Types.Event evt) => new()
    {
        Timestamp = FromUnixNano(evt.TimeUnixNano),
        Name = EmptyToNull(evt.Name),
        Attributes = Flatten(evt.Attributes),
    };

    private static DateTimeOffset FromUnixNano(ulong unixNano) =>
        DateTimeOffset.UnixEpoch.AddTicks((long)(unixNano / 100));

    /// <summary>
    /// Proto3 string fields default to <c>""</c> when unset on the wire - there's no way
    /// to distinguish "unset" from "explicitly empty" at the protobuf level. Flare treats
    /// both as "absent" for every nullable string field on <see cref="SpanRecord"/>.
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
