# ADR-0018: Extract `AlertEvaluationWorker` into its own `Flare.AlertWorker` process

Status: Accepted
Date: 2026-09-10

## Context

`AlertEvaluationWorker` (a `BackgroundService` that polls every enabled alert rule on a
fixed interval and notifies on breach - see `Flare.Api/Alerting/AlertEvaluationWorker.cs`'s
history) has run inside `Flare.Api` since alerting was added. That coupling wasn't
prompted by an observed incident, but it's a real cost:

- Every `Flare.Api` deploy/restart also stops alert evaluation until the process comes
  back - the worker's own per-tick Redis lock (`flare:alerts:eval-lock`, coordinating
  multiple replicas so only one evaluates per tick) only helps once you're already running
  ≥2 replicas.
- The poll loop shares `Flare.Api`'s thread pool and CLR process with query/dashboard
  traffic, so a slow rule-condition query or a heavy tick can degrade both.
- Every `Flare.Api` replica added purely for query-load scaling also adds a redundant,
  lock-contending evaluator, even though only one wins per tick.

Unlike SigNoz's `alertmanager`/`query-service` split, `AlertEvaluationWorker` has no
state coupling to `Flare.Api` worth preserving: `AlertQueryService` holds only an
`IClickHouseClient` (rules and alert history live in ClickHouse, not Identity's
embedded SQLite), and the Redis lock already assumes and coordinates multiple concurrent
instances. That means the split needs no new backing store, and - unlike SigNoz, where
Alertmanager is a third-party binary that only speaks HTTP back to query-service - no
internal HTTP surface either.

## Decision

**A new project, `Flare.AlertWorker`, runs `AlertEvaluationWorker` as its own process,
pointed at the same ClickHouse + Redis config every other Flare process already uses.**
No new backing store, no HTTP between it and `Flare.Api`.

**Only `AlertEvaluationWorker` and `AlertingOptions` physically move.** Everything else
`AlertEvaluationWorker` depends on - `AlertQueryService`, `CompositeAlertNotifier` + its
four channel notifiers, `EmailOptions`, and the `AlertRule`/`AlertThreshold`/
`LogFilter`/`AlertHistoryEntry` model types - stays defined in `Flare.Api`, because
`Flare.Api` itself still needs every one of them directly: `AlertEndpoints`' CRUD/history/
dry-run-test routes use `AlertQueryService`, and its `/api/alerts/*/send-test` routes use
`IAlertNotifier`/`CompositeAlertNotifier` to fire a real, synchronous test notification
independent of the poll loop. `LogFilter`/`LogFilterSqlBuilder` in particular are the one
shared filter shape reused verbatim for both `/api/logs/*` endpoints and an alert rule's
condition (`CLAUDE.md`) - forking a second copy for evaluation-only use would break that
invariant.

`Flare.AlertWorker` reuses all of it via a plain `ProjectReference` to `Flare.Api.csproj`
rather than duplicating it. This is a different shape from the `Flare.Ingest`→`Flare.Api`
boundary, which deliberately mirrors DTOs instead of sharing a project reference (see
`LogEventDto`'s own remarks) - that convention exists to decouple an internal ingestion
model from a public API DTO across a real process/version boundary with external
callers. Alerting has no such external caller on either side; this is instead the same
shape `Flare.Identity` already is - a shared library `ProjectReference`d by more than one
Flare process (`Flare.Ingest` and `Flare.Api` today). `Flare.Api`'s own DI registrations
for `AlertQueryService`/the notifiers are left in place unchanged; `Flare.AlertWorker`'s
`Program.cs` registers the same types a second time in its own container - normal,
expected duplication of *wiring*, not of *code*.

`Flare.AlertWorker` is `Microsoft.NET.Sdk.Web` (not a bare worker SDK) solely to expose
`/health`/`/alive` via the same `Flare.ServiceDefaults` `MapDefaultEndpoints()` every
other Flare process already uses - Aspire's `WithHttpHealthCheck` needs a real HTTP
endpoint to poll, and Docker/k8s health probes need one too. It has no other HTTP
surface.

## Consequences

- `docker-compose.yml` and `Flare.AppHost` (the two install paths that build from source)
  both gain a fourth/fifth service: `alert-worker`. Wired in the same PR as this ADR.
- **Release gate:** `Aspire.Hosting.Flare`'s `AddFlare()` and the `flare` CLI's own
  embedded compose templates (`TopologyProfile.cs`) both pull *pinned, published* Docker
  Hub image tags rather than building from source, and neither is updated in this PR -
  doing so productively requires an actual released `alert-worker` image to point at,
  which doesn't exist yet. Until both are updated to add the `alert-worker` container,
  **the next Flare release's default `imageTag`/`DefaultImageTag` must not be adopted by
  either path** - the released `api` image will no longer run `AlertEvaluationWorker` at
  all (it moved out), and nothing would replace it, so upgrading through `AddFlare()` or
  `flare update` would silently stop alert evaluation entirely with no error surfaced
  anywhere. Tracked as a follow-up in `docs-internal/planning/roadmap.md`.
- One more deployable unit to build, publish, and monitor per install path that does get
  wired - the cost side of the split, accepted for the reasons in Context.
- `AlertEvaluationWorker` remains deliberately un-unit-tested (real ClickHouse/Redis/HTTP
  I/O), now documented in `Flare.AlertWorker/README.md` instead of `Flare.Api/README.md`.
