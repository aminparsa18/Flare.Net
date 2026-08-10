using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class SpanSearchCursorTests
{
    [Fact]
    public void EncodeThenTryDecode_RoundTrips()
    {
        var original = new SpanSearchCursor(
            new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero),
            "0102030405060708090a0b0c0d0e0f10",
            "a1a2a3a4a5a6a7a8");

        var decoded = SpanSearchCursor.TryDecode(original.Encode());

        Assert.Equal(original, decoded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void TryDecode_ReturnsNull_ForMissingCursor(string? cursor)
    {
        Assert.Null(SpanSearchCursor.TryDecode(cursor));
    }

    [Theory]
    [InlineData("not-valid-base64!!")]
    [InlineData("aGVsbG8=")] // valid base64, but not the "{ticks}|{traceId}|{spanId}" shape
    public void TryDecode_ReturnsNull_ForMalformedCursor(string cursor)
    {
        Assert.Null(SpanSearchCursor.TryDecode(cursor));
    }
}
