# How alerting works

How does Flare decide that a rule is firing, who hears about it, and when
does it stop talking? This page explains the evaluation model, the lifecycle
of an incident, and why each of the less obvious rule options exists. To
build a rule step by step, start with the
[first alert tutorial](../tutorials/first-alert.md); for the daily tasks see
[acknowledge or snooze an alert](../how-to/alerting/acknowledge-and-snooze-alerts.md),
[define SLOs](../how-to/alerting/define-slos.md) and
[share notification templates](../how-to/alerting/share-alert-notification-templates.md).

## The evaluation model

Alert rules are evaluated by their own process, `Flare.AlertWorker`, not by
`Flare.Api`. Restarting or scaling the API therefore never pauses alerting,
and a slow rule query can't slow the dashboard
([ADR-0018](../../docs-internal/adr/0018-alert-worker-extraction.md)).

Three time settings interact, and they are easy to confuse:

- **Window** (`WindowSeconds`, per rule): how far back a single evaluation
  looks. Every evaluation asks the same question about the last *window* of
  data ending now, so the window is rolling, not a fixed bucket.
- **Poll interval** (`Alerting:PollInterval`, instance-wide, 30 seconds by
  default): the worker's heartbeat. Each tick it takes a short Redis lock, so
  with several worker replicas only one evaluates per tick.
- **Evaluation interval** (`EvaluationIntervalSeconds`, per rule, optional):
  how often *this* rule is actually evaluated. Zero means every tick. A
  six-hour histogram quantile has no reason to re-scan ClickHouse every
  30 seconds, so a rule may ask to be checked less often. The
  last-evaluated marker lives in Redis, not in the rule row, because the lock
  moves between replicas and a per-evaluation write to a versioned config
  table would pile up row versions
  ([ADR-0046](../../docs-internal/adr/0046-per-rule-alert-evaluation-interval.md)).
  An evaluation interval can't exceed the rule's window, otherwise stretches
  of time would never be looked at.

A single tick also has a cap on how many rules it evaluates
(`Alerting:MaxRulesPerTick`, 500 by default), so a huge rule set can't stall
the loop.

Rules and their history live in ClickHouse. A rule is "a saved condition plus
a threshold plus where to send it"; the condition reuses the same filter types
the explorers use, so what you can search for, you can alert on.

## Incident lifecycle

Flare keeps **no separate state table**. Whether a rule is firing,
acknowledged or resolved is derived from the history it already writes
(`alert_events`) and an append-only acknowledgement log. There is no second
copy of state that could drift from what the history shows
([ADR-0064](../../docs-internal/adr/0064-alert-resolved-notifications.md),
[ADR-0124](../../docs-internal/adr/0124-alert-acknowledgement-and-snooze.md)).

```
ok ──breach──▶ firing ──ack / snooze──▶ silenced (repeats muted)
                 │                          │
                 └──── "not breached" evaluation ────▶ resolved ("Resolved" sent)
```

- **Firing.** The first breach notifies the rule's channels and writes a
  history row. While the rule stays breached it re-notifies once per
  **cooldown** (`CooldownSeconds`, 300 by default). Cooldown exists so a rule
  that stays red for an hour doesn't page every thirty seconds.
- **Acknowledged or snoozed.** Both silence the repeat notifications, for the
  current incident only. An acknowledgement lasts until the incident ends; a
  snooze lasts until a chosen time, after which a still-breached rule
  notifies again. Both can be set from the dashboard, the CLI, a signed link
  in the notification, a Slack button, or by acknowledging in PagerDuty
  ([ADR-0127](../../docs-internal/adr/0127-alert-ack-link.md),
  [ADR-0138](../../docs-internal/adr/0138-alert-ack-slack-pagerduty.md)).
- **Resolved.** An evaluation that is genuinely "not breached" resolves a
  firing rule and sends a **Resolved** message to channels that haven't opted
  out. An evaluation that *can't decide* never resolves: too few data points,
  a misconfigured rule or a failed query leave the incident as it was. PagerDuty
  incidents carry a per-rule dedup key, so the resolution closes the same
  incident the trigger opened.

Because the worker also records what it decided not to send, history stays
honest: a breach during a maintenance window is written as `Suppressed`, a
resolution nobody needed is `Skipped`.

## Why the extra options exist

Each option below solves one specific kind of noise or blindness. None is
needed for a simple rule.

- **Recovery threshold (hysteresis).** A value hovering around 90% flaps
  between breached and not breached, and each dip sends a Resolved message
  and closes the incident. A recovery threshold (fire at 90, clear below 80)
  keeps the rule firing, silently, until the value is clearly healthy. It is
  a second number rather than "ok for N evaluations" because it needs no
  per-rule counter state and recovers at once when the value drops far enough
  ([ADR-0076](../../docs-internal/adr/0076-alert-recovery-threshold.md)). It
  doesn't apply to anomaly rules, which have no fixed threshold.
