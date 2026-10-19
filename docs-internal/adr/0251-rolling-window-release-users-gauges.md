# ADR-0251: Rolling-window crash-free users gauges

Status: Accepted

Date: 2026-10-19

## Context

[ADR-0250](0250-release-health-prometheus-metrics.md) exposed release health on the Prometheus API with `flare_release_users`
and `flare_release_crashed_users` as per-step gauges. Distinct users do not add up across steps, so those gauges answer
"users in this step": an instant query has a one-minute step and sees one minute of users, and a crash-free users panel
only works at a step equal to the period wanted. Crash-free users is the number teams usually watch, over a day.

## Decision

- **Two more gauges, `flare_release_users_24h` and `flare_release_crashed_users_24h`**, same labels
  (`service_name`, `service_version`). A step's value is the distinct users with a session that started in the 24 hours up
  to and including that step, whatever the query's step is. `1 - flare_release_crashed_users_24h / flare_release_users_24h`
  is crash-free users over the trailing day, in an instant query or on a panel at any step. The per-step gauges stay.
- **A fixed 24-hour window, not a parameter.** A window has to be part of the metric name (a label cannot ask the query to
  scan more), so another window is another pair of names, one entry in `ReleaseHealthMetrics.All`. Only 24 hours ships: a
  7-day window scans seven days of spans on every refresh.
- **Computed in ClickHouse without a set per step.** `AppSessionQueryBuilder.BuildRollingReleaseUsers` finds each user's
  session steps (same session definition as ADR-0250), merges them into runs of consecutive covered steps (a session step
  covers itself and the rest of the window), expands each run into the steps it covers inside the requested range and counts
  runs per step, service and version. Runs are disjoint per user, so a plain `count()` is the distinct count. Memory is one
  counter per step, service and version; time is the number of (user, step) pairs. The window is rounded down to whole
  steps, at least one, so a step longer than a day reports its own users.
- **Scans one window before the range.** A query for `[from, to)` reads spans from `from` minus the window. Users appear
  only when the app sets `user.id`; unlike the per-step gauges, a service and version with no users has no series at all
  rather than a 0, because rows without a user are dropped before the aggregation.
- **Same bounds as ADR-0250:** 50,000 service x version x step rows, beyond which the query fails and asks to narrow it.

## Consequences

- The cost of a refresh grows with active users, the range and the step, not just the range: a 7-day range at a 1-minute
  step expands each active user to up to 1,440 steps per day they were active. Raising the step is the control, and the
  `Query__*` caps bound a query that gets too big.
- A session that began before the scanned window and is still sending spans is counted at the first step of the scan, which
  is inside the window, so its user is correctly present. A session's user is counted from the step of the session's first
  span, as for every other release-health metric.
- The 24h gauges are not cached, like the other `flare_release_*` metrics.

## Not decided here

Release health as a pre-aggregated table for long ranges (which would also make the 24h gauges cheap), and the Grafana data
source plugin for sessions and errors.
