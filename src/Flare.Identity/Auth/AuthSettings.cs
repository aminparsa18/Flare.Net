namespace Flare.Identity.Auth;

/// <summary>
/// The global authentication on/off switch, configured through the dashboard's
/// Admin-only (or, while <see cref="Enabled"/> is false, open-to-anyone) <c>/auth</c>
/// screen - see docs/auth.md. When <see cref="Enabled"/> is false, every authorization
/// check in <c>Flare.Api</c> short-circuits to allowed (see
/// <c>ConditionalAuthorizationMiddlewareResultHandler</c>) - a fresh Flare instance has
/// no login requirement at all until an Admin turns this on.
/// </summary>
/// <param name="LocalEnabled">Whether local username/password login is one of the active
/// methods - its own toggle, symmetric with Entra/LDAP's own <c>Enabled</c> flags, so an
/// org that's fully migrated to SSO can eventually turn it off. Defaults to true (the
/// natural first/bootstrap method).</param>
public sealed record AuthSettings(bool Enabled, bool LocalEnabled, DateTimeOffset? UpdatedAt);
