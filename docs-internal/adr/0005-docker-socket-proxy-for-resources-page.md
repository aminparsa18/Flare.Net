# ADR-0005: Docker access for the Resources page goes through a read-only socket proxy, never a direct mount

Status: Accepted
Date: 2026-08-14 (Resources page/producer-overlay, PR #48/#49)

## Context

The dashboard's Resources page shows Flare's own containers (ClickHouse,
Redis, ingest, api, dashboard) as a live graph — state, health, URLs, and
the relationships between them. Building this means `flare-api` needs some
way to query the Docker Engine API. Both the standalone (`docker-compose.yml`)
and Aspire (`Aspire.Hosting.Flare`, local `aspire run`) deployment paths
need the same capability, so the decision applies to both.

## Decision

`flare-api` never mounts `/var/run/docker.sock` directly. Instead, a
dedicated sidecar — [`tecnativa/docker-socket-proxy`](https://github.com/Tecnativa/docker-socket-proxy)
— sits between them: it's the only thing that ever mounts the real socket
(read-only), configured with `CONTAINERS=1` and nothing else, so it only
answers container *list*/*inspect* calls. `POST=0` blocks every mutating
endpoint outright (no start/stop/create/kill/exec), and there's no `EXEC`,
`IMAGES`, `VOLUMES`, `NETWORKS`, or `BUILD` env var set either, so those
endpoints aren't reachable through it either. `flare-api` only ever talks
to the proxy's scoped HTTP endpoint, on the internal compose/AppHost
network — no published host port.

The feature is **off by default**, requiring two explicit opt-ins
(`FLARE_DOCKER_PROXY_URL` at the app level, plus actually starting the
proxy container — a Compose profile standalone, `enableResourceGraph: true`
on `AddFlare` under Aspire). With neither set, no proxy container exists at
all, `flare-api` never gains any form of Docker access, and the page shows
a plain "not enabled" state rather than an error.

## Alternatives considered

- **Mount `/var/run/docker.sock` directly into `flare-api`.** Rejected:
  raw Docker socket access is effectively root-equivalent access to the
  whole host — start, stop, or inspect *any* container, read *any*
  container's environment variables/secrets, real container-escape
  potential in some configurations. Doing that to a network-facing service
  (unlike a one-off CLI script) would be a meaningfully worse security
  posture than this repo is comfortable defaulting anyone into, silently
  or otherwise.
- **Ship it enabled by default.** Rejected for the same reason the
  trusted-header auth method (see
  [`docs/explanation/authentication-model.md`](../../docs/explanation/authentication-model.md#reverse-proxy-trusted-header))
  has no "trust everyone" default: a working default that quietly grants
  Docker-adjacent access isn't a trade-off this repo makes without the
  operator deciding to.

## Consequences

- Every deployment path that wants the Resources page pays the same
  two-opt-in cost and gets the same scoped-down capability — no path
  (standalone, Aspire/Docker Compose target) gets broader Docker access
  than another.
- This is a scoped-as-tightly-as-practical mitigation, not a
  fully-eliminated risk — same "no working default because the trade-off
  is real" posture as this repo's other security-sensitive defaults (see
  `Program.cs`'s CORS comment: *"v1 has no auth story anywhere yet...
  Revisit once auth lands"*).
- On a Kubernetes deployment target, this exact mechanism doesn't apply at
  all — there's no `/var/run/docker.sock` equivalent inside a pod. A
  separate provider and a separate RBAC-based access model exists for
  that case; see
  [ADR-0006](0006-kubernetes-resource-graph-rbac-scoping.md).

## Related documentation

- `docs/how-to/run-standalone.md` — enabling it via Docker Compose
- `docs/how-to/run-with-aspire.md` — enabling it via `enableResourceGraph`
- ADR-0006 — the Kubernetes-side equivalent decision