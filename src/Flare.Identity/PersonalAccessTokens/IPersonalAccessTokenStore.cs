namespace Flare.Identity.PersonalAccessTokens;

public interface IPersonalAccessTokenStore
{
    /// <summary>Creates a new token owned by <paramref name="userId"/> and returns both
    /// the stored record and the raw token value - the raw value is generated here and
    /// only ever returned this once. <paramref name="expiresAt"/> null means the token
    /// never expires.</summary>
    Task<(PersonalAccessToken Token, string RawToken)> CreateAsync(
        Guid userId, string name, DateTimeOffset? expiresAt, CancellationToken cancellationToken = default);

    /// <summary>Every token owned by <paramref name="userId"/>, newest first - self-service
    /// scope, so there's deliberately no "list every user's tokens" method here (see
    /// <see cref="Flare.Api.Endpoints.PersonalAccessTokenEndpoints"/>'s remarks for why an
    /// admin oversight view isn't in scope yet).</summary>
    Task<IReadOnlyList<PersonalAccessToken>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Looks up a single token by id regardless of owner, so the endpoint layer
    /// can check ownership itself before calling <see cref="RevokeAsync"/> (the same
    /// separation of "storage" from "who's allowed to do this" used throughout
    /// Flare.Api's endpoint files). Null if no such token exists.</summary>
    Task<PersonalAccessToken?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    Task RevokeAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Validates a raw <c>Authorization: Bearer</c> value against the stored
    /// hash and returns the owning token if it exists, isn't revoked, and hasn't expired -
    /// null otherwise. Does NOT itself update <see cref="PersonalAccessToken.LastUsedAt"/> -
    /// callers throttle that the same way <see cref="Auth.SessionAuthenticationHandler"/>
    /// already throttles <see cref="Auth.ISessionStore.TouchLastSeenAsync"/> (see
    /// <see cref="TouchLastUsedAsync"/>). Called on Flare.Api's query-API auth path -
    /// unlike <see cref="IngestKeys.IIngestApiKeyStore"/>'s ingest side, there's no
    /// in-memory polled cache in front of this: query-API bearer-token traffic (scripts/CI,
    /// not a browser on every click) is nowhere near OTLP ingest's QPS, so a direct SQLite
    /// lookup per request is fine.</summary>
    Task<PersonalAccessToken?> ValidateAsync(string rawToken, CancellationToken cancellationToken = default);

    /// <summary>Bumps <see cref="PersonalAccessToken.LastUsedAt"/> to now. Callers should
    /// throttle how often this is invoked per token (e.g. at most once a minute) - it's
    /// for an admin/self-facing "last used" display only.</summary>
    Task TouchLastUsedAsync(Guid id, CancellationToken cancellationToken = default);
}