- **Minimum data points.** A metric rule over a window holding one or two
  sparse samples gives a technically correct but meaningless average. With a
  minimum set, too few points means *no verdict*: nothing fires, nothing
  resolves, nothing is recorded
  ([ADR-0050](../../docs-internal/adr/0050-alert-minimum-data-points.md)).
- **Absent-data alerting.** A dead exporter produces nothing to compare
  against a threshold, so a plain threshold rule is silent exactly when you
  most want to hear from it. An optional per-rule no-data window fires when
  the condition matched *nothing*, using its own (usually longer) window and
  taking precedence over the threshold. It is a flag on a rule rather than a
  separate rule kind so the filter isn't duplicated and left to drift apart
  ([ADR-0045](../../docs-internal/adr/0045-absent-data-alerting.md)). It
  doesn't apply to exception rules, where zero is the healthy state. Order of
  checks: no-data, then minimum points, then the threshold.
- **Maintenance windows.** Planned downtime shouldn't page anyone, but
  disabling rules by hand loses the evaluation record. A window (one-off,
  daily or weekly, in its own time zone, covering all rules, chosen rules or
  rules matching labels) lets evaluation continue and only suppresses the
  notification. A breach that outlasts the window notifies on the first tick
  after it ends. If the windows can't be read, the worker fails open and
  notifies: an unwanted page costs less than a swallowed real alert
  ([ADR-0055](../../docs-internal/adr/0055-alert-maintenance-windows.md),
  [ADR-0084](../../docs-internal/adr/0084-alert-rule-labels.md)). A resolution
  to someone who was paged waits until the window ends too, so a PagerDuty
  incident is never left open by a silent resolve.
- **Severity and labels.** Severity (Critical by default) is metadata that
  maps to PagerDuty's severity and tags other channels' text; it doesn't
  change evaluation. Labels are free key/value tags used to scope maintenance
  windows and filter the rule list
  ([ADR-0077](../../docs-internal/adr/0077-alert-rule-severity.md)).

## Which condition fits which problem

Every kind runs through the same loop (cooldown, maintenance
windows, channels, history); only the question differs.

| Kind | Use it when | Notes |
| --- | --- | --- |
| Log count | "More than N matching log lines in 5 minutes" | The original kind; the condition is a log filter |
| Metric threshold | A value crossing a line: p99 latency, a gauge, a counter's increase | Counters use a reset-aware increase, so a restart mid-window doesn't hide or fake a spike; gauges can compare the average, last, min or max ([ADR-0020](../../docs-internal/adr/0020-metric-threshold-alerting.md), [ADR-0044](../../docs-internal/adr/0044-metric-alert-reset-aware-increase.md), [ADR-0049](../../docs-internal/adr/0049-gauge-metric-alert-match-types.md)) |
| Exception count | "This exception type N times in a window" | Counts exception events matching an exact type (and optionally message) ([ADR-0022](../../docs-internal/adr/0022-exception-count-alerting.md)) |
| Anomaly | The right number depends on time of day or week | Scores the current window against the same window over the previous days or weeks; the series is one of the count or metric kinds. Needs enough history, and stays quiet without it ([ADR-0048](../../docs-internal/adr/0048-anomaly-detection-alerting.md)) |
| SLO burn rate | You have a reliability objective and want to know the error budget is being spent too fast | Fires only when both a long and a short window burn quickly, so it resolves soon after the burn stops ([ADR-0108](../../docs-internal/adr/0108-slo-error-budgets.md)) |
| Release errors | A deploy introduced new exception groups | Quiet between deploys ([ADR-0186](../../docs-internal/adr/0186-release-error-alerting.md)) |
| Crash-free sessions | A mobile or client release is crashing more | Compares the worst version's crash-free percentage, ignoring versions with too few sessions ([ADR-0189](../../docs-internal/adr/0189-release-health-trends.md)) |

A fixed threshold is the simplest thing to reason about, so prefer it when a
good number exists. Reach for anomaly detection only for series with a strong
daily or weekly rhythm, and for SLO burn rate when the objective, not a
single metric, is what you care about. Several options are rejected on kinds
where they make no sense (for example a recovery threshold on a burn-rate
rule) instead of being silently ignored.

## Escalation and on-call

If nobody acknowledges a firing incident, repeating the same message to the
same channel doesn't help. A rule can **escalate**: after a delay, notify
another set of channels once per incident, and optionally a second step after
a further delay. Only an acknowledgement stops escalation; a snooze quiets
the first channel but the incident is still unowned. A maintenance window
defers it
([ADR-0125](../../docs-internal/adr/0125-alert-escalation.md),
[ADR-0136](../../docs-internal/adr/0136-alert-multi-step-escalation.md)).

