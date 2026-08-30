# `flare` CLI reference

Exact command, layout, and configuration reference for `Flare.Cli` (the
`flare` command). For installing and using it, see
[`../how-to/run-with-cli.md`](../how-to/run-with-cli.md); for why this
install path exists alongside Aspire/standalone, see
[`../explanation/architecture.md`](../explanation/architecture.md).

> **Status: new, unreleased.** Published to nuget.org as `Flare.Cli` under
> tag `flare-cli-v*.*.*`. Package ID is `Flare.Cli`, not `flare` (that id is
> already taken on nuget.org, an old unrelated unlisted package) — the
> installed *command* is still `flare`; `ToolCommandName` and `PackageId`
> are independent, the same trick this repo uses for
> `Flare.Hosting.Aspire`/`Aspire.Hosting.Flare`.

## Command reference

Every command accepts `-n`/`--name <NAME>` to target a named instance
instead of the default one at `~/.flare/` — see
[Instance naming](#instance-naming-and-resolution) below. `FLARE_INSTANCE`
is an opt-in env var shorthand for the same thing.

| Command | What it does |
|---|---|
| `flare start [--cluster]` | First run initializes `~/.flare/`; every run brings the stack up and waits for it to become healthy. `--cluster` (first-init only) stands the instance up as a multi-node ClickHouse cluster instead of the single-node default — see [Cluster mode](#cluster-mode). |
| `flare stop` | Stops containers, keeps data volumes — a pause, not a teardown. |
| `flare status` | Table of each service's state/health/port. |
| `flare ingestion [--since range]` | OTLP ingestion health via `Flare.Api`'s `GET /api/ingestion/stats` + `GET /api/ingestion/pipeline` — the CLI-native equivalent of the dashboard's Ingestion page. A colored Healthy/Degraded/Down verdict with reasons, ingress/event/data rates, a Receivers table (gRPC/HTTP, Healthy/Idle/Degraded/Down), and a per-signal Pipeline table (buffer %, pending, last flush, status, last error). Same thresholds as the dashboard's own verdict, so the two never disagree. `--since` defaults to `1h` (e.g. `15m`, `6h`, `24h`). |
| `flare open` | Opens the dashboard in your default browser. |
| `flare update [--tag TAG]` | Pulls the latest images for the currently pinned tag, recreates containers, prints a per-service digest diff. Never touches data. `--tag` rewrites `~/.flare/.env`'s `FLARE_IMAGE_TAG` to `TAG` first — the CLI-native way to move an existing install onto a newer pin instead of hand-editing `.env`. |
| `flare logs [service] [-f]` | Shows or follows **container** logs (raw Docker stdout). Omit the service for all of them. |
| `flare tail [-s service]... [-l level]... [--trace-id id] [--search text]` | Live-tails **app-level structured log events** via `Flare.Api`'s live-tail WebSocket — the CLI-native equivalent of the dashboard's Logs Explorer live-tail, not the same thing as `flare logs`. `-l`/`--level` accepts `trace`/`debug`/`info`/`warn`/`error`/`fatal`, repeatable. |
| `flare search [-s service]... [-l level]... [--trace-id id] [--span-id id] [--pattern-id id] [--search text] [--since range] [--limit count]` | One-shot log search via `Flare.Api`'s `POST /api/logs/search` — the CLI-native equivalent of the dashboard's Logs Explorer, without live-tail (that's `flare tail`). `--since` defaults to `1h` (e.g. `15m`, `6h`, `24h`, `7d`). `--limit` defaults to `20`. Attribute filters aren't exposed yet — planned as a follow-up. |
| `flare export [-s service]... [-l level]... [--trace-id id] [--span-id id] [--pattern-id id] [--search text] [--since range] [--format ndjson\|csv] [-o path] [--limit count]` | Streams a time range of log events to NDJSON (default) or CSV via `Flare.Api`'s `POST /api/logs/search`, paginating in the background — a support-bundle-for-a-bug-report command. Omit `-o`/`--output` to stream to stdout (composes with `\| jq`, `> file`, etc). `--limit` defaults to `100000` as a safety cap, not a hard ceiling like the dashboard's own CSV/XLSX export dialog. Field set matches that dialog's for parity: EventId, Timestamp, Severity, SeverityNumber, Service, EventName, Message, TraceId, SpanId, LogAttributes(Json). |
| `flare alerts list` / `flare alerts test <ID>` | `list` tables saved alert rules (name, enabled, threshold, window, notification channel) via `Flare.Api`'s `GET /api/alerts`. `test <ID>` dry-runs a saved rule's condition/threshold against current data via `POST /api/alerts/{id}/test` — **ignores cooldown, sends no notification** — safe to run repeatedly to verify a Slack/webhook/email/Telegram channel is wired correctly without waiting for (or faking) a real threshold breach. |
| `flare apikey create <NAME>` | Mints a new ingest API key via `Flare.Api`'s `POST /api/ingest-keys` — scripted/CI OTLP setup without clicking through the dashboard's Settings page. The raw key is printed **once**; Flare never stores or shows it again. `apikey list`/`apikey revoke` aren't implemented yet even though the underlying endpoints exist. |
| `flare traces [-s service]... [--status s]... [--kind k]... [--trace-id id] [--min-duration d] [--max-duration d] [--since range] [--limit count]` | Searches recent traces (root spans) via `Flare.Api`'s Query API — the CLI-native equivalent of the dashboard's Trace List. `--status` accepts `ok`/`error`/`unset`, repeatable. `--kind` accepts `internal`/`server`/`client`/`producer`/`consumer`, repeatable. `--min-duration`/`--max-duration` take e.g. `500ms`/`2s`/`1.5m`. `--since` defaults to `1h` (e.g. `15m`, `6h`, `24h`, `7d`). One-shot only, no live-tail equivalent. |
| `flare trace <TRACE_ID>` | Renders one trace as a text waterfall (indented span tree, colored duration bar, tick axis) via `Flare.Api`'s `GET /api/traces/{traceId}` — the CLI-native equivalent of the dashboard's trace-detail page. No critical-path highlighting or service-map yet (planned as a follow-up). |
| `flare metrics [-s service]... [--since range] [--limit count]` | Lists discoverable metrics via `Flare.Api`'s `POST /api/metrics/names` — the CLI-native equivalent of the dashboard's Metric Picker sidebar. Table of name/service/type (Gauge/Sum/Histogram)/unit/series count. |
| `flare metric <NAME> [-s service] [--group-by key] [--mode mode] [--since range]` | Charts one metric as ASCII sparklines via `Flare.Api`'s `POST /api/metrics/query` — the CLI-native equivalent of the dashboard's Metrics chart. `-s/--service` disambiguates when more than one service emits the name. `--mode` mirrors the chart's aggregation picker: `sum`/`rate`(default)/`count` for Sum, `percentiles`(default)/`mean`/`p75`/`p95`/`max` for Histogram, not valid for Gauge. Values print in the metric's own declared unit (no ms↔s/B↔MB rescaling — a v1 simplification of the dashboard's axis scaling). |
| `flare doctor` | Read-only diagnostics, framed as "why isn't Flare working" rather than a bare checklist: Docker/Compose versions, per-container health (reads each service's own Docker healthcheck, not just process state), host-port availability (while the stack is down), ClickHouse/Redis reachability plus a row-count sanity check, API/Dashboard HTTP health, and OTLP gRPC/HTTP listening checks. Passing groups (ports, containers) collapse to one summary row; any failure expands back to a per-item row with a "suggested action" line. Ends with a single `Result: HEALTHY`/`UNHEALTHY` line. |
| `flare destroy [--yes] [--purge-config]` | **Destructive.** Removes containers and data volumes. Refuses to run without `--yes` (or an interactive confirm) — never proceeds silently on a non-interactive invocation. Keeps `.env` unless `--purge-config` is also passed. |
| `flare instances list` | Tables every Flare instance on this machine — the default one (if initialized) plus every named one — with mode (standalone/cluster), directory, running-service count, and pinned image tag. |
| `flare --version` | Prints the installed CLI version. |

## Where Flare's data lives (`~/.flare/`)

```
~/.flare/
  docker-compose.yml   # default instance - generated on first `flare start`; never overwritten afterward
  .env                  # default instance - generated on first init: RANDOM CLICKHOUSE_PASSWORD/REDIS_PASSWORD
  db/clickhouse/*.sql    # default instance - ClickHouse init scripts, materialized from the CLI's own build
  state.json               # default instance - last-pulled image digests, for `flare update`'s diff output
  instances/
    work/                # `flare start -n work` - same layout as above, scoped to this instance
      docker-compose.yml
      .env
      db/clickhouse/*.sql
      state.json
    bignode/             # `flare start --cluster -n bignode` - a cluster-mode instance instead
      docker-compose.yml
      .env
      db/clickhouse-cluster/config/*.xml   # keeper/macros/remote-servers/nginx-lb config, not db/clickhouse/*.sql
      state.json
```

## Instance naming and resolution

- Instance names: lowercase letters, digits, and hyphens only, not
  leading/trailing. `default` is reserved (it's what omitting `--name`
  already means).
- **Ports are auto-assigned on a named instance's first-ever start** — the
  CLI probes for the next free host port at or after each service's
  documented default (`8123`, `4317`, `4318`, `8080`, `7777`) and bakes the
  result into that instance's own `.env`. Assigned once; `.env` is still
  yours to hand-edit afterward for different values.
- `FLARE_INSTANCE` env var is an opt-in shorthand for `-n`/`--name` — set
  once in a shell profile, CI job, or `.envrc` and every `flare` invocation
  targets that instance without repeating `-n`. An explicit `-n`/`--name` on
  the command line always wins over the env var. `FLARE_INSTANCE=default`
  explicitly targets the default instance.
- **Resolution when `--name` is omitted and the env var is unset/empty**:
  the default instance if initialized; otherwise the sole named instance,
  if there's exactly one. Once a second named instance exists with still no
  default, omitting `--name` resolves to the (uninitialized) default
  instance again rather than guessing which one you meant. Once the default
  instance itself exists, it's always the implicit target regardless of how
  many named instances also exist.
- **Passwords are randomly generated per instance**, not the repo's
  `docker-compose.yml` `flare`/`flare` default — generated once at first
  init, never rotated afterward (rotating would break
  `identity-data`/ClickHouse auth on the next start). The file is plain
  text and yours to hand-edit if you'd rather set your own.

## Cluster mode

`flare start --cluster` stands an instance up as a real 4-node ClickHouse
cluster (2 shards × 2 replicas) coordinated by a 3-node ClickHouse Keeper
quorum, plus two `Flare.Ingest` replicas sharing one Redis Streams consumer
group, instead of the single-node default — the no-checkout-required
equivalent of running
[`docker-compose.cluster.yml`](../../docker-compose.cluster.yml) directly.
See [`../explanation/clustering.md`](../explanation/clustering.md) for the
full topology/design writeup and
[`../how-to/run-cluster-mode.md`](../how-to/run-cluster-mode.md) for running
it directly — those stay the source of truth for the cluster itself; this
section only covers the CLI surface.

Orthogonal to naming: either the default instance or any named one can be
cluster mode, independent of every other named/default instance on the same
machine. Decided once at an instance's first `flare start` and persisted —
**not a live migration path**, same as `docker-compose.cluster.yml` itself:
`flare start --cluster` against an already-initialized standalone instance
(or plain `flare start` against an already-initialized cluster one) fails
with a clear error. Point `--cluster` at a fresh `--name` (or
`flare destroy --purge-config` first) to switch an instance's mode.

**Defaults to `FLARE_IMAGE_TAG=0.3.0`**, a different pin than the standalone
instance's own default (`0.2.0`) — cluster-mode support merged after
`v0.2.0` was tagged, and `v0.3.0` is the first stable release that includes
it. `flare update --tag TAG` still works normally to move a cluster
instance onto a newer pin later.

What's different from a standalone instance:

- **More containers, more `flare status`/`flare doctor` rows**: `keeper-1..3`,
  `clickhouse-1..4`, `clickhouse-lb` (an nginx reverse proxy round-robining
  the 4 ClickHouse nodes with passive failover), `redis`, `ingest-1`,
  `ingest-2`, `api`, `dashboard` — 13 services instead of 5.
- **Fewer host ports**: only `ingest-1`/`ingest-2` (distinct
  `FLARE_INGEST_*_PORT`/`FLARE_INGEST2_*_PORT` pairs, default `4317`/`4318`
  and `4319`/`4320`), `api` (`FLARE_API_PORT`), and `dashboard`
  (`FLARE_DASHBOARD_PORT`) are published to the host — no
  `CLICKHOUSE_HTTP_PORT` here, since none of the ClickHouse nodes (or
  `clickhouse-lb`) publish a host port in cluster mode; everything
  ClickHouse-side is reached through `clickhouse-lb` from inside the
  compose network only.
- **`~/.flare/db/clickhouse-cluster/config/`** (keeper/macros/remote-servers/
  nginx-lb config, materialized from the CLI's own build, the same trick
  `db/clickhouse/` already uses for the standalone init scripts) instead of
  `~/.flare/db/clickhouse/` — cluster schema itself isn't applied via a
  bind-mounted init script at all; it's already embedded in the published
  `flare-ingest`/`flare-api` images and applied by `ClickHouseMigrationRunner`
  at startup, the same way it already works for `docker-compose.cluster.yml`.
- **`flare update`** only diffs `ingest-1`, `ingest-2`, `api`, `dashboard` —
  ClickHouse/Keeper/Redis stay on their own floating upstream tags,
  untracked by `FLARE_IMAGE_TAG`, same exclusion the standalone
  `clickhouse`/`redis` services already have today.

## Image tag policy

`~/.flare/.env` defaults `FLARE_IMAGE_TAG` to the latest stable Flare
release this CLI version was tested against (currently `0.2.0` for
standalone, `0.3.0` for cluster mode — see
[Cluster mode](#cluster-mode) above; see
[`../../.github/workflows/docker-publish.yml`](../../.github/workflows/docker-publish.yml)
for how `vX.Y.Z` tags get cut). Deliberately not the floating `edge`/`latest`
tags — see [`../explanation/architecture.md`](../explanation/architecture.md)
for why. Plain `flare update` (no `--tag`) re-pulls that same pinned tag.
`flare update --tag TAG` rewrites `~/.flare/.env`'s `FLARE_IMAGE_TAG` in
place, then pulls — hand-editing `.env` still works too, `--tag` is the same
edit without leaving the CLI. Set `FLARE_IMAGE_TAG=edge` (via either route)
to track Flare's unreleased `main` branch instead, or
`flare destroy --purge-config` to reset to the currently-installed CLI
version's own default.

Version history (standalone instance's own default):

| `Flare.Cli` version | Default `FLARE_IMAGE_TAG` (standalone) |
|---|---|
| 0.1.0 (2026-08-16) | `edge` |
| 0.1.1 (2026-08-19) | `0.2.0` |
| 0.1.2 (2026-08-19) | `0.2.0` (unchanged — this release's own changes were the dashboard port default and the `flare start`/`doctor` port-availability check, plus adding `--tag` above) |
| 0.1.4 (2026-08-23) | `0.2.0` (unchanged — this release added cluster mode, whose own separate default started at `edge` since no stable release included it yet) |
| 0.1.5 (2026-08-23) | `0.2.0` (unchanged — cluster mode's own default moved `edge` → `0.3.0` the same day, once that first cluster-capable stable release shipped) |