# ADR-0262: Check-in auto-create and status page components

Status: Accepted

Date: 2026-10-21

## Context

[ADR-0260](0260-cron-job-checkins.md) left two things open. A monitor has to exist before its job pings, so a deploy
that adds a cron job must also call the API, and a ping for an unknown slug is stored and monitors nothing. And a
check-in monitor cannot appear on a [status page](0158-status-pages.md), which takes only synthetic monitors and SLOs.

## Decision

- **A ping can create its monitor.** `GET|POST /v1/checkins/{slug}[/status]` accepts `create=1` (or `true`) with `cron`
  or `interval` (exactly one), and optionally `tz`, `grace`, `max_runtime` and `name`. If no live monitor has the slug,
  `Flare.Ingest` inserts one into `checkin_monitors` before storing the ping, so the first ping is never late. If one
  exists the parameters are ignored: a ping never edits a schedule, so a stale URL in a script cannot undo a change
  made in the dashboard. Without `create=1` nothing changes, and a typo in a slug still creates nothing. An invalid
  request answers `400` and stores nothing; the limits mirror `CheckInMonitorRequest`.
- **Ingest writes the row itself**, with the same direct insert it uses for the ping, so it keeps working when
  `Flare.Api` is down (the reason ADR-0260 put pings on Ingest). This adds Cronos to `Flare.Ingest` to check the cron
  expression, a package the repo already pins for the API and worker.
- **Races are absorbed by a derived id.** The monitor's `Id` is a hash of the slug, so two pings (or two Ingest
  replicas) creating the same slug write the same row, and `ReplacingMergeTree` keeps one. A deleted monitor's slug can
  be created again the same way: the new version is newer than the tombstone. A slug already known to exist is
  remembered in memory for five minutes, so a job that sends `create=1` on every ping costs one read per interval.
- **It is bounded.** `CheckIns__AutoCreate` (default on) turns it off, and such pings answer `403`.
  `CheckIns__MaxMonitors` (default 500) is the most live monitors it will leave behind; past it a `create=1` ping for a
  new slug answers `403`. Any valid ingest key can already ping any slug, so without the cap a leaked key could fill the
  table and give the worker a metric per row to write.
- **A status page component can be a check-in monitor.** `StatusComponentKind.CheckIn` joins `Monitor` and `Slo`. The
  state is `StatusEvaluator.CheckInState` over `CheckInEvaluator.Evaluate`, the function the page and the alert already
  share: on schedule or running is Operational, late, failed or overran is Outage, and pending or paused is Unknown (so
  a paused job does not stay green). There is no Degraded, because a job either made its run or it did not. The 90-day
  bar is the daily mean of `checkin.up` from `metrics_gauge`, the same source and shape as synthetic monitors. The
  public response still carries no id, slug or monitor name.

## Alternatives considered

- **Create monitors from a config block on the first ping body.** Rejected: a query string works with a bare `curl`
  and any scheduler, and a body is already the ping's message.
- **Create on every ping that has a schedule, with no `create` flag.** Rejected: it would make a typo in a slug, or a
  stale parameter on a retired job, create a monitor that then reports late.
- **Update the schedule when the parameters differ.** Rejected for the reason above, and because it would race with
  dashboard edits. Exposing an upsert is a separate decision.
- **Post to `Flare.Api` instead.** Rejected for the reasons in ADR-0260.
- **A Degraded state for a late job.** Rejected: a missed run has no partial form, and "degraded" would read as
  "mostly working" to visitors.

## Consequences

- An ingest key can now create monitors, which are visible in Settings and count against `MaxMonitors`. Give jobs
  their own key, as ADR-0260 already advised; `CheckIns__AutoCreate=false` removes the ability.
- The slug is the monitor's name when `name` is omitted. Rename it in the dashboard.
- Cluster mode: `checkin_monitors` shards by `rand()`, so a slug's versions can sit on different shards; reads already
  take the latest version per `Id`, and the cap check does the same.
- The status page build reads `checkin_monitors`, `checkin_events` and `metrics_gauge` on a cache miss, once each per
  page, on top of the reads in ADR-0158.
- The Terraform provider's `flare_status_page` accepts `CheckIn` as a component kind.
