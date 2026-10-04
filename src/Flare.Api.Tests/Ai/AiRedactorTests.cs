using Flare.Api.Ai;
using Xunit;

namespace Flare.Api.Tests.Ai;

public class AiRedactorTests
{
    [Theory]
    [InlineData("user bob@example.com failed", "bob@example.com")]
    [InlineData("Authorization: Bearer abcdef1234567890xyz", "abcdef1234567890xyz")]
    [InlineData("password=hunter2;Server=db", "hunter2")]
    [InlineData("{\"apiKey\": \"sk-live-abc123\"}", "sk-live-abc123")]
    [InlineData("https://admin:s3cret@host/db", "s3cret")]
    [InlineData("connect to 10.1.2.3 failed", "10.1.2.3")]
    [InlineData("token eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.abcdefghijkl", "eyJhbGciOiJIUzI1NiJ9")]
    [InlineData("key 0123456789abcdef0123456789abcdef0123", "0123456789abcdef0123456789abcdef0123")]
    public void Redact_RemovesSecret(string input, string secret)
    {
        var result = AiRedactor.Redact(input);

        Assert.DoesNotContain(secret, result);
        Assert.Contains(AiRedactor.Mask, result);
    }

    [Fact]
    public void Redact_LeavesOrdinaryStackFramesAlone()
    {
        const string frame = "   at Acme.Shop.OrderService.PlaceOrder(Int32 id) in /src/Shop/OrderService.cs:line 42";

        Assert.Equal(frame, AiRedactor.Redact(frame));
    }

    [Fact]
    public void Redact_RemovesPrivateKeyBlock()
    {
        var result = AiRedactor.Redact("x\n-----BEGIN RSA PRIVATE KEY-----\nMIIabc\n-----END RSA PRIVATE KEY-----\ny");

        Assert.DoesNotContain("MIIabc", result);
    }
}
