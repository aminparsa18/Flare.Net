using Flare.Api.Alerting;
using Flare.Api.Model;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flare.Api.Tests.Alerting;

/// <summary>Covers <see cref="AlertAckLinkSigner"/> (ADR-0127): a link round-trips its claims, and anything else - tampered, from another key ring, expired, no public URL - is refused.</summary>
public class AlertAckLinkSignerTests
{
    private static readonly Guid RuleId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly DateTimeOffset Issued = DateTimeOffset.FromUnixTimeMilliseconds(1_760_000_000_123);

    private static AlertAckLinkSigner Make(IDataProtectionProvider? provider = null, string publicUrl = "https://flare.example.com/", int lifetimeHours = 24) =>
        new(provider ?? new EphemeralDataProtectionProvider(), Options.Create(new AlertLinkOptions { PublicUrl = publicUrl, AckLinkLifetimeHours = lifetimeHours }));

    private static string TokenOf(string url) => Uri.UnescapeDataString(url[(url.IndexOf("token=", StringComparison.Ordinal) + "token=".Length)..]);

    [Fact]
    public void Link_round_trips_its_claims_and_trims_the_public_url()
    {
        var signer = Make();
        var url = signer.CreateUrl(RuleId, Issued);

        Assert.StartsWith("https://flare.example.com/ack?token=", url);
        var claims = Assert.NotNull(signer.Validate(TokenOf(url!)));
        Assert.Equal(RuleId, claims.RuleId);
        Assert.Equal(Issued, claims.IssuedAt);
    }

    [Fact]
    public void Blank_public_url_yields_no_link()
    {
        Assert.Null(Make(publicUrl: "").CreateUrl(RuleId, Issued));
        Assert.Null(Make(publicUrl: "  ").CreateUrl(RuleId, Issued));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-token")]
    public void Malformed_tokens_are_refused(string? token) => Assert.Null(Make().Validate(token));

    [Fact]
    public void Tampered_token_is_refused()
    {
        var signer = Make();
        var token = TokenOf(signer.CreateUrl(RuleId, Issued)!);
        var flipped = token[..^3] + (token[^3] == 'A' ? 'B' : 'A') + token[^2..];

        Assert.Null(signer.Validate(flipped));
    }

    [Fact]
    public void Token_from_another_key_ring_is_refused()
    {
        var token = TokenOf(Make().CreateUrl(RuleId, Issued)!);

        Assert.Null(Make().Validate(token));
    }

    [Fact]
    public void Token_from_a_shared_key_ring_is_accepted()
    {
        var provider = new EphemeralDataProtectionProvider();
        var token = TokenOf(Make(provider).CreateUrl(RuleId, Issued)!);

        Assert.NotNull(Make(provider).Validate(token));
    }

    [Fact]
    public void Link_is_not_a_valid_token_for_a_different_purpose()
    {
        var provider = new EphemeralDataProtectionProvider();
        var other = provider.CreateProtector("something-else").Protect($"{RuleId:N}|{Issued.ToUnixTimeMilliseconds()}");

        Assert.Null(Make(provider).Validate(other));
    }
}
