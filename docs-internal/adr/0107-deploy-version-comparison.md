# ADR-0107: Deploy / version comparison

Status: accepted

## Context

"Did my deploy break anything?" is answered today by reading the Services, Errors and Logs pages
side by side and remembering what each looked like before. Every span and log already carries the
`service.version` resource attribute, so the data to compare two deploys exists; what is missing is
the comparison.

## Decision

- **Versions come from `service.version`, no new storage.** `POST /api/services/version-comparison`
  reads `spans` and `logs` for one service over a lookback (7 days by default, 30 at most).
  `VersionComparisonQueryBuilder` lists the versions seen, ordered by first-seen, and builds four
  comparison queries for one pair. Nothing is pre-aggregated: every query filters on `ServiceName`
  (which has a skip index) and a bounded time range.
- **Default pair is newest vs the one first seen before it.** First-seen is the earliest span
  carrying the version inside the lookback. The caller can pick any two versions seen in the window.
- **"New" is decided in SQL.** `HAVING countIf(current) > 0 AND countIf(baseline) = 0` over the
  exception type (span `exception` events), the outbound target (the same external-target and
  database-system expressions as the service drill-down) and the Drain `PatternId` (ADR-0007), so
  only differences leave ClickHouse.
- **Endpoints are entry spans.** Server and consumer spans grouped by name, with count, error count
  and p95 per version. The dashboard flags a regression when the error rate rises by a percentage
  point or more, or p95 rises by a quarter and at least 20 ms. Those two thresholds are constants in
  the component, not settings.
- **Where it shows.** A section in the Services tab's per-service drill-down, under the runtime
  health findings. Rows link to the Traces, Errors and Logs explorers scoped to the service and
  version, over the window the current version was seen in.

## Consequences

- A baseline that stopped sending before the lookback began is invisible, and a current version that
  has a baseline in the window but sent little can make ordinary things look new. The section states
  the window it compared.
- Telemetry without `service.version` is not compared at all.
- Comparing windows of different length (a long-lived baseline against a day-old current) makes call
  counts incomparable; error rate and p95 are the figures to read.
- Rollbacks: ordering is by first-seen, so a rolled-back version keeps its old position and is
  not "newest". Pick it explicitly in the selector.
