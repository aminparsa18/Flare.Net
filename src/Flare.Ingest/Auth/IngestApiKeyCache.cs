using Flare.Identity.IngestKeys;
using Microsoft.Extensions.Options;

namespace Flare.Ingest.Auth;

/// <summary>
/// In-memory cache of active ingest API key hashes, refreshed on a timer from the shared
/// SQLite <see cref="IIngestApiKeyStore"/>, plus <see cref="IngestAuthOptions.StaticIngestApiKey"/>
/// if configured - <see cref="IngestApiKeyValidationMiddleware"/> (the actual OTLP hot
/// path) only ever reads this cache, never hits SQLite per request.
/// </summary>
/// <remarks>
/// A revoked SQLite-backed key stays valid for up to <see cref="RefreshInterval"/> after
/// revocation - an accepted trade-off for a self-hosted ingest key, not a bug. Call
/// <see cref="InitializeAsync"/> once at startup (before the app starts accepting
/// traffic - see <c>Program.cs</c>) so the cache isn't empty for the brief window before
/// the hosted-service loop's first tick would otherwise have populated it.
/// </remarks>
public sealed class IngestApiKeyCache(IIngestApiKeyStore store, IOptions<IngestAuthOptions> options, ILogger<IngestApiKeyCache> logger) : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

    private volatile HashSet<string> _activeHashes = [];

    /// <summary>Takes the raw presented key (e.g. straight off the <c>Authorization:
    /// Bearer</c> header) and hashes it internally - callers never need to know the hash
    /// scheme, see <see cref="IngestApiKeyHasher"/>.</summary>
    public bool IsValid(string rawKey) => _activeHashes.Contains(IngestApiKeyHasher.Hash(rawKey));

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
            var hashes = await store.ListActiveKeyHashesAsync(cancellationToken);
            var set = new HashSet<string>(hashes, StringComparer.Ordinal);

            // Merged into the same set/lookup path rather than a separate check - the
            // static key behaves exactly like any SQLite-backed one from IsValid's point
            // of view, it just isn't individually revocable/named.
            if (!string.IsNullOrEmpty(options.Value.StaticIngestApiKey))
            {
                set.Add(IngestApiKeyHasher.Hash(options.Value.StaticIngestApiKey));
            }

            _activeHashes = set;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Keep serving the previous (stale but not wrong) set rather than fail
            // ingestion outright on a transient SQLite hiccup.
            logger.LogWarning(ex, "Failed to refresh the ingest API key cache; keeping the previous set.");
        }
    }
}
