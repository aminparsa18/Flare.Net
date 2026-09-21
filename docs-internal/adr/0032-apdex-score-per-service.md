# ADR-0032: Apdex score per service, computed live with a per-service SQLite threshold

Status: Accepted

Date: 2026-09-21

## Context

`docs-internal/planning/roadmap.md`'s "Apdex score per service" item flagged
that Flare has no Apdex support: the Traces page's Services tab (Table
view) shows Rate/Errors/Duration but nothing summarizing whether a
service's actual latency is "good" from a user's perspective. Apdex
(Application Performance Index) needs two things: a per-service
satisfaction threshold T (an admin-tunable value, since "good" latency
means something different for a checkout API than a background worker),
and a live classification of each root span's duration into
satisfied (`<= T`), tolerating (`T < duration <= 4T`), or frustrated
(`> 4T`), reduced to `(satisfied + tolerating / 2) / total`.

The Services tab's RED metrics were recently split (ADR-0030) into a live
query over `spans` and a pre-aggregated read from the `service_metrics`
materialized view, specifically to avoid a full `spans` scan on every
10-second poll. That pre-aggregation only stores `RequestCount`/
`ErrorCount` sums and `quantile()` *states* (`P50State`/`P95State`/
`P99State`) - none of which let you reclassify a duration against an
arbitrary threshold after the fact. Since T is a per-service setting an
admin can change at any time, any pre-aggregation keyed to a threshold
baked in at write time would silently go stale (or need a full
historical recompute) the moment someone edits it.

## Decision

**Apdex is computed as a separate, always-live ClickHouse query
(`ServiceApdexQueryBuilder`), run unconditionally alongside the existing
RED-metrics query - not folded into `service_metrics`'s pre-aggregation,
and not gated by `ServiceMetricsOptions.Enabled`.** Concretely:

- `ServiceApdexQueryBuilder.Build` produces one `GROUP BY ServiceName`
  query over `spans`, using the same window/root-span (`ParentSpanId =
  ''`)/resource-attribute-filter predicates as
  `ServiceOverviewQueryBuilder`, but computing only
  `countIf(DurationNano <= T) AS ApdexSatisfiedCount` and
  `countIf(DurationNano > T AND DurationNano <= T * 4) AS
  ApdexToleratingCount`. The denominator (frustrated count is implicit)
  reuses the same query's own `RequestCount` rather than a third
  `count()` here, so the two queries can never disagree on a service's
  total.
- T varies per service, so it isn't a single bound parameter: it's a
  ClickHouse `multiIf(ServiceName = svc0, t0, ServiceName = svc1, t1,
  ..., default)` expression built from every configured override, each
  service name/threshold still bound as a real parameter (never
  string-interpolated).
- `ServiceOverviewQueryService.GetOverviewAsync` runs this query
  unconditionally (regardless of whether the RED-metrics half takes the
  live or pre-aggregated path) and joins its counts onto each
  `ServiceMetrics` row by `ServiceName`. The math itself
  (`ApdexScoreCalculator.Calculate`) is a pure static, split out the same
  way `BuildMetrics` already is, purely for unit testing.
- **Threshold storage is Identity's embedded SQLite**
  (`ApdexThresholds` table, `Migrations/0015_apdex_thresholds.sql`), not
  ClickHouse - per-installation config, not telemetry, same category as
  `AuthSettings`. It's the first Identity table keyed by a plain string
  business key (`ServiceName`) rather than a `Guid Id`: there's no
  natural surrogate key, since `ServiceName` *is* the identity of the
  thing being configured. Only overrides are stored; a service with no
  row uses `ServiceApdexQueryBuilder.DefaultThresholdMs` (500ms, the
  classic APM default).
- `GET /api/services/apdex-thresholds` is Viewer-readable (same
  `authenticatedRoutes` group as the rest of the Services tab - a Viewer
  needs the configured value to understand the score they're looking
  at). `PUT`/`DELETE /api/services/apdex-thresholds/{serviceName}` are
  Admin-only (`adminRoutes`), same "mutating a global, cross-user
  setting is Admin-only" reasoning `AuthSettingsEndpoints` already
  establishes.

Scope is deliberately limited to the Table view, same scoping precedent
ADR-0030 already set for that same tab: the Map view's dependency graph
and per-node call breakdown don't get an Apdex score.

## Alternatives considered

- **Extend `service_metrics_mv` with fixed exponential-latency-bucket
  counts** (e.g. 1/2/5/10/20/50/100/200/500ms/... buckets), approximating
  any T at read time by summing the buckets nearest T and 4T. This would
  keep Apdex inside the existing pre-aggregation architecture, at the
  cost of turning an exact ratio into a bucket-rounded approximation and
  a materially bigger migration to an already-shipped materialized view.
  Rejected for now - the plain live query is simpler, exact, and (unlike
  the percentile query it doesn't replace) cheap: two `countIf`s per row
  scanned, not a t-digest quantile estimator. Worth revisiting if this
  query ever shows up as a real cost in practice.
- **Bake a fixed, non-overridable default threshold into
  `service_metrics_mv`,** falling back to a live query only for services
  with an explicit override (mirroring how a resource-attribute filter
  already forces the live RED-metrics path). Rejected: a global default
  change would still invalidate every pre-aggregated row silently, and
  the added branching complexity isn't worth it for a query this cheap.

## Consequences

- Every Services-tab Table-view load (and its 10-second poll) now always
  issues one additional live `spans` scan for Apdex, even when the
  RED-metrics half is served from the pre-aggregated `service_metrics`
  table. This is an accepted, documented cost - same "document the known
  tradeoff" precedent ADR-0030 itself sets for its own window-rounding
  behavior - not a silent regression of ADR-0030's optimization, since
  the RED-metrics query itself is unaffected.
- No instant rollback valve (no `ApdexOptions.Enabled`-style toggle):
  unlike `service_metrics`, there's no pre-aggregated alternative to fall
  back to, so disabling this query would just mean not showing Apdex at
  all. If this needs a kill switch later, it's a small addition.
- `ApdexThresholds` has no seeded rows and no backfill - a fresh install
  simply has every service at the 500ms default until an admin
  overrides one, same "starts empty, no historical backfill" precedent
  `service_metrics`/`PatternId` already set for their own new columns/tables.

## Related documentation

- `docs-internal/adr/0030-service-red-metrics-pre-aggregation.md` - the
  pre-aggregation this ADR deliberately does *not* extend, and why.
- `db/clickhouse/README.md` - `spans`' `DurationNano`/`ServiceName`
  columns this query reads directly.
- `src/Flare.Identity/Migrations/0015_apdex_thresholds.sql`.
