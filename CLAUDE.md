# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Flare is a self-hosted, OpenTelemetry-native observability platform for .NET — logs, traces, and metrics correlated in one place, with threshold/query-based alert rules (webhook/Slack, Telegram, email). Full architecture: [docs/explanation/architecture.md](docs/explanation/architecture.md). Design decisions: [docs-internal/adr/](docs-internal/adr/) (numbered, sequential, never edited in place — a changed decision gets a new ADR that supersedes the old one). Forward-looking work: [docs-internal/planning/roadmap.md](docs-internal/planning/roadmap.md) (completed items are deleted, not checked off).

**Before any structural/architecture question, query `graphify-out/` (a pre-built knowledge graph) rather than doing a fresh Explore/grep sweep.**

## Repo layout

```
src/Flare.Ingest         OTLP receiver (gRPC :4317, HTTP :4318) -> LogEvent -> Redis Streams -> ClickHouse
src/Flare.Api             Query API over ClickHouse: search/aggregate/live-tail (WebSocket)/alerts/views
src/Flare.Identity        Auth: local accounts, Entra ID, LDAP, OIDC, reverse-proxy trusted headers (embedded SQLite)
src/Flare.AppHost         .NET Aspire local orchestration (the dev inner loop)
src/Flare.Cli             Global dotnet tool ("flare start/stop/status/...") managing a standing standalone stack
src/Aspire.Hosting.Flare  NuGet PackageId Flare.Hosting.Aspire — AddFlare() hosting integration for consumer AppHosts
src/Aspire.Flare          NuGet PackageId Flare.Aspire — shared client-side integration bits
src/Flare.ServiceDefaults Shared Aspire service defaults + ClickHouseMigrations runner
src/dashboard             SvelteKit 2 (Svelte 5 runes) + Tailwind 4 + shadcn-svelte SPA — talks to Flare.Api over HTTP/WS
src/website               Next.js marketing site (separate from the dashboard app)
src/*.Tests               xUnit unit tests, one per testable project (Flare.Ingest.Tests, Flare.Api.Tests, Flare.Identity.Tests)
db/clickhouse             Numbered .sql migrations, mounted at container init (docker-entrypoint-initdb.d convention)
db/clickhouse-cluster     Same migrations, cluster-mode variant
examples/                 ExampleApp.AppHost + ExampleApp.LogGenerator — a runnable OTLP-emitting sample
docs/                     User-facing docs (Diátaxis: tutorials/how-to/reference/explanation)
docs-internal/            Maintainer docs: adr/, investigations/, planning/roadmap.md
```

The projects named `Aspire.Flare`/`Aspire.Hosting.Flare` on disk deliberately do **not** match their NuGet `PackageId`s (`Flare.Aspire`/`Flare.Hosting.Aspire`) — the `Aspire.*` prefix is reserved on nuget.org. See the comment at the top of each `.csproj`.

Solution file is **`Flare.slnx`** (not `.sln`).

## Commands

**.NET** (targets `net10.0`, SDK pinned via `global.json` to `10.0.100`, Aspire SDK `13.4.6`):
```bash
dotnet build Flare.slnx
dotnet test                          # run from repo root, or inside a specific src/*.Tests project
dotnet test --filter "FullyQualifiedName~OtlpLogMapper"   # single test/class
dotnet run --project src/Flare.AppHost   # whole stack via Aspire (recommended dev inner loop)
dotnet run --project src/Flare.Ingest    # or standalone, per-project
```
Tests are plain xUnit, no hosting/network/containers — classes that need real ClickHouse/Redis/HTTP I/O (`ClickHouseFlushWorker`, `LogQueryService`, `LogTailBroadcaster`, `AlertEvaluationWorker`, etc.) are deliberately **not** unit-tested against a fake; they're covered by real end-to-end runs instead (see each project's own README's "Tests" section for exactly what is/isn't covered and why).

If a `dotnet restore`/build hangs, stop and run `scripts/kill-build-processes.sh` (`--dry-run` to just list) before retrying — known issue on this machine, including orphaned Aspire CLI helper processes that ignore SIGTERM.

**Dashboard** (`src/dashboard`):
```bash
npm install
npm run dev -- --open    # requires Flare.Api running first; PUBLIC_API_URL in .env (copy .env.example)
npm run build && npm run preview
npm run check             # svelte-check type checking
```
Uses `@sveltejs/adapter-node` (not `adapter-static`) — required because `PUBLIC_API_URL` is read via `$env/dynamic/public` at request time, not baked in at build time.

**Whole stack, no Aspire:**
```bash
docker compose up            # standalone stack: clickhouse, redis, ingest, api, dashboard
./scripts/run-full-stack.sh  # same, plus waits for health + runs the example log generator for sample data
```

**Docs:** `python3 scripts/check-docs-links.py` after touching Markdown under `docs/`, `docs-internal/`, `README.md`, or `CONTRIBUTING.md` — validates relative links/anchors and that every `docs/{tutorials,how-to,reference,explanation}/` page is reachable from `docs/README.md`'s index. Also runs in CI (`.github/workflows/docs-links.yml`).

