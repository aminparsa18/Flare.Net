# ADR-0253: Export a dashboard as Grafana JSON, with queries

Status: Accepted

Date: 2026-10-19

## Context

Importing a Grafana dashboard into Flare is structural only: a Grafana panel's query is PromQL, LogQL or whatever its own
data source speaks, and Flare had no engine for those. That is no longer true in the other direction. Flare serves
Prometheus, Loki and Tempo compatible APIs ([ADR-0109](0109-prometheus-query-api.md),
[ADR-0236](0236-loki-compatible-log-api.md), [ADR-0217](0217-tempo-compatible-trace-api.md)), so a Flare panel's saved query
can be written as a query those APIs answer, and the exported dashboard works in Grafana pointed at Flare. The roadmap listed
"export a Flare dashboard as Grafana JSON" as the next step after those APIs.

## Decision

- **Client-side, next to the Flare JSON export.** `buildGrafanaDashboard` (`dashboards/grafana-export.ts`) is a pure function
  from the dashboard and the metric catalog to Grafana JSON; `DashboardsState.exportGrafanaDashboard` fetches the catalog and
  downloads the file. No endpoint, no migration, no CLI or Terraform surface. The Dashboards list gets a second icon beside
  Export.
- **Queries are translated, not reset.**
  - Metrics: the OTel name becomes the Prometheus name with the rules of `PromNames` (sanitised, unit suffix, `_total` for a
    Sum, `_bucket`/`_sum`/`_count` for a histogram). The saved query holds only the OTel name, so the unit comes from
    `POST /api/metrics/names` over the last 30 days; if that call fails the export still happens, with unit-less names, and says so.
    A gauge is `avg by (...)`, a Sum `sum by (...) (increase(... [$__interval]))`, a histogram `histogram_quantile` at p50/p95/p99
    (p95 when grouped), the `_sum`/`_count` mean for Value/Pie/Table/Bar, and `increase` of the buckets as a heatmap for
    Histogram/Heatmap. Group by and Top N become a `by` label and `topk`. A formula is parsed with the Explorer's own parser and
    written as PromQL arithmetic over the operand queries.
  - Logs: a stacked bar of `sum by (detected_level) (count_over_time(<selector> [$__interval]))` (or by the grouped attribute),
    with the filters written as a LogQL selector and pipeline using the label mapping of the Loki API.
  - Traces: a Tempo table panel running a TraceQL search.
  - Text: a Markdown text panel.
- **Data sources are `__inputs`.** Each used kind (`DS_FLARE_PROMETHEUS`, `DS_FLARE_LOKI`, `DS_FLARE_TEMPO`) is declared so
  Grafana's import screen asks which data source to use, and only the ones a panel needs are declared.
- **Variables stay variables.** Each Flare variable becomes a Grafana variable (`label_values` for a Query source, the typed
  values for Custom, an "All"-plus-free-text custom variable for Textbox and for Span attributes, which have no label list)
  and is added as a matcher to exactly the panels it narrows in Flare, minus the panels that opted out. Chained variables
  narrow their option query by the parent. "All" is the regex `.+`. A repeating panel uses Grafana's `repeat`.
- **Layout.** Grid positions are rescaled from Flare's 12 columns to 24. Rows become Grafana rows (collapsed rows nest their
  panels); each Flare row's grid is relative, so panel `y` is offset by the rows above it.
- **Presentation settings that have a Grafana form are carried:** units (OTel to Grafana units), decimals, soft axis bounds,
  log scale, stacking, thresholds (Flare's ordered operator rules are probed into Grafana's ascending steps), legend
  placement and format, the reducer, and the default time range. Flare's alert incidents and marked deploys
  ([ADR-0252](0252-alert-and-release-annotation-metrics.md)) are added as annotations when the dashboard reads Prometheus.
- **What does not translate is dropped, and said so.** A panel lists what was left out (post-processing functions,
  previous-period and time-shift overlays, the having filter, structural trace queries, entry-spans-only, N+1 and duration
  bucket filters, scope-bag span attributes, has/not-has JSON body filters, `runningDiff`) in its Grafana description, and the
  UI reports how many panels were partial. Approximations are named too.
- **Parse-tested.** `GrafanaDashboardExportQueryTests` feeds the shapes the exporter writes to the real `PromQlParser`,
  `LokiQlParser` and `TraceQlParser`.

## Consequences

- Two approximations are deliberate. A Metrics panel without Group by is drawn per service, because Flare's per-attribute-set
  lines would be hundreds of series in Grafana; and a severity filter that selects part of a level matches the whole
  `detected_level`.
- A Service variable narrows within a panel's own saved service list (both matchers apply), where Flare replaces the list with
  the selection. They agree whenever the selected service is one of the panel's own, and for panels with no service list.
- Importing the exported file back into Flare is the structural Grafana import again; there is no round trip, and the Flare
  JSON export remains the backup format.
- The exporter duplicates the metric name mapping of `PromNames` in TypeScript. The unit-suffix table must be kept in step
  with it.
- Exemplars, the Traces Drilldown and per-series colours are not exported.

## Not decided here

A Grafana data source plugin for sessions, errors and release health, a `flare dashboards export --format grafana` CLI form,
and exporting a single panel.
