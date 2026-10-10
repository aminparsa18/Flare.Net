using System.Text;
using System.Text.Json;

namespace Flare.Maui;

/// <summary>
/// Turns the binary or JSON crash payloads the platforms hand back into the plain-text stacks the Errors page shows.
/// Pure, so it is unit-tested without a device; the platform glue only supplies the bytes.
/// </summary>
internal static class NativeStackRenderer
{
    /// <summary>
    /// An Android native-crash trace. Android 12+ writes the tombstone as protobuf (<c>tombstone.proto</c>); Android 11
    /// wrote text, which is returned as is. Null when the bytes are neither.
    /// </summary>
    public static string? Tombstone(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return null;
        if (LooksLikeText(data)) return Encoding.UTF8.GetString(data);
        try { return RenderTombstone(data); }
        catch { return null; }
    }

    /// <summary>
    /// A MetricKit <c>MXCallStackTree</c> JSON document: the call stack attributed to the crash (or the first one),
    /// one frame per line as <c>#n binary+offset (address)</c>. The offsets are what a dSYM symbolicates server-side.
    /// </summary>
    public static string? MetricKitCallStack(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("callStacks", out var stacks) || stacks.ValueKind != JsonValueKind.Array) return null;
            JsonElement? chosen = null;
            foreach (var s in stacks.EnumerateArray())
            {
                chosen ??= s;
                if (s.TryGetProperty("threadAttributed", out var a) && a.ValueKind == JsonValueKind.True) { chosen = s; break; }
            }
            if (chosen is null || !chosen.Value.TryGetProperty("callStackRootFrames", out var roots)) return null;

