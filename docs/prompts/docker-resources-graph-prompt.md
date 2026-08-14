# Docker-driven Resources page for Flare's dashboard

Paste everything below into a new session.

---

Add a "Resources" page to Flare's dashboard (`src/dashboard`) showing the resources Flare
manages (ClickHouse, Redis, Ingest, Api, Dashboard) as a graph — nodes with live
state/health/URL, edges for their relationships — sourced from the **Docker Engine API**,
not from Aspire's resource service. This repo already tried the Aspire-resource-service
approach today and discarded it: it only worked in Flare's own internal dev-loop AppHost
(`src/Flare.AppHost`), which is not a real target — nobody outside this repo ever sees
that AppHost. **Do not build anything against Aspire's resource-service gRPC/proto again.**

## Who this is for

Real developers in Flare's two actual deployment shapes, both of which run Flare's own
components as **Docker containers**:

1. **Standalone**: `docker compose up` via the repo-root `docker-compose.yml`. No AppHost
   involved at all.
2. **A consumer's own Aspire AppHost**, via the published `Flare.Hosting.Aspire` NuGet
   package's `AddFlare()` extension (`src/Aspire.Hosting.Flare/FlareResourceBuilderExtensions.cs`),
   which wraps Flare's three published Docker images (`xracer007/flare-ingest`,
   `xracer007/flare-api`, `xracer007/flare-dashboard`) via Aspire's `AddContainer(...)`.

`src/Flare.AppHost` (Flare's own internal dev loop, where `ingest`/`api` run as plain
`dotnet` processes via `AddProject`, not containers) is explicitly **out of scope**. Don't
special-case it, don't try to make it show resources too — it doesn't need to.

## What's already confirmed today (don't re-derive, verify before relying on if it matters)

- **Docker Compose's `depends_on` is NOT exposed via container labels or the Docker API at
  all** — confirmed by web research (only `com.docker.compose.project`/`.service`/
  `.project.working_dir` are real labels; dependency structure only ever exists in the
  compose YAML itself, unreadable at runtime by inspecting a container). So relationships
  can't be derived generically — **Flare has to author them itself**, as a custom label on
  every container it creates:
  - In `docker-compose.yml`, add a `labels:` entry per service, e.g.
    `flare.relationships=clickhousedb:Reference,redis:Reference` (whatever shape you land
    on — same relationship-type vocabulary Aspire itself used doesn't matter here, pick
    something Flare.Api can parse cleanly).
  - In `Aspire.Hosting.Flare/FlareResourceBuilderExtensions.cs`'s `AddFlare()`, add the
    equivalent labels when building each `AddContainer(...)` call. Check what label-setting
    API Aspire's `IResourceBuilder<ContainerResource>` actually exposes (something like
    `.WithContainerRuntimeArgs` or a more direct label method — verify against the Aspire
    13.4.6 source, same version this repo is pinned to in `Directory.Packages.props`).
  - Also add a Flare-authored identity label (e.g. `flare.resource=true`) on every
    container Flare creates, in both places, so Flare.Api can filter "containers that are
    part of my own install" out of everything else running on the Docker host — don't rely
    on `com.docker.compose.project` for this (it doesn't apply to `AddFlare()`/DCP-created
    containers at all).

