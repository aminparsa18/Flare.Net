# ADR-0248: Grafana deep links in both directions

Status: Accepted

Date: 2026-10-18

## Context

Flare serves Grafana through the Prometheus, Tempo and Loki compatible APIs (ADR-0109, ADR-0217, ADR-0236), so people
move between the two tools while chasing one problem. Neither side could take you to the same trace in the other.

## Decision

- **"Open in Grafana" is an operator setting on the dashboard, not a stored setting.** `PUBLIC_GRAFANA_TRACE_URL` is a
  public runtime variable in `src/dashboard/src/env.ts` (read when the server starts, like `PUBLIC_API_URL`). It is a
  URL template with a `{traceId}` placeholder, so it fits an Explore URL, a Tempo deep link or a Grafana Cloud stack
  alike. The trace page shows the button only when the template is usable.
- **The template is validated in the browser.** `buildGrafanaTraceUrl` (`$lib/grafana-links.ts`) requires the
  placeholder, URL-encodes the trace id, and accepts only `http:`/`https:` results, so a typo cannot become a
  `javascript:` link.
- **Grafana to Flare needs no code.** `/traces/<traceId>`, `/traces/<traceId>?span=<spanId>` (the span deep
  link) and `/sessions/<sessionId>` already open the right page, so the how-to documents data links that build those
  URLs from a field value.

## Consequences

- The template is one per dashboard deployment, not per user or per project. A team with several Grafana instances
  would need a list; nothing asks for that yet.
- Nothing is stored server-side, so no migration, no audit entry and no CLI/Terraform surface.
- The button only covers traces. Sessions and logs have no "Open in Grafana" action, because Grafana has no page for
  a Flare session, and a Loki log link would need a query template of its own.
- Whether the trace id reaches Grafana in the form Tempo expects (32 hex characters) is not checked; the Tempo API
  Flare serves accepts it as stored.
