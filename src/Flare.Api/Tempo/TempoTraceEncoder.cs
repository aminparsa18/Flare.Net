using System.Buffers;
using System.Text.Json;
using Flare.Api.Model;

namespace Flare.Api.Tempo;

/// <summary>
/// Writes a trace's spans back out as OTLP <c>ResourceSpans</c>, the shape Tempo's trace-by-id API
/// returns: <c>tempopb.Trace</c> is wire-compatible with OTLP <c>TracesData</c> (a repeated
/// <c>ResourceSpans</c> as field 1). Grafana's Tempo data source asks for
/// <c>application/protobuf</c>; everything else gets OTLP-style JSON.
/// </summary>
/// <remarks>
/// The protobuf is written by hand (<see cref="ProtoWriter"/>) rather than from generated types: the
/// OTLP <c>.proto</c> files are vendored into <c>Flare.Ingest</c>, and this project deliberately does not
/// share types across the service boundary (see CLAUDE.md). Only the fields Flare stores are written -
/// no dropped-attribute counts, no flags. Attribute values are strings because the <c>spans</c> table
/// keeps every attribute as one.
/// </remarks>
public static class TempoTraceEncoder
{
    // OTLP field numbers (opentelemetry/proto/trace/v1/trace.proto, common.proto, resource.proto).
    private const int TracesDataResourceSpans = 1;

    private static readonly string[] KindNames =
    [
        "SPAN_KIND_UNSPECIFIED", "SPAN_KIND_INTERNAL", "SPAN_KIND_SERVER", "SPAN_KIND_CLIENT", "SPAN_KIND_PRODUCER", "SPAN_KIND_CONSUMER",
    ];

    /// <summary>The OTLP <c>TracesData</c> message: the bytes of tempopb.Trace.</summary>
    public static byte[] ToTraceProtobuf(IReadOnlyList<SpanDto> spans) => Encode(w => WriteTrace(w, spans));

    /// <summary>
    /// Tempo's v2 <c>TraceByIDResponse</c>: the trace as field 1, then the partial status
    /// (<c>PARTIAL</c> = 1, field 3) and message (field 4) when the trace was cut short.
    /// </summary>
    public static byte[] ToTraceByIdResponseProtobuf(IReadOnlyList<SpanDto> spans, string? partialMessage) => Encode(w =>
    {
        var trace = new ProtoWriter();
        WriteTrace(trace, spans);
        w.Message(1, inner => inner.Raw(trace));
        // Tempo always sends the metrics submessage; Grafana's data source dereferences it, so it must be present.
        w.Message(2, metrics => metrics.Varint(1, (ulong)trace.Length));
        if (partialMessage is not null)
        {
            w.Varint(3, 1);
            w.String(4, partialMessage);
        }
    });

    /// <summary>Tempo v1 JSON: <c>{"batches":[ResourceSpans...]}</c>.</summary>
    public static byte[] ToTraceJson(IReadOnlyList<SpanDto> spans) => WriteJson(w =>
    {
        w.WriteStartObject();
        w.WriteStartArray("batches");
        WriteResourceSpansJson(w, spans);
        w.WriteEndArray();
        w.WriteEndObject();
    });

    /// <summary>Tempo v2 JSON: <c>{"trace":{"resourceSpans":[...]},"status":"PARTIAL","message":"..."}</c>.</summary>
    public static byte[] ToTraceByIdResponseJson(IReadOnlyList<SpanDto> spans, string? partialMessage) => WriteJson(w =>
    {
        w.WriteStartObject();
        w.WriteStartObject("trace");
        w.WriteStartArray("resourceSpans");
        WriteResourceSpansJson(w, spans);
        w.WriteEndArray();
        w.WriteEndObject();
        w.WriteStartObject("metrics");
        w.WriteEndObject();
        if (partialMessage is not null)
        {
            w.WriteString("status", "PARTIAL");
            w.WriteString("message", partialMessage);
        }

        w.WriteEndObject();
    });

