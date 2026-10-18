# ADR-0241: Metric exemplars: ingest and the Prometheus `query_exemplars` endpoint

Status: Accepted

Date: 2026-10-17

## Context

An exemplar is a sample measurement an SDK keeps next to a metric point together with the trace and span that were
active when it was recorded. It is what turns a latency chart into a path to a real slow request: Grafana draws
exemplars as dots on the series and links each to the trace. The .NET SDK emits them for histograms and counters
recorded inside an Activity, and OTLP carries them on every point type.

Flare dropped them at ingest, so the Prometheus-compatible API (ADR-0109, ADR-0239) could not offer the one feature
that connects its metrics to its traces. The roadmap named exemplars as the remaining gap.

## Decision

- **Ingest keeps exemplars that carry a trace id.** `OtlpMetricsMapper` maps each point's exemplars (value, time,
  trace id and span id as lowercase hex, filtered attributes) and drops the ones without a trace id, since there is
  nothing to link to. At most 50 per point; an SDK keeps about one per bucket.
- **A separate `metric_exemplars` table** (migration 0086, single-node and cluster), one row per exemplar, not a
  Nested column on the four metrics tables. Exemplars are read by trace link and never aggregated into a chart, so
  they should not widen the hot metric rows, and one point carries several. Each row repeats the point's
  `MetricName`, `PointType`, `ServiceName`, `ResourceAttributes` and `DataPointAttributes`, so a selector's label
  matchers select exemplars the same way they select series. It has `_retention_days` and is listed under the Metrics
  retention signal, so retention rules and per-resource TTLs cover it; erasure requests cover it too.
- **Written by the metric flush path.** `ClickHouseMetricWriter` inserts the exemplar rows after the four point
  tables in the same batch, so a failure retries the whole batch (at-least-once, like every other write there).
  `MetricPointRecord` gains an `Exemplars` member on each concrete point type, last, not on the base: MemoryPack writes
  base members first, so a base member would shift every derived field and break entries an older Ingest already
  buffered in Redis.
- **`GET|POST /api/v1/query_exemplars?query=&start=&end=`** returns Prometheus's shape: per series
  `{seriesLabels, exemplars: [{labels: {trace_id, span_id, ...}, value, timestamp}]}`. Every selector in the query
  (inside `rate`, aggregations, operators) is resolved the way a series query resolves it, with its matchers applied.
  Exemplar labels are `trace_id` and `span_id` plus the filtered attributes, sanitized like any label. Histogram
  `_sum` and `_count` carry none; a histogram's exemplars are returned on `<name>_bucket`. At most 1000 are returned,
  newest first, with a warning when more matched.
- `PromEvaluator` takes the exemplar store as an optional dependency, so every existing caller and test is
  unchanged, and the series fetch's equality push-down is shared with the exemplar lookup.

## Alternatives considered

- **Store exemplars in the metrics rows** (a `Nested` column). Rejected: it widens every point read by a chart for
  data a chart never uses, and the histogram tables already carry arrays.
- **Keep exemplars without a trace id.** Rejected: their only job is the link. The filtered attributes of such an
  exemplar are still in the point's own data.
- **Add a `le` label to histogram exemplars.** Not done: the exemplar value places it on a bucket, and Grafana does not
  need it to draw the dot.

## Consequences

- Grafana's Prometheus data source can show exemplars, with **Exemplars** enabled and `trace_id` mapped to a Tempo (or
  Flare's Tempo-compatible) data source, and open the trace.
- Existing data has no exemplars; they appear for points ingested after the migration.
- Exemplars from the Prometheus scrape path are not captured: the exposition parser ignores OpenMetrics exemplar
  comments. Only OTLP carries them.
- The dashboard does not show exemplars yet. That is a UI step on the metric chart, left on the roadmap.
