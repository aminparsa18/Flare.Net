# ADR-0128: Synthetic monitoring

Status: Accepted

Date: 2026-10-06

## Context

Flare watches what applications report about themselves. It cannot tell you that an endpoint is
unreachable, a port stopped accepting connections, or a TLS certificate expires next week, because
nothing is running to report it. Those checks need a prober on the outside looking in.

Flare already has the pieces that consume a result: metric alerts (including absent-data and
recovery thresholds), SLO-style charts, dashboards. A separate "monitor alert" type would duplicate
all of them.

## Decision

- **A monitor is a saved probe.** Table `synthetic_monitors` (migration 0056, same
  `ReplacingMergeTree` tombstone shape as `maintenance_windows` and `oncall_rotations`), CRUD at
  `/api/synthetic-monitors` (Member or Admin), with a Settings > Workspace > Synthetic monitors page
  (table, create/edit dialog, enable switch). Three kinds: `Http` (request, status match, default
  any 2xx/3xx), `Tcp` (connect to `host:port`), `Tls` (handshake, plus days to certificate expiry).
  Interval 10 s to 24 h, timeout 1 to 120 s and not above the interval.
- **Results are metrics, not a new store.** `Flare.AlertWorker` inserts gauge points straight into
  `metrics_gauge`, service `flare-synthetic`, attributes `monitor`, `kind`, `target`:
  `synthetic.up` (1 or 0), `synthetic.duration` (ms), `synthetic.http.status_code`,
  `synthetic.cert.expiry_days`. An alert on "up is 0", "duration above 2 s" or "cert expiry under 14
  days" is an ordinary metric alert, and a monitor that stops reporting is an absent-data alert.
- **Direct insert, not OTLP.** Sending the result through Ingest and Redis would stop reporting
  exactly when those are down, and the prober is already inside Flare. The cost is that these points
  skip Ingest's metric attribute rules, which is acceptable for a fixed, low-cardinality schema.
- **Scheduling.** The worker lists monitors every `Synthetic:PollInterval` (5 s) and claims each due
  probe with `SET NX` on `flare:synthetic:claim:{id}`, expiring after the monitor's interval. Replicas
  therefore split the monitors with no per-tick lock; a replica dying after claiming loses one probe.
  Probes run concurrently up to `Synthetic:MaxConcurrency` (20). `Synthetic:Enabled=false` turns the
  runner off.
- **A failed probe is data.** Connect errors, TLS failures (default certificate validation applies, so
  an expired, untrusted or mismatched certificate is down), timeouts and unexpected statuses all
  record `synthetic.up = 0`. The probe HTTP client has no resilience handler: one attempt, bounded by
  the monitor's timeout.

## Consequences

- The probe runs from wherever `Flare.AlertWorker` runs, so it measures reachability from there. One
  location only.
- A Member can make the server send requests to any host it can reach, including internal ones. That
  is the point for a self-hosted tool, and the same trust level as a webhook channel, but an operator
  who does not want it can set `Synthetic:Enabled=false`.
- Points are written at probe time, so a monitor's series starts when it is created. Disabling a
  monitor stops its series, which an absent-data alert will notice.
- `metrics_gauge` grows by up to four points per monitor per interval.
- Cluster mode: migration 0056 has a cluster variant; the probe claim key is in Redis, shared by
  replicas.

## Not decided here

- **`flare` CLI commands** for monitors, and showing each monitor's latest status on the settings page.
- **Multi-location probing**, response-body assertions, request headers and bodies, redirect policy,
  and per-monitor TLS validation overrides.
- **UDP, DNS and ICMP probes.**
