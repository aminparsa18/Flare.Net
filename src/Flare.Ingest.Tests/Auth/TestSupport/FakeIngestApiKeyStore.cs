using Flare.Identity.IngestKeys;

namespace Flare.Ingest.Tests.Auth.TestSupport;

/// <summary>In-memory <see cref="IIngestApiKeyStore"/> - same "fake the interface, no
/// real datastore" convention as Flare.Api.Tests' FakeUserStore/FakeSessionStore.</summary>
internal sealed class FakeIngestApiKeyStore : IIngestApiKeyStore
{
    private readonly Dictionary<Guid, IngestApiKey> _keysById = [];
    private readonly Dictionary<Guid, string> _rawKeysById = [];

    public Task<(IngestApiKey Key, string RawKey)> CreateAsync(string name, Guid? projectId = null, CancellationToken cancellationToken = default)
    {
        var key = new IngestApiKey(Guid.NewGuid(), name, DateTimeOffset.UtcNow, RevokedAt: null) { ProjectId = projectId };
        var rawKey = Guid.NewGuid().ToString("N");
        _keysById[key.Id] = key;
        _rawKeysById[key.Id] = rawKey;
        return Task.FromResult((key, rawKey));
    }

    public Task<IReadOnlyList<IngestApiKey>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<IngestApiKey>>(_keysById.Values.ToList());

    public Task<bool> SetProjectAsync(Guid id, Guid? projectId, CancellationToken cancellationToken = default)
    {
        if (!_keysById.TryGetValue(id, out var key))
        {
            return Task.FromResult(false);
        }
        _keysById[id] = key with { ProjectId = projectId };
        return Task.FromResult(true);
    }

    public Task<bool> RenameAsync(Guid id, string name, CancellationToken cancellationToken = default)
    {
        if (!_keysById.TryGetValue(id, out var key))
        {
            return Task.FromResult(false);
        }
        _keysById[id] = key with { Name = name };
        return Task.FromResult(true);
    }

    public Task RevokeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (_keysById.TryGetValue(id, out var key))
        {
            _keysById[id] = key with { RevokedAt = DateTimeOffset.UtcNow };
        }
        return Task.CompletedTask;
    }

    public Task<bool> UpdateLimitsAsync(Guid id, IngestApiKeyLimits limits, CancellationToken cancellationToken = default)
    {
        if (!_keysById.TryGetValue(id, out var key))
        {
            return Task.FromResult(false);
        }
        _keysById[id] = key with { Limits = limits };
        return Task.FromResult(true);
    }

    public Task<bool> SetAllowedOriginsAsync(Guid id, IReadOnlyList<string> origins, CancellationToken cancellationToken = default)
    {
        if (!_keysById.TryGetValue(id, out var key))
        {
            return Task.FromResult(false);
        }
        _keysById[id] = key with { AllowedOrigins = origins };
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<ActiveIngestApiKey>> ListActiveKeysAsync(CancellationToken cancellationToken = default)
    {
        var keys = _keysById.Values
            .Where(k => k.IsActive)
            .Select(k => new ActiveIngestApiKey(k.Id, k.Name, IngestApiKeyHasher.Hash(_rawKeysById[k.Id]), k.Limits) { AllowedOrigins = k.AllowedOrigins })
            .ToList();
        return Task.FromResult<IReadOnlyList<ActiveIngestApiKey>>(keys);
    }
}