## Architecture

```
apps (Serilog/NLog/ZLogger/MS ILogger/any OTLP source)
  --OTLP (gRPC :4317 / HTTP :4318)--> Flare.Ingest --Redis Streams (buffer)--> ClickHouse
                                                                                    |
                                                              Flare.Api (query/aggregate/live-tail WS)
                                                                                    |
                                                              Flare.Dashboard (SvelteKit SPA)
```

- **One protocol in: OTLP only** (ADR-0012). No per-logger ingestion adapters.
- **ClickHouse is the storage engine** (ADR-0013), wrapped rather than reinvented. Migrations in `db/clickhouse/*.sql` are numbered and applied in order at container init; each new one is additive (`ALTER TABLE ... ADD COLUMN`, new table), never edited retroactively.
- **Redis Streams, not an in-memory buffer**, sits between Ingest and ClickHouse specifically so buffered-but-unflushed events survive `Flare.Ingest` restarting (ADR-0002). `ClickHouseFlushWorker` reads via a consumer group and only ACKs after a successful insert (at-least-once). Live-tail (`Flare.Api`'s `LogTailBroadcaster`) reads the same stream with a plain `XREAD`, not the consumer group — it doesn't touch Ingest's PEL/delivery accounting.
- **`Model`/`Query` layers are pure and ClickHouse-free** in both `Flare.Ingest` and `Flare.Api` (`OtlpLogMapper`, `ClickHouseRowMapper`, `LogFilterSqlBuilder`, `LogSearchQueryBuilder`, `LogAggregateQueryBuilder`, `LogSearchCursor`, `LogFilterMatcher`) — this is what's unit-tested. The one seam that actually talks to ClickHouse is `LogQueryService`.
- **`LogFilter`** (in `Flare.Api/Model`) is the one shared filter shape reused verbatim for both `/api/logs/*` endpoints *and* as an alert rule's condition — an alert rule is a saved `LogFilter` plus a threshold and notification target.
- **`/api/logs/search` and `/api/logs/aggregate` are POST**, not GET-with-query-string, because filters are multi-valued/structured (service lists, attribute key/value pairs).
- **Keyset pagination** via `(Timestamp, EventId) < (cursor)` tuple comparison, not offset — `EventId` (a `UUID` added in `db/clickhouse/0002_logs_event_id.sql`) is the tiebreaker for same-`Timestamp` rows. The returned cursor is an opaque base64 token, not a public contract.
- **Every ClickHouse query sets execution caps** (`max_execution_time`, `max_rows_to_read`, `max_result_rows`, etc. via `QueryOptions.CustomSettings`) — self-hosted ClickHouse has no defaults for these. `/api/logs/search` also defaults to a 1-hour lookback when `From`/`To` are omitted, so an unfiltered request can't scan the whole table.
- **`LogEventDto`/`BufferedLogEvent` in `Flare.Api` deliberately mirror, rather than reference, `Flare.Ingest`'s `LogEvent`** — same convention repeated at each API-facing boundary rather than sharing a type across projects.
- **Identity is embedded SQLite**, not a fourth backing-store container — `Flare.Ingest` and `Flare.Api` both resolve to the same `Identity__DbPath` file (a gitignored `.data/identity/` dir locally under Aspire; a named Docker volume in `docker-compose.yml`).
- **Log pattern clustering (Drain) happens at ingest/flush time**, not query time (ADR-0007) — `PatternId`/`PatternTemplate` columns are computed once and just `GROUP BY`'d later, not recomputed per query.
- **Aspire wiring lives only in `src/Flare.AppHost/AppHost.cs`** — read its inline comments before changing resource wiring; they record non-obvious constraints (e.g. ClickHouse's init password must avoid shell-special characters or the official image's init script breaks; the OTLP ports are fixed/unproxied on purpose so external loggers can point at conventional port numbers).
- **Three legitimate install paths**, not redundant: Aspire integration (`Flare.Hosting.Aspire`, ties Flare's lifecycle to a consumer AppHost), standalone `docker compose up` (one-off local eval), and the `flare` CLI (a standing instance shared across unrelated local projects, independent of any one AppHost).

## Where documentation goes

Read [docs-internal/README.md](docs-internal/README.md)'s decision tree before adding docs. Short version: user-facing "how do I / what is the exact value / why does it work this way" → `docs/` (Diátaxis-typed, use the `diataxis` skill); a significant, hard-to-reverse architectural decision → a new `docs-internal/adr/NNNN-*.md`; a debugging/benchmark finding with real evidence → `docs-internal/investigations/`; still-open future work → one line in `docs-internal/planning/roadmap.md`; anything scoped to building/testing one project → that project's own `src/*/README.md`. A PR that changes user-visible behavior, adds a config option, or makes an architectural call updates the matching doc (or adds the ADR) in the *same* PR, not after.
