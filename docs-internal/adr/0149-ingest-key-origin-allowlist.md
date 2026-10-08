# ADR-0149: Browser-origin allowlist on ingest keys

Status: Accepted

Date: 2026-10-08

## Context

Browser telemetry (OTel-JS, Faro) sends OTLP/HTTP cross-origin, so the receiver needs
CORS, and the ingest key it uses sits in page JavaScript where every visitor can read
it. [ADR-0051](0051-per-ingest-key-ingestion-limits.md) caps what a key can send but not where it can
be used from. The mobile SDK item needs the same public-key story.

## Decision

- **Per-key `AllowedOrigins`** (Identity migration Sqlite `0030` / Postgres `0005`,
  newline-separated, NULL = unrestricted). Origins are `scheme://host[:port]`,
  normalized to lower case by `IngestKeyOrigins`; `*` and paths are rejected. Set with
  `PUT /api/ingest-keys/{id}/origins` (Admin, audited); an empty list lifts it.
- **Enforcement** in `IngestApiKeyValidationMiddleware`: a restricted key is a browser
  key. A request whose `Origin` header is missing or unlisted gets `403`, so the key is
  also refused from curl and server exporters. The header is spoofable outside a browser,
  so this limits where a leaked key works from a web page, not who can send with it.
  Limits (ADR-0051) remain the abuse backstop.
- **CORS** is one dynamic policy on the OTLP/HTTP receiver, answering preflight (which
  carries no key) for an origin in `Otlp:AllowedOrigins` or listed on any active key.
  Which key may use which origin is checked on the real request. The static key has no
  row and cannot be restricted.
- Origin checks only run when `IngestKeyRequired` is on, like every other key check.

## Not decided here

A per-key service allowlist (stopping a public key from writing as another service)
needs a check in each receiver after parsing and is a separate change. Until then a
leaked browser key can write under any service name.

## Consequences

Existing keys behave as before. Changes reach Ingest within the key cache refresh
interval (30 s), like limits and revocation. The dashboard UI and CLI for editing
origins are not built; use the API.
