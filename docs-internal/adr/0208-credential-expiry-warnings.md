# ADR-0208: Credential expiry warnings

Status: Accepted

Date: 2026-10-12

## Context

[ADR-0207](0207-ingest-key-expiry.md) made ingest keys expire, and personal access tokens already had an optional expiry,
but the only warning was a badge on the Ingest keys page. A key that nobody opens the page to look at simply stopped working.

## Decision

- **A background service in Flare.Api** (`CredentialExpiryWorker`) checks ingest keys and personal access tokens every
  `CredentialExpiry:CheckInterval` (6 hours) and sends a message through the configured notification channels. It lives in
  Flare.Api, not Flare.AlertWorker, because the Identity stores are registered there.
- **Off until configured.** `CredentialExpiry:ChannelIds` lists saved notification channels; empty sends nothing, since there is
  no sensible default recipient. Only webhook/Slack, Telegram, email, Teams and Discord channels are used (the same set as
  status-page announcements), never PagerDuty or ticketing channels.
- **One warning per credential per threshold.** `CredentialExpiry:WarnDays` (default 14 and 3) are the thresholds. A
  credential gets the smallest threshold that covers the time it has left, so one first seen 2 days out gets the "3 days"
  warning once. A Redis `SET NX` on `flare:credential-expiry:{kind}:{id}:{expiresAtMs}:{threshold}` (60-day TTL) records it,
  which covers restarts and several Flare.Api replicas without a migration. The expiry is part of the key, so extending a
  credential re-arms its warnings.
- **Sent as a synthetic alert rule** with literal title and body, like status-page incidents, so no channel needs
  expiry-specific code. Personal access token warnings name the owner.
- **Personal access tokens page** shows the same *Expires in Nd* badge as the Ingest keys page for the last 14 days.
- **CLI and config.** `flare apikey create --expires-in-days N` sets the expiry at creation. `flare config export` includes
  `expiresAt` for ingest keys, and `apply` sets it through `PUT /api/ingest-keys/{id}/expiry` when the file names one. A file
  without `expiresAt` leaves the key's expiry alone, so files written before this change don't clear expiries.

## Consequences

- Warnings go to shared channels, not to the token's owner by email. Per-owner email is not done.
- Redis losing its data repeats warnings once. The warning for an already-expired credential is never sent.
- A new `IPersonalAccessTokenStore.ListWithExpiryAsync` scans all non-revoked tokens that have an expiry; fine at the
  expected token counts.
