using System.Diagnostics.CodeAnalysis;
using Flare.Identity.IngestKeys;
using Microsoft.Extensions.Options;

namespace Flare.Ingest.Auth;

/// <summary>
/// In-memory cache of active ingest API key hashes, refreshed on a timer from the shared
/// SQLite <see cref="IIngestApiKeyStore"/> (each with its id/name/limits - ADR-0051), plus <see cref="IngestAuthOptions.StaticIngestApiKey"/>
/// if configured - <see cref="IngestApiKeyValidationMiddleware"/> (the actual OTLP hot
/// path) only ever reads this cache, never hits SQLite per request.
/// </summary>
/// <remarks>
/// A revoked SQLite-backed key stays valid for up to <see cref="RefreshInterval"/> after
/// revocation - an accepted trade-off for a self-hosted ingest key, not a bug. The same
/// delay applies to a changed limit (<see cref="IIngestApiKeyStore.UpdateLimitsAsync"/>). Call
/// <see cref="InitializeAsync"/> once at startup (before the app starts accepting
/// traffic - see <c>Program.cs</c>) so the cache isn't empty for the brief window before
/// the hosted-service loop's first tick would otherwise have populated it.
/// </remarks>
public sealed class IngestApiKeyCache(IIngestApiKeyStore store, IOptions<IngestAuthOptions> options, ILogger<IngestApiKeyCache> logger) : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

    private volatile Dictionary<string, IngestKeyCacheEntry> _activeKeys = [];

    /// <summary>Takes the raw presented key (e.g. straight off the <c>Authorization:
    /// Bearer</c> header) and hashes it internally - callers never need to know the hash
    /// scheme, see <see cref="IngestApiKeyHasher"/>.</summary>
    public bool TryGetKey(string rawKey, [NotNullWhen(true)] out IngestKeyCacheEntry? entry) =>
        _activeKeys.TryGetValue(IngestApiKeyHasher.Hash(rawKey), out entry);

    public bool IsValid(string rawKey) => TryGetKey(rawKey, out _);

    public Task InitializeAsync(CancellationToken cancellationToken) => RefreshAsync(cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(RefreshInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            await RefreshAsync(stoppingToken);
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var keys = await store.ListActiveKeysAsync(cancellationToken);
            var map = new Dictionary<string, IngestKeyCacheEntry>(keys.Count + 1, StringComparer.Ordinal);
            foreach (var key in keys)
            {
                map[key.KeyHash] = new IngestKeyCacheEntry(key.Id, key.Name, key.Limits);
            }

            // Merged into the same lookup path rather than a separate check - the static
            // key behaves exactly like any SQLite-backed one from IsValid's point of view,
            // it just isn't individually revocable/named/limited.
            if (!string.IsNullOrEmpty(options.Value.StaticIngestApiKey))
            {
                map.TryAdd(IngestApiKeyHasher.Hash(options.Value.StaticIngestApiKey), IngestKeyCacheEntry.StaticKey);
            }

            _activeKeys = map;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Keep serving the previous (stale but not wrong) set rather than fail
            // ingestion outright on a transient SQLite hiccup.
            logger.LogWarning(ex, "Failed to refresh the ingest API key cache; keeping the previous set.");
        }
    }
}

/// <summary>One cached, valid ingest key. <see cref="KeyId"/> is null only for
/// <see cref="IngestAuthOptions.StaticIngestApiKey"/>, which has no SQLite row and so no
/// limits or usage tracking (ADR-0051).</summary>
public sealed record IngestKeyCacheEntry(Guid? KeyId, string Name, IngestApiKeyLimits Limits)
{
    public static readonly IngestKeyCacheEntry StaticKey = new(null, "static", IngestApiKeyLimits.None);
}
