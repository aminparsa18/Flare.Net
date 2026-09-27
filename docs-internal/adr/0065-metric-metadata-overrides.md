# ADR-0065: Admin overrides for a metric's unit and description

Status: Accepted

Date: 2026-09-27

## Context

The Metrics catalog, the metric picker, and chart axes show the unit and
description the instrumentation sent. These are often missing or wrong. A
Prometheus-bridged metric arrives with no unit, a library reports `ms` for a
value in seconds, or a description is blank. Operators usually can't change
the emitting code. Prior art:
[signoz#7235](https://github.com/SigNoz/signoz/commit/5b6b5bf359a5940c21681639e7f1094a2fa3a5d9),
which also lets the metric *type* and temporality be edited.

## Decision

- **Storage is Identity's embedded SQLite** (`MetricMetadataOverrides`,
  `Migrations/0017_metric_metadata_overrides.sql`), keyed by `MetricName`. It
  is per-installation config, not telemetry, which is the same reasoning and
  the same shape as `ApdexThresholds` (ADR-0032). No ClickHouse migration.
- **Each member overrides independently.** `Unit` and `Description` are
  nullable. Null means "show the emitted value", so correcting a unit never
  freezes today's description. A `PUT` with both blank is rejected. Removing
  an override is `DELETE`.
- **Applied on read, after the query and after the cache.**
  `MetricMetadataOverlay` merges the override map into
  `/api/metrics/catalog`, `/api/metrics/catalog/detail`, and
  `/api/metrics/names`. For names, the merge runs after
  `CachingMetricQueryService`, so an edit shows on the next request instead of
  after a cache entry expires. The map is small and read in one query.
- **Admin-only writes, no separate read route.**
  `PUT`/`DELETE /api/metrics/metadata-overrides` sit on `adminRoutes`, the
  same "global, cross-user setting" rule as Apdex thresholds. The metric name
  goes in the body or query string, not the path, because OTel instrument
  names may contain `/`. Readers get overridden values from the endpoints
  above. The detail response also carries `EmittedUnit`/`EmittedDescription`
  and `HasMetadataOverride` so the editor can show what a reset restores.

## Not overriding the type

SigNoz can change a metric's type because all its points share one table
with the type as a column. In Flare, the type decides which table the points
live in (`metrics_gauge`/`_sum`/`_histogram`/`_exponential_histogram`, see
`db/clickhouse/0008_metrics.sql`). Relabeling a Gauge as a Sum wouldn't move
its data. It would need a query mode that differences a gauge table's values
like a counter. That is a real feature (a "treat as counter" option for
untyped Prometheus gauges), not a metadata edit, so it stays on the roadmap.

## Consequences

- An override applies to every service emitting that metric name. A name
  whose services disagree on meaning can't be split. That was already a
  naming problem.
- Alert and notification text doesn't use metric units today, so nothing
  there changes.
- Overrides live in the Identity SQLite file, so they're backed up and moved
  with the rest of the instance config.
