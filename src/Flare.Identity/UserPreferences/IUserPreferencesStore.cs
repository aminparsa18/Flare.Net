namespace Flare.Identity.UserPreferences;

/// <summary>
/// Per-user UI preference documents (ADR-0110), stored as opaque JSON keyed by preference
/// group (e.g. <c>appearance</c>). The server never interprets the value; the dashboard owns
/// its shape and validates it on read.
/// </summary>
public interface IUserPreferencesStore
{
    /// <summary>The stored JSON for <paramref name="key"/>, or null if the user has none.</summary>
    Task<string?> GetAsync(Guid userId, string key, CancellationToken cancellationToken = default);

    /// <summary>Stores (replacing any existing) the JSON document for <paramref name="key"/>.</summary>
    Task SetAsync(Guid userId, string key, string json, CancellationToken cancellationToken = default);

    /// <summary>Removes the document for <paramref name="key"/>. A no-op if none exists.</summary>
    Task DeleteAsync(Guid userId, string key, CancellationToken cancellationToken = default);
}
