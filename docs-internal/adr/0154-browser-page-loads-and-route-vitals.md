# ADR-0154: Page loads and per-route vitals on the Frontend page

Status: Accepted

Date: 2026-10-09

## Context

[ADR-0153](0153-frontend-page-and-source-map-settings.md) left two items open: page-load counts
and per-route vitals. The OTel document-load instrumentation emits spans only, and ADR-0151
left the route attribute undecided.

## Decision

- **Route attribute is `url.template`** on the `browser.web_vital.*` data points: the low-cardinality
  route pattern (`/orders/:id`), never the raw URL. It is the semconv key
  [ADR-0071](0071-external-api-monitoring.md) already reads for HTTP endpoints. Expanding a service
  row on `/frontend` queries that service's vitals grouped by it (one query per vital, window-wide
  bucket, top 200 routes). Without the attribute the row says no breakdown is available.
- **Page loads = the FCP sample count.** FCP is reported once per page load, so no new metric or
  trace aggregate is needed. Loads where FCP never fires (background tabs) are not counted.

## Consequences

Cardinality is the caller's job: a route attribute holding raw URLs multiplies series; the
how-to says to use the template. The page-load figure undercounts pages that never paint.
No backend change or migration.
