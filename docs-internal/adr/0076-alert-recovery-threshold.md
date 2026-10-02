# ADR-0076: Recovery threshold (hysteresis) for alert rules

Status: Accepted

Date: 2026-10-02

Partially supersedes [ADR-0064](0064-alert-resolved-notifications.md), which
decided on "no extra hysteresis".

## Context

A rule has one threshold. A firing rule resolves on the first evaluation that
isn't breached (ADR-0064). A value that hovers around the threshold, such as
CPU between 89% and 91% against a 90% rule, therefore flips between breached
and not breached. Cooldown only caps how often it *fires*. Each dip still
records a resolution, sends a "Resolved" notification and closes the
PagerDuty incident, and the next breach after the cooldown pages again as a
new incident.

ADR-0064 rejected "ok for N consecutive evaluations" hysteresis for now and
left a per-rule field as a later option. This is that field, with a different
shape: a second threshold instead of a count of evaluations. Prior art:
[signoz#9428](https://github.com/SigNoz/signoz/commit/52228bc6c41daec311887fd69db12cf0a4cd0a66).

## Decision

**A per-rule `AlertRule.RecoveryThreshold` (nullable double). Null means off.**
Null is the column default, so every existing rule behaves as before. When
set, a firing rule resolves only once its observed value has crossed the
recovery threshold. While the value is between the threshold and the recovery
threshold, the rule stays firing: no resolution, and no new notification
either, because it isn't breached.

- **Which value.** The observed count for `LogCount`/`ExceptionCount`, the
  metric value for `MetricThreshold`. The recovery threshold lives on the same
  scale as the rule's own threshold (`AlertThreshold.Count` or
  `MetricThresholdValue`).
- **Direction follows the comparator.** For the default "at or above" rule
  (fire at >= 90) the rule holds while `observed >= recovery` and recovers
  below it (recover at < 80). For `LessThan` (fire below 20) it holds while
  `observed <= recovery` and recovers above it. The check is a pure method,
  `AlertThreshold.HoldsFiring`, next to `IsBreached`.
- **Validation.** The recovery threshold must be finite and on the recovering
  side of the threshold: at or below it for "at or above" rules, at or above
  it for `LessThan` rules. Equal is allowed and is the same as having none.
  The other side would make the rule hold forever, since the value would have
  to be both below the threshold and above the recovery value.
- **`Anomaly` rules are rejected.** Their breach is a z-score against a seasonal
  baseline and there is no fixed threshold to recover from.
- **NaN recovers.** A metric window with no points evaluates to NaN. Without
  hysteresis that already resolves a firing rule, and it still does. NaN
  compared against the recovery value would otherwise hold the rule forever.
- **State needs no new storage.** Firing/ok is still derived from
  `alert_events` (ADR-0064). Holding is "not breached, rule is firing, value
  hasn't crossed recovery" and writes nothing, like a rule that isn't due.
  The worker already has the firing state for every due rule from its once
  per tick read.
- **Insufficient data and no-data work as before.** Insufficient data returns
  before the threshold is evaluated, so it neither resolves nor holds. A
  no-data breach fires as before. When data comes back, the recovery
  threshold applies like any other evaluation.
- **The dry-run endpoints are unchanged.** `WouldFire` is about the breach
  condition, and the recovery threshold only matters once a rule is firing.

Schema: migration `0038_alert_recovery_threshold.sql` (plus the cluster
variant) adds one nullable column. The change is additive only.

## Alternatives considered

- **"Ok for N consecutive evaluations."** This was ADR-0064's option. It needs
  per-rule counter state between ticks, which would be a second copy of state
  next to `alert_events` or a Redis key, and it delays recovery by a fixed
  count even when the value dropped far below the threshold. A recovery
  threshold is stateless, matches how on-call people already describe the
  problem ("fire at 90, clear at 80"), and recovers immediately once the value
  is clearly healthy.
- **A recovery threshold for anomaly rules.** This would be a lower z-score.
  The z-score band is already a statistical margin, and there's no demand for
  it. It can be added later.

## Consequences

- `alert_rules` gains `RecoveryThreshold`, and `AlertRule`/`AlertRuleRequest`
  append `RecoveryThreshold` (nullable). The hand-written MemoryPack
  TypeScript companions are extended in the same order.
- The alert form has a "Use a recovery threshold" switch for non-anomaly rules.
- A held rule is silent. It logs at debug level only, and the history table
  shows the rule as still firing until the resolution row arrives.
