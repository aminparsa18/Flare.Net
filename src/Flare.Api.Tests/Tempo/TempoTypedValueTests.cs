using System.Text.Json;
using Flare.Api.Tempo;
using Xunit;

namespace Flare.Api.Tests.Tempo;

public class TempoTypedValueTests
{
    [Theory]
    [InlineData("200", TempoValueKind.Int)]
    [InlineData("0", TempoValueKind.Int)]
    [InlineData("-3", TempoValueKind.Int)]
    [InlineData("9223372036854775807", TempoValueKind.Int)]
    [InlineData("-9223372036854775808", TempoValueKind.Int)]
    [InlineData("0.25", TempoValueKind.Double)]
    [InlineData("-1.5", TempoValueKind.Double)]
    [InlineData("1E+21", TempoValueKind.Double)]
    [InlineData("1.1", TempoValueKind.Double)]
    [InlineData("true", TempoValueKind.Bool)]
    [InlineData("false", TempoValueKind.Bool)]
    public void Infer_TakesWhatWritesBackToTheSameText(string text, TempoValueKind kind) =>
        Assert.Equal(kind, TempoTypedValue.Infer(text).Kind);

    [Theory]
    [InlineData("0123")]      // an id with a leading zero stays an id
    [InlineData("007")]
    [InlineData("+5")]
    [InlineData("-0")]
    [InlineData("5.0")]       // would print back as 5
    [InlineData("1.50")]
    [InlineData("1e5")]       // would print back as 100000
    [InlineData("1,000")]
    [InlineData("1_000")]
    [InlineData(" 5")]
    [InlineData("5 ")]
    [InlineData("0x10")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("9223372036854775808")]   // beyond 64 bits
    [InlineData("True")]
    [InlineData("TRUE")]
    [InlineData("yes")]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1.2.3")]
    [InlineData(".5")]
    [InlineData("5.")]
    [InlineData("12345678901234567890123456789012345678901234567890")]
    public void Infer_LeavesAnythingAmbiguousAsText(string text)
    {
        var value = TempoTypedValue.Infer(text);

        Assert.Equal(TempoValueKind.String, value.Kind);
        Assert.Equal(text, value.Text);
    }

    [Fact]
    public void Infer_KeepsTheNumbers()
    {
        Assert.Equal(-3L, TempoTypedValue.Infer("-3").Integer);
        Assert.Equal(0.25, TempoTypedValue.Infer("0.25").Number);
        Assert.True(TempoTypedValue.Infer("true").Boolean);
        Assert.False(TempoTypedValue.Infer("false").Boolean);
    }

    [Theory]
    [InlineData("200", true, """{"intValue":"200"}""")]
    [InlineData("0.25", true, """{"doubleValue":0.25}""")]
    [InlineData("true", true, """{"boolValue":true}""")]
    [InlineData("0123", true, """{"stringValue":"0123"}""")]
    [InlineData("abc", true, """{"stringValue":"abc"}""")]
    [InlineData("200", false, """{"stringValue":"200"}""")]
    [InlineData("true", false, """{"stringValue":"true"}""")]
    public void WriteMember_WritesTheMemberOfTheInferredKind(string text, bool infer, string expected)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            TempoTypedValue.WriteMember(writer, text, infer);
            writer.WriteEndObject();
        }

        Assert.Equal(expected, System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public void WriteMember_WritesAnExponentAsAJsonNumber()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            TempoTypedValue.WriteMember(writer, "1E+21", infer: true);
            writer.WriteEndObject();
        }

        Assert.Equal(1e21, JsonDocument.Parse(stream.ToArray()).RootElement.GetProperty("doubleValue").GetDouble());
    }
}
