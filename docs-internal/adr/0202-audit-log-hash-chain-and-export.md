# ADR-0202: Audit log hash chain and export

Status: Accepted

Date: 2026-10-12

## Context

[ADR-0079](0079-audit-log.md) made the audit trail append-only with a trigger
that rejects `UPDATE`, but anyone with write access to the Identity database
can still `DELETE` a row or drop the trigger, and nothing would show it.
Compliance reviewers also ask for the trail in a form they can load into a
spreadsheet or a SIEM. Forwarding to a syslog/webhook sink and recording read
access to telemetry are separate pieces of the same roadmap item and are not
part of this decision.

## Decision

**Each new event stores `PrevHash` and `Hash` (Identity migration 0033 Sqlite,
0008 Postgres); `GET /api/audit-events/verify` walks the chain; and
`GET /api/audit-events/export` streams the trail as CSV, JSON or NDJSON.**

- **Hash.** `Hash = SHA-256(PrevHash, timestamp, actorId, actorName, actorKind,
  action, resourceType, resourceId, route, statusCode, sourceIp, changes)`,
  lower-case hex. Every field is length-prefixed (`5:alert`) and a null is `-`,
  so two different rows cannot produce the same bytes. The inputs are the
  stored column text (round-trip UTC timestamp, lower-case guid, the redacted
  `changes` JSON string), so an offline verifier can recompute from an export.
  `Id` is not hashed: it does not exist until the insert.
- **Serialised appends.** An append reads the newest `Hash` and inserts the next
  link inside one transaction. A process-wide semaphore covers a single Api;
  on Postgres a `pg_advisory_xact_lock` also covers several replicas sharing
  the database. Without it two concurrent writers would both link to the same
  parent and fork the chain. The cost is one extra indexed read per audited
  request, on admin/config calls only.
- **Verify.** Oldest to newest in batches of 1000. A chained row must link to the
  previous row's hash and recompute to its own. The first chained row anchors
  the walk, because retention deletes the rows before it, so pruning does not
  break verification. A row with no hash after the chain has started is
  reported as broken. The response names the first broken id and returns the
  head hash.
- **Legacy rows.** Rows written before the migration have null hashes. They
  are not backfilled (rewriting them would defeat the point) and are reported
  as a count, not as a failure.
- **Export.** Same filters as the list endpoint, keyset-paged internally in
  pages of 500 so memory stays flat. `ndjson` is one event per line for SIEM
  ingest. JSON and NDJSON rows carry the canonical timestamp text and the raw
  `changes` string so hashes can be recomputed. CSV prefixes an apostrophe to
  cells starting with `=`, `+`, `-`, `@`, tab or CR (actor names and resource
  ids are user-influenced), which means CSV is for reading, not for
  re-verifying. Exports are not themselves audited.

## Not included

- **Detecting truncation of the newest events.** A chain only proves what is
  between its ends. Verify returns `headHash` so an operator can record it
  elsewhere; automatic anchoring comes with the forwarding sink.
- **Signing.** Someone who can rewrite the whole table and recompute every hash
  can forge a valid chain. Defence against that is shipping events to a system
  they cannot write to, which is the forwarding item left on the roadmap.
- **Syslog/webhook forwarding and read auditing** stay on the roadmap.

## Consequences

- Audit writes are serialised across the process. Fine at admin-call volume.
- A deleted-then-recreated Identity database starts a new chain; that is
  indistinguishable from a fresh install, same as before.
