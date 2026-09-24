# ADR-0050: Minimum data points for metric alert evaluation

Status: Accepted

Date: 2026-09-24

## Context

A `MetricThreshold` rule compares one scalar, computed over its window,
against a threshold. When the window holds only one or two sparse samples,
for example a gauge exported every few minutes or a series that just
started, that scalar is technically correct but statistically meaningless.
A single spike then becomes a 5-minute "average" that fires the rule.

Absent-data alerting ([ADR-0045](0045-absent-data-alerting.md)) covers zero
points. It doesn't cover one point.

Prior art: SigNoz added a per-rule "require minimum points"
([signoz#5242](https://github.com/SigNoz/signoz/commit/4f76e13dbe6a62e394a1b3c583cd1c1d8826af0f)).

## Decision

**A per-rule `AlertRule.MinDataPoints`. 0 means off.** 0 is the column
default, so every existing rule is unchanged. When it's greater than 0, the
worker counts the raw data points the metric condition matched over the
rule's window before comparing the threshold. If the count is below the
minimum, the result is "insufficient data": the rule doesn't fire and no
event is written.

- **`MetricThreshold` only.** `ValidateCondition` rejects it for every other
  kind. A `LogCount`/`ExceptionCount` rule's observed count *is* its sample
  size, so a minimum would just be a second threshold. An `Anomaly` rule
  already refuses to score without enough baseline windows (ADR-0048).
- **The count reuses `MetricAlertConditionQueryBuilder.BuildPointCount`**,
  the same `WHERE` the threshold query uses (the query ADR-0045 added). It
  counts raw rows across every matched series, not distinct series or
  buckets. That keeps "points" to one plain meaning, the same as the
  Metrics Explorer's row counts.
- **It's a separate query, only run when the rule opts in.** Folding a
  `count()` into each per-type threshold query would change their
  positional result shapes (Sum's windowed CTE, Histogram's arrays) for a
  feature most rules won't use. Rules without a minimum pay nothing extra.
- **Order: no-data, then minimum points, then threshold.** Zero points still
  fires a no-data alert when that's enabled. The two settings work together:
  "alert if the source goes silent, but don't judge it on too few samples".
- **Insufficient data isn't a notification or a history row.** It's the
  absence of a verdict, like a rule that isn't due yet (ADR-0046). The
  dry-run endpoints report it (`AlertTestResult.InsufficientData`, plus
  `DataPointCount`) so the form can say why a rule won't fire. The worker
  logs it at debug level.
- **Validation.** 0, or 1 to 100,000. The upper bound only stops a typo
  from making a rule impossible to fire. It isn't tied to the window,
  because the right number depends on the exporter's interval, which Flare
  doesn't know.

Schema: migration `0029_alert_min_data_points.sql` (plus the cluster
variant) adds one column. The change is additive only.

## Alternatives considered

- **Minimum distinct series or minimum filled buckets.** Rejected for now.
  Either one needs a bucket size or a series definition the rule doesn't
  otherwise have. Raw points are enough for the sparse-sample case this
  addresses.
- **Treat insufficient data as its own firing state (like no-data).**
  Rejected. The point of the setting is to suppress a verdict. A rule that
  should alert when a source is quiet already has absent-data alerting.

## Consequences

- An enabled minimum costs one extra `count()` query per evaluation of that
  rule.
- A `LessThan` rule on a Sum/Histogram metric (whose empty-window value
  reads as 0) no longer fires on an empty or nearly empty window once a
  minimum is set. That's usually the intent. Pair it with absent-data
  alerting to still catch a silent source.
- Changing a rule's kind away from `MetricThreshold` requires clearing the
  minimum. The dashboard only sends it in metric mode.
