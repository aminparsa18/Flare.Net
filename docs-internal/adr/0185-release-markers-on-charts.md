# ADR-0185: Release markers on charts

Status: Accepted

Date: 2026-10-11

## Context

[ADR-0182](0182-release-tracking.md) records deploys but left them off the charts, so a step change in a graph still
has to be matched to a deploy by hand.

## Decision

- **Dashboard only, no API change.** The Logs volume chart and the Metrics chart load `GET /api/releases` (once per
  service in the explorer's service filter, all services when it is empty) and draw a dashed vertical line per release
  that falls inside the chart's range. A failed load draws nothing.
- **Placement follows each chart's x axis.** The volume chart is linear in time across the requested window; the metrics
  chart is linear in bucket *index*, so a release is interpolated between its two neighbouring buckets.
- **Explorer pages opt in (`releaseMarkers`).** Dashboard panels do not, because each would add a request per panel.
- **At most 12 markers**, newest first, so a chatty pipeline cannot cover the chart.

## Consequences

- Panels on custom dashboards and the Traces charts show no markers.
- The marker list is fetched per chart mount and service filter change, not on every auto-refresh, so a release marked
  while the page is open appears on the next filter change or reload.
