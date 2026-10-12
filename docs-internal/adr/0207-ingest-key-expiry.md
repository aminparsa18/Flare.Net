# ADR-0207: Ingest key expiry

Status: Accepted

Date: 2026-10-12

## Context

Ingest keys lived until someone revoked them; personal access tokens already had an optional `ExpiresAt`. A leaked or
forgotten collector key therefore worked forever, and there was nothing to warn an operator that rotation was due.

## Decision

- **Optional `IngestApiKeys.ExpiresAt`** (UTC ISO-8601 text, NULL = never), migration `0035` (SQLite) and `0010` (Postgres).
  Existing keys are untouched. It is set at creation (`expiresAtUnixMs`) or changed with `PUT /api/ingest-keys/{id}/expiry`;
  a past time is refused, so a key expires only by time passing or by revoking it.
- **Enforced per request in Ingest, not at cache refresh.** `ActiveIngestApiKey`/`IngestKeyCacheEntry` carry `ExpiresAt` and
  `IngestApiKeyCache.TryGetKey` compares it with the clock, so a key stops working at its expiry rather than up to the 30 s
  refresh interval later (revocation keeps the 30 s delay). Expired keys stay in the cache (a refresh does not drop them):
  they are still listed as non-revoked, which is what lets an extension bring one back.
- **Expired is not revoked.** `RevokedAt` stays null; the API reports `isActive=false` for an expired key, its name is free
  to reuse, and setting a new future expiry makes it usable again. A revoked key stays revoked.
- **Warning in the dashboard.** The Ingest keys page shows an Expires column, an *Expires in Nd* badge for the last 14 days,
  *Expired* afterwards, and a one-click extend by 90 days. Wire format: `expiresAt` is appended last on `IngestApiKeyDto`;
  requests use Unix milliseconds because `DateTimeOffset` cannot carry the TypeScript generator.

## Consequences

- No notification is sent before expiry; operators see it only on the page. A notification channel hook is future work.
- The static `Ingest:StaticIngestApiKey` never expires. `flare config` export/apply and `flare apikey create` do not carry the
  expiry yet. Personal access token expiry warnings are not covered.
