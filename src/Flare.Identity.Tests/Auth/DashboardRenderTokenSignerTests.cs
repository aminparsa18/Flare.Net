using Flare.Identity.Auth;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace Flare.Identity.Tests.Auth;

/// <summary>Covers <see cref="DashboardRenderTokenSigner"/> (ADR-0142): a token round-trips its claims, and anything else is refused.</summary>
public class DashboardRenderTokenSignerTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid DashboardId = Guid.Parse("66666666-7777-8888-9999-000000000000");

    [Fact]
    public void Token_round_trips_its_claims()
    {
        var signer = new DashboardRenderTokenSigner(new EphemeralDataProtectionProvider());
        var token = signer.Create(UserId, DashboardId, TimeSpan.FromMinutes(10));

        Assert.StartsWith(DashboardRenderTokenSigner.Prefix, token);
        var claims = Assert.NotNull(signer.Validate(token));
        Assert.Equal(UserId, claims.UserId);
        Assert.Equal(DashboardId, claims.DashboardId);
    }

    [Fact]
    public void Token_from_another_key_ring_is_refused()
    {
        var token = new DashboardRenderTokenSigner(new EphemeralDataProtectionProvider()).Create(UserId, DashboardId, TimeSpan.FromMinutes(10));
        Assert.Null(new DashboardRenderTokenSigner(new EphemeralDataProtectionProvider()).Validate(token));
    }

    [Fact]
    public void Tampered_token_is_refused()
    {
        var signer = new DashboardRenderTokenSigner(new EphemeralDataProtectionProvider());
        var token = signer.Create(UserId, DashboardId, TimeSpan.FromMinutes(10));
        var flipped = token[..^2] + (token[^2] == 'A' ? 'B' : 'A') + token[^1];
        Assert.Null(signer.Validate(flipped));
    }

    [Fact]
    public void Expired_token_is_refused()
    {
        var signer = new DashboardRenderTokenSigner(new EphemeralDataProtectionProvider());
        var token = signer.Create(UserId, DashboardId, TimeSpan.FromMilliseconds(-1));
        Assert.Null(signer.Validate(token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a-session-id")]
    [InlineData("flr_render_")]
    [InlineData("flr_render_garbage")]
    public void Anything_else_is_refused(string? token) =>
        Assert.Null(new DashboardRenderTokenSigner(new EphemeralDataProtectionProvider()).Validate(token));
}
