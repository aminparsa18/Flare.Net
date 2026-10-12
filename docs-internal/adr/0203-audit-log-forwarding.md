# ADR-0203: Audit log forwarding

Status: Accepted

Date: 2026-10-12

## Context

[ADR-0202](0202-audit-log-hash-chain-and-export.md) made tampering detectable but left two gaps it named: nothing
anchors the chain outside the Identity database (a rewritten table with recomputed hashes, or truncated newest
events, still verifies), and compliance tooling wants events pushed, not pulled by export.

## Decision

**Optional sinks, configured by `Audit:Forwarding:*`, fed by one background service with a per-sink cursor.**

- **Sinks.** Webhook (`POST` a JSON array of the export rows; optional `X-Flare-Signature: sha256=<HMAC-SHA256 of the
  body>`) and syslog (RFC 5424, facility authpriv, severity info, message = the export row as JSON; TCP with RFC 6587
  octet counting, or UDP). Either, both or neither; empty means off.
- **Delivery.** `AuditForwardingService` polls every `IntervalSeconds` (10), reads events strictly after the sink's
  cursor, oldest first, `BatchSize` (100) at a time, and advances the cursor (`AuditForwardingCursor`, Identity
  migration 0034 Sqlite / 0009 Postgres) only after a successful send. At-least-once and in id order; a failing sink
  retries the same batch next tick and never blocks the other sink. Receivers de-duplicate on `id`.
- **Backfill.** A sink with no cursor receives the whole retained trail. Sending nothing old would leave the receiver
  with a chain that starts mid-way; the first row's `prevHash` anchors it either way.
- **Why the chain needs this.** The shipped copy carries `hash`/`prevHash`, so an operator can verify the external
  copy against the live database, and truncation shows up as a missing tail.
- **Config, not UI.** Sink URLs and secrets are operator-set environment configuration, like `Audit:RetentionDays`,
  not admin-typed values, so the SSRF guard of [ADR-0201](0201-outbound-ssrf-guard.md) is not applied: an internal
  receiver is the normal case. Changing a sink name's destination does not replay; deleting its cursor row does.

## Not included

- **Runtime settings UI and per-sink status.** Failures are logged (`Audit forwarding to {Sink} failed`) only.
- **Multiple Api replicas.** Cursors are shared through the database but nothing elects a sender, so replicas on one
  database each send every event. Documented as: run one Api when forwarding is on.
- **Retries with backoff, dead-letter, TLS client certificates, CEF/LEAF formats.**
- **Syslog over TLS.**

## Consequences

- Plain-text syslog (TCP/UDP) carries actor names, resource ids and redacted field changes; use a trusted network or a
  local relay that adds TLS.
- Events pruned by retention before they were sent are never delivered.
