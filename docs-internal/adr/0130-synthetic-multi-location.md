# ADR-0130: Synthetic probe locations

Status: Accepted

Date: 2026-10-06

## Context

ADR-0128 probes from wherever `Flare.AlertWorker` runs. An endpoint that is reachable from the
datacenter can still be down for users elsewhere, and one probe location cannot tell a regional
outage from a single failing network path.

## Decision

- **A location is a name a worker gives itself.** `Synthetic:Location` (`Synthetic__Location`) on
  `Flare.AlertWorker`, default `default`. Running another worker in another region with a different
  name is the whole deployment story; there is no location registry and no central scheduler.
- **A monitor lists the locations that run it.** Migration 0058 adds `Locations Array(String)` to
  `synthetic_monitors`. Empty (the default, and every existing monitor) means every location, so a
  single-worker setup behaves exactly as before. Names are 1 to 64 letters, digits, `.`, `_` or `-`,
  at most 20 per monitor.
- **Each location probes independently.** The Redis claim key becomes
  `flare:synthetic:claim:{id}:{location}`, so every location runs the monitor once per interval, and
  replicas that share a location still split the work.
- **Results carry the location.** Every point gets a `location` attribute beside `monitor`, `kind` and
  `target`. Existing alerts keep working (they filter on `monitor`), but a rule that does not group by
  `location` now sees one series per location. An alert on "down from any location" is `synthetic.up`
  Min below 1; "down from all" needs a group-by `location` and is left to the rule author.
- **Status per location.** The list endpoint keeps `latest` (the newest result across locations) and adds
  `locationStatuses`, one entry per reporting location. Points with no `location` attribute (written
  before this change) are reported as `default`.

## Consequences

- A monitor pinned to a location that has no worker never runs, and nothing says so except an
  absent-data alert on its metrics.
- Workers must agree on the monitor list and the same Redis; a location is not isolated from the
  control plane, only from the probe path.
- Metric volume scales with the number of locations.

## Not decided here

- Quorum alerting ("down from N of M locations") as a first-class rule; today that is a group-by on
  `location`.
- Surfacing which locations have a live worker.
