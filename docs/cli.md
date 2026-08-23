# Running Flare via the `flare` CLI

A global .NET tool (`Flare.Cli`) that manages a **standing, standalone** Flare instance —
the same non-Aspire Docker stack [`docker compose up`](standalone.md) brings up, wrapped
as `flare start`/`stop`/`status`/`open`/`update`/`logs`/`doctor`/`destroy`, installable
and runnable from anywhere with no Flare.Net checkout required.

This is **not** an Aspire integration and has zero interaction with Aspire
orchestration — if your app already has an AppHost, use
[`Flare.Hosting.Aspire`](aspire-hosting.md) instead; `aspire start` already covers that
lifecycle. `flare` exists for the case Aspire mode structurally can't cover: one
long-running Flare instance you start once and point many unrelated local projects'
OTLP output at, independent of any single AppHost's lifecycle.

> **Status:** new, unreleased. Published to nuget.org as `Flare.Cli` under tag
> `flare-cli-v*.*.*` (see [`.github/workflows/nuget-publish.yml`](../.github/workflows/nuget-publish.yml)) -
> **package ID is `Flare.Cli`, not `flare`** (that id is already taken on nuget.org, an
> old unrelated unlisted package). The installed *command* is still `flare` -
> `ToolCommandName` and `PackageId` are independent, same trick this repo already uses
> for `Flare.Hosting.Aspire`/`Aspire.Hosting.Flare`. If you try the literal
> `dotnet tool install -g flare` some older notes here may still say, it will fail with
> a not-found error - use the command below instead.

## Install

```sh
dotnet tool install --global Flare.Cli
```

## Quick start

```sh
flare start   # first run also initializes ~/.flare/ with a generated compose file + .env
flare open    # launches the dashboard in your default browser
flare stop    # pauses the stack - data volumes are kept
```

Requires Docker (or another Docker-compatible engine, with the Compose v2 plugin)
running, same as [the standalone path](standalone.md) - `flare doctor` checks this and
tells you plainly if it isn't.

## Command reference

