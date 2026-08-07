using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class LogSearchCursorTests
{
    [Fact]
    public void EncodeThenTryDecode_RoundTrips()
    {
        var original = new LogSearchCursor(new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero), Guid.NewGuid());

        var decoded = LogSearchCursor.TryDecode(original.Encode());

        Assert.Equal(original, decoded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void TryDecode_ReturnsNull_ForMissingCursor(string? cursor)
    {
        Assert.Null(LogSearchCursor.TryDecode(cursor));
    }

    [Theory]
    [InlineData("not-valid-base64!!")]
    [InlineData("aGVsbG8=")] // valid base64, but not the "{ticks}|{guid}" shape
    public void TryDecode_ReturnsNull_ForMalformedCursor(string cursor)
    {
        Assert.Null(LogSearchCursor.TryDecode(cursor));
    }
}