    /// <summary>Spans grouped by resource, then by instrumentation scope - the nesting OTLP puts them in.</summary>
    internal static List<(SpanDto Resource, List<(SpanDto Scope, List<SpanDto> Spans)> Scopes)> Group(IReadOnlyList<SpanDto> spans)
    {
        var result = new List<(SpanDto, List<(SpanDto, List<SpanDto>)>)>();
        var resources = new Dictionary<string, int>(StringComparer.Ordinal);
        var scopes = new Dictionary<(int, string), List<SpanDto>>();
        foreach (var span in spans)
        {
            var resourceKey = $"{span.ResourceSchemaUrl}\0{AttributesKey(span.ResourceAttributes)}";
            if (!resources.TryGetValue(resourceKey, out var resourceIndex))
            {
                resourceIndex = result.Count;
                resources[resourceKey] = resourceIndex;
                result.Add((span, []));
            }

            var scopeKey = (resourceIndex, $"{span.ScopeName}\0{span.ScopeVersion}\0{span.ScopeSchemaUrl}\0{AttributesKey(span.ScopeAttributes)}");
            if (!scopes.TryGetValue(scopeKey, out var group))
            {
                group = [];
                scopes[scopeKey] = group;
                result[resourceIndex].Item2.Add((span, group));
            }

            group.Add(span);
        }

        return result;
    }

    private static string AttributesKey(IReadOnlyDictionary<string, string> attributes) =>
        attributes.Count == 0
            ? ""
            : string.Join('\u0001', attributes.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}\u0002{kv.Value}"));

    private static void WriteTrace(ProtoWriter w, IReadOnlyList<SpanDto> spans)
    {
        foreach (var (resource, scopes) in Group(spans))
        {
            w.Message(TracesDataResourceSpans, rs =>
            {
                rs.Message(1, r => WriteAttributes(r, 1, resource.ResourceAttributes));
                foreach (var (scope, scopeSpans) in scopes)
                {
                    rs.Message(2, ss =>
                    {
                        ss.Message(1, s =>
                        {
                            s.String(1, scope.ScopeName);
                            s.String(2, scope.ScopeVersion);
                            WriteAttributes(s, 3, scope.ScopeAttributes);
                        });
                        foreach (var span in scopeSpans)
                        {
                            ss.Message(2, sp => WriteSpan(sp, span));
                        }

                        ss.String(3, scope.ScopeSchemaUrl);
                    });
                }

                rs.String(3, resource.ResourceSchemaUrl);
            });
        }
    }

    private static void WriteSpan(ProtoWriter w, SpanDto span)
    {
        w.Bytes(1, HexBytes(span.TraceId));
        w.Bytes(2, HexBytes(span.SpanId));
        w.String(3, span.TraceState);
        w.Bytes(4, HexBytes(span.ParentSpanId));
        w.String(5, span.Name);
        w.Varint(6, span.Kind);
        w.Fixed64(7, UnixNanos(span.StartTime));
        w.Fixed64(8, UnixNanos(span.EndTime));
        WriteAttributes(w, 9, span.SpanAttributes);
        foreach (var e in span.Events)
        {
            w.Message(11, ev =>
            {
                ev.Fixed64(1, UnixNanos(e.Timestamp));
                ev.String(2, e.Name);
                WriteAttributes(ev, 3, e.Attributes);
            });
        }

        foreach (var l in span.Links)
        {
            w.Message(13, link =>
            {
                link.Bytes(1, HexBytes(l.TraceId));
                link.Bytes(2, HexBytes(l.SpanId));
                link.String(3, l.TraceState);
                WriteAttributes(link, 4, l.Attributes);
            });
        }

        // Always present, even when empty: Tempo's Go types hold the status by value, and Grafana's data
        // source reads span.Status.Code without a nil check (an omitted status panicked its plugin).
        var status = StatusNumber(span.StatusCode);
        w.Message(15, st =>
        {
            st.String(2, span.StatusMessage);
            st.Varint(3, status);
        });
    }

