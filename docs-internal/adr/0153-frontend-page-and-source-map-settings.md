# ADR-0153: Frontend page and source-map settings

Status: Accepted

Date: 2026-10-09

## Context

[ADR-0151](0151-browser-errors-and-web-vitals-conventions.md) put browser errors and web vitals
on existing signals, and [ADR-0152](0152-source-map-upload.md) left a management page for
uploaded maps undecided. The roadmap's last two browser items were a Frontend page and that
maps view.

## Decision

- **`/frontend` is a read-only composition of existing queries, with no endpoint of its own.**
  Per vital it issues two `POST /api/metrics/query` histogram queries over one window-wide bucket:
  grouped by a nonexistent attribute (one series per service, giving the p75), and grouped by
  `rating` (giving the share of samples the browser rated good). It also calls
  `POST /api/errors/groups` with the resource filter `telemetry.sdk.language = webjs`. Cells are
  coloured against Google's p75 thresholds (LCP 2.5 s/4 s, INP 200/500 ms, CLS 0.1/0.25,
  FCP 1.8/3 s, TTFB 0.8/1.8 s). A service appears if it reported any vital.
- **Errors link to `/errors`** through the existing `?state=` deep link, scoped to the browser
  resource filter and the loaded window, so triage, occurrences and symbolication stay on one page.
- **`/settings/source-maps`** lists maps grouped by release (service + version) with a bundle
  drill-down and delete per release or bundle. Delete is shown to Admins (or everyone while auth
  is off), matching the API. Upload stays CLI/CI only. It uses the endpoint's plain JSON, not
  MemoryPack: the list is small and has no generated TS type.
- The page is in the user menu's **More** list, not the top bar, like other secondary pages.

## Not decided here

Page-load metrics (the OTel document-load spans have no metric form, and a count from spans would
need a trace aggregate query) and per-route vitals (ADR-0151 left the route attribute convention
open). Both stay on the roadmap. Per-service error counts are not shown: `/api/errors/groups`
returns affected services as a list without per-service counts.

## Consequences

Ten metric queries and one errors query run on each load or window change. They are bounded by the usual query caps. A web-vitals series with no `rating` attribute
shows the p75 without the good-share line. No migration, no API change.
