# ADR-0080: Threshold unit for metric alert rules

Status: Accepted

Date: 2026-10-02

## Context

A metric rule's threshold was always in the series' own unit. "Alert when p95
exceeds 500 ms" against `http.server.request.duration`, which is recorded in
seconds, had to be typed as `0.5`. Typing `500` instead is a silent mistake:
the rule never fires and nothing says so. Prior art:
[signoz#10020](https://github.com/SigNoz/signoz/commit/8cabaafc584d1aa92a603d85b2c4d021dff9e911).

## Decision

**A per-rule `AlertRule.ThresholdUnit` (string). Empty means "the series' own
unit".** Empty is the column default (`alert_rules.ThresholdUnit`, migration
0040), so every existing rule behaves as before.

- **Stored as typed.** `MetricThresholdValue` and `RecoveryThreshold` keep the
  numbers the user entered, in `ThresholdUnit`. Re-opening the rule shows
  `500 ms`, not `0.5`. Nothing is converted at save time.
- **Converted at evaluation.** The alert worker reads the unit the points were
  recorded in from the same query that gives the value
  (`EvaluateMetricConditionAsync`), converts both thresholds with
  `MetricUnitConverter.ToSeriesUnit`, and compares. The converted rule is also
  what notifications and history read, so the threshold is formatted by
  `MetricUnitFormatter` at the series' scale, next to the observed value
  ("0.5 s" and "620 ms" become "500 ms" and "620 ms").
- **Families.** Time (`ns`..`d`) and bytes (`By`..`TiBy`), the families
  `MetricUnitFormatter` already knows. `MetricUnitConverter` shares its tables.
  Other units (`%`, `Cel`, `{request}`) are not convertible and are rejected as
  a `ThresholdUnit` on save.
- **Mismatch compares as typed.** If the series' unit isn't convertible or is
  in another family (a rule saved with `ms`, then the metric's emitted unit is
  `By`), the worker logs a warning and compares the value unconverted, like a
  rule without a unit. Save-time validation can't catch this because the
  series' unit is only known from the data.
- **Metric rules only.** Rejected for log, exception and anomaly rules. An
  anomaly rule's source metric is scored by z-score, which is unit-free.
- **Dashboard.** The rule form shows a unit picker beside the metric
  threshold, listing the selected metric's family. The first entry is the
  metric's own unit and the recovery threshold uses the same unit. The rules
  table shows the threshold with its unit.

## Consequences

- The conversion target is the unit the points were *emitted* with, not an
  ADR-0065 override. The worker has no access to the override store, and an
  override that relabels a unit without changing the numbers would make the
  conversion wrong in the other direction. The form, which sees the
  override-applied unit, only offers a family that matches in practice.
- The "Test" dry run converts as the worker does. History rows record the
  threshold in the series' unit (the converted value), like the observed value.
- Existing PagerDuty/webhook payloads carry `thresholdValue` in the series'
  unit, converted, so receivers that compare it to the observed value keep
  working.
