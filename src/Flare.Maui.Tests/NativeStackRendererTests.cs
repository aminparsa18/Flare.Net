using System.Text;
using Xunit;

namespace Flare.Maui.Tests;

public class NativeStackRendererTests
{
    private static byte[] Varint(ulong v)
    {
        var o = new List<byte>();
        while (v >= 0x80) { o.Add((byte)(v | 0x80)); v >>= 7; }
        o.Add((byte)v);
        return [.. o];
    }

    private static byte[] Num(int field, ulong v) => [.. Varint((ulong)field << 3), .. Varint(v)];
    private static byte[] Msg(int field, byte[] body) => [.. Varint((ulong)field << 3 | 2), .. Varint((ulong)body.Length), .. body];
    private static byte[] Str(int field, string s) => Msg(field, Encoding.UTF8.GetBytes(s));
    private static byte[] Cat(params byte[][] parts) => [.. parts.SelectMany(p => p)];

    private static byte[] Frame(ulong relPc, string? fn, ulong off, string file) =>
        Cat(Num(1, relPc), fn is null ? [] : Str(4, fn), Num(5, off), Str(6, file));

    [Fact]
    public void Tombstone_protobuf_renders_signal_cause_and_crashing_thread()
    {
        var signal = Cat(Num(1, 11), Str(2, "SIGSEGV"), Str(4, "SEGV_MAPERR"), Num(8, 1), Num(9, 0xdead));
        var crashing = Cat(Num(1, 42), Str(2, "RenderThread"),
            Msg(4, Frame(0x1234, "crash_here", 8, "/data/app/lib/libnative.so")),
            Msg(4, Frame(0x20, null, 0, "/apex/com.android.runtime/lib64/bionic/libc.so")));
        var other = Cat(Num(1, 7), Str(2, "other"), Msg(4, Frame(1, "idle", 0, "/x.so")));
        var tomb = Cat(Num(1, 1), Num(6, 42), Str(9, "com.example.app"), Msg(10, signal),
            Msg(15, Str(1, "null pointer dereference")),
            Msg(16, Cat(Num(1, 7), Msg(2, other))),
            Msg(16, Cat(Num(1, 42), Msg(2, crashing))));

        var text = NativeStackRenderer.Tombstone(tomb);

        Assert.NotNull(text);
        Assert.Contains("Signal: SIGSEGV (SEGV_MAPERR), fault addr 0xdead", text);
        Assert.Contains("Cause: null pointer dereference", text);
        Assert.Contains("backtrace (tid 42, RenderThread)", text);
        Assert.Contains("#00 pc 0000000000001234  /data/app/lib/libnative.so (crash_here+8)", text);
        Assert.Contains("#01 pc 0000000000000020", text);
        Assert.DoesNotContain("idle", text);
    }

    [Fact]
    public void Text_tombstone_passes_through()
    {
        var text = NativeStackRenderer.Tombstone("*** *** ***\nsignal 11"u8);
        Assert.Equal("*** *** ***\nsignal 11", text);
    }

    [Fact]
    public void Truncated_or_empty_protobuf_returns_null_not_throws()
    {
        var tomb = Cat(Num(1, 1), Str(9, "com.example.app"));
        Assert.Null(NativeStackRenderer.Tombstone(tomb.AsSpan(0, tomb.Length - 3)));
        Assert.Null(NativeStackRenderer.Tombstone([]));
    }

    [Fact]
    public void MetricKit_call_stack_renders_the_attributed_thread_as_frames()
    {
        const string json = """
        {"callStackPerThread":true,"callStacks":[
          {"threadAttributed":false,"callStackRootFrames":[{"binaryName":"other","offsetIntoBinaryTextSegment":1,"address":1}]},
          {"threadAttributed":true,"callStackRootFrames":[
            {"binaryName":"MyApp","offsetIntoBinaryTextSegment":4096,"address":4294971392,
             "subFrames":[{"binaryName":"UIKitCore","offsetIntoBinaryTextSegment":16,"address":255}]}]}]}
        """;

        var text = NativeStackRenderer.MetricKitCallStack(json);

        Assert.Equal("#0 MyApp+0x1000 (0x100001000)\n#1 UIKitCore+0x10 (0xff)", text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"callStacks\":[]}")]
    public void MetricKit_bad_input_returns_null(string? json) => Assert.Null(NativeStackRenderer.MetricKitCallStack(json));
}