Every command below accepts `-n`/`--name <NAME>` to target a named instance instead of
the default one at `~/.flare/` - see [Multi-instance support](#multi-instance-support).
`FLARE_INSTANCE` is an opt-in env var shorthand for the same thing.

| Command | What it does |
|---|---|
| `flare start [--cluster]` | First run initializes `~/.flare/`; every run brings the stack up and waits for it to become healthy. `--cluster` (first-init only) stands the instance up as a multi-node ClickHouse cluster instead of the single-node default - see [Cluster mode](#cluster-mode). |
| `flare stop` | Stops containers, keeps data volumes - a pause, not a teardown. |
| `flare status` | Table of each service's state/health/port. |
| `flare ingestion [--since range]` | OTLP ingestion health via `Flare.Api`'s `GET /api/ingestion/stats` + `GET /api/ingestion/pipeline` - the CLI-native equivalent of the dashboard's Ingestion page. A colored Healthy/Degraded/Down verdict with reasons, ingress/event/data rates, a Receivers table (gRPC/HTTP, Healthy/Idle/Degraded/Down), and a per-signal Pipeline table (buffer %, pending, last flush, status, last error). Same thresholds as the dashboard's own verdict, so the two never disagree. `--since` defaults to `1h` (e.g. `15m`, `6h`, `24h`). |
| `flare open` | Opens the dashboard in your default browser. |
| `flare update [--tag TAG]` | Pulls the latest images for the currently pinned tag, recreates containers, prints a per-service digest diff. Never touches data. `--tag` rewrites `~/.flare/.env`'s `FLARE_IMAGE_TAG` to `TAG` first - the CLI-native way to move an existing install onto a newer pin instead of hand-editing `.env`. |
| `flare logs [service] [-f]` | Shows or follows **container** logs (raw Docker stdout). Omit the service for all of them. |
| `flare tail [-s service]... [-l level]... [--trace-id id] [--search text]` | Live-tails **app-level structured log events** via `Flare.Api`'s live-tail WebSocket - the CLI-native equivalent of the dashboard's Logs Explorer live-tail, not the same thing as `flare logs`. `-l`/`--level` accepts `trace`/`debug`/`info`/`warn`/`error`/`fatal`, repeatable. |
| `flare search [-s service]... [-l level]... [--trace-id id] [--span-id id] [--pattern-id id] [--search text] [--since range] [--limit count]` | One-shot log search via `Flare.Api`'s `POST /api/logs/search` - the CLI-native equivalent of the dashboard's Logs Explorer, without live-tail (that's `flare tail`). `--since` defaults to `1h` (e.g. `15m`, `6h`, `24h`, `7d`). `--limit` defaults to `20`. Attribute filters aren't exposed yet - planned as a follow-up. |
| `flare export [-s service]... [-l level]... [--trace-id id] [--span-id id] [--pattern-id id] [--search text] [--since range] [--format ndjson\|csv] [-o path] [--limit count]` | Streams a time range of log events to NDJSON (default) or CSV via `Flare.Api`'s `POST /api/logs/search`, paginating in the background - a support-bundle-for-a-bug-report command. Omit `-o`/`--output` to stream to stdout (composes with `\| jq`, `> file`, etc). `--limit` defaults to `100000` as a safety cap, not a hard ceiling like the dashboard's own CSV/XLSX export dialog. Field set matches that dialog's for parity: EventId, Timestamp, Severity, SeverityNumber, Service, EventName, Message, TraceId, SpanId, LogAttributes(Json). |
| `flare alerts list` / `flare alerts test <ID>` | `list` tables saved alert rules (name, enabled, threshold, window, notification channel) via `Flare.Api`'s `GET /api/alerts`. `test <ID>` dry-runs a saved rule's condition/threshold against current data via `POST /api/alerts/{id}/test` - **ignores cooldown, sends no notification**, safe to run repeatedly to verify a Slack/webhook/email/Telegram channel is wired correctly without waiting for (or faking) a real threshold breach. |
| `flare apikey create <NAME>` | Mints a new ingest API key via `Flare.Api`'s `POST /api/ingest-keys` - scripted/CI OTLP setup without clicking through the dashboard's Settings page. The raw key is printed **once**; Flare never stores or shows it again. `apikey list`/`apikey revoke` aren't implemented yet even though the underlying endpoints exist. |
| `flare traces [-s service]... [--status s]... [--kind k]... [--trace-id id] [--min-duration d] [--max-duration d] [--since range] [--limit count]` | Searches recent traces (root spans) via `Flare.Api`'s Query API - the CLI-native equivalent of the dashboard's Trace List. `--status` accepts `ok`/`error`/`unset`, repeatable. `--kind` accepts `internal`/`server`/`client`/`producer`/`consumer`, repeatable. `--min-duration`/`--max-duration` take e.g. `500ms`/`2s`/`1.5m`. `--since` defaults to `1h` (e.g. `15m`, `6h`, `24h`, `7d`). One-shot only, no live-tail equivalent. |
| `flare trace <TRACE_ID>` | Renders one trace as a text waterfall (indented span tree, colored duration bar, tick axis) via `Flare.Api`'s `GET /api/traces/{traceId}` - the CLI-native equivalent of the dashboard's trace-detail page. No critical-path highlighting or service-map yet (planned as a follow-up). |
| `flare metrics [-s service]... [--since range] [--limit count]` | Lists discoverable metrics via `Flare.Api`'s `POST /api/metrics/names` - the CLI-native equivalent of the dashboard's Metric Picker sidebar. Table of name/service/type (Gauge/Sum/Histogram)/unit/series count. |
| `flare metric <NAME> [-s service] [--group-by key] [--mode mode] [--since range]` | Charts one metric as ASCII sparklines via `Flare.Api`'s `POST /api/metrics/query` - the CLI-native equivalent of the dashboard's Metrics chart. `-s/--service` disambiguates when more than one service emits the name. `--mode` mirrors the chart's aggregation picker: `sum`/`rate`(default)/`count` for Sum, `percentiles`(default)/`mean`/`p75`/`p95`/`max` for Histogram, not valid for Gauge. Values print in the metric's own declared unit (no ms↔s/B↔MB rescaling - a v1 simplification of the dashboard's axis scaling). |
| `flare doctor` | Read-only diagnostics: Docker reachable, Compose present, per-service state, host-port availability (while the stack is down), and a ClickHouse row-count sanity check. |
| `flare destroy [--yes] [--purge-config]` | **Destructive.** Removes containers and data volumes. Refuses to run without `--yes` (or an interactive confirm) - never proceeds silently on a non-interactive invocation. Keeps `.env` unless `--purge-config` is also passed. |
| `flare instances list` | Tables every Flare instance on this machine - the default one (if initialized) plus every named one - with mode (standalone/cluster), directory, running-service count, and pinned image tag. |
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

## Multi-instance support

`-n`/`--name <NAME>` (every command accepts it - see the [command reference](#command-reference))
targets a **named instance** instead of the default one at `~/.flare/` itself, so a
single machine can run multiple independent Flare stacks side by side - useful for
compliance-segregated logs, a box hosting several clients' stacks, or running old/new
versions side by side during a migration. Named instances live in `~/.flare/instances/<NAME>/`
and are otherwise a full, independent instance: their own `docker-compose.yml`/`.env`,
their own Docker Compose project (`flare-<NAME>`, so containers/network/volumes never
collide with the default instance or with each other), and their own credentials.

```sh
flare start -n work    # first run also initializes ~/.flare/instances/work/
flare status            # -n not needed from here on - work is the only instance so far
flare open
flare instances list   # every instance on this machine, default plus named
```

**Ports are auto-assigned for a named instance's first-ever start** - the CLI probes for
the next free host port at or after each service's documented default (`8123`, `4317`,
`4318`, `8080`, `7777`) and bakes the result into that instance's own `.env`, so
`flare start -n work` doesn't collide with an already-running default instance (or
another named one) without any manual `.env` editing first. Ports are only assigned
once, the same as the default instance's - `.env` is still yours to hand-edit
afterward if you want different values.

Instance names: lowercase letters, digits, and hyphens only, not leading/trailing.
`default` is reserved (it's what omitting `--name` already means).

**`FLARE_INSTANCE` env var is an opt-in shorthand for `-n`/`--name`** - set it once in a
shell profile, CI job, or `.envrc` and every `flare` invocation targets that instance
without repeating `-n` each time:

```sh
export FLARE_INSTANCE=work
flare status   # same as: flare status -n work
flare open     # same as: flare open -n work
```

An explicit `-n`/`--name` on the command line always wins over the env var.
`FLARE_INSTANCE=default` explicitly targets the default instance (useful to override an
inherited env var for one shell without unsetting it). Unset (or empty) falls back to
the normal resolution: the default instance if initialized, otherwise the sole named
instance if there's exactly one - see above.

**Omitting `--name` isn't forced onto a solely-named setup.** If the default instance
doesn't exist yet but exactly one named instance does, every command targets that one
instance automatically - `flare start -n work` once, then plain `flare status`/`flare
open`/etc. from then on, no repeated `-n work`. This stops being unambiguous the moment
a second named instance exists with still no default: at that point omitting `--name`
resolves to the (uninitialized) default instance again, the same "not initialized, run
`flare start`" outcome as always, rather than guessing which one you meant. Once the
default instance itself exists, it's always the implicit target regardless of how many
named instances also exist alongside it.

**Passwords are randomly generated, not the repo's `docker-compose.yml` `flare`/`flare`
default.** This instance is meant to stand for weeks with its ports bound on your
machine the whole time, not be torn down after a quick eval - reusing a documented,
public default password for something long-lived is a foot-gun the CLI doesn't default
into. Generated once at first init; never rotated afterward (that would break
`identity-data`/ClickHouse auth on the next start). The file is plain text and
yours to hand-edit if you'd rather set your own.

## Cluster mode

`flare start --cluster` stands an instance up as a real 4-node ClickHouse cluster (2
shards x 2 replicas) coordinated by a 3-node ClickHouse Keeper quorum, plus two
`Flare.Ingest` replicas sharing one Redis Streams consumer group, instead of the
single-node default - the no-checkout-required equivalent of running
[`docker-compose.cluster.yml`](../docker-compose.cluster.yml) directly (see
[docs/clustering.md](clustering.md) for the full topology/design writeup - that stays
the source of truth for the cluster itself; this section only covers the CLI surface).

```sh
flare start --cluster              # default instance, cluster mode
flare start --cluster -n bignode   # a named instance, cluster mode - same -n as always
flare status -n bignode            # -n still needed here on, same as any named instance
```

**Defaults to `FLARE_IMAGE_TAG=edge`, unlike the standalone instance's pinned stable
default.** Cluster-mode support merged after `v0.2.0` was tagged, so no stable Flare
release actually contains it yet - pinning a cluster instance to `0.2.0` the way
standalone does would pull a cluster-unaware image that crash-loops trying to bootstrap
`clickhousedb` (confirmed live while building this feature). Move a cluster instance
onto a real pinned tag yourself via `flare update --tag TAG` once a stable release that
includes cluster mode ships - see the Image tag policy section below.

Orthogonal to naming: either the default instance or any named one can be cluster mode,
independent of every other named/default instance on the same machine. Decided once at
an instance's first `flare start` and persisted - **not a live migration path**, same as
`docker-compose.cluster.yml` itself: `flare start --cluster` against an
already-initialized standalone instance (or plain `flare start` against an
already-initialized cluster one) fails with a clear error rather than silently doing the
wrong thing. Point `--cluster` at a fresh `--name` (or `flare destroy --purge-config`
first) if you want to switch an instance's mode.

What's different from a standalone instance:
- **More containers, more `flare status`/`flare doctor` rows**: `keeper-1..3`,
  `clickhouse-1..4`, `clickhouse-lb` (an nginx reverse proxy round-robining the 4
  ClickHouse nodes with passive failover), `redis`, `ingest-1`, `ingest-2`, `api`,
  `dashboard` - 13 services instead of 5.
- **Fewer host ports**: only `ingest-1`/`ingest-2` (distinct `FLARE_INGEST_*_PORT`/
  `FLARE_INGEST2_*_PORT` pairs, default `4317`/`4318` and `4319`/`4320`), `api`
  (`FLARE_API_PORT`), and `dashboard` (`FLARE_DASHBOARD_PORT`) are published to the host
  - no `CLICKHOUSE_HTTP_PORT` here, since none of the ClickHouse nodes (or
    `clickhouse-lb`) publish a host port in cluster mode; everything ClickHouse-side is
    reached through `clickhouse-lb` from inside the compose network only.
- **`~/.flare/db/clickhouse-cluster/config/`** (keeper/macros/remote-servers/nginx-lb
  config, materialized from the CLI's own build, same trick `db/clickhouse/` already
  uses for the standalone init scripts) instead of `~/.flare/db/clickhouse/` - cluster
  schema itself isn't applied via a bind-mounted init script at all; it's already
  embedded in the published `flare-ingest`/`flare-api` images and applied by
  `ClickHouseMigrationRunner` at startup, the same way it already works for
  `docker-compose.cluster.yml`.
- **`flare update`** only diffs `ingest-1`, `ingest-2`, `api`, `dashboard` - ClickHouse/
  Keeper/Redis stay on their own floating upstream tags, untracked by `FLARE_IMAGE_TAG`,
  same exclusion the standalone `clickhouse`/`redis` services already have today.

## Relationship to the other install paths

- **Already using .NET Aspire?** Use [`Flare.Hosting.Aspire`](aspire-hosting.md) -
  `aspire start` already orchestrates it as part of your own AppHost.
- **Want a one-off, repo-local eval?** [`docker compose up`](standalone.md) at the repo
  root is still the fastest way to just look at Flare once.
- **Want a standing instance you start once and forget about, from any directory,
  across many unrelated projects?** This CLI.

## Image tag policy

`~/.flare/.env` defaults `FLARE_IMAGE_TAG` to the latest stable Flare release this CLI
version was tested against (currently `0.2.0`, see
[`.github/workflows/docker-publish.yml`](../.github/workflows/docker-publish.yml) for
how those `vX.Y.Z` tags get cut) - deliberately not the floating `edge`/`latest` tags,
so a given `Flare.Cli` version keeps pulling the same images forever. Plain `flare
update` (no `--tag`) re-pulls that same pinned tag; it does not auto-discover or
auto-track newer Flare releases - and deliberately never will, since only this CLI's
own author knows which newer Flare Docker images have actually been tested against a
given `Flare.Cli` version (a newer tag on Docker Hub isn't necessarily one). Each new
`Flare.Cli` release re-pins its own template's default once tested against a newer
Flare image - existing installs keep tracking whatever tag they were generated with
unless you move them explicitly. `flare update --tag TAG` is the CLI-native way to do
that (rewrites `~/.flare/.env`'s `FLARE_IMAGE_TAG` in place, then pulls) - hand-editing
`.env` still works too, `--tag` is just the same edit without leaving the CLI. Set
`FLARE_IMAGE_TAG=edge` (via either route) to track Flare's unreleased `main` branch
instead, or `flare destroy --purge-config` to reset to the currently-installed CLI
version's own default.

| `Flare.Cli` version | Default `FLARE_IMAGE_TAG` |
|---|---|
| 0.1.0 (2026-08-16) | `edge` |
| 0.1.1 (2026-08-19) | `0.2.0` |
| 0.1.2 (2026-08-19) | `0.2.0` (unchanged - this release's own changes were the dashboard port default and the `flare start`/`doctor` port-availability check, plus adding `--tag` above) |

## Known limitations

- **`flare start --cluster` currently requires `edge`**: no stable Flare release
  contains cluster-mode support yet (it merged after `v0.2.0` was tagged) - see
  [Cluster mode](#cluster-mode) above. Move to a real pinned tag via
  `flare update --tag TAG` once one ships.
- **Not verified on Windows yet** - state-directory resolution and the browser-launch
  in `flare open` should work per .NET's own cross-platform guarantees, but haven't
  been run end-to-end there as of this doc.
- **`Flare.Cli` itself is pre-1.0** (currently `0.1.4`) - normal SemVer "still shifting,
  no compatibility guarantee yet", unrelated to whether it's published (it is - see the
  Install section above). Separately, and by design rather than as a gap: an existing
  install's image-tag pin never moves on its own - see the Image tag policy section
  above for why, and `flare update --tag TAG` for the CLI-native way to move it
  yourself.
