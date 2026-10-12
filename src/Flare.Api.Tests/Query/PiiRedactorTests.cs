using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class PiiRedactorTests
{
    [Theory]
    [InlineData("mail bob@example.com now", "mail [REDACTED:email] now")]
    [InlineData("card 4111 1111 1111 1111", "card [REDACTED:card]")]
    [InlineData("iban GB82 WEST 1234 5698 7654 32 end", "iban [REDACTED:iban] end")]
    [InlineData("order 4111 1111 1111 1112", "order 4111 1111 1111 1112")]
    public void Redact_matches_ingest_behaviour(string input, string expected) =>
        Assert.Equal(expected, PiiRedactor.Redact(input, PiiDetectors.All));
}