    /// <summary>One <c>KeyValue { key = 1; AnyValue value = 2 { string_value = 1 } }</c> per attribute, in key order so output is stable.</summary>
    private static void WriteAttributes(ProtoWriter w, int field, IReadOnlyDictionary<string, string> attributes)
    {
        foreach (var (key, value) in attributes.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            w.Message(field, kv =>
            {
                kv.String(1, key);
                // An empty string value still has to be present, or the value reads as unset.
                kv.Message(2, any => any.StringAlways(1, value));
            });
        }
    }

    private static void WriteResourceSpansJson(Utf8JsonWriter w, IReadOnlyList<SpanDto> spans)
    {
        foreach (var (resource, scopes) in Group(spans))
        {
            w.WriteStartObject();
            w.WriteStartObject("resource");
            WriteAttributesJson(w, "attributes", resource.ResourceAttributes);
            w.WriteEndObject();
            w.WriteStartArray("scopeSpans");
            foreach (var (scope, scopeSpans) in scopes)
            {
                w.WriteStartObject();
                w.WriteStartObject("scope");
                w.WriteString("name", scope.ScopeName);
                w.WriteString("version", scope.ScopeVersion);
                WriteAttributesJson(w, "attributes", scope.ScopeAttributes);
                w.WriteEndObject();
                w.WriteStartArray("spans");
                foreach (var span in scopeSpans)
                {
                    WriteSpanJson(w, span);
                }

                w.WriteEndArray();
                WriteOptionalString(w, "schemaUrl", scope.ScopeSchemaUrl);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            WriteOptionalString(w, "schemaUrl", resource.ResourceSchemaUrl);
            w.WriteEndObject();
        }
    }

    private static void WriteSpanJson(Utf8JsonWriter w, SpanDto span)
    {
        w.WriteStartObject();
        w.WriteString("traceId", span.TraceId);
        w.WriteString("spanId", span.SpanId);
        WriteOptionalString(w, "parentSpanId", span.ParentSpanId);
        WriteOptionalString(w, "traceState", span.TraceState);
        w.WriteString("name", span.Name);
        w.WriteString("kind", span.Kind < KindNames.Length ? KindNames[span.Kind] : KindNames[0]);
        w.WriteString("startTimeUnixNano", UnixNanos(span.StartTime).ToString(System.Globalization.CultureInfo.InvariantCulture));
        w.WriteString("endTimeUnixNano", UnixNanos(span.EndTime).ToString(System.Globalization.CultureInfo.InvariantCulture));
        WriteAttributesJson(w, "attributes", span.SpanAttributes);
        if (span.Events.Count > 0)
        {
            w.WriteStartArray("events");
            foreach (var e in span.Events)
            {
                w.WriteStartObject();
                w.WriteString("timeUnixNano", UnixNanos(e.Timestamp).ToString(System.Globalization.CultureInfo.InvariantCulture));
                w.WriteString("name", e.Name);
                WriteAttributesJson(w, "attributes", e.Attributes);
                w.WriteEndObject();
            }

            w.WriteEndArray();
        }

        if (span.Links.Count > 0)
        {
            w.WriteStartArray("links");
            foreach (var l in span.Links)
            {
                w.WriteStartObject();
                w.WriteString("traceId", l.TraceId);
                w.WriteString("spanId", l.SpanId);
                WriteOptionalString(w, "traceState", l.TraceState);
                WriteAttributesJson(w, "attributes", l.Attributes);
                w.WriteEndObject();
            }

            w.WriteEndArray();
        }

        var status = StatusNumber(span.StatusCode);
        if (status != 0 || span.StatusMessage.Length > 0)
        {
            w.WriteStartObject("status");
            WriteOptionalString(w, "message", span.StatusMessage);
            w.WriteString("code", status switch { 1 => "STATUS_CODE_OK", 2 => "STATUS_CODE_ERROR", _ => "STATUS_CODE_UNSET" });
            w.WriteEndObject();
        }

        w.WriteEndObject();
    }

    private static void WriteAttributesJson(Utf8JsonWriter w, string property, IReadOnlyDictionary<string, string> attributes)
    {
        if (attributes.Count == 0)
        {
            return;
        }

        w.WriteStartArray(property);
        foreach (var (key, value) in attributes.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            w.WriteStartObject();
            w.WriteString("key", key);
            w.WriteStartObject("value");
            w.WriteString("stringValue", value);
            w.WriteEndObject();
            w.WriteEndObject();
        }

        w.WriteEndArray();
    }

    private static void WriteOptionalString(Utf8JsonWriter w, string property, string value)
    {
        if (value.Length > 0)
        {
            w.WriteString(property, value);
        }
    }

    internal static ulong UnixNanos(DateTimeOffset time)
    {
        var ticks = time.UtcTicks - DateTime.UnixEpoch.Ticks;
        return ticks <= 0 ? 0 : (ulong)ticks * 100;
    }

    private static ulong StatusNumber(string statusCode) => statusCode switch
    {
        "STATUS_CODE_OK" => 1,
        "STATUS_CODE_ERROR" => 2,
        _ => 0,
    };

    /// <summary>A lower-hex id as raw bytes; empty (so the field is omitted) for a root span's empty parent id or an unreadable id.</summary>
    private static byte[] HexBytes(string hex)
    {
        return hex.Length == 0 || hex.Length % 2 != 0 || !hex.All(char.IsAsciiHexDigit) ? [] : Convert.FromHexString(hex);
    }

    private static byte[] Encode(Action<ProtoWriter> body)
    {
        var writer = new ProtoWriter();
        body(writer);
        return writer.ToArray();
    }

    private static byte[] WriteJson(Action<Utf8JsonWriter> body)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            body(writer);
        }