- **Health**: `docker-compose.yml` (repo root) already has real `healthcheck:` blocks for
  every Flare service (ClickHouse, Redis, ingest, api, dashboard) — confirmed by reading
  it. That means `docker inspect`'s `.State.Health.Status` is already populated for free
  in standalone mode, no compose changes needed there. **But** none of the three
  Dockerfiles (`src/Flare.Api/Dockerfile`, `src/Flare.Ingest/Dockerfile`,
  `src/dashboard/Dockerfile`) have a `HEALTHCHECK` instruction baked in — confirmed by
  `grep`. Aspire's own `.WithHttpHealthCheck(...)` (used in `AddFlare()`) is DCP polling
  from *outside* the container, not a Docker-native healthcheck, so a container started
  via `AddFlare()` likely won't have `.State.Health` populated the same way standalone
  does. **Add `HEALTHCHECK` directly to all three Dockerfiles** (mirroring what
  `docker-compose.yml`'s existing `healthcheck:` blocks already test) so Docker's own
  health tracking works uniformly regardless of which orchestrator started the container.
  Verify this empirically once built — start each mode and confirm `.State.Health` is
  populated in both.

- **URLs**: real and live, no extra work — `docker inspect`'s
  `.NetworkSettings.Ports`/`GetContainerJSON`'s port bindings give actual published host
  ports; construct `http://localhost:<port>` from them.

- **State**: `.State.Status` (running/exited/restarting/paused) is always populated
  regardless of any healthcheck config — use it as the base signal, `.State.Health.Status`
  as a refinement when present.

## Docker access — the one thing that needs explicit, loud opt-in

Reading any of this requires Flare.Api to reach the Docker Engine API, which means Docker
socket access one way or another. **This is a real, meaningful security decision — treat
it accordingly:**

- Do **not** silently mount `/var/run/docker.sock` into Flare.Api by default in
  `docker-compose.yml`. Raw socket access is effectively root-equivalent host access
  (start/stop/inspect *any* container on the host, read *any* container's env vars/secrets,
  real container-escape potential in some configurations).
- Use a **scoped, read-only socket proxy** instead — `tecnativa/docker-socket-proxy` is the
  standard, widely-used one for exactly this. Configure it to only allow the
  `CONTAINERS=1` (list/inspect) endpoint, nothing else (no exec, no start/stop, no
  image/volume/network management).
- Make the whole feature **opt-in**, off by default, so `docker compose up`'s existing
  "zero-config, works out of the box" promise (see `docker-compose.yml`'s own header
  comment) isn't broken for everyone. A Docker Compose
  [profile](https://docs.docker.com/compose/how-tos/profiles/) for the socket-proxy
  service, gated behind an env var Flare.Api checks (unset = feature disabled, same
  "absent config = off" pattern the discarded Aspire-resource-service attempt used) is a
  reasonable shape — pick whatever's cleanest, but the default `docker compose up` with no
  extra flags/env vars must not mount the socket or proxy.
- Document this loudly in `docs/standalone.md` and `docs/aspire-hosting.md` — what it
  requires, why, and what the socket-proxy scoping actually restricts, matching this repo's
  existing habit of being explicit about security tradeoffs (see e.g. `Program.cs`'s CORS
  comment: `"v1 has no auth story anywhere yet... Revisit once auth lands"` — same
  transparent tone, not overselling the mitigation).
- For a consumer's `AddFlare()` mode: same proxy pattern, wired into the AppHost via
  `Aspire.Hosting.Flare`'s `AddFlare()` — figure out the cleanest way to add an optional
  socket-proxy sidecar resource there too, off by default (an `AddFlare()` parameter,
  e.g. `enableResourceGraph: bool = false`, is a reasonable shape).

## Backend shape

New code in `Flare.Api`, following patterns already established in this repo rather than
inventing new ones — read these first:

- `src/Flare.Api/LiveTail/LogTailBroadcaster.cs` — the "one background poller, many
  WebSocket subscribers" pattern to mirror for pushing live container state to the
  dashboard (poll Docker's container list on an interval, or use Docker's own event stream
  if you want push-based updates instead of polling — Docker Engine API has a
  `/events` endpoint that streams container state changes; worth using instead of polling
  if it's not meaningfully more complex).
- `src/Flare.Api/Endpoints/LogTailEndpoints.cs` + `LogsEndpoints.cs` — the "REST snapshot +
  WebSocket stream" endpoint pairing convention.
- `src/Flare.Api/Json/LogsJsonContext.cs` / `LogTailJsonContext.cs` — camelCase properties,
  string enums via `UseStringEnumConverter` (no naming policy applied to enum *values* —
  see the asymmetry documented at the top of `src/dashboard/src/lib/api.ts`), source-gen
  `JsonSerializerContext` per feature area.
- `Directory.Packages.props` — centrally-managed package versions; add whatever Docker
  Engine API client you pick there (a `Docker.DotNet`-based client, or a minimal
  hand-rolled `HttpClient` over the Unix socket via `SocketsHttpHandler.ConnectCallback` —
  check current `Docker.DotNet` maintenance status before depending on it, it's had
  stretches of low activity in the past; a hand-rolled client talking to a well-documented
  REST API over a Unix socket is a legitimate, low-dependency alternative worth seriously
  considering here instead).
- `src/Flare.Api/Model/`, `src/Flare.Api/Endpoints/` — put new DTOs/endpoints in
  matching new subfolders (e.g. `Model/ResourceGraphDto.cs`, `Endpoints/ResourceGraphEndpoints.cs`,
  a `DockerResources/` folder for the poller/client, mirroring `LiveTail/`'s shape) rather
  than piling into existing files.
- Endpoint shape: `GET /api/resources/snapshot` (REST) + `GET /api/resources/watch`
  (WebSocket) is a reasonable pairing to reuse, but reconsider the "available: false" /
  "Unavailable" message semantics from scratch for this feature — here "unavailable" means
  "Docker socket access isn't configured," not "no AppHost," which is a different and
  probably clearer story to tell in the UI.

## Frontend shape

- `@xyflow/svelte` is **already a dependency** in `src/dashboard/package.json` — kept
  specifically from today's earlier (fully reverted) exploration, for this exact use case.
  Use it, don't reach for something else.
- There's currently **no sidebar/nav and no `/resources` route** — that was part of the
  fully-reverted work too. Start fresh: decide the route structure, add nav, wire a
  `ResourcesState` (Svelte 5 runes class) + context, following
  `src/dashboard/src/lib/logs/state.svelte.ts` / `src/dashboard/src/lib/logs/context.ts`'s
  established shape (a generic `createContext<T>` helper is worth factoring out again if
  you add a second context — it doesn't exist in the codebase right now).
- `src/dashboard/src/lib/api.ts` — existing conventions to match: typed DTOs mirroring the
  backend's C# models 1:1, a documented field-casing/enum-casing note at the top, a
  `connect*`-style function returning a handle with `close()` for the WebSocket case (see
  `connectLiveTail`).
- Reuse `src/dashboard/src/lib/components/ui/empty/*` (shadcn-style Empty/EmptyHeader/
  EmptyMedia/EmptyTitle/EmptyDescription, already in the repo) for the
  disabled/no-Docker-access state, and `src/dashboard/src/lib/components/ui/badge/*` for
  status badges (variant-by-status pattern already used in `LogsToolbar.svelte` for the
  live-tail connection badge — reuse the same idea for container state/health).
- SvelteFlow has no built-in auto-layout; pair it with a small dagre-based layout helper if
  the graph needs one (today's discarded work had one at a `resources/layout.ts` shape you
  can reconstruct — dagre isn't currently a dependency, add `@dagrejs/dagre` back if you
  go this route).
- **Verify dark-mode styling early**: SvelteFlow ships light-mode CSS by default; this
  dashboard is dark-only (`src/dashboard/src/app.html` hardcodes `class="dark"` on
  `<html>`, no toggle exists). Pass `colorMode="dark"` to `<SvelteFlow>` from the start —
  discovered the hard way today that skipping this renders Controls and edge labels as
  broken white-on-white boxes.

## Verification

Whoever picks this up should actually run both target modes end-to-end before calling it
done, not just get it to compile:

1. `docker compose up` (repo root, standalone) with the new opt-in Docker-access flag/
   profile enabled — confirm the Resources page shows real containers, real health
   (stop one manually with `docker stop`, confirm the badge updates), real relationships
   from the new labels, real URLs.
2. A consumer AppHost using `AddFlare(..., enableResourceGraph: true)` (or whatever the
   final parameter shape is) — `examples/ExampleApp.AppHost` in this repo is exactly this
   scenario already wired up; confirm the same things there.
3. Confirm the *default*, no-flag `docker compose up` and default `AddFlare()` call
   **don't** mount any Docker socket/proxy and the Resources page shows a clean, honest
   "not enabled" state instead of erroring.
4. `dotnet build`, `dotnet test`, `npm run check` all clean, same bar as the rest of this
   repo.
