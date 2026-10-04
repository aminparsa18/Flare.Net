# ADR-0106: .NET runtime health detectors

Status: accepted

## Context

Services instrumented with the .NET `System.Runtime` meter already send thread-pool, GC, lock and
exception metrics. The built-in dashboards chart them, but reading "queue length rising while
completed work items flatline" off several charts is the part users want done for them.

## Decision

- **Findings computed at query time, no new storage.** `POST /api/services/runtime-health` reads
  `dotnet.*` rows from `metrics_sum` for one service and window. `RuntimeHealthQueryBuilder` returns
  `(Bucket, Metric, Instance, Value)` rows: per-bucket increases for counters (the reset-aware
  `lagInFrame` classification from ADR-0044, as in the Hosts page) and the per-bucket maximum for the
  thread-pool queue length. About 60 buckets per window, never under 60s.
- **Pure detector.** `RuntimeHealthDetector` turns the rows into findings, so every rule is
  unit-tested without ClickHouse, matching the repo's Model/Query convention. Rules: thread-pool
  starvation (queue of 10+ while completions are at most half their median, 3+ buckets, queue not
  draining); GC pressure (10%+ of wall-clock time paused, 2+ buckets); lock-contention and exception
  jumps (a multiple of the lower-quartile baseline and over an absolute floor, 2+ buckets).
- **Per instance.** Detection runs per `service.instance.id`, else pod name, else `host.name`, so one
  starved replica isn't averaged away by healthy ones.
- **Fixed thresholds.** They are constants with the reasoning in the detector's remarks, not
  settings. A rule that fires the same way everywhere is easier to trust and to document; make them
  configurable when someone has a concrete need.
- **No data is not healthy.** The response says whether the service sent any runtime metrics, and
  the dashboard shows a "no data" message rather than an all-clear.
- **Where it shows.** A section at the top of the Services tab's per-service drill-down. Each finding
  links to the Traces and Logs explorers over exactly the window it covers.

## Consequences

- Only the stable OTel names (`dotnet.thread_pool.*`, `dotnet.gc.pause.time`,
  `dotnet.monitor.lock_contentions`, `dotnet.exceptions`) are read. The older
  `process.runtime.dotnet.*` names are not.
- Findings are not alerts. Alerting on them would reuse metric-threshold rules over the same metrics.
- A step change lasting more than three quarters of the window raises the baseline and can go
  unreported; a longer window or the raw dashboards show it.
