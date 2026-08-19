using System.Security.Cryptography.X509Certificates;

namespace Flare.Api.Auth;

/// <summary>
/// Custom-trust-anchor chain validation backing LDAP certificate pinning (see
/// docs/auth.md's "Active Directory (LDAP)" section). When an Admin has pasted one or
/// more PEM certificates into
/// <see cref="Identity.Auth.LdapSettings.PinnedCertificatePem"/>, this is the *only*
/// trust material <c>LdapAuthEndpoints.CreateConnection</c>'s
/// <c>SessionOptions.VerifyServerCertificate</c> callback accepts - the OS/container
/// trust store is bypassed entirely for that connection. Extracted as a static,
/// connection-free method so it's unit-testable without a real directory, same reasoning
/// as <c>LdapFilterEncoder.Escape</c>.
/// </summary>
public static class LdapCertificateTrust
{
    /// <summary>True when <paramref name="certificate"/> is self-signed (its own issuer),
    /// i.e. it's usable as a trust anchor by itself - a CA's root certificate, or a
    /// domain controller's own certificate pinned directly. An intermediate CA
    /// certificate (issued by some other CA) returns false here - it's path-building
    /// material for the chain builder, not something that can terminate a chain on its
    /// own.</summary>
    public static bool IsTrustAnchor(X509Certificate2 certificate) =>
        certificate.SubjectName.RawData.AsSpan().SequenceEqual(certificate.IssuerName.RawData);

    /// <param name="pinnedCertificates">Parsed from
    /// <see cref="Identity.Auth.LdapSettings.PinnedCertificatePem"/> - a bundle (same
    /// concatenated-PEM-blocks convention as any CA bundle file), not necessarily a
    /// single certificate. Covers three admin-facing scenarios uniformly: pinning a
    /// self-signed DC certificate directly; pinning a private CA's root plus the
    /// intermediate(s) that actually signed the DC's leaf certificate (a bare root alone
    /// can't complete that chain - there's no guaranteed AIA fetch to pull a missing
    /// intermediate in a container with restricted egress); and pinning two DC
    /// certificates side by side to ride out a certificate-rotation window. Certificates
    /// are sorted by <see cref="IsTrustAnchor"/>: self-signed ones become trust anchors
    /// (<see cref="X509ChainPolicy.CustomTrustStore"/>), everything else is intermediate
    /// path-building material (<see cref="X509ChainPolicy.ExtraStore"/>). A bundle with
    /// no self-signed certificate at all has no trust anchor and always fails closed -
    /// <c>LdapSettingsEndpoints</c> rejects that at save time rather than letting it
    /// surface as a mysterious login failure later.</param>
    /// <param name="serverCertificate">The certificate the LDAPS handshake actually
    /// presented - <c>System.DirectoryServices.Protocols</c>' <c>VerifyServerCertificate</c>
    /// delegate hands this over as a plain <see cref="X509Certificate"/>, not
    /// <see cref="X509Certificate2"/>.</param>
    public static bool Validate(X509Certificate2Collection pinnedCertificates, X509Certificate serverCertificate)
    {
        using var presented = new X509Certificate2(serverCertificate);
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        // No CRL/OCSP endpoint exists to check a private CA's (or a self-signed cert's
        // own) revocation status against - pinning already means "possession of the
        // private key is the whole trust model," not "and also stays externally
        // revocable."
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

        foreach (var pinnedCertificate in pinnedCertificates)
        {
            if (IsTrustAnchor(pinnedCertificate))
            {
                chain.ChainPolicy.CustomTrustStore.Add(pinnedCertificate);
            }
            else
            {
                chain.ChainPolicy.ExtraStore.Add(pinnedCertificate);
            }
        }

        // CustomRootTrust handles all target cases identically: a leaf certificate
        // chaining (directly, or through one or more ExtraStore intermediates) up to a
        // pinned root, and a self-signed leaf that *is* one of the pinned certificates
        // (chain of length 1). An expired presented certificate fails closed here -
        // NotTimeValid remains a checked chain status - deliberately; pinning isn't an
        // "ignore expiry" escape hatch.
        return chain.Build(presented);
    }
}