        return buffer.WrittenSpan.ToArray();
    }
}

/// <summary>The little of the protobuf wire format the encoder needs: varints, fixed64 and length-delimited fields.</summary>
internal sealed class ProtoWriter
{
    private readonly ArrayBufferWriter<byte> _buffer = new();

    public byte[] ToArray() => _buffer.WrittenSpan.ToArray();

    public int Length => _buffer.WrittenCount;

    /// <summary>Appends another writer's bytes as is (already-encoded fields).</summary>
    public void Raw(ProtoWriter other) => _buffer.Write(other._buffer.WrittenSpan);

    /// <summary>A length-delimited sub-message. Written even when empty, since presence can matter (an empty <c>Resource</c>).</summary>
    public void Message(int field, Action<ProtoWriter> body)
    {
        var inner = new ProtoWriter();
        body(inner);
        WriteTag(field, 2);
        WriteVarint((ulong)inner._buffer.WrittenCount);
        _buffer.Write(inner._buffer.WrittenSpan);
    }

    /// <summary>A string field; proto3 omits an empty one.</summary>
    public void String(int field, string value)
    {
        if (value.Length > 0)
        {
            StringAlways(field, value);
        }
    }

    public void StringAlways(int field, string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        WriteTag(field, 2);
        WriteVarint((ulong)bytes.Length);
        _buffer.Write(bytes);
    }

    public void Bytes(int field, byte[] value)
    {
        if (value.Length == 0)
        {
            return;
        }

        WriteTag(field, 2);
        WriteVarint((ulong)value.Length);
        _buffer.Write(value);
    }

    /// <summary>A varint field; proto3 omits a zero.</summary>
    public void Varint(int field, ulong value)
    {
        if (value == 0)
        {
            return;
        }

        WriteTag(field, 0);
        WriteVarint(value);
    }

    public void Fixed64(int field, ulong value)
    {
        if (value == 0)
        {
            return;
        }

        WriteTag(field, 1);
        var span = _buffer.GetSpan(8);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(span, value);
        _buffer.Advance(8);
    }

    private void WriteTag(int field, int wireType) => WriteVarint((ulong)((field << 3) | wireType));

    private void WriteVarint(ulong value)
    {
        while (value >= 0x80)
        {
            _buffer.Write([(byte)(value | 0x80)]);
            value >>= 7;
        }

        _buffer.Write([(byte)value]);
    }
}
