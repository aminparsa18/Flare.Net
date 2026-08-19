using Flare.Api.Auth;
using Flare.Api.Tests.TestSupport;
using Xunit;

namespace Flare.Api.Tests.Auth;

public class LdapCertificateTrustTests
{
    [Fact]
    public void Validate_ReturnsTrue_WhenServerCertificateIsExactlyThePinnedSelfSignedCertificate()
    {
        var cert = TestCertificates.CreateSelfSigned();

        Assert.True(LdapCertificateTrust.Validate(cert, cert));
    }

    [Fact]
    public void Validate_ReturnsTrue_WhenServerCertificateChainsUpToThePinnedCa()
    {
        var (ca, leaf) = TestCertificates.CreateCaAndLeaf();

        // Pin the CA's root, present the leaf it signed - the "private CA" scenario
        // docs/auth.md describes, distinct from pinning a self-signed cert directly.
        Assert.True(LdapCertificateTrust.Validate(ca, leaf));
    }

    [Fact]
    public void Validate_ReturnsFalse_WhenServerCertificateIsUnrelatedToThePin()
    {
        var pinned = TestCertificates.CreateSelfSigned(subject: "CN=pinned.corp.example.com");
        var presented = TestCertificates.CreateSelfSigned(subject: "CN=impostor.corp.example.com");

        Assert.False(LdapCertificateTrust.Validate(pinned, presented));
    }

    [Fact]
    public void Validate_ReturnsFalse_WhenThePinnedCertificateItselfHasExpired()
    {
        // Fail-closed by design (docs/auth.md's "Active Directory (LDAP)" section, and
        // LdapCertificateTrust's own remarks) - pinning isn't an "ignore expiry" escape
        // hatch, even for the exact pinned certificate being presented back.
        // CreateSelfSigned's notBefore is fixed at "1 day ago"; a 1-hour validity window
        // puts notAfter 23 hours in the past too, while keeping notAfter > notBefore.
        var expired = TestCertificates.CreateSelfSigned(validity: TimeSpan.FromHours(1));

        Assert.False(LdapCertificateTrust.Validate(expired, expired));
    }
}