An **on-call rotation** is an ordered list of channels with a fixed shift
length. Who is on call is *computed* from the current time, never stored, so
a handover needs no rule edit and the worker and dashboard always agree. A
rule's first escalation step can point at a rotation. A rotation can carry
one-off **overrides** ("Priya covers Tuesday") that don't shift the
underlying schedule, and one weekly **coverage window** for teams that only
staff business hours; outside it nobody is on call, so give a rule a fixed
escalation channel as a backstop if it must always page. Shifts are
fixed-length and back to back, so follow-the-sun schedules (different people
in different windows) can't be expressed
([ADR-0126](../../docs-internal/adr/0126-alert-oncall-rotations.md),
[ADR-0137](../../docs-internal/adr/0137-oncall-rotation-overrides.md),
[ADR-0139](../../docs-internal/adr/0139-oncall-rotation-coverage.md)).

## Notification templates

By default each channel gets Flare's built-in wording. A rule can override the
title and body with plain `{{placeholder}}` substitution; there are
deliberately no conditionals or loops, which keeps it safe for any rule editor
to write one and means nothing needs sandboxing. Unknown placeholders are
rejected when you save, not discovered during an incident
([ADR-0052](../../docs-internal/adr/0052-alert-notification-templates.md)).
Named **shared templates** let many rules use one wording; precedence is the
rule's own text, then its template or the instance default, then the built-in
text. Editing a template changes the next notification of every rule that
uses it
([ADR-0148](../../docs-internal/adr/0148-shared-alert-notification-templates.md)).

## Trade-offs to know about

- **Polling, not streaming.** An alert can be delayed by up to a poll
  interval plus ingest latency. In return, rules are plain queries against the
  same data you can inspect.
- **One aggregate per rule.** A rule produces a single value, not one per
  group, so there is one incident per rule and no per-series labels.
- **Failures lean towards notifying.** Unreadable maintenance windows or
  schedule markers mean extra evaluations or pages, never silence.
- **Failed sends aren't retried.** A failed notification is recorded as
  `Failed`; a failed resolution is not retried either.

## Related

- Tutorial: [your first alert](../tutorials/first-alert.md)
- How-to: [acknowledge or snooze alerts](../how-to/alerting/acknowledge-and-snooze-alerts.md), [define SLOs](../how-to/alerting/define-slos.md), [share notification templates](../how-to/alerting/share-alert-notification-templates.md), [summarize alerts with AI](../how-to/alerting/summarize-alerts-with-ai.md)
- Explanation: [Architecture](architecture.md)
- ADRs: [0018](../../docs-internal/adr/0018-alert-worker-extraction.md), [0020](../../docs-internal/adr/0020-metric-threshold-alerting.md), [0022](../../docs-internal/adr/0022-exception-count-alerting.md), [0044](../../docs-internal/adr/0044-metric-alert-reset-aware-increase.md), [0045](../../docs-internal/adr/0045-absent-data-alerting.md), [0046](../../docs-internal/adr/0046-per-rule-alert-evaluation-interval.md), [0048](../../docs-internal/adr/0048-anomaly-detection-alerting.md), [0049](../../docs-internal/adr/0049-gauge-metric-alert-match-types.md), [0050](../../docs-internal/adr/0050-alert-minimum-data-points.md), [0052](../../docs-internal/adr/0052-alert-notification-templates.md), [0055](../../docs-internal/adr/0055-alert-maintenance-windows.md), [0064](../../docs-internal/adr/0064-alert-resolved-notifications.md), [0076](../../docs-internal/adr/0076-alert-recovery-threshold.md), [0077](../../docs-internal/adr/0077-alert-rule-severity.md), [0084](../../docs-internal/adr/0084-alert-rule-labels.md), [0108](../../docs-internal/adr/0108-slo-error-budgets.md), [0124](../../docs-internal/adr/0124-alert-acknowledgement-and-snooze.md), [0125](../../docs-internal/adr/0125-alert-escalation.md), [0126](../../docs-internal/adr/0126-alert-oncall-rotations.md), [0127](../../docs-internal/adr/0127-alert-ack-link.md), [0136](../../docs-internal/adr/0136-alert-multi-step-escalation.md), [0137](../../docs-internal/adr/0137-oncall-rotation-overrides.md), [0138](../../docs-internal/adr/0138-alert-ack-slack-pagerduty.md), [0139](../../docs-internal/adr/0139-oncall-rotation-coverage.md), [0148](../../docs-internal/adr/0148-shared-alert-notification-templates.md), [0186](../../docs-internal/adr/0186-release-error-alerting.md), [0189](../../docs-internal/adr/0189-release-health-trends.md)
