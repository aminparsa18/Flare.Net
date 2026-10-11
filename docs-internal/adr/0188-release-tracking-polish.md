# ADR-0188: Release tracking polish

Status: Accepted

Date: 2026-10-11

## Context

[ADR-0185](0185-release-markers-on-charts.md), [ADR-0186](0186-release-error-alerting.md) and
[ADR-0187](0187-release-regressions.md) left three gaps: dashboard panels drew no deploy markers, a release-errors alert
did not say which release, and regressions matched a release on version alone.

## Decision

- **Panel markers.** Dashboard Metrics and Logs panels pass `releaseMarkers`. To avoid one request per panel, marker loading
  shares a single unscoped `GET /api/releases` for 30 seconds and filters by service in the browser (the unscoped list
  also skips the span scan that the scoped list's new-error counts need). Traces charts still draw none.
- **Release named in the alert.** `AlertEvaluationWorker` sets the label `flare.release` (`"web 1.4.2"`) on the in-memory
  rule of a `ReleaseErrors` fire. The stored rule is untouched. The notification text appends it, and templates and
  payloads get it as `{{labels.flare.release}}`. It names the release with the most new groups.
- **Regressions per service.** A regressed group (ADR-0121) now counts for a release only if it occurred in that service
  under that version: one extra query per service (`BuildGroupsSeenInService`, the given type/message pairs, from a day before
  the earliest deploy). This replaces ADR-0187's version-only match.

## Consequences

- A release marked while a dashboard is open appears within 30 seconds of the next chart load, not instantly.
- A regressed group whose recurrence is older than a day before the deploy is not matched to the release.
- `flare.release` is a reserved label name in practice; a user label with that key is overwritten on a fire.
