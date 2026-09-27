# ADR-0066: Chart a gauge as a counter

Status: Accepted

Date: 2026-09-27

## Context

Untyped Prometheus metrics reach Flare as OTLP Gauges, even when they're
counters (`*_total`). Their points land in `metrics_gauge`, and the Gauge query
averages each bucket, so the chart shows the ever-growing running total. It
never shows the increase, and a process restart looks like a crash to zero.
ADR-0065 left the type out of the admin metadata overrides, because the type
picks which table holds the points. Prior art:
[signoz#7235](https://github.com/SigNoz/signoz/commit/5b6b5bf359a5940c21681639e7f1094a2fa3a5d9).

## Decision

- **A per-metric flag, stored with the metadata override.**
  `MetricMetadataOverrides.TreatAsCounter` (Identity
  `Migrations/0018_metric_treat_as_counter.sql`) is set through the same
  Admin-only `PUT /api/metrics/metadata-overrides`
  (`SetMetricMetadataOverrideRequest.TreatAsCounter`). It's the same kind of
  per-installation correction as a wrong unit, keyed the same way, and
  `DELETE` clears it with the rest. A row may now hold only the flag.
- **A query mode, not a type change.** `MetricQueryRequest.TreatAsCounter`
  makes `MetricSeriesQueryBuilder` run the Sum query shape (ADR-0035's
  windowed, reset-aware increase) against `metrics_gauge`. That table has no
  `AggregationTemporality`/`IsMonotonic` columns, so every row counts as a
  monotonic cumulative sample: the first row adds 0, and a negative delta is a
  reset. Series ranking uses Sum's `max - min` too. The metric keeps its Gauge
  type everywhere else: picker, catalog, alert form.
- **Resolved server-side, before the cache.** A null `TreatAsCounter` means
  "use the admin setting". `/api/metrics/query` fills it in from the override
  store before calling the cached query service, so the cache key has the real
  value and a toggle shows on the next request. An explicit `true`/`false`
  from a caller wins. Clients don't need to know the setting.
- **The response says how it was shaped.**
  `MetricQueryResponse.TreatedAsCounter` tells callers the points are
  Sum-shaped. The dashboard explorer, dashboard panels, the in-app terminal and
  the `flare metric` CLI then use Sum's Rate/Sum/Count modes and reducer.

## Consequences

- Charts only. Gauge alert rules keep evaluating the raw value
  (Last/Min/Max, ADR-0049). A counter-style alert on such a metric would need
  the same mode in `MetricAlertConditionQueryBuilder`. It can be added the same
  way if anyone asks.
- The catalog's Inspect view still classifies these samples as gauge levels.
- Each chart query of a Gauge now reads the override map from SQLite. The map
  is small and read in one query, like `/api/metrics/names` already does.
- A real gauge flagged by mistake charts as nonsense increases. The
  **Counter** badge in the catalog makes that visible, and unchecking fixes it.
