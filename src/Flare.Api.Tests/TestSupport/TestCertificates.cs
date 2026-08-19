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
}
