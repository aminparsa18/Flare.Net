using Flare.Identity.IngestKeys;

namespace Flare.Ingest.Tests.Auth.TestSupport;

/// <summary>In-memory <see cref="IIngestApiKeyStore"/> - same "fake the interface, no
/// real datastore" convention as Flare.Api.Tests' FakeUserStore/FakeSessionStore.</summary>
internal sealed class FakeIngestApiKeyStore : IIngestApiKeyStore
{
    private readonly Dictionary<Guid, IngestApiKey> _keysById = [];
    private readonly Dictionary<Guid, string> _rawKeysById = [];

    public Task<(IngestApiKey Key, string RawKey)> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        var key = new IngestApiKey(Guid.NewGuid(), name, DateTimeOffset.UtcNow, RevokedAt: null);
        var rawKey = Guid.NewGuid().ToString("N");
        _keysById[key.Id] = key;
        _rawKeysById[key.Id] = rawKey;
        return Task.FromResult((key, rawKey));
    }

    public Task<IReadOnlyList<IngestApiKey>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<IngestApiKey>>(_keysById.Values.ToList());

    public Task RevokeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (_keysById.TryGetValue(id, out var key))
        {
            _keysById[id] = key with { RevokedAt = DateTimeOffset.UtcNow };
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListActiveKeyHashesAsync(CancellationToken cancellationToken = default)
    {
        var hashes = _keysById.Values
            .Where(k => k.IsActive)
            .Select(k => IngestApiKeyHasher.Hash(_rawKeysById[k.Id]))
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(hashes);
    }
}
