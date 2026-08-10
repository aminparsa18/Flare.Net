namespace Flare.Identity.IngestKeys;

public interface IIngestApiKeyStore
{
    /// <summary>Creates a new key and returns both the stored record and the raw key
    /// value - the raw value is generated here and only ever returned this once.</summary>
    Task<(IngestApiKey Key, string RawKey)> CreateAsync(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IngestApiKey>> ListAsync(CancellationToken cancellationToken = default);

    Task RevokeAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Every currently-active key's hash. <c>Flare.Ingest</c> polls this on a
    /// timer (see docs/auth.md) to build its in-memory validation cache, rather than
    /// hitting SQLite on every OTLP request - the ingest hot path only ever reads this
    /// cache, never calls this store directly per-request.</summary>
    Task<IReadOnlyList<string>> ListActiveKeyHashesAsync(CancellationToken cancellationToken = default);
}
