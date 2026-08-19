using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Flare.Api.Tests.TestSupport;

/// <summary>Generates throwaway X.509 certificates for LDAP certificate-pinning tests
/// (see <see cref="Flare.Api.Auth.LdapCertificateTrust"/>) - hermetic, no external cert
/// files or real PKI infrastructure needed.</summary>
internal static class TestCertificates
{
    /// <summary>A self-signed certificate, usable either as its own pin (chain length 1)
    /// or as an unrelated cert to exercise a pin mismatch.</summary>
    public static X509Certificate2 CreateSelfSigned(string subject = "CN=test-dc.corp.example.com", TimeSpan? validity = null)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = notBefore + (validity ?? TimeSpan.FromDays(365));
        return request.CreateSelfSigned(notBefore, notAfter);
    }

    /// <summary>An internal-CA root plus a leaf certificate it signed - exercises
    /// <see cref="Flare.Api.Auth.LdapCertificateTrust.Validate"/>'s "chains up to the
    /// pinned CA" path, distinct from pinning a self-signed certificate directly.</summary>
    public static (X509Certificate2 Ca, X509Certificate2 Leaf) CreateCaAndLeaf(
        string caSubject = "CN=Test Internal CA",
        string leafSubject = "CN=test-dc.corp.example.com")
    {
        using var caKey = RSA.Create(2048);
        var caRequest = new CertificateRequest(caSubject, caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, critical: true));
        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = DateTimeOffset.UtcNow.AddYears(5);
        var ca = caRequest.CreateSelfSigned(notBefore, notAfter);

        using var leafKey = RSA.Create(2048);
        var leafRequest = new CertificateRequest(leafSubject, leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var serial = RandomNumberGenerator.GetBytes(16);
        var leaf = leafRequest.Create(ca, notBefore, notAfter, serial);

        return (ca, leaf);
    }

    /// <summary>A root CA, an intermediate (issuing) CA it signed, and a leaf certificate
    /// the intermediate signed - the real two-tier private-CA shape
    /// <see cref="Flare.Api.Auth.LdapCertificateTrust.Validate"/>'s bundle support exists
    /// for: a bare pinned root alone can't complete this chain, since the intermediate
    /// has to come from somewhere (ExtraStore, in production from the same pinned PEM
    /// bundle as the root).</summary>
    public static (X509Certificate2 Root, X509Certificate2 Intermediate, X509Certificate2 Leaf) CreateRootIntermediateAndLeaf(
        string rootSubject = "CN=Test Root CA",
        string intermediateSubject = "CN=Test Issuing CA",
        string leafSubject = "CN=test-dc.corp.example.com")
    {
        using var rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest(rootSubject, rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, critical: true));
        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = DateTimeOffset.UtcNow.AddYears(10);
        var root = rootRequest.CreateSelfSigned(notBefore, notAfter);

        using var intermediateKey = RSA.Create(2048);
        var intermediateRequest = new CertificateRequest(intermediateSubject, intermediateKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        intermediateRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: true, pathLengthConstraint: 0, critical: true));
        intermediateRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, critical: true));
        var intermediateSerial = RandomNumberGenerator.GetBytes(16);
        var intermediate = intermediateRequest.Create(root, notBefore, notAfter, intermediateSerial);

        using var leafKey = RSA.Create(2048);
        var leafRequest = new CertificateRequest(leafSubject, leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var leafSerial = RandomNumberGenerator.GetBytes(16);
        // CertificateRequest.Create's issuer overload needs the issuer's private key to
        // sign with - intermediate.Create(root, ...) above only returned the public half,
        // so re-attach intermediateKey before using it to sign the leaf.
        using var intermediateWithKey = intermediate.CopyWithPrivateKey(intermediateKey);
        var leaf = leafRequest.Create(intermediateWithKey, notBefore, notAfter, leafSerial);

        return (root, intermediate, leaf);
    }
}
