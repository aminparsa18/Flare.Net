# ADR-0046: Per-rule alert evaluation interval

Status: Accepted

Date: 2026-09-24

## Context

`Flare.AlertWorker` evaluates every enabled rule on every tick of one global
`AlertingOptions.PollInterval` (30s by default). That suits a cheap
`LogCount` rule over a 5-minute window. It's wasteful for slow or expensive
rules, like a 6-hour histogram quantile, where the answer barely changes
between ticks but ClickHouse re-runs the full query every 30 seconds. The
only lever was the global poll interval, which slows every rule at once.

Prior art: SigNoz added a per-rule evaluation frequency
([signoz#4697](https://github.com/SigNoz/signoz/commit/83f68f13db3dbedf692f7d2b0eb25c4ea99410cb)).

## Decision

**A per-rule `AlertRule.EvaluationIntervalSeconds`. 0 means every poll
tick.** 0 is the column default, so every existing rule is unchanged. The
poll interval is still the loop's heartbeat. A rule with a non-zero interval
is skipped on ticks where it isn't due yet.

- **The last-evaluated marker lives in Redis, one key per rule**
  (`flare:alerts:last-eval:{ruleId}`). The value is the Unix-ms time of the
  last evaluation, with a TTL equal to the interval. Other places were
  rejected:
  - *An in-memory dictionary in the worker.* With more than one replica, the
    tick lock (`flare:alerts:eval-lock`) moves between replicas. Each one
    would keep its own schedule, and a rule would run whenever the lock
    landed on a replica that hadn't seen it recently.
  - *A column on `alert_rules`.* That table is a `ReplacingMergeTree` of rule
    versions. A write on every evaluation would add a new row version per rule
    per tick, and it would race with user edits for the latest version.
  - *`alert_events`.* That table only records fires, not evaluations.

  Redis is already a hard dependency of the worker, and the lock is already
  there. The TTL means markers for deleted rules clean themselves up.
- **Due check with a half-poll tolerance.** A rule is due when
  `now - lastEvaluated >= interval - pollInterval / 2`
  (`AlertEvaluationSchedule.IsDue`, a pure function in `Flare.Api/Alerting`
  so it can be unit-tested). Tick times drift by however long each tick
  takes, and under multiple replicas they aren't phase-aligned. Without the
  tolerance, a 60s rule whose next tick lands at 59.9s would wait until
  about 90s. With it, the effective interval stays within
  `interval ± pollInterval / 2`.
- **The marker is written before evaluating, not after success.** A rule that
  keeps timing out (the kind a long interval is meant for) is retried at its
  own interval, not on every tick.
- **Fail open.** If the markers can't be read, every rule is evaluated that
  tick. Evaluating a slow rule early costs less than silently not alerting.
- **Due filtering happens before `MaxRulesPerTick`.** Skipped rules don't use
  up the per-tick cap.
- **Validation.** 0, or 60s to 24h. Never longer than the rule's
  `WindowSeconds`. A 1-minute window evaluated every 15 minutes would never
  look at 14 of every 15 minutes, and nothing would say so. With interval ≤
  window, consecutive evaluation windows always overlap or touch.

Schema: migration `0027_alert_evaluation_interval.sql` (plus the cluster
variant) adds one column. The change is additive only.

## Alternatives considered

- **Cron-style schedules.** Rejected. Threshold rules need a period, not wall-clock times.
  A cron field adds parsing and UI weight for no real use case.
- **A Redis sorted set of next-due times.** This would avoid reading one
  marker per scheduled rule. Rejected for now. One `MGET` for the scheduled
  rules is cheap at the 500-rule cap, and a sorted set would need explicit
  cleanup when a rule is deleted or its interval changes.

## Consequences

- If a rule's interval is lengthened, its existing marker still expires at
  the old TTL. The rule may run once early before it settles into the new
  interval. If the interval is shortened, the timestamp comparison applies
  the new value right away.
- Losing Redis state (a flush or a new instance) makes every scheduled rule
  due on the next tick. That's the safe direction.
- The interval doesn't change cooldown or window semantics. A rule evaluated
  every 15 minutes with a 5-minute cooldown can still fire on every
  evaluation.
- The dry-run test endpoints ignore the interval. They always evaluate
  immediately, the same way they ignore cooldown.
