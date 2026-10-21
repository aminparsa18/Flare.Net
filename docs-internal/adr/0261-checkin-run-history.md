# ADR-0261: Check-in run history

Status: Accepted

Date: 2026-10-21

## Context

[ADR-0260](0260-cron-job-checkins.md) judges a monitor from its newest start and finish pings, so the page shows a
state and a last ping but not how long runs take or which ones failed. Every ping is already stored in
`checkin_events` for a year; what is missing is a view that pairs them.

## Decision

- **Runs are derived at read time.** `CheckInRunHistory.Build` is a pure function over a slug's pings, oldest
  first: a finish ping (`ok` or `fail`) closes the latest open start before it and yields a run with a duration;
  a second `start` while one is open leaves the first run `Unfinished`; a trailing start is `Running`, or
  `Unfinished` once it outlives `maxRuntimeSeconds`; a finish with no start is a run with no duration. Nothing is
  added to the schema or to the ping contract, and existing data works.
- **`GET /api/checkin-monitors/{id}/runs?limit=`** returns the newest runs first (default 50, at most 200). It reads
  the newest `2 * limit + 1` pings for the slug, since a run is at most two pings, then pairs them. It sits with the
  other reads, open to every signed-in user.
- **The page** gets a history button per monitor that opens a dialog with start time, duration, outcome and the
  message, plus a count, failure count and average duration.

## Alternatives considered

- **A `run_id` on every ping.** Pairs overlapping runs correctly, but needs a migration, a new query parameter
  every job must send, and does nothing for jobs already deployed. Not rejected for good: add it if overlapping
  runs of one job turn out to be common.
- **Storing runs in a table when the finish ping arrives.** Ingest would have to read before it writes, which the
  direct-insert design ([ADR-0260](0260-cron-job-checkins.md)) avoids.

## Consequences

- Pings carry no run id, so two overlapping runs of the same job can be paired wrongly. The state evaluator already
  has the same limit.
- The oldest run in a full window can be cut off (its start is outside it), so it may show no duration. It falls
  past the `limit` cut for jobs that send both pings.
- No duration metric is written; a chart of run times would need one.
