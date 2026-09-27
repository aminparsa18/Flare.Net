using Flare.Api.Model;
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
    [InlineData(SpanSortKey.Duration, false, 1_500_000_000UL)]
    [InlineData(SpanSortKey.SpanCount, true, 42UL)]
    public void EncodeThenTryDecode_RoundTrips_SortKeyAndDirection(SpanSortKey sortBy, bool ascending, ulong value)
    {
        var original = new SpanSearchCursor(sortBy, ascending, value, "0102030405060708090a0b0c0d0e0f10", "a1a2a3a4a5a6a7a8");

        Assert.Equal(original, SpanSearchCursor.TryDecode(original.Encode()));
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
    [InlineData("aGVsbG8=")] // valid base64, but not the "{sortBy}|{a|d}|{value}|{traceId}|{spanId}" shape
    [InlineData("MTIzfDB8MXwy")] // "123|0|1|2" - the pre-sort 3-part shape plus one, wrong direction marker
    [InlineData("OXxkfDF8YXxi")] // "9|d|1|a|b" - undefined sort key
    public void TryDecode_ReturnsNull_ForMalformedCursor(string cursor)
    {
        Assert.Null(SpanSearchCursor.TryDecode(cursor));
    }
}
