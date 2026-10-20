# ADR-0258: Normalize exception messages before grouping errors

Status: Accepted

Date: 2026-10-20

## Context

`/errors` grouped exception events by the exact `(exception.type, exception.message)` pair (ADR-0121). A message that
carries an order id, a GUID or a timestamp therefore made a new group per occurrence: the list filled with one-count
groups, triage state (resolve, ignore, assign) applied to a single id, and an ignore never covered the next one.

## Decision

- **Normalize the message in SQL, at query time.** `ErrorMessageNormalizer` is a short ordered list of RE2 rules applied
  with `replaceRegexpAll`: GUIDs to `<guid>`, `0x` hex to `<hex>`, ISO-8601 timestamps to `<ts>`, quoted values to
  `'<s>'` / `"<s>"` (only a quote that opens a token, so "can't" is not a quote), standalone numbers to `<n>`. A number
  inside an identifier (`HTTP2`, `sha256`) is left alone. The group is `(exception.type, normalized message)` everywhere a
  group is keyed: the groups list, the occurrences drill-down, exception-count alerts and their incident summary,
  issue-state evidence and known versions, and release tracking.
- **One implementation, in SQL.** The same expression is used to compute a group's key and to match a request against it,
  so the two cannot disagree, and `ErrorIssueQueryService` canonicalizes a message it is asked to store with a
  `SELECT` of the expression instead of a second C# copy. Normalization is idempotent, so `MatchSql` normalizes both sides
  and a request may carry either a raw message or a group's message. That keeps older alert rules and deep links working.
- **The group's message is the template.** `ExceptionGroup.ExceptionMessage` is the normalized form; each occurrence
  carries its own raw `Message` (last member of `ExceptionOccurrence`, a MemoryPack-safe trailing addition) and the
  occurrences dialog shows it under the service.
- **State is re-keyed once, by migration 0087.** `error_issues.Id` is a hash of the pair, so a row saved under a raw
  message would stop matching. The migration inserts a new version under the new Id for every live row whose message
  changes and tombstones the old Id. When several rows collapse into one group, the most recently updated wins. Rows
  whose message does not change are untouched. A unit test asserts the migration's expression is the normalizer's, in both
  the single-node and cluster files.
- **No switch to turn it off.** The builders are static and pure, and a per-request or per-instance mode would have to
  thread through every one of them and through stored fingerprints. Custom rules and merging groups are the follow-up that
  would give an operator control.

## Consequences

- Grouping changes for everyone on upgrade. A group that used to be several rows is one; two messages that differ only in
  a number are now one group, including ones a person would call different (`HTTP 404` and `HTTP 500`).
- A variable part that is a word (a user name, a host name) is not normalized and still splits groups.
- The expression runs per exception event inside the `ARRAY JOIN`, on queries that were already the heaviest on the
  page. Six regex passes over a short string is small next to the join, but the groups query is slower than before.
