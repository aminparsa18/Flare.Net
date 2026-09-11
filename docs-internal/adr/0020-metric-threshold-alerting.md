# ADR-0020: Metric-threshold alerting as a second `AlertConditionKind`, not a new rule engine

Status: Accepted
Date: 2026-09-11

## Context

`AlertRule.Condition` was hard-typed to `LogFilter`, and
`AlertThreshold.IsBreached` only ever compared a row count. There was no
way to alert on e.g. "p99 latency > 500ms" or a gauge crossing a value,
even though the metrics query machinery (`MetricQueryService`/
`MetricSeriesQueryBuilder`/`MetricFilter`/`HistogramQuantileEstimator`)
already existed and simply wasn't wired into alert evaluation. See
former `docs-internal/planning/roadmap.md` entry "Metric-threshold
alerting, not just log-count alerting" (SigNoz's own version of this,
[signoz#1346](https://github.com/SigNoz/signoz/commit/3a287b2b169dfae093a656d10da5ab8e816b7d1f)/
[#1359](https://github.com/SigNoz/signoz/commit/a8c7237bb), is a
~4700-line rewrite borrowing Prometheus's rule-engine internals — a
useful reference for the shape of a condition, not a scale worth
copying here). Exception-count conditions ("this exception fired N
times in 5 minutes") were named in the same roadmap item as a natural
third condition kind, but are explicitly **out of scope** for this ADR —
tracked as a follow-up, not folded in here, to keep this change reviewable.

## Decision

**A new `AlertConditionKind` discriminator (`LogCount` default /
`MetricThreshold`) on `AlertRule`, evaluated through the existing
`AlertEvaluationWorker` poll loop — not a new rule engine or a parallel
evaluation path.**

- `AlertRule.Condition`/`Threshold.Count` are used, unchanged, for
  `LogCount` rules (every rule that existed before this shipped reads
  back as `LogCount`). A new `MetricAlertCondition` (metric name, point
  type, `MetricFilter`, an aggregation) plus a new `MetricThresholdValue
  double?` carry the `MetricThreshold` case. Both new fields are simply
  ignored when `ConditionKind == LogCount`, and vice versa — the same
  "field present, meaningful only for one mode" convention `AlertRule`
  already uses for its four mutually-exclusive notification-channel
  field groups (`WebhookUrl`/`Telegram*`/`EmailTo`/`PagerDutyRoutingKey`).
- **`AlertThreshold` itself is untouched** — its `Comparator`
  (GreaterThanOrEqual/LessThan) is generic enough to reuse for a metric
  value threshold too, so only a sibling `MetricThresholdValue` (double)
  was added rather than widening `Count` (ulong) in place. This matters
  because `AlertThreshold` carries `[GenerateTypeScript]` — its
  TypeScript mirror is Roslyn-generated (`npm run codegen` →
  `dotnet build`), and there's no proven precedent in this codebase of a
  nullable *value* type (`double?`) surviving that generator correctly
  (see ADR-0016's own catalog of what the generator can/can't reach).
  Not touching it removes that risk entirely. `MetricAlertCondition` is
  `[MemoryPackable]` only (no `[GenerateTypeScript]`, since it nests
  `MetricFilter`, itself already generator-ineligible for the same
  reason `LogFilter` is) — a hand-written TypeScript companion, reusing
  the `MetricFilter.ts` that already existed for the Metrics Explorer.
- **Evaluation**: a new pure `MetricAlertConditionQueryBuilder` mirrors
  `MetricSeriesQueryBuilder`'s per-type SQL minus bucketing/grouping/
  top-N — alert evaluation wants one scalar over the rule's whole
  window, not a chart. `IAlertQueryService.EvaluateMetricConditionAsync`
  reuses `MetricFilterSqlBuilder`/`MetricTables`/
  `HistogramQuantileEstimator` exactly as `MetricQueryService` already
  does for percentile/max-approx aggregations.
  `AlertEvaluationWorker.EvaluateRuleAsync` branches on `ConditionKind`
  to get an observed `double`, then calls a new
  `AlertThreshold.IsBreachedValue(double, double)` sibling to the
  existing `IsBreached(ulong)`, reusing the same `Comparator` switch.
  **No matching data in the window evaluates to `double.NaN`**
  (mirroring ClickHouse's own `avg()`-of-empty-set behavior) rather than
  a real zero — both comparator branches are false against `NaN`, so
  "no data" never breaches either direction instead of silently reading
  as a genuine zero value.
- **Notifier/formatter widen from `ulong observedCount` to
  `double observedValue`** across `IAlertNotifier`/
  `AlertMessageFormatter`/all four channel notifiers. Safe: every real
  log count is a whole number well within `double`'s exact-integer
  range, so this changes nothing for existing `LogCount` rules. This is
  a pure in-memory C# signature change, not a stored-schema change —
  the webhook/PagerDuty JSON payloads keep their existing `observedCount`/
  `thresholdCount` fields verbatim (zeroed for `MetricThreshold` events)
  and add new `observedValue`/`thresholdValue`/`conditionKind`/
  `metricName` fields alongside them, so an existing webhook consumer
  parsing the old integer fields sees no change.
- **ClickHouse schema: additive `ADD COLUMN`s only**
  (`db/clickhouse/0014_alert_rules_metric_condition.sql`,
  `0015_alert_events_metric_value.sql`, mirrored in
  `db/clickhouse-cluster/`), same convention as migrations 0005/0006/
  0012 (Telegram/Email/PagerDuty channel columns) — `alert_rules`/
  `alert_events`' existing columns are never edited in place. The new
  `ConditionKind` columns default to `'LogCount'`, so every pre-existing
  row is correct with zero backfill. `MetricThresholdValue`/
  `ObservedValue`/`ThresholdValue` are `Nullable(Float64)`, not
  `Float64 DEFAULT 0` — `0.0` is a plausible real threshold/value (e.g.
  a gauge alerting on "== 0 available replicas"), so a real `NULL` is
  needed to mean "not a metric-threshold rule/event" rather than
  colliding with a legitimate zero.

## Alternatives considered

- **Widening `AlertThreshold.Count` (ulong) to a `double` in place**,
  unifying log-count and metric-value comparison behind one field.
  Rejected primarily for the `[GenerateTypeScript]`/nullable-double
  generator risk above; secondarily because it would have required a
  `ClickHouse ALTER ... MODIFY COLUMN` type change on `ThresholdCount`
  (`UInt64` → `Float64`) — a real mutation, not the `ADD COLUMN`-only
  shape every other alerting migration in this codebase has kept to.
- **A generic "condition expression" type** (e.g. a small typed
  expression tree covering both log filters and metric queries)
  instead of a discriminator with two concrete condition shapes.
  Rejected as speculative for a two-kind (soon three, with exception
  conditions) problem — SigNoz's own scale of rewrite for this exact
  feature is the cautionary reference point named in Context.

## Consequences

- A rule is either a `LogCount` filter+count rule or a
  `MetricThreshold` metric-query+value rule — never both, and never
  neither. `AlertRuleRequest.ValidateCondition()` enforces this
  server-side (mirroring `ValidateChannel()`'s existing shape) on
  create/update; the dry-run test endpoints are intentionally more
  lenient (an incomplete `MetricThreshold` draft still being edited
  reports "wouldn't fire" rather than a 400), matching how they already
  skip `ValidateChannel()` too.
- Exception-count conditions (the roadmap item's other named use case)
  are **not** covered by this ADR — a rule cannot yet alert on "this
  exception type occurred N times in 5 minutes." Revisit as a third
  `AlertConditionKind` reusing `ExceptionFilter`/
  `ExceptionGroupQueryBuilder`, following the same discriminator shape
  established here, not a separate design.
- The dashboard's metric-name picker in `AlertRuleFormDialog.svelte`
  dedupes by metric name across services (taking the first match's
  point type) — a metric name emitted with two different instrument
  types across services is not a shape this picker handles, and isn't
  expected in practice (one metric name → one instrument type per app).
- Reusable, named notification channels (a separate, already-tracked
  roadmap item) are unaffected — this ADR only changes what a rule's
  *condition* can be, not how it notifies.

## Related documentation

- `docs-internal/adr/0016-memorypack-dashboard-typescript-adoption.md` —
  the generated-vs-hand-written TypeScript split this ADR's design
  leans on directly.
- `docs-internal/adr/0018-alert-worker-extraction.md` — why evaluation
  runs in `Flare.AlertWorker`, not `Flare.Api`; unchanged by this ADR.
