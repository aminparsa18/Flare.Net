# ADR-0213: Encrypt telemetry-export secrets at rest

Status: Accepted

Date: 2026-10-13

## Context

`telemetry_exports.ConfigJson` holds forwarding-target header values (typically `Authorization` tokens) and the S3
archive access/secret keys in plaintext. [ADR-0205](0205-encrypt-notification-secrets-at-rest.md) and
[ADR-0212](0212-encrypt-identity-secrets-at-rest.md) covered the other secret stores. Unlike those, forwarding targets
are read by Flare.Ingest directly from ClickHouse, and Ingest had no Data Protection key ring.

## Decision

- `TelemetryExportQueryService` protects every forwarding header value and the archive `AccessKey`/`SecretKey` inside the
  JSON it writes, and unprotects them on read, with the ADR-0205 protector (same purpose and `enc:v1:` prefix). Callers
  (endpoints, masking, Flare.AlertWorker's archive reads) still see plaintext.
- **Flare.Ingest joins the shared key ring**: it registers Data Protection with the same application name and Redis key as
  Flare.Api and decrypts header values through `ExportSecretUnprotector`, a read-only counterpart of the protector.
  Flare.Api stays the only writer.
- **No migration.** Unprefixed values read as plaintext; `NotificationSecretBackfillService` re-saves legacy rows once at
  API startup (idempotent, failure only logs).

## Consequences

- Ingest now needs Redis key-ring access at forwarding-target load time (it already depends on Redis). If it cannot
  decrypt a header, target loading fails and the target is not forwarded until the ring is available.
- Same key-ring caveats as ADR-0205: losing the ring makes the stored values unrecoverable; the ring itself is not
  encrypted.
