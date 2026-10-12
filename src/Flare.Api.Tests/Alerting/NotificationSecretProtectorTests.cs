using Xunit;
using Flare.Api.Alerting;
using Microsoft.AspNetCore.DataProtection;

namespace Flare.Api.Tests.Alerting;

public class NotificationSecretProtectorTests
{
    private static NotificationSecretProtector Create() => new(new EphemeralDataProtectionProvider());

    [Fact]
    public void Protect_EncryptsAndUnprotectRoundTrips()
    {
        var protector = Create();

        var stored = protector.Protect("https://hooks.slack.com/services/T0/B0/secret");

        Assert.StartsWith(NotificationSecretProtector.Prefix, stored);
        Assert.DoesNotContain("secret", stored);
        Assert.Equal("https://hooks.slack.com/services/T0/B0/secret", protector.Unprotect(stored));
    }

    [Fact]
    public void Protect_LeavesEmptyAndAlreadyProtectedValuesAlone()
    {
        var protector = Create();
        var stored = protector.Protect("token");

        Assert.Equal("", protector.Protect(""));
        Assert.Equal(stored, protector.Protect(stored));
    }

    [Fact]
    public void Unprotect_ReturnsLegacyPlaintextUnchanged()
    {
        var protector = Create();

        Assert.Equal("123:abc", protector.Unprotect("123:abc"));
        Assert.Equal("", protector.Unprotect(""));
    }

    [Fact]
    public void NeedsProtection_IsTrueOnlyForNonEmptyPlaintext()
    {
        var protector = Create();

        Assert.True(protector.NeedsProtection("123:abc"));
        Assert.False(protector.NeedsProtection(""));
        Assert.False(protector.NeedsProtection(protector.Protect("123:abc")));
    }
}
