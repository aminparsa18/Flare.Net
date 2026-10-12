# ADR-0187: Regressions per release

Status: Accepted

Date: 2026-10-11

## Context

[ADR-0182](0182-release-tracking.md) lists the error groups a release introduced but deferred "which resolved groups came
back in this release", which [ADR-0121](0121-error-issue-lifecycle.md) already derives as the Regressed status.

## Decision

- **No new storage or query.** A release's regressions are the issues whose effective status is Regressed and whose
  `RegressedVersion` equals the release's version. The release endpoints read them through `IErrorIssueQueryService`
  (the evidence query ADR-0121 already runs), not through `ReleaseQueryService`, so the alert worker, which also uses
  that service, gains no dependency.
- **API:** `GET /api/releases/errors` gains `regressions` (type, message, when it was resolved, assignee). The list gains
  `regressionCount` under the same condition as `newErrorCount`: only when scoped with `?service=`.
- **Surfaces:** a Regressions column and a regressions block above the new errors in the expanded row on the Releases
  page, and a column in `flare releases list`.

## Consequences

- Issues are keyed by exception, not service, so the match is on the version string only: two services releasing the
  same version number share its regressions. Telemetry without `service.version` regresses under the empty version and
  matches no release.
- Only groups someone resolved can regress; a new error is never both new and regressed.
- The list reads every triaged issue and its evidence once per load, the same cost as opening the Errors page.
