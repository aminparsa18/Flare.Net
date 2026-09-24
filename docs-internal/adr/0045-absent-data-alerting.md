# ADR-0045: Absent-data ("no data") alerting

Status: Accepted

Date: 2026-09-24

## Context

Every existing `AlertConditionKind` compares a *present* value against a
threshold. None of them fires when a rule's query returns nothing at all, so
a dead exporter or a service that stopped emitting goes unnoticed:

- A `MetricThreshold` rule over an empty window evaluates to `NaN` (Gauge
  `avg()`), and `AlertThreshold.IsBreachedValue` is false against `NaN` in
  both directions. That was deliberate: "no data" should not read as a real
  zero. The side effect is that silence is invisible. For Sum and Histogram,
  `sum()` over zero rows is `0`, which can't be told apart from a real zero.
- A `LogCount` rule can approximate the check with `LessThan 1`, but only by
  giving up its real threshold. The notification then reports "0 events
  (< 1)", not "no data".

Prior art: SigNoz added an opt-in `absentFor` per rule
([signoz#3245](https://github.com/SigNoz/signoz/commit/3c419677e1cb266e483d078692d05f02bbffcac2)).

## Decision

**An opt-in per-rule window, `AlertRule.NoDataWindowSeconds`, not a fourth
`AlertConditionKind`.** 0 means off. That is the column default, so every
existing rule is unchanged. When the window is greater than 0, each tick
first checks whether the rule's condition matched **anything** over
`[now - NoDataWindowSeconds, now]`. If nothing matched, the rule fires a
"no data" notification and the threshold is not evaluated. Otherwise the
threshold is evaluated as before.

- **"Matched anything" is a real row count.** It is not inferred from the
  threshold query's result. For `LogCount` it is the same `CountMatchingLogsAsync`
  over the no-data window. For `MetricThreshold` it is
  `MetricAlertConditionQueryBuilder.BuildPointCount`: `SELECT count()` with
  exactly the `WHERE` clause the threshold query uses. So "no data" means
  "nothing the threshold query could have seen", and it works the same way for
  Gauge, Sum and Histogram.
- **Its own window.** `WindowSeconds` is not reused, so a 5-minute p99
  threshold can be paired with a 30-minute "stopped reporting" check.
  The minimum is 60 seconds (`AlertRuleRequest.MinNoDataWindowSeconds`).
  Anything shorter would trip on normal ingest/flush latency.
- **Not supported for `ExceptionCount`.** There, zero exceptions is the
  healthy state, not a silent source. `ValidateCondition` rejects it.
- **No data takes precedence over the threshold.** Over an empty window, the
  threshold result means nothing.
- **Same cooldown and channels** as a threshold fire. The notifiers take a
  `noData` flag and use distinct wording ("fired: no data - no matching log
  events in the last 900s"). The generic webhook and PagerDuty payloads carry
  `noData: true`. The scoped "Matching logs" link is omitted, because there
  are none to show.
- **History and dry runs are marked.** `alert_events.NoData` and
  `AlertHistoryEntry.NoData` record it; `WindowSeconds` is the no-data window
  on those rows. `AlertTestResult.NoData` shows it in the form's and the
  CLI's dry run. Both evaluation paths share `AlertNoDataEvaluator`, so the
  dry run and the worker can't disagree.

Schema: migration `0026_alert_no_data.sql` (plus the cluster variant) adds
the two columns. The change is additive only.

## Alternatives considered

- **A fourth `AlertConditionKind` (`NoData`).** Rejected. It would force a
  choice between a threshold rule and a silence rule on the same query.
  Users would need two rules, with duplicated filters that drift apart. A
  flag composes with any existing rule.
- **Infer no data from `NaN`.** Rejected. It only works for Gauge. Sum's
  `sum()` and Histogram's `sum(Count)` return `0` over an empty window.
- **Reuse `WindowSeconds`.** Rejected. Threshold windows are usually short.
  "Stopped reporting" usually needs a longer window, so short gaps between
  exports don't cause noise.

## Consequences

- An enabled rule runs one extra `count()` query per tick. It uses the same
  `EvaluationSafetyOptions` caps as the other alert queries.
- Flare doesn't send resolution/recovery notifications ("data is back") for
  any kind of fire, and this ADR doesn't change that.
