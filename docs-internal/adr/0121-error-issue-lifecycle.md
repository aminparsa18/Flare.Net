# ADR-0121: Error issue lifecycle on `/errors`

Status: Accepted

Date: 2026-10-05

## Context

`/errors` groups exception span events by exact `(exception.type, exception.message)` and
lists them, but a group has no state: nobody can say "fixed", "known noise" or "mine", and an
exception-count alert rule (ADR-0022) keeps firing for noise someone already decided to live
with. Triage state is per-instance configuration, not event data, so it doesn't belong in
`spans`.

## Decision

- **One CRUD table, `error_issues`** (migration 0050, plus the cluster variant): a
  `ReplacingMergeTree(UpdatedAt)` with an `IsDeleted` tombstone, read through
  `LatestVersionSql` (ADR-0009, ADR-0074), the same shape as `maintenance_windows` and
  `slos`. It is not an Identity-store table: the data is tiny and per-instance, and every
  other alert-adjacent config table already lives in ClickHouse.
- **Keyed by a fingerprint of the group**, a SHA-256 prefix of `type`, the unit separator
  and `message` (`ErrorIssueFingerprint`), computed server-side. The dashboard matches state
  to groups by `(type, message)` and never sees the key. A group with no row is Open; going
  back to Open with no assignee writes a tombstone rather than a row of defaults.
- **Stored status is Open, Resolved or Ignored.** Regressed is never stored: it is derived at
  read time (`ErrorIssueEvaluator`) so it can't go stale and needs no writer.
- **Regression = recurrence in a version the group wasn't known in.** Resolving snapshots
  the distinct `service.version` values the group appeared in over the last 30 days
  (`KnownVersions`). A later occurrence in any other version reads as Regressed. A group
  that was only ever seen without a version regresses on any later occurrence. A recurrence
  in a known version doesn't: the fix just hasn't been deployed to that instance yet.
- **Ignore can lapse**: forever, until an instant, or after N further occurrences (counted
  from `StatusChangedAt`). Both limits are evaluated at read time, so an expired ignore reads
  Open without a background job.
- **Exception-count alerts skip ignored groups.** `AlertQueryService` and
  `AlertEvidenceQueryService` fetch the effectively-ignored group keys and add
  `concat(type, char(31), message) NOT IN (...)` to the count and the incident summary's top
  groups. The key set is read once per evaluation; an ignore takes effect on the next one.
- **API:** `GET /api/errors/issues` (any authenticated user) and `PUT /api/errors/issues`
  (Member/Admin: ignoring silences alerts), a partial upsert where a null field means "keep".
- The evidence read is one query for all count- or version-sensitive issues, comparing each
  event's time with its own issue's change instant via `transform`.

## Consequences

- The table is read in full on each page load and each alert evaluation. Fine while triaged
  groups number in the hundreds; a group that is ignored for good stays in it until reopened.
- Message-exact grouping means a message with a changing id is a new group and starts Open.
  Resolving or ignoring one doesn't carry to its siblings. Normalizing messages is a
  separate change that would also change the fingerprint.
- Regression only sees versions reported as `service.version`; apps that don't report one
  regress on any recurrence, which can be noisy.
- Ignoring is Member/Admin-only because it changes what pages people. There is no
  per-group audit trail beyond `StatusChangedBy` and the request audit log (ADR-0079).
