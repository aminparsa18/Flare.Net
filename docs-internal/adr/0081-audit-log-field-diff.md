# ADR-0081: Audit log field diff

Status: Accepted

Date: 2026-10-02

## Context

[ADR-0079](0079-audit-log.md) records who changed a resource and when, but not
what changed. After an alert stops firing the next question is "what did they
edit?". ADR-0079 deliberately stored no request body so no secret could reach
the table; a diff has to keep that property.

## Decision

**Handlers report a before and after snapshot; the diff and the redaction run
before anything is stored, and only the redacted list of changed fields is
persisted in a new `AuditEvents.Changes` column (Identity migration 0020).**

- **Handler-reported, not middleware-captured.** The middleware cannot see the
  prior state, and re-reading the request body would store whatever the client
  sent. Each update handler calls `AuditContext.SetChange(http, before, after)`
  with the resource it loaded before the update and the one the update
  returned. Handlers that already load the row (dashboards, for the ownership
  check) reuse it; the others add one `GetAsync`.
- **Snapshots are the resource's own response DTO** (source-generated type
  info), or for the singleton auth settings and ingest-key limits the settings
  record itself (reflection). Settings records are used rather than their
  DTOs because the DTOs expose `hasClientSecret`, not the secret, so a
  rotated secret would not differ. The snapshot lives only in memory.
- **`AuditDiff`** is pure: both snapshots become JSON, nested objects are
  flattened to dotted paths, arrays and scalars compare as whole values.
  `updatedAt` is ignored (every save bumps it). At most 50 changed fields,
  values cut at 300 characters.
- **Redaction by field name.** A field whose last path segment contains
  `secret`, `password`, `token`, `url`, `key`, `authorization`, `credential`,
  `webhook`, `bearer` or `dsn` has both sides replaced by `[redacted]`. The
  comparison happens before redaction, so a changed secret still shows as a
  change and an unchanged one does not. The list is deliberately broad: a
  false positive hides a harmless value, a false negative leaks a secret.
- **Scope: updates of alert, notification-channel, maintenance-window,
  pipeline-rule, dashboard, saved-view, user (role, disabled), ingest-key
  limits, and the five auth settings.** Creates and deletes keep a null
  `Changes`: a create's content is the resource itself, which is already
  readable, and a delete's content would be a full snapshot of a possibly
  large dashboard. The API returns `changes: []` for those.

## Not included

- **Apdex thresholds, metric metadata overrides** and the other small
  allowlisted routes do not report a diff yet.
- **Create/delete snapshots.**
- **Per-resource field labels or localized names.** The page shows the JSON
  field path as stored.

## Consequences

- A new secret-bearing field is hidden only if its name matches the list; a
  field named `passphrase` would leak. `AuditDiffTests` pins the markers.
- Each audited update adds one read before the write.
- Rows written before migration 0020 have no `Changes` and show a dash.
