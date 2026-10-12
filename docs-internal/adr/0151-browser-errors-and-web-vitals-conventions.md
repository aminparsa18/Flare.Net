# ADR-0151: Browser errors and web vitals use existing signals

Status: Accepted

Date: 2026-10-09

## Context

The browser RUM roadmap item needs JavaScript errors and Core Web Vitals in Flare.
OTel-JS has no stable error or web-vitals instrumentation, and Faro uses its own wire
format. Flare's `/errors` page already groups `exception` span events
([ADR-0022](0022-exception-count-alerting.md)), and the Metrics page, dashboards and alerts
already handle histograms.

## Decision

- **JS errors are spans with an exception event.** The browser reports each uncaught error
  or unhandled rejection as a short `js.error` span using `span.recordException`. No new
  table, endpoint or query: the errors appear on `/errors`, can be filtered with the
  `telemetry.sdk.language = webjs` resource attribute, and can drive exception-count
  alerts. Non-`Error` throwables have no `exception.type` and are not grouped; the how-to
  tells users to wrap them in `Error`.
- **Web vitals are histograms named `browser.web_vital.<lcp|inp|cls|fcp|ttfb>`**, with the
  vital's rating as the `rating` attribute and explicit bucket boundaries chosen by the
  caller. The Metrics page, dashboards, formulas and metric alerts work on them
  unchanged. A built-in **Web vitals** dashboard template charts them.
- Faro and other non-OTLP formats stay out of scope; they would need a collector in front.

## Not decided here

Symbolicating minified stacks (source-map upload) and a dedicated Frontend page that
combines page loads, vitals and errors are separate items. Per-page-route breakdown of
vitals needs a convention for a route attribute and is left to that Frontend page.

## Consequences

No backend change and no migration. The convention is documented in the browser how-to
and is not enforced: a differently named metric simply does not fill the template.
Histogram metrics are per-service, not per-user-session, so a single slow page view
cannot be traced back from a vital until the Frontend page links them.
