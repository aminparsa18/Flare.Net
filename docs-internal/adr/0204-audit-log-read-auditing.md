# ADR-0204: Audit log read auditing

Status: Accepted

Date: 2026-10-12

## Context

[ADR-0079](0079-audit-log.md) records state changes only and deliberately skipped reads: most POST endpoints are
read-only queries and would drown the log. Compliance reviewers still ask who looked at telemetry, which can hold
personal data. That is a different volume and a different cost than config changes, so it must be opt-in.

## Decision

**`Audit:RecordReads=true` makes the existing audit middleware also record an allowlisted set of telemetry reads,
collapsed per user.**

- **Allowlist, again.** `AuditActionClassifier.ClassifyRead` names the routes that return stored telemetry: log
  search, aggregate, patterns, query, context and tail; span search and duration percentile; trace and subtree
  views; metric query; error groups and occurrences. Metadata lookups that feed filter pickers (attribute keys,
  facet values, metric names) are excluded. Resource types are `logs`, `traces`, `metrics`, `errors`; actions are
  `query`, `view`, `tail`. A trace view carries the trace id as the resource id.
- **Nothing about the query is stored.** Same rule as ADR-0079: no body, no query string, no filter. A search for an
  email address must not copy the address into the audit table. The event says who, when, which signal and from
  where; the route is the template, not the URL.
- **Dedupe.** `AuditReadThrottle` records one event per (actor, resource type, action, resource id) per
  `Audit:ReadDedupeSeconds` (60; `0` disables). An auto-refreshing dashboard otherwise writes dozens of events a
  minute, each a serialised hash-chain append ([ADR-0202](0202-audit-log-hash-chain-and-export.md)). The state is in
  memory: a restart or a second Api replica can record one extra event, never lose one that has not been seen.
- **Success only.** 2xx, and 101 for the live-tail WebSocket upgrade. A 401/403 read is not a read.
- **Same pipeline.** Read events use the same table, chain, retention, export and forwarding as config changes
  ([ADR-0203](0203-audit-log-forwarding.md)); the dashboard filter lists the four new resource types.

## Not included

- **What was searched.** Deliberate; see above. Operators who need it have the Api request logs.
- **Ingest-side reads and the MCP/CLI path as a separate category.** They reach the same Api routes, so they are
  recorded with `ActorKind` `pat`.
- **Per-user opt-in, or recording only sensitive services.**
- **Dedupe across replicas.**

## Consequences

- Turning it on multiplies audit volume (roughly one event per user per signal per minute of activity) and shares the
  `Audit:RetentionDays` setting; a busy team may want a shorter retention or a forwarding sink.
- Dedupe means the log shows that a user was querying during a window, not how many queries they ran.
