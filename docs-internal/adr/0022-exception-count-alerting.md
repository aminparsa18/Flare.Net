# ADR-0022: Exception-count alerting as a third `AlertConditionKind`

Status: Accepted
Date: 2026-09-12

## Context

ADR-0020 added `AlertConditionKind.MetricThreshold` alongside the
original `LogCount` condition, but explicitly deferred exception-count
conditions ("this exception type occurred N times in 5 minutes") as a
named follow-up, to keep that change reviewable. Exceptions are already
queried through their own `ExceptionFilter`/`ExceptionFilterSqlBuilder`/
`ExceptionGroupQueryBuilder` path over span-event data (the Exceptions
page, `/api/errors/*`), entirely separate from `LogFilter`/
`MetricAlertCondition`, so there was still no way to alert on an
exception spike without a matching `LogCount` rule scraping structured
log fields instead. SigNoz has this as its own alert type
([signoz#1752](https://github.com/SigNoz/signoz/commit/33d34af2a)).

## Decision

**A third `AlertConditionKind` value, `ExceptionCount`, following
ADR-0020's discriminator/additive-migration pattern exactly - not a
new rule engine, and not a value-comparison condition like
`MetricThreshold`.**

- **`ExceptionCountCondition`** (new, `[MemoryPackable]` only, same
  reasoning as `MetricAlertCondition`) carries an `ExceptionType`
  (required), an optional `ExceptionMessage` (empty = match every
  message for that type, narrowing to `ExceptionGroup`'s exact (type,
  message) grouping only when set), and an `ExceptionFilter` for
  service scope - reusing the same filter type the Exceptions page
  already uses, not a new one.
- **Unlike `MetricThreshold`, this condition needs no threshold-*value*
  sibling.** It's a count, exactly like `LogCount` - so it reuses
  `AlertThreshold.Count`/`Comparator`/`IsBreached(ulong)` as-is, with no
  new `AlertThreshold` member and no new `alert_events` columns
  (`ObservedCount`/`ThresholdCount`, added in migration 0004, already
  cover it). This is the simpler of the two possible shapes ADR-0020's
  Context section anticipated - `MetricThreshold` needed a value
  comparison because a metric query returns a scalar that isn't a count;
  an exception-occurrence tally is already exactly the same shape
  `LogCount` counts.
- **Evaluation**: a new pure `ExceptionCountConditionQueryBuilder`
  reuses `ExceptionFilterSqlBuilder`'s `WHERE` fragment - the same one
  `ExceptionGroupQueryBuilder`/`ExceptionOccurrenceQueryBuilder` build
  on - adding an exact `exception.type` match (and `exception.message`
  when set) then `count()`s instead of grouping.
  `IAlertQueryService.CountMatchingExceptionsAsync` is the direct
  `CountMatchingLogsAsync` counterpart. `AlertEvaluationWorker.EvaluateRuleAsync`
  gets a third `ConditionKind` branch alongside `MetricThreshold`'s,
  both feeding the same `observedCount`/`AlertThreshold.IsBreached` path
  `LogCount` already uses (only `MetricThreshold` needs the separate
  `observedValue`/`IsBreachedValue` path).
- **ClickHouse schema: one additive `ADD COLUMN`**
  (`db/clickhouse/0019_alert_rules_exception_condition.sql`, mirrored in
  `db/clickhouse-cluster/`) - `alert_rules.ExceptionConditionJson`,
  mirroring `MetricConditionJson`'s "store opaque, round-trip through
  the C# model" shape. No `alert_events` migration, per the point above.
  `ConditionKind` itself needs no schema change - it was already a plain
  `LowCardinality(String)` since migration 0014, so `'ExceptionCount'`
  is just a new value in an existing column, same as email/PagerDuty
  channel values needed no enum-widening migration either.

## Alternatives considered

- **Giving `ExceptionCountCondition` its own threshold-value field**,
  mirroring `MetricThreshold`'s shape for consistency between the two
  newer condition kinds. Rejected: an occurrence count is exactly what
  `AlertThreshold.Count` already means, and introducing a parallel value
  field with no real value to carry would just be an unused column an
  `IsBreached` caller has to remember not to read - the shape should
  follow what the data actually is, not what the previous ADR happened
  to need.

## Consequences

- A rule is now exactly one of `LogCount`, `MetricThreshold`, or
  `ExceptionCount` - `AlertRuleRequest.ValidateCondition()` gains a
  third arm requiring `ExceptionCondition` when `ConditionKind =
  ExceptionCount`, same shape as its existing `MetricThreshold` arm.
- The dashboard's condition-kind picker (`AlertRuleFormDialog.svelte`)
  gains a third branch: an exception-type input (free text, no
  autocomplete against `/api/errors/groups` in this pass - a possible
  follow-up, not attempted here to keep the form's data-fetching
  surface unchanged) plus an optional message and the existing service
  multi-select.
- Exact `(type, message)` matching only, no fingerprint/template
  normalization - same known, deliberate limitation `ExceptionGroup`'s
  own remarks already document for the Exceptions page this condition
  reuses.

## Related documentation

- `docs-internal/adr/0020-metric-threshold-alerting.md` - the ADR this
  one directly follows the shape of, and whose Context section named
  this as deferred work.
- `docs-internal/adr/0018-alert-worker-extraction.md` - why evaluation
  runs in `Flare.AlertWorker`, not `Flare.Api`; unchanged by this ADR.
