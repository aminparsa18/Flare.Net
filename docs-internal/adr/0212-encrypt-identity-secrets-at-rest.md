# ADR-0212: Encrypt identity-store secrets at rest

Status: Accepted

Date: 2026-10-13

## Context

[ADR-0205](0205-encrypt-notification-secrets-at-rest.md) encrypted notification credentials and left the rest of the
secret-bearing stores for an audit. The audit found four more plaintext columns in the Identity database
(SQLite or Postgres): `EntraSettings.ClientSecret`, `OidcSettings.ClientSecret`, `LdapSettings.BindPassword` and
`SourceLinks.AccessToken`. The API already masks them in every response, but a copy of the database or a backup
exposed them. Only Flare.Api reads these stores.

## Decision

- **Decorate the Identity stores in Flare.Api** (`ProtectedEntraSettingsStore`, `ProtectedOidcSettingsStore`,
  `ProtectedLdapSettingsStore`, `ProtectedSourceLinkStore`). They encrypt on save and decrypt on read with the
  ADR-0205 `NotificationSecretProtector` (same `enc:v1:` prefix, same Data Protection key ring), so endpoints, the
  OIDC/Entra option configurators and the LDAP bind keep working with plaintext and `Flare.Identity` gains no
  dependency. "Null keeps the stored value" and "empty clears" are preserved, because `Protect` leaves both alone.
- **No migration.** Unprefixed values read as plaintext; `IdentitySecretBackfillService` re-saves them once at API
  startup (idempotent, failure only logs).
- The purpose string stays `Flare.NotificationSecrets.v1` so one protector serves both; the name is historical.

## Consequences

- The same key-ring caveats as ADR-0205 apply: losing the Redis ring makes these secrets unrecoverable and they must be
  re-entered; the ring is not itself encrypted.
- Telemetry-export config is covered separately by [ADR-0213](0213-encrypt-telemetry-export-secrets-at-rest.md).
