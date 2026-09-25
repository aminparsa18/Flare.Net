using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using OpenTelemetry.Proto.Common.V1;

namespace Flare.Ingest.Otlp;

/// <summary>
/// Renders OTLP <see cref="AnyValue"/>s into the flat strings Flare stores - shared by
/// <see cref="OtlpLogMapper"/>, <see cref="OtlpTraceMapper"/> and <see cref="OtlpMetricsMapper"/>.
/// </summary>
/// <remarks>
/// Scalars render as their bare text (a string attribute stays unquoted). Arrays and
/// key/value lists render as compact JSON, so a structured log body or a dictionary-valued
/// attribute (which the .NET SDK sends as a <c>kvlist</c> since 1.18) is queryable with the
/// body-JSON filters rather than landing as an ad-hoc <c>{k=v}</c> string.
/// </remarks>
public static class OtlpAnyValue
{
    // Stored text, never emitted into HTML - keep non-ASCII (e.g. Cyrillic, CJK) readable
    // in the dashboard instead of \uXXXX-escaped.
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string? ToFlatString(AnyValue? value) => value?.ValueCase switch
    {
        AnyValue.ValueOneofCase.StringValue => value.StringValue,
        AnyValue.ValueOneofCase.BoolValue => value.BoolValue ? "true" : "false",
        AnyValue.ValueOneofCase.IntValue => value.IntValue.ToString(CultureInfo.InvariantCulture),
        AnyValue.ValueOneofCase.DoubleValue => value.DoubleValue.ToString(CultureInfo.InvariantCulture),
        AnyValue.ValueOneofCase.BytesValue => Convert.ToBase64String(value.BytesValue.Span),
        AnyValue.ValueOneofCase.ArrayValue or AnyValue.ValueOneofCase.KvlistValue => ToJson(value),
        _ => null,
    };

    public static Dictionary<string, string> Flatten(IEnumerable<KeyValue>? attributes)
    {
        var result = new Dictionary<string, string>();
        if (attributes is null)
        {
            return result;
        }

        foreach (var kv in attributes)
        {
            var value = ToFlatString(kv.Value);
            if (value is not null)
            {
                result[kv.Key] = value;
            }
        }

        return result;
    }

    private static string ToJson(AnyValue value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            WriteJson(writer, value);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteJson(Utf8JsonWriter writer, AnyValue? value)
    {
        switch (value?.ValueCase)
        {
            case AnyValue.ValueOneofCase.StringValue:
                writer.WriteStringValue(value.StringValue);
                break;
            case AnyValue.ValueOneofCase.BoolValue:
                writer.WriteBooleanValue(value.BoolValue);
                break;
            case AnyValue.ValueOneofCase.IntValue:
                writer.WriteNumberValue(value.IntValue);
                break;
            // JSON has no NaN/Infinity literals - Utf8JsonWriter throws on them.
            case AnyValue.ValueOneofCase.DoubleValue when !double.IsFinite(value.DoubleValue):
                writer.WriteStringValue(value.DoubleValue.ToString(CultureInfo.InvariantCulture));
                break;
            case AnyValue.ValueOneofCase.DoubleValue:
                writer.WriteNumberValue(value.DoubleValue);
                break;
            case AnyValue.ValueOneofCase.BytesValue:
                writer.WriteBase64StringValue(value.BytesValue.Span);
                break;
            case AnyValue.ValueOneofCase.ArrayValue:
                writer.WriteStartArray();
                foreach (var element in value.ArrayValue.Values)
                {
                    WriteJson(writer, element);
                }

                writer.WriteEndArray();
                break;
            case AnyValue.ValueOneofCase.KvlistValue:
                writer.WriteStartObject();
                foreach (var kv in value.KvlistValue.Values)
                {
                    writer.WritePropertyName(kv.Key);
                    WriteJson(writer, kv.Value);
                }

                writer.WriteEndObject();
                break;
            default:
                writer.WriteNullValue();
                break;
        }
    }
}
