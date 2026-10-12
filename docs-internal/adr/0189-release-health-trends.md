# ADR-0189: Release health trends and crash-free alerting

Status: Accepted

Date: 2026-10-11

## Context

[ADR-0175](0175-release-health.md) gives one crash-free number per version for the whole window. That hides whether a
release got worse after rollout or recovered after a hotfix, and nothing pages anyone when a release starts crashing.

## Decision

- **Trend endpoint.** `POST /api/app-sessions/release-health/trend {windowMinutes, endUnixMs, service, bucketMinutes}`
  returns `{bucketUnixMs, version, sessions, crashedSessions}` per bucket. Still derived from `spans` at query time, no
  table or migration (ADR-0167, ADR-0175). A session is counted once, in the bucket of its first span, so a long
  session does not inflate later buckets; the rate is `1 - crashed / sessions` computed by the caller. Buckets with no
  sessions are absent. The bucket is chosen from the window (5 minutes to a day, at most 60 buckets) unless requested,
  and a request that would exceed 500 buckets falls back to that choice.
- **A new `AlertConditionKind.CrashFreeRate`**, the same discriminator pattern as
  [ADR-0186](0186-release-error-alerting.md): no migration, and the stored shape is reused.
  - Threshold: `MetricThresholdValue` as a percentage (0-100) with the rule's `Comparator` (`LessThan` is the usual
    choice). The recovery threshold, escalation, templates and the evaluation interval all work as for a metric rule.
  - Scope: `ExceptionCondition.Filter.Services` only (empty = every service), the shortcut ADR-0186 took.
  - Sample size: `MinDataPoints` is the minimum sessions a version needs in the window (below 1 counts as 1), the
    ADR-0050 meaning transplanted. Without it a version with one launch and one crash reads 0%.
  - Value: the window is `WindowSeconds` rounded up to whole minutes (at least 5, the query's floor). Each version is
    rated separately and the **lowest** rate among qualifying versions is compared with the threshold; the worst
    version's name is attached to the fired notification as the `flare.release` label.
  - **No qualifying version is skipped,** neither fired nor resolved, like a metric rule with too few points.
  - Not supported: no-data alerting, anomaly source and a threshold unit; the API rejects them.
- **Surfaces:** the Sessions page chart (top six versions by sessions, 99% line), the alert form, `AlertRuleTable`, the
  dry-run test endpoints, the notification text (links to `/sessions`) and the incident-summary prompt.

## Consequences

- An old bad version keeps a rule firing until the window moves past it; a short window and a minimum session count
  are the controls. The alternative, rating only the newest version, needs a version ordering Flare does not have.
- Every evaluation runs the release-health query once per scoped service (or once for all), bounded by the `Query__*`
  caps; use a long `EvaluationIntervalSeconds` on instances with long windows.
- Crash-free users are not alerted on or charted; they stay a table column until apps set `user.id` widely.
- Overloading `ExceptionCondition` is the same shortcut as ADR-0186 and has the same way out: its own condition type
  with a migration if it grows settings.
