using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Google.Protobuf;

namespace Flare.Ingest.Otlp;

/// <summary>
/// Parses OTLP/HTTP JSON request bodies - shared by the logs, traces and metrics HTTP
/// endpoints.
/// </summary>
/// <remarks>
/// OTLP/JSON deliberately deviates from the generic proto3 JSON mapping for trace and
/// span ids: they're hex-encoded, not base64 (see the OTLP spec's "JSON Protobuf
/// Encoding" section). <see cref="JsonParser"/> only knows the generic mapping, so on its
/// own it base64-decodes a 32-char hex trace id into 24 garbage bytes. This rewrites every
/// <c>traceId</c>/<c>spanId</c>/<c>parentSpanId</c> (spans, links, log records, metric
/// exemplars) from hex to base64 first, then hands the result to <see cref="JsonParser"/>.
/// A value that isn't exactly 32/16 hex chars is left alone, so a sender already
/// (wrongly) using base64 - a 16-byte id is 24 base64 chars, never 32 - keeps working.
/// </remarks>
public static class OtlpJson
{
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static T Parse<T>(ReadOnlySpan<byte> utf8Json)
        where T : IMessage<T>, new()
        => JsonParser.Default.Parse<T>(NormalizeIds(utf8Json));

    /// <summary>Returns <paramref name="utf8Json"/> with hex trace/span ids re-encoded as base64.</summary>
    public static string NormalizeIds(ReadOnlySpan<byte> utf8Json)
    {
        var output = new ArrayBufferWriter<byte>(utf8Json.Length);
        using (var writer = new Utf8JsonWriter(output, WriterOptions))
        {
            var reader = new Utf8JsonReader(utf8Json);
            var idByteLength = 0;
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject: writer.WriteStartObject(); break;
                    case JsonTokenType.EndObject: writer.WriteEndObject(); break;
                    case JsonTokenType.StartArray: writer.WriteStartArray(); break;
                    case JsonTokenType.EndArray: writer.WriteEndArray(); break;
                    case JsonTokenType.PropertyName:
                        var name = reader.GetString()!;
                        idByteLength = IdByteLength(name);
                        writer.WritePropertyName(name);
                        continue;
                    case JsonTokenType.String:
                        var value = reader.GetString()!;
                        writer.WriteStringValue(idByteLength > 0 && TryHexToBase64(value, idByteLength, out var base64) ? base64 : value);
                        break;
                    // Raw, not reparsed: int64 fields may arrive as numbers too large for a
                    // double, and must reach JsonParser digit-for-digit.
                    case JsonTokenType.Number: writer.WriteRawValue(reader.ValueSpan, skipInputValidation: true); break;
                    case JsonTokenType.True: writer.WriteBooleanValue(true); break;
                    case JsonTokenType.False: writer.WriteBooleanValue(false); break;
                    case JsonTokenType.Null: writer.WriteNullValue(); break;
                }
                idByteLength = 0;
            }
        }
        return Encoding.UTF8.GetString(output.WrittenSpan);
    }

    // JsonParser accepts both the lowerCamelCase JSON name and the original proto field
    // name, so both spellings are rewritten.
    private static int IdByteLength(string propertyName) => propertyName switch
    {
        "traceId" or "trace_id" => 16,
        "spanId" or "span_id" or "parentSpanId" or "parent_span_id" => 8,
        _ => 0,
    };

    private static bool TryHexToBase64(string value, int byteLength, out string base64)
    {
        base64 = string.Empty;
        if (value.Length != byteLength * 2) return false;
        try
        {
            base64 = Convert.ToBase64String(Convert.FromHexString(value));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
