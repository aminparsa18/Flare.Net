# ADR-0055: Planned maintenance windows (alert silencing)

Status: Accepted

Date: 2026-09-25

## Context

Flare had no way to mute notifications. A deploy or planned downtime
paged everyone whose rules watched the affected services. The only
workaround was to disable each rule by hand and remember to re-enable
it afterwards, which also lost the evaluation record for that period.
Prior art:
[signoz#4863](https://github.com/SigNoz/signoz/commit/7e79900973da430179292ffc865ad44908763035)
added "planned maintenance" with one-off and recurring schedules scoped to
a set of rules.

## Decision

A new entity, `MaintenanceWindow`, stored in a new `maintenance_windows`
table (migration `0031_maintenance_windows.sql`). It uses the same
`ReplacingMergeTree(UpdatedAt)` tombstone CRUD as `alert_rules` and
`notification_channels`. It is managed through `/api/maintenance-windows`
(Member/Admin, like rules and channels) and a "Maintenance" tab on the
dashboard's alerts page.

- **A window names its rules, or covers all of them.** An empty `RuleIds`
  means every rule, including rules created later. This is the common
  "whole-system deploy" case. Deleted rule ids are simply never matched.
- **Three schedules: one-off, daily and weekly.** `StartsAt`/`EndsAt` is
  the first (or only) occurrence. A recurring window repeats that span at
  the same local time of day, either every day or on the selected
  `DaysOfWeek`, with an optional `RepeatUntil`. This covers "tonight's
  deploy" and "every Sunday 02:00-04:00" without a cron expression to
  write, read or validate. A daily occurrence is capped at 24 hours and a
  weekly one at 7 days. Anything longer is really a one-off window.
- **Recurrence is computed in the window's own IANA time zone.** Repeating
  a fixed UTC instant every 24 hours would move a 02:00 window to 01:00 or
  03:00 local time twice a year. `MaintenanceWindowSchedule` converts each
  candidate local day to an instant through `TimeZoneInfo`. A start time
  skipped by a spring-forward transition shifts an hour later, and one
  repeated by a fall-back transition uses its first instant. The weekday
  check is local too, so "Monday" in Tokyo means Monday in Tokyo. The API
  rejects unknown zone ids. If the worker's host can't resolve a zone the
  API host accepted, it falls back to UTC rather than failing to evaluate.
- **The worker still evaluates, and records rather than drops.**
  `AlertEvaluationWorker` loads every window once per tick. On a breach it
  checks for an active window covering the rule. If there is one, it writes
  the usual `alert_events` row with `NotificationStatus = 'Suppressed'` and
  the window's name in the new `SuppressedByWindow` column, and it skips
  channel resolution and sending entirely. The history then shows that the
  condition breached during the deploy, which is often exactly what someone
  wants to check afterwards.
- **Cooldown counts suppressed rows only while a window is active.** Inside
  a window, cooldown includes suppressed rows, so history gets one row per
  cooldown period rather than one per poll tick. Outside a window, the
  cooldown query (`GetLastFiredAsync(includeSuppressed: false)`) ignores
  them. A breach that outlasts the window therefore notifies on the first
  tick after the window ends. It doesn't wait out a cooldown that started
  with a notification nobody received.
- **Loading windows fails open.** If the windows can't be read, the tick
  proceeds as if none were active. An unwanted page during planned
  downtime costs far less than a real alert silently dropped. This is the
  same trade-off the per-rule evaluation-interval markers make (ADR-0046).
- **"Active now" is computed server-side.** `GET /api/maintenance-windows`
  returns `ActiveWindowIds` alongside the windows. The dashboard uses it
  for each window's status badge and for a "Muted" badge on covered rules,
  so it never reimplements time-zone-aware recurrence in TypeScript. The
  form still converts wall-clock inputs in the chosen zone to instants,
  using `Intl.DateTimeFormat` offsets.

## Alternatives considered

- **Silencing by label matchers (Alertmanager-style).** Flare's rules have
  no group-by labels to match on (see ADR-0052), so matchers would only
  select rules by name or condition. An explicit rule list, or "all", is
  simpler and says exactly what it covers.
- **Cron expressions for recurrence.** They are more expressive, but need a
  parser dependency and a human-readable preview, and they make
  "duration" awkward. Daily and weekly cover the requested cases. A cron
  mode can be added later as another `Recurrence` value.
- **Dropping suppressed breaches.** Rejected: the roadmap item and the
  prior art both keep the record. A `Suppressed` status is also what
  distinguishes "muted" from "didn't breach".
- **Skipping evaluation during a window.** This would save ClickHouse
  queries, but the history would lose the breach, and a no-data or anomaly
  rule would come out of the window with no recent evaluation.
- **A per-rule "muted until" field.** It can't express recurring windows or
  one window across many rules, and it mutates the rules themselves.

## Consequences

- `alert_events` gains `SuppressedByWindow String DEFAULT ''`, and
  `AlertHistoryEntry` appends `SuppressedByWindow`. The hand-written
  `$lib/memorypack/AlertHistoryEntry.ts` appends it after `zScore`, so older
  payloads still deserialize.
- `NotificationStatus` has a third value, `Suppressed`. Anything that
  assumed `Sent | Failed` now has to handle it. In this repo that is only
  the dashboard's history sheet.
- `IAlertQueryService.GetLastFiredAsync` takes an `includeSuppressed` flag.
- Each worker tick runs one extra small `FINAL` query against
  `maintenance_windows`.
- Windows cover alert rules only. Send-test and dry-run endpoints ignore
  them, because they're explicit actions.
