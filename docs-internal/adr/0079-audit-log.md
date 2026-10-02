# ADR-0079: Audit log

Status: Accepted

Date: 2026-10-02

## Context

Nothing recorded who changed an alert rule, notification channel, dashboard,
pipeline rule, maintenance window, user role, personal access token or auth
setting, or when. For a shared instance that is the first question after an
alert stops firing or a login setting changes. Prior art:
[signoz#10791](https://github.com/SigNoz/signoz/commit/42415e08739c4e8237e7856c0661f30638d68cd1).

## Decision

**An append-only `AuditEvents` table in the Identity SQLite database (Identity
migration 0019), written by one middleware, read by an admin-only endpoint and
page.**

- **Storage.** Identity SQLite, not ClickHouse. It is low volume, must survive
  independently of the telemetry store, and the Api already owns that file.
  Rows are insert-only (a trigger aborts `UPDATE`); retention is the only
  delete.
- **Capture.** `AuditMiddleware` runs after authentication and authorization,
  lets the request execute, and on a 2xx records an event if
  `AuditActionClassifier` recognizes the route *template*. One place instead
  of ad hoc calls in 30 handlers, and rejected requests (401/403/400) are never
  recorded because nothing changed.
- **Allowlist, not "every non-GET".** Most POST endpoints here are read-only
  queries (`/api/logs/search`, `/api/metrics/query`, ...), as are test sends
  and previews. The classifier's rule table names each state-changing route.
  The cost is that a new mutating endpoint is unaudited until someone adds it
  there; the classifier tests pin the table.
- **Fields.** Timestamp, actor id and name (copied, so the row outlives the
  account), how they authenticated (`session` or `pat`), action, resource type
  and id, `METHOD route-template`, status code, source IP. A resource id comes
  from the route, or from the handler via `AuditContext.SetResourceId` for
  creates (the id only exists after the insert).
- **Read.** `GET /api/audit-events` on `adminRoutes`: filters `from`, `to`,
  `actorId`, `resourceType`, `action`; newest first, keyset-paged on the
  autoincrement id (`before`), so a retention prune never shifts pages.
  Dashboard page `/audit-log`, reachable from the user menu.
- **Retention.** `Audit:RetentionDays`, default 365, `0` keeps forever. An
  hourly hosted service prunes.
- **Failure mode.** If writing the audit row fails, the error is logged and the
  response is unchanged. The change already happened; turning it into a 500
  would invite a retry that repeats it.

## Not included

- **Before/after diff.** Needs per-resource field comparison and secret
  redaction; tracked in the roadmap.
- **Sign-ins and failed attempts.** Different question (security events, not
  config changes); login throttling already has its own store.
- **Cluster-wide ordering.** Identity SQLite is single-writer by design
  (ADR-0004), so ids are totally ordered.

## Consequences

- A request body is never stored, so no secret reaches the audit table.
- Every audited request pays one SQLite insert after the response is produced
  but before it completes; these are admin/config calls, not the query path.
