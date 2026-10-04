# ADR-0108: SLOs with error budgets and burn-rate alerts

Status: accepted

## Context

Alert rules say "something crossed a line". They do not say how much reliability a service has
promised and how much of that promise is left. Teams that work from service level objectives want
three things: the objective and its remaining error budget in one place, a signal that the budget
is being spent faster than it should be, and that signal routed through the alert channels they
already use. Error budgets span days (99.5% over 28 days), so the data behind them cannot be
re-scanned from `spans` on every read: a 28-day scan trips the query row caps, and a burn-rate rule
would repeat it every evaluation tick.

## Decision

- **An SLO is a stored definition over entry spans.** `slos` (migration 0049) holds the name, the
  service, an optional endpoint (entry-span name), a target percent, a window in days and a kind:
  *availability* (good = the span did not end in an error) or *latency* (good = the span finished
  within a threshold). Same `ReplacingMergeTree(UpdatedAt)` + tombstone shape as
  `maintenance_windows` (ADR-0055), read through `LatestVersionSql` (ADR-0074). Reads are open to any
  authenticated user, writes are Member/Admin.
- **An event is an entry span.** Server and consumer spans (`Kind IN (2, 5)`, the classification the
  version comparison and messaging pages already use) plus root spans (`ParentSpanId = ''`, for jobs
  that start their own trace). Client and internal spans are not requests the service serves.
- **A per-minute pre-aggregate, filled at flush time.** `span_sli_minute` is an `AggregatingMergeTree`
  keyed `(ServiceName, Name, TimeBucket)`, written by a materialized view on `spans`, the same
  "compute once at flush time" precedent as `service_metrics` (ADR-0030). Every column is a plain
  `sum`. It stores `TotalCount`, `ErrorCount`, and one counter per rung of a fixed latency ladder
  (50, 100, 250, 500, 1000, 2500, 5000, 10000 ms). Reads are bounded by services x endpoints x
  minutes, not by span count.
- **Latency thresholds are a ladder, not free-form.** A "fraction under T" cannot be derived from the
  quantile states `service_metrics` keeps, and a counter per arbitrary T is not a fixed schema. The
  ladder gives one column per supported threshold; the form offers exactly those, and the API rejects
  any other value.
- **Budget arithmetic is one pure class.** `SloCalculator`: the budget is `1 - target/100`, the burn
  rate is `(bad / total) / budget`, and budget remaining is `1 - burn rate` over the window. A window
  with no events yields null everywhere. It is not a healthy 100% and not a breach.
- **Burn-rate alerting is a fifth `AlertConditionKind`, `SloBurnRate`,** following ADR-0022 and
  ADR-0048: a `SloBurnRateCondition` (SLO id, long window, short window, burn-rate threshold) stored in
  `alert_rules.SloConditionJson`, with the rule's `WindowSeconds` equal to the long window.
  `SloBurnRateEvaluator` reads good/bad counts for both windows in one query and breaches only when
  the burn rate is at or above the threshold over *both* (`SloCalculator.IsBurning`): the long window
  shows the burn is sustained, the short one that it is still happening, so the alert resolves
  quickly once it stops. Everything after the breach decision is unchanged: cooldown, maintenance
  windows, channels, resolved notifications, history. The history row's `ObservedValue` is the long
  window's burn rate and `ThresholdValue` the burn-rate threshold; no `alert_events` change.
- **The standard pairs are presets, scaled to the SLO window.** The SLO page creates the two rules
  from the Google SRE workbook: *fast burn* (page, 2% of the budget in 1 hour, confirmed over 5
  minutes) and *slow burn* (ticket, 5% in 6 hours, confirmed over 30 minutes). A burn rate B held for
  H hours spends `B * H / (windowDays * 24)` of the budget, so the thresholds are
  `fraction * windowDays * 24 / H`: 14.4 and 6 for a 30-day window, 13.4 and 5.6 for 28 days. They are
  ordinary alert rules afterwards and can be deleted or recreated; a second click only creates a
  missing one.
- **Options that don't apply are rejected, not ignored.** An `SloBurnRate` rule rejects absent-data
  alerting, a recovery threshold, minimum data points and a threshold unit: the short window is
  already its recovery behavior, and no traffic means no burn rate, not a silent exporter.

## Alternatives considered

- **Scan `spans` live.** Rejected: a 28-day window is the row-cap case ADR-0030 exists for, and
  burn-rate rules would repeat it every tick.
- **Reuse `service_metrics`.** It is keyed by service only and counts root spans, so it has no
  endpoint dimension and no latency threshold counters.
- **A free-form latency threshold with per-SLO storage.** A materialized view cannot join the SLO
  definitions at insert time, and a per-SLO table would need a backfill and a lifecycle of its own.
- **A separate SLO alerting engine.** Rejected: cooldown, maintenance windows, channels, resolved
  notifications and history already exist as the alert pipeline.

## Consequences

- The view only sees spans inserted after migration 0049, so an SLO's window is short until it fills.
  The migration carries a one-off `INSERT ... SELECT` to back-fill an existing instance; the how-to
  points at it.
- The endpoint dimension is the entry-span name, so a span name with an id in it makes one row per id.
  Name spans with route templates, as the endpoint tables already assume.
- Latency SLIs count every entry span, errors included: an error that returned fast counts as good for
  latency. Pair a latency SLO with an availability SLO for the same endpoint.
- Window starts are floored to the minute, so a "5m" window is up to a minute longer, never shorter.
- A burn-rate rule whose SLO was deleted skips evaluation with a warning instead of failing; delete
  the rules with the SLO (the SLO page lists them).
- Burn-rate values are floats in `alert_events.ObservedValue`, which is already `Nullable(Float64)`.