            var sb = new StringBuilder();
            var n = 0;
            // A crash stack is a single chain: each frame's first sub-frame is its caller.
            var frame = roots.ValueKind == JsonValueKind.Array && roots.GetArrayLength() > 0 ? roots[0] : (JsonElement?)null;
            if (roots.ValueKind == JsonValueKind.Object) frame = roots;
            while (frame is { ValueKind: JsonValueKind.Object } f)
            {
                var name = f.TryGetProperty("binaryName", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : "?";
                sb.Append('#').Append(n++).Append(' ').Append(name);
                if (f.TryGetProperty("offsetIntoBinaryTextSegment", out var o) && o.TryGetInt64(out var off)) sb.Append("+0x").Append(off.ToString("x"));
                if (f.TryGetProperty("address", out var addr) && addr.TryGetUInt64(out var a)) sb.Append(" (0x").Append(a.ToString("x")).Append(')');
                sb.Append('\n');
                frame = f.TryGetProperty("subFrames", out var subs) && subs.ValueKind == JsonValueKind.Array && subs.GetArrayLength() > 0 ? subs[0] : null;
            }
            return n == 0 ? null : sb.ToString().TrimEnd();
        }
        catch { return null; }
    }

    private static bool LooksLikeText(ReadOnlySpan<byte> d) => d[0] == (byte)'*';

    // Field numbers from AOSP system/core/debuggerd/proto/tombstone.proto.
    private static string RenderTombstone(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder();
        string? abort = null, cmd = null;
        var causes = new List<string>();
        var threads = new List<(uint Id, byte[] Body)>();
        uint tid = 0;
        byte[]? signal = null;

        foreach (var (field, bytes, value) in Fields(data))
        {
            switch (field)
            {
                case 6: tid = (uint)value; break;
                case 9 when bytes is not null: cmd ??= Encoding.UTF8.GetString(bytes); break;
                case 10: signal = bytes; break;
                case 14 when bytes is not null: abort = Encoding.UTF8.GetString(bytes); break;
                case 15 when bytes is not null:
                    foreach (var (f, b, _) in Fields(bytes)) if (f == 1 && b is not null) causes.Add(Encoding.UTF8.GetString(b));
                    break;
                case 16 when bytes is not null: // map<uint32, Thread> entry: key = 1, value = 2
                    uint key = 0; byte[]? body = null;
                    foreach (var (f, b, v) in Fields(bytes)) { if (f == 1) key = (uint)v; else if (f == 2) body = b; }
                    if (body is not null) threads.Add((key, body));
                    break;
            }
        }

        if (cmd is not null) sb.Append("Cmdline: ").Append(cmd).Append('\n');
        if (signal is not null) sb.Append(RenderSignal(signal)).Append('\n');
        foreach (var c in causes) sb.Append("Cause: ").Append(c).Append('\n');
        if (abort is not null) sb.Append("Abort message: ").Append(abort).Append('\n');

        // The crashing thread first; other threads would only bury it under the size cap.
        foreach (var (id, body) in threads.OrderBy(t => t.Id == tid ? 0 : 1).Take(1))
        {
            string? name = null;
            var frames = new List<string>();
            foreach (var (f, b, _) in Fields(body))
            {
                if (f == 2 && b is not null) name = Encoding.UTF8.GetString(b);
                else if (f == 4 && b is not null) frames.Add(RenderFrame(frames.Count, b));
            }
            sb.Append("\nbacktrace (tid ").Append(id).Append(name is null ? "" : ", " + name).Append("):\n");
            foreach (var fr in frames) sb.Append(fr).Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

    private static string RenderSignal(byte[] signal)
    {
        string? name = null, codeName = null;
        ulong faultAddr = 0; var hasFault = false;
        foreach (var (f, b, v) in Fields(signal))
        {
            if (f == 2 && b is not null) name = Encoding.UTF8.GetString(b);
            else if (f == 4 && b is not null) codeName = Encoding.UTF8.GetString(b);
            else if (f == 8) hasFault = v != 0;
            else if (f == 9) faultAddr = v;
        }
        var text = $"Signal: {name ?? "?"}" + (codeName is null ? "" : $" ({codeName})");
        return hasFault ? $"{text}, fault addr 0x{faultAddr:x}" : text;
    }

    private static string RenderFrame(int index, byte[] frame)
    {
        ulong relPc = 0, offset = 0; string? fn = null, file = null;
        foreach (var (f, b, v) in Fields(frame))
        {
            switch (f)
            {
                case 1: relPc = v; break;
                case 4 when b is not null: fn = Encoding.UTF8.GetString(b); break;
                case 5: offset = v; break;
                case 6 when b is not null: file = Encoding.UTF8.GetString(b); break;
            }
        }
        var line = $"#{index:D2} pc {relPc:x16}  {file ?? "<unknown>"}";
        if (fn is not null) line += $" ({fn}{(offset != 0 ? $"+{offset}" : "")})";
        return line;
    }

    /// <summary>Walks protobuf wire format: length-delimited fields yield their bytes, varint and fixed fields their value.</summary>
    private static IEnumerable<(int Field, byte[]? Bytes, ulong Value)> Fields(ReadOnlySpan<byte> data)
    {
        var list = new List<(int, byte[]?, ulong)>();
        var i = 0;
        while (i < data.Length)
        {
            var tag = Varint(data, ref i);
            var field = (int)(tag >> 3);
            switch ((int)(tag & 7))
            {
                case 0: list.Add((field, null, Varint(data, ref i))); break;
                case 1: list.Add((field, null, BitConverter.ToUInt64(data.Slice(i, 8)))); i += 8; break;
                case 2:
                    var len = (int)Varint(data, ref i);
                    if (len < 0 || i + len > data.Length) throw new FormatException("truncated");
                    list.Add((field, data.Slice(i, len).ToArray(), 0)); i += len;
                    break;
                case 5: list.Add((field, null, BitConverter.ToUInt32(data.Slice(i, 4)))); i += 4; break;
                default: throw new FormatException("unsupported wire type");
            }
        }
        return list;
    }

    private static ulong Varint(ReadOnlySpan<byte> data, ref int i)
    {
        ulong result = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            if (i >= data.Length) throw new FormatException("truncated");
            var b = data[i++];
            result |= (ulong)(b & 0x7f) << shift;
            if ((b & 0x80) == 0) return result;
        }
        throw new FormatException("bad varint");
    }
}
