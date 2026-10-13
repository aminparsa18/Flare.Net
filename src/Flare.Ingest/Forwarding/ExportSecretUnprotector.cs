using Microsoft.AspNetCore.DataProtection;

namespace Flare.Ingest.Forwarding;

/// <summary>
/// Decrypts values Flare.Api's <c>NotificationSecretProtector</c> stored (ADR-0205/0213): same purpose string and
/// <c>enc:v1:</c> prefix, same shared key ring. A value without the prefix is a row saved before encryption and is
/// returned as is. Ingest only reads; Flare.Api owns writes.
/// </summary>
public sealed class ExportSecretUnprotector(IDataProtectionProvider provider)
{
    private const string Purpose = "Flare.NotificationSecrets.v1";
    private const string Prefix = "enc:v1:";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public string Unprotect(string stored) =>
        stored.StartsWith(Prefix, StringComparison.Ordinal) ? _protector.Unprotect(stored[Prefix.Length..]) : stored;
}
