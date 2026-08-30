# How to run Flare with the `flare` CLI

Run a **standing, standalone** Flare instance from anywhere on your
machine, with no repo checkout required — useful when you want one
long-running instance shared across several unrelated local projects,
rather than one scoped to a single repo or AppHost. For the exact commands
and config, see [`../reference/cli-commands.md`](../reference/cli-commands.md);
for why this path exists alongside Aspire and plain Docker Compose, see
[`../explanation/architecture.md`](../explanation/architecture.md).

This is **not** an Aspire integration and has zero interaction with Aspire
orchestration — if your app already has an AppHost, use
[`Flare.Hosting.Aspire`](run-with-aspire.md) instead (`aspire start` already
covers that lifecycle).

## Prerequisites

- Docker (or another Docker-compatible engine, with the Compose v2 plugin)
  running — same requirement as [the standalone path](run-standalone.md).
  `flare doctor` checks this and tells you plainly if it isn't.

## Install

```sh
dotnet tool install --global Flare.Cli
```

> Package ID is `Flare.Cli`, not `flare` — the installed *command* is still
> `flare`. If you try `dotnet tool install -g flare` directly, it fails
> with a not-found error; use the command above.

## Steps

```sh
flare start   # first run also initializes ~/.flare/ with a generated compose file + .env
flare open    # launches the dashboard in your default browser
flare stop    # pauses the stack - data volumes are kept
```

That's the whole lifecycle for a single default instance. See
[Running multiple instances](#running-multiple-instances) and
[Running in cluster mode](#running-in-cluster-mode) below for the two ways
to go further.

`flare export` is worth calling out separately — it's built to compose like
a normal Unix command, not just dump to a file:

```sh
flare export --trace-id abc123 --since 6h > bug-report.ndjson   # redirect to a file
flare export -l error --since 1h | jq '.Message'                # pipe straight into jq
```

That's the everyday shape (a quick support-bundle-for-a-bug-report, no `-o`
needed). A bundled `incident.zip` mode (trace + logs + metrics together) is
a real want but not built yet — see
[the roadmap](../../docs-internal/planning/roadmap.md) for its current
status.

## Running multiple instances

`-n`/`--name <NAME>` (accepted by every command) targets a **named
instance** instead of the default one at `~/.flare/`, so a single machine
can run multiple independent Flare stacks side by side — useful for
compliance-segregated logs, a box hosting several clients' stacks, or
running old/new versions side by side during a migration. Named instances
are otherwise full, independent instances: their own compose file/`.env`,
their own Docker Compose project (so containers/network/volumes never
collide), and their own credentials.

```sh
flare start -n work    # first run also initializes ~/.flare/instances/work/
flare status            # -n not needed from here on - work is the only instance so far
flare open
flare instances list   # every instance on this machine, default plus named
```

Set `FLARE_INSTANCE` once to avoid repeating `-n` on every command:

```sh
export FLARE_INSTANCE=work
flare status   # same as: flare status -n work
flare open     # same as: flare open -n work
```

See [the reference](../reference/cli-commands.md#instance-naming-and-resolution)
for exact naming rules, port auto-assignment behavior, and how the CLI
resolves which instance you mean when `-n`/`FLARE_INSTANCE` are both
omitted.

## Running in cluster mode

```sh
flare start --cluster              # default instance, cluster mode
flare start --cluster -n bignode   # a named instance, cluster mode - same -n as always
flare status -n bignode            # -n still needed from here on, same as any named instance
```

Not a live migration path, same as `docker-compose.cluster.yml` itself —
point `--cluster` at a fresh `--name` (or `flare destroy --purge-config`
first) if you want to switch an existing instance's mode. See
[`../reference/cli-commands.md#cluster-mode`](../reference/cli-commands.md#cluster-mode)
for exactly what's different about a cluster-mode instance (containers,
ports, config layout), and
[`../explanation/clustering.md`](../explanation/clustering.md) /
[`run-cluster-mode.md`](run-cluster-mode.md) for the cluster itself.

## Verification

```sh
flare status   # every service healthy
flare doctor   # deeper diagnostics if something looks wrong
```

## Troubleshooting

`flare doctor` is the first stop for anything not working — it's built as
"why isn't Flare working," not a bare checklist: Docker/Compose versions,
per-container health, host-port availability, ClickHouse/Redis reachability,
API/Dashboard HTTP health, and OTLP listening checks, each with a suggested
action on failure.

Known gaps, stated plainly:

- **Not verified on Windows yet** — state-directory resolution and the
  browser-launch in `flare open` should work per .NET's own cross-platform
  guarantees, but haven't been run end-to-end there.
- **`Flare.Cli` itself is pre-1.0** (currently `0.1.5`) — normal SemVer
  "still shifting, no compatibility guarantee yet."