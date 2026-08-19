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

| Command | What it does |
|---|---|
| `flare start` | First run initializes `~/.flare/`; every run brings the stack up and waits for it to become healthy. |
| `flare stop` | Stops containers, keeps data volumes - a pause, not a teardown. |
| `flare status` | Table of each service's state/health/port. |
| `flare open` | Opens the dashboard in your default browser. |
| `flare update` | Pulls the latest images for the currently pinned tag, recreates containers, prints a per-service digest diff. Never touches data. |
| `flare logs [service] [-f]` | Shows or follows **container** logs (raw Docker stdout). Omit the service for all of them. |
| `flare tail [-s service]... [-l level]... [--trace-id id] [--search text]` | Live-tails **app-level structured log events** via `Flare.Api`'s live-tail WebSocket - the CLI-native equivalent of the dashboard's Logs Explorer live-tail, not the same thing as `flare logs`. `-l`/`--level` accepts `trace`/`debug`/`info`/`warn`/`error`/`fatal`, repeatable. |
| `flare doctor` | Read-only diagnostics: Docker reachable, Compose present, per-service state, and a ClickHouse row-count sanity check. |
| `flare destroy [--yes] [--purge-config]` | **Destructive.** Removes containers and data volumes. Refuses to run without `--yes` (or an interactive confirm) - never proceeds silently on a non-interactive invocation. Keeps `~/.flare/.env` unless `--purge-config` is also passed. |
| `flare --version` | Prints the installed CLI version. |

## Where Flare's data lives (`~/.flare/`)

```
~/.flare/
  docker-compose.yml   # generated on first `flare start`; never overwritten afterward
  .env                  # generated on first init: RANDOM CLICKHOUSE_PASSWORD/REDIS_PASSWORD
  db/clickhouse/*.sql    # ClickHouse init scripts, materialized from the CLI's own build
  state.json               # last-pulled image digests, for `flare update`'s diff output
```

**Passwords are randomly generated, not the repo's `docker-compose.yml` `flare`/`flare`
default.** This instance is meant to stand for weeks with its ports bound on your
machine the whole time, not be torn down after a quick eval - reusing a documented,
public default password for something long-lived is a foot-gun the CLI doesn't default
into. Generated once at first init; never rotated afterward (that would break
`identity-data`/ClickHouse auth on the next start). The file is plain text and
yours to hand-edit if you'd rather set your own.

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
so a given `Flare.Cli` version keeps pulling the same images forever. `flare update`
re-pulls that same pinned tag; it does not auto-track newer Flare releases. Each new
`Flare.Cli` release re-pins its own template's default once tested against a newer
Flare image - existing installs keep tracking whatever tag they were generated with
unless you hand-edit `.env` or run `flare destroy --purge-config`. Set
`FLARE_IMAGE_TAG=edge` yourself to track Flare's unreleased `main` branch instead.

| `Flare.Cli` version | Default `FLARE_IMAGE_TAG` |
|---|---|
| 0.1.0 (2026-08-16) | `edge` |
| 0.1.1 (2026-08-19) | `0.2.0` |

## Known limitations

- **Port conflicts**: `flare start` and a repo-local `docker compose up` both default
  to the same host ports (3000/8080/4317/4318/8123) - running both at once will fail
  to bind. Not detected or resolved automatically; change one side's ports via its
  `.env`.
- **No multi-instance support**: one `~/.flare/` per machine/user, one standing stack.
- **No `flare aspire`-anything**: this tool has zero interaction with `aspire start`
  or AppHost wiring - out of scope by design, not a gap.
- **Not verified on Windows yet** - state-directory resolution and the browser-launch
  in `flare open` should work per .NET's own cross-platform guarantees, but haven't
  been run end-to-end there as of this doc.
- **Versioning is pre-alpha**: pinning tracks the CLI release, not automatically the
  newest Flare image - `flare update` re-pulls whatever tag your `.env` already has,
  it doesn't move you onto a newer pin without a new `Flare.Cli` release (or a
  hand-edit).
