using Flare.Ingest.Otlp;
using Google.Protobuf;
using OpenTelemetry.Proto.Common.V1;
using Xunit;

namespace Flare.Ingest.Tests;

public class OtlpAnyValueTests
{
    [Fact]
    public void ToFlatString_LeavesTopLevelStringUnquoted()
    {
        Assert.Equal("hello", OtlpAnyValue.ToFlatString(new AnyValue { StringValue = "hello" }));
    }

    [Fact]
    public void ToFlatString_ReturnsNull_ForUnsetValue()
    {
        Assert.Null(OtlpAnyValue.ToFlatString(new AnyValue()));
        Assert.Null(OtlpAnyValue.ToFlatString(null));
    }

    [Fact]
    public void ToFlatString_RendersStringArrayAsJson()
    {
        var value = new AnyValue
        {
            ArrayValue = new ArrayValue { Values = { new AnyValue { StringValue = "a" }, new AnyValue { StringValue = "b,c" } } },
        };

        Assert.Equal("""["a","b,c"]""", OtlpAnyValue.ToFlatString(value));
    }

    [Fact]
    public void ToFlatString_RendersNestedKvlistAsTypedJson()
    {
        var value = Kvlist(
            ("str", new AnyValue { StringValue = "x" }),
            ("int", new AnyValue { IntValue = 7 }),
            ("double", new AnyValue { DoubleValue = 1.5 }),
            ("bool", new AnyValue { BoolValue = false }),
            ("bytes", new AnyValue { BytesValue = ByteString.CopyFrom([1, 2, 3]) }),
            ("unset", new AnyValue()),
            ("inner", Kvlist(("list", new AnyValue
            {
                ArrayValue = new ArrayValue { Values = { new AnyValue { IntValue = 1 }, new AnyValue { StringValue = "two" } } },
            }))));

        Assert.Equal(
            """{"str":"x","int":7,"double":1.5,"bool":false,"bytes":"AQID","unset":null,"inner":{"list":[1,"two"]}}""",
            OtlpAnyValue.ToFlatString(value));
    }

    [Fact]
    public void ToFlatString_EscapesQuotesButKeepsNonAsciiReadable()
    {
        var value = Kvlist(("msg", new AnyValue { StringValue = "say \"привет\" 你好 <b>" }));

        Assert.Equal("""{"msg":"say \"привет\" 你好 <b>"}""", OtlpAnyValue.ToFlatString(value));
    }

    [Fact]
    public void ToFlatString_RendersNonFiniteDoublesAsStrings_SinceJsonHasNoLiteralForThem()
    {
        var value = new AnyValue
        {
            ArrayValue = new ArrayValue
            {
                Values = { new AnyValue { DoubleValue = double.NaN }, new AnyValue { DoubleValue = double.PositiveInfinity } },
            },
        };

        Assert.Equal("""["NaN","Infinity"]""", OtlpAnyValue.ToFlatString(value));
    }

    [Fact]
    public void Flatten_SkipsUnsetValues()
    {
        var result = OtlpAnyValue.Flatten(
        [
            new KeyValue { Key = "set", Value = new AnyValue { IntValue = 1 } },
            new KeyValue { Key = "unset", Value = new AnyValue() },
        ]);

        Assert.Equal("1", Assert.Single(result, kv => kv.Key == "set").Value);
        Assert.False(result.ContainsKey("unset"));
    }

    private static AnyValue Kvlist(params (string Key, AnyValue Value)[] entries)
    {
        var list = new KeyValueList();
        foreach (var (key, value) in entries)
        {
            list.Values.Add(new KeyValue { Key = key, Value = value });
        }

        return new AnyValue { KvlistValue = list };
    }
}
