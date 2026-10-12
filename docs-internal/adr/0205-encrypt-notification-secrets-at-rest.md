# ADR-0205: Encrypt notification secrets at rest

Status: Accepted

Date: 2026-10-12

## Context

Notification channels (`notification_channels`) and the legacy per-rule fields on `alert_rules` hold live
credentials: webhook URLs (a Slack/Teams/Discord webhook URL is itself the credential), Telegram bot tokens,
PagerDuty routing keys, Jira API tokens, incident.io tokens and JSM Ops API keys. The API masks them in every
response ([`NotificationSecrets`](../../src/Flare.Api/Alerting/NotificationSecrets.cs)), but the columns held them
as plaintext, so anyone with ClickHouse read access, a backup or a table export saw them.

## Decision

**Secrets are encrypted with ASP.NET Core Data Protection at the query-service boundary.**

- `NotificationSecretProtector` wraps `IDataProtector` (purpose `Flare.NotificationSecrets.v1`) and stores
  `enc:v1:<ciphertext>`. `NotificationChannelQueryService` and `AlertQueryService` protect on insert and unprotect
  on read, so every caller (endpoints, notifiers, the alert worker, export/import) keeps working with plaintext
  objects and no column or migration changes.
- **No migration for existing rows.** A value without the prefix is read as plaintext. `NotificationSecretBackfillService`
  re-saves rows that still hold plaintext once at API startup; it is idempotent and failure only logs.
- **Same key ring as the signed ack links.** Flare.Api and Flare.AlertWorker persist it to Redis
  ([ADR-0127](0127-alert-ack-link.md)), so the worker can decrypt what the API wrote.
- **Rotation.** Data Protection creates a new key every 90 days by default and keeps old keys for decryption. A
  secret is re-encrypted under the newest key the next time its channel is saved; nothing becomes unreadable on
  rotation.

## Consequences

- Losing the Redis key ring makes stored secrets unrecoverable; they must be re-entered. Back the ring up with
  Redis persistence.
- The ring itself is not encrypted at rest unless an `IXmlEncryptor` is configured. Encryption here protects the
  ClickHouse data (backups, exports, read access to the table), not an attacker who also holds Redis.
- Telemetry export credentials (`telemetry_exports.ConfigJson`) and other stores are not covered; they follow the
  same pattern if wanted.
