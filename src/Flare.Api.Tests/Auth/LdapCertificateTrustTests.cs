using System.Security.Cryptography.X509Certificates;
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

        Assert.True(LdapCertificateTrust.Validate(new X509Certificate2Collection(cert), cert));
    }

    [Fact]
    public void Validate_ReturnsTrue_WhenServerCertificateChainsUpToThePinnedCa()
    {
        var (ca, leaf) = TestCertificates.CreateCaAndLeaf();

        // Pin the CA's root, present the leaf it signed - the "private CA" scenario
        // docs/auth.md describes, distinct from pinning a self-signed cert directly.
        Assert.True(LdapCertificateTrust.Validate(new X509Certificate2Collection(ca), leaf));
    }

    [Fact]
    public void Validate_ReturnsFalse_WhenServerCertificateIsUnrelatedToThePin()
    {
        var pinned = TestCertificates.CreateSelfSigned(subject: "CN=pinned.corp.example.com");
        var presented = TestCertificates.CreateSelfSigned(subject: "CN=impostor.corp.example.com");

        Assert.False(LdapCertificateTrust.Validate(new X509Certificate2Collection(pinned), presented));
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

        Assert.False(LdapCertificateTrust.Validate(new X509Certificate2Collection(expired), expired));
    }

    [Fact]
    public void Validate_ReturnsTrue_WhenServerCertificateChainsThroughAnIntermediateBundledWithTheRoot()
    {
        var (root, intermediate, leaf) = TestCertificates.CreateRootIntermediateAndLeaf();

        // The real two-tier private-CA shape this bundle support exists for: a bare
        // pinned root alone can't complete this chain (no guaranteed AIA fetch for the
        // missing intermediate) - both root and intermediate have to be in the pinned
        // bundle, order shouldn't matter.
        Assert.True(LdapCertificateTrust.Validate(new X509Certificate2Collection(new X509Certificate2[] { root, intermediate }), leaf));
    }

    [Fact]
    public void Validate_ReturnsFalse_WhenOnlyTheIntermediateIsBundled_NoRootTrustAnchor()
    {
        var (_, intermediate, leaf) = TestCertificates.CreateRootIntermediateAndLeaf();

        // An intermediate alone (no self-signed root) has nothing for the chain builder
        // to terminate trust at - LdapSettingsEndpoints rejects this at save time, but
        // Validate itself still needs to fail closed against a hand-edited DB row that
        // bypassed that check.
        Assert.False(LdapCertificateTrust.Validate(new X509Certificate2Collection(intermediate), leaf));
    }

    [Fact]
    public void Validate_ReturnsTrue_WhenEitherOfTwoBundledSelfSignedCertificatesIsPresented()
    {
        // The DC-certificate-rotation scenario: two unrelated self-signed certificates
        // pinned side by side so either one validates during the rotation window.
        var oldCert = TestCertificates.CreateSelfSigned(subject: "CN=old-dc.corp.example.com");
        var newCert = TestCertificates.CreateSelfSigned(subject: "CN=new-dc.corp.example.com");
        var bundle = new X509Certificate2Collection(new X509Certificate2[] { oldCert, newCert });

        Assert.True(LdapCertificateTrust.Validate(bundle, oldCert));
        Assert.True(LdapCertificateTrust.Validate(bundle, newCert));
    }

    [Fact]
    public void IsTrustAnchor_ReturnsTrue_ForSelfSignedCertificate()
    {
        Assert.True(LdapCertificateTrust.IsTrustAnchor(TestCertificates.CreateSelfSigned()));
    }

    [Fact]
    public void IsTrustAnchor_ReturnsFalse_ForCertificateIssuedByAnotherCertificate()
    {
        var (_, leaf) = TestCertificates.CreateCaAndLeaf();

        Assert.False(LdapCertificateTrust.IsTrustAnchor(leaf));
    }
}
