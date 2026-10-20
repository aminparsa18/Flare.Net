# ADR-0260: Cron and job check-ins

Status: Accepted

Date: 2026-10-20

## Context

Flare notices a job that runs and reports an error. It cannot notice a job that never runs: a crashed
scheduler, a deleted Kubernetes CronJob or a host that was down at 02:00 emits nothing, so every
existing alert stays quiet. Absent-data alerting ([ADR-0045](0045-absent-data-alerting.md)) covers it
only approximately. Its window is a fixed length, not a schedule, so "daily at 02:00" and "every 5
minutes" need different rules, and a job that starts and hangs looks healthy.

[ADR-0128](0128-synthetic-monitoring.md) settled how a monitor feeds alerting: results become gauge
metrics, and an ordinary metric alert does the rest. A separate "monitor alert" type would duplicate
recovery thresholds, cooldowns, maintenance windows and every notification channel.

## Decision

- **A monitor is a saved schedule.** Table `checkin_monitors` (migration 0089, the same
  `ReplacingMergeTree` tombstone shape as `synthetic_monitors`), CRUD at `/api/checkin-monitors`
  (reads for any signed-in user, writes Member or Admin), and a Settings > Workspace > Cron and job
  check-ins page. A monitor has a `slug` (the name in the ping URL, unique among live monitors,
  fixed after creation, derived from the name when omitted), a schedule that is **either** a
  five-field cron expression read in an IANA `timeZone` **or** a fixed `intervalSeconds` (60 s to 31
  days), a `graceSeconds` (default 300) and an optional `maxRuntimeSeconds`.
- **Pings go to `Flare.Ingest`.** `GET` or `POST /v1/checkins/{slug}[/start|ok|fail]` on :4318, `ok`
  by default, with an optional body kept as a message of up to 1,000 characters. It sits under `/v1`, so the ingest-key
  middleware, per-key limits and rate caps cover it like the signal endpoints. Ingest is where
  external jobs already send data, and it keeps working when `Flare.Api` is down. Both verbs are
  accepted so a bare `curl` works. Each ping is one row in `checkin_events` (`Slug`, `Time`,
  `Status`, `Message`; one-year TTL), inserted directly with no Redis stream, like feedback
  ([ADR-0193](0193-maui-user-feedback.md)): a ping is small and worthless late.
- **State is derived, not stored.** `CheckInEvaluator` is a pure function of the monitor, its newest
  start and finish pings, and the time. The next ping is expected at the first scheduled time after
  the last ping. The job is **Late** once the grace period passes without it. A last finish that
  reported `fail` is **Failed** and stays so until a run reports `ok`. A start with no finish is
  **Running**, and **Overran** once `maxRuntimeSeconds` pass. Before the first ping the state is
  **Pending**. The "last ping" for lateness is floored at the monitor's `UpdatedAt`, so creating,
  re-enabling or re-scheduling a monitor never reads as late at once. The list endpoint and the worker
  call the same function, so the page and the alert cannot disagree.
- **The result is a metric.** `Flare.AlertWorker` runs `CheckInWorker` every `CheckIn:PollInterval`
  (15 s) and inserts `checkin.up` (1 on schedule, 0 when Late, Failed or Overran) into
  `metrics_gauge`, service `flare-checkin`, attribute `monitor` = the slug. A paused monitor writes
  nothing. An alert is a `MetricThreshold` rule on `checkin.up` with **Last** below 1; the page's
  "Create alert" button pre-fills it. One replica judges each tick, claimed with `SET NX` on
  `flare:checkin:tick`.

## Alternatives considered

- **Absent-data alerting only.** The roadmap's cheap first step. Rejected as the whole answer: it
  has no schedule, no failure ping and no run duration.
- **A scheduler library (Hangfire, Quartz).** Not needed. Flare never runs the user's job; it only
  asks when the next ping is due, which Cronos (already a dependency for scheduled dashboard
  reports) answers. A scheduler would add a job store and its own leader election.
- **Pings to `Flare.Api`.** Rejected. It would need a personal access token per job and would stop
  receiving pings whenever the API restarts. Ingest keys are the machine credential.
- **A new `AlertConditionKind`.** Rejected for the reason in ADR-0128.

## Consequences

- Any valid ingest key can ping any slug. A job that should not be able to mark others healthy needs
  its own key; origin-restricted browser keys can still ping from an allowed origin, so use a
  separate server key for jobs.
- A ping for a slug with no monitor is stored and monitors nothing, so a typo shows up as a monitor
  stuck on Pending or Late, not as an error.
- For an interval schedule the next ping is measured from the last ping, so the grace period must
  also absorb the run time of a job that pings at its end.
- Judgement runs at most `PollInterval` after the grace period ends, so a late alert needs a
  window longer than that.
- Cluster mode: migration 0089 has a cluster variant, with `checkin_events` sharded by `Slug`.

## Not decided here

- `flare checkin-monitors` CLI commands, a Terraform resource and an MCP tool.
- A per-monitor run history (durations, recent pings) on the page, and a duration metric.
- Auto-creating a monitor on first ping, and status page components for check-ins.
