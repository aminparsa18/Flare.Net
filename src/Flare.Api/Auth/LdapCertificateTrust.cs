using System.Security.Cryptography.X509Certificates;

namespace Flare.Api.Auth;

/// <summary>
/// Custom-trust-anchor chain validation backing LDAP certificate pinning (see
/// docs/auth.md's "Active Directory (LDAP)" section). When an Admin has pasted a PEM
/// certificate into <see cref="Identity.Auth.LdapSettings.PinnedCertificatePem"/>, this is
/// the *only* trust root <c>LdapAuthEndpoints.CreateConnection</c>'s
/// <c>SessionOptions.VerifyServerCertificate</c> callback accepts - the OS/container
/// trust store is bypassed entirely for that connection. Extracted as a static,
/// connection-free method so it's unit-testable without a real directory, same reasoning
/// as <c>LdapFilterEncoder.Escape</c>.
/// </summary>
public static class LdapCertificateTrust
{
    /// <param name="pinnedCertificate">Parsed from
    /// <see cref="Identity.Auth.LdapSettings.PinnedCertificatePem"/> - either an internal
    /// CA's root certificate, or the domain controller's own certificate if
    /// self-signed.</param>
    /// <param name="serverCertificate">The certificate the LDAPS handshake actually
    /// presented - <c>System.DirectoryServices.Protocols</c>' <c>VerifyServerCertificate</c>
    /// delegate hands this over as a plain <see cref="X509Certificate"/>, not
    /// <see cref="X509Certificate2"/>.</param>
    public static bool Validate(X509Certificate2 pinnedCertificate, X509Certificate serverCertificate)
    {
        using var presented = new X509Certificate2(serverCertificate);
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(pinnedCertificate);
        // No CRL/OCSP endpoint exists to check a private CA's (or a self-signed cert's
        // own) revocation status against - pinning already means "possession of the
        // private key is the whole trust model," not "and also stays externally
        // revocable."
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

        // CustomRootTrust handles both target cases identically: a leaf certificate
        // chaining up to the pinned root (private CA), and a self-signed leaf that *is*
        // the pinned certificate (chain of length 1). An expired presented certificate
        // fails closed here - NotTimeValid remains a checked chain status - deliberately;
        // pinning isn't an "ignore expiry" escape hatch.
        return chain.Build(presented);
    }
}
