# `Flare.Hosting.Aspire` reference

Exact `AddFlare` API and Kubernetes-deployment facts for `Flare.Hosting.Aspire`
(`src/Aspire.Hosting.Flare`, package ID `Flare.Hosting.Aspire` — not
`Aspire.Hosting.Flare`; that prefix is reserved on nuget.org for
Microsoft's own official integrations). For how to use it, see
[`../how-to/run-with-aspire.md`](../how-to/run-with-aspire.md).

> **Status:** published on nuget.org as `Flare.Hosting.Aspire` (currently
> `0.4.1`).

## `AddFlare`

```csharp
IResourceBuilder<FlareResource> AddFlare(
    this IDistributedApplicationBuilder builder,
    string name = "flare",
    string imageTag = "0.5.0",
    bool enableResourceGraph = false)
```

Only the three things that decide *what gets built* (a new sidecar
container or RBAC objects, in `enableResourceGraph`'s case) are
constructor parameters; everything else is a `With*` chain method on the
returned `FlareResource` builder, the usual Aspire convention (compare
`AddRedis(...).WithPersistence(...)`).

| Parameter | Meaning |
|---|---|
| `name` | The Flare resource group's name in the Aspire dashboard. |
| `imageTag` | Defaults to the latest stable Flare release this package version was tested against (currently `"0.5.0"`), pulled from Docker Hub's immutable `v*.*.*` tags. Deliberately not the floating `latest`/`edge` tags — this default only moves forward when a new `Flare.Hosting.Aspire` release bumps it. Pass `imageTag: "edge"` to track Flare's unreleased `main` branch instead. `WithIngestImage`/`WithApiImage`/`WithDashboardImage` reuse this same tag when overriding just an image name/registry — no separate per-image tag override. |
| `enableResourceGraph` | Turns on the dashboard's Resources page for this Flare instance. Off by default. Kept as a constructor argument (unlike everything below) because it decides whether whole extra resources exist at all — a docker-socket-proxy sidecar on Docker ([ADR-0005](../../docs-internal/adr/0005-docker-socket-proxy-for-resources-page.md)), or an RBAC `ServiceAccount`/`Role`/`RoleBinding` on Kubernetes ([ADR-0006](../../docs-internal/adr/0006-kubernetes-resource-graph-rbac-scoping.md)) — more invasive to add after the fact than reconfiguring a port or image on an already-created resource. |

### `With*` chain methods

| Method | What it overrides |
|---|---|
| `WithIngestGrpcPort(int)` / `WithIngestHttpPort(int)` | The OTLP receiver's host ports. Default `4317`/`4318` if uncalled. Always unproxied, so external OTLP clients can point at them directly — same fixed-port story as `docker-compose.yml`. |
| `WithApiPort(int)` / `WithDashboardPort(int)` | The query API / dashboard host ports. Normal proxied Aspire HTTP endpoints. |
| `WithIngestImage(string)` / `WithApiImage(string)` / `WithDashboardImage(string)` | Image name/registry (not tag — `imageTag` still supplies that) — for local-dev use against images built with `docker compose build` instead of Docker Hub. |
| `WithApiKey(IResourceBuilder<ParameterResource>)` | Pass a `secret: true` `AddParameter` result to require OTLP callers to present an ingest API key. Uncalled = ingest stays anonymous. No automatic flow-through: a project calling `AddFlareOtlpExporter` still needs the same raw value passed to its own `configureSettings: s => s.ApiKey = ...` delegate. |
| `WithPublicApiUrl(...)` / `WithPublicDashboardUrl(...)` | Override the `localhost`-pinned browser-facing URLs (`PUBLIC_API_URL`/`ORIGIN`/`Cors__AllowedOrigins__0`). Only needed once actually publishing/deploying — see [Publishing to Kubernetes/Docker Compose](#deployment-facts) below. |
| `WithPersistentStorage(clickHouseVolume, redisVolume, identityVolume)` | Binds three `kubernetesEnvironment.AddPersistentVolume(...)` results (each with whatever `WithStorageClass`/`WithCapacity`/`WithAccessMode` you need) to ClickHouse's/Redis's/the identity database's storage — see [the Kubernetes section](#kubernetes) below. Requires suppressing `ASPIRECOMPUTE002` (same requirement `AddPersistentVolume` itself carries) — this method is marked `[Experimental("ASPIRECOMPUTE002")]` for that reason. |

> **Breaking change in `0.3.2`:** before this version, all of the above
> were parameters on `AddFlare` itself
> (`AddFlare(..., ingestGrpcPort: ..., apiKey: ..., ...)`). Replaced
> outright rather than kept as a deprecated overload — update any
> `AddFlare(..., someParam: ...)` call beyond `name`/`imageTag`/
> `enableResourceGraph` to the matching `With*` method above.

## Deployment facts

Facts to know before deploying (not `aspire run`) a Flare-containing
AppHost — for the actual steps, see
[`../how-to/run-with-aspire.md#publishing--deploying`](../how-to/run-with-aspire.md#publishing--deploying).

### Docker Compose

- `publicApiUrl`/`publicDashboardUrl` are unset by default — `aspire run`
  assumes the browser is on the same machine and points
  `PUBLIC_API_URL`/`ORIGIN`/`Cors__AllowedOrigins__0` at `localhost`. Left
  unset, Aspire captures them as `.env.{environment}` placeholders you fill
  in with real deployed URLs per environment.
- ClickHouse init builds a small custom image at publish time (`FROM`
  whatever image `AddClickHouse` resolves to, plus the init scripts
  `COPY`-ed in) rather than a bind mount — the bind-mount source path only
  exists on whatever machine ran `aspire publish`, not the deploy target.
  This means local `aspire run`/`aspire start` needs `docker build`
  capability too, not just pull/run.
- `enableResourceGraph`'s docker-socket-proxy still needs a real Docker
  host — its `/var/run/docker.sock` bind mount requires the machine
  actually running `docker compose up` to have that socket present.

### Kubernetes

- **A container registry is required, but only because of the
  ClickHouse-init image.** `flare-ingest`/`flare-api`/`flare-alert-worker`/
  `flare-dashboard` need no registry at all (Flare's own pre-published Docker Hub images,
  referenced directly) — only the generated ClickHouse-init image shows up
  in `values.yaml` as a registry-relative placeholder that `aspire deploy`
  builds and pushes to the registry configured via
  `AddContainerRegistry`/`WithContainerRegistry`. The container registry
  APIs are still preview in Aspire itself (`ASPIRECOMPUTE003`).
- **ClickHouse/Redis/identity data does NOT survive a pod restart unless
  you call `WithPersistentStorage`.** `AddFlare`'s `WithDataVolume()`/
  `WithVolume()` calls render as plain `emptyDir: {}` volumes in the
  generated `StatefulSet`/`Deployment` specs, not `PersistentVolumeClaim`s,
  until you bind a real
  [`AddPersistentVolume`](https://aspire.dev/deployment/kubernetes/persistent-volumes/)
  to each of `{name}-clickhouse-data`, `{name}-redis-data`, and
  `{name}-identity-data`. `WithPersistentStorage` (`Flare.Hosting.Aspire`
  `0.4.0`+, needs `Aspire.Hosting.Kubernetes` `13.5.3-preview.1.26425.3`+ —
  see `Directory.Packages.props`) does exactly that binding for you:

    ```csharp
    var k8s = builder.AddKubernetesEnvironment("k8s");
    var flare = builder.AddFlare("flare")
        .WithPersistentStorage(
            clickHouseVolume: k8s.AddPersistentVolume("flare-clickhouse-data")
                .WithStorageClass("standard").WithCapacity("20Gi")
                .WithAccessMode(PersistentVolumeAccessMode.ReadWriteOnce),
            redisVolume: k8s.AddPersistentVolume("flare-redis-data")
                .WithStorageClass("standard").WithCapacity("5Gi")
                .WithAccessMode(PersistentVolumeAccessMode.ReadWriteOnce),
            identityVolume: k8s.AddPersistentVolume("flare-identity-data")
                .WithStorageClass("standard").WithCapacity("1Gi")
                .WithAccessMode(PersistentVolumeAccessMode.ReadWriteOnce));
    ```

    `AddFlare` still has no way to pick a storage class/capacity/access-mode
    policy on your behalf — you supply three already-configured
    `AddPersistentVolume` results, `WithPersistentStorage` just performs the
    binding. Requires suppressing `ASPIRECOMPUTE002` in your own AppHost
    project (same requirement `AddPersistentVolume` itself carries).
    Verified live against a real k3s cluster (`aspire deploy`, not just
    generated manifests): PVCs bind, ClickHouse and Redis both survive a pod
    delete with their data intact, and the shared identity PVC really is one
    file, mounted read-write by both the ingest and api StatefulSets' Pods
    at once with no corruption — see
    [the investigation](../../docs-internal/investigations/aspire-kubernetes-publish-and-resource-graph.md)'s
    finding #8. One caveat that pass couldn't rule out: it ran on a
    single-node cluster, where `ReadWriteOnce`'s node-scoped restriction
    doesn't come into play at all — whether the shared identity PVC still
    works with ingest and api scheduled onto two *different* nodes on a real
    multi-node cluster (where most block-storage CSI drivers would need
    `ReadWriteMany` instead) remains unverified.
  - Skipping `WithPersistentStorage` against a registered Kubernetes
    environment triggers a `⚠️` warning from `AddFlare` — but as of Aspire
    `13.5.3`'s new pipeline-execution CLI, that warning no longer reaches
    the terminal during `aspire publish`/`aspire deploy` (it's still
    written, but only ends up in `~/.aspire/logs/cli_*.log`, not the
    console) — see the investigation's finding #7. Don't rely on seeing it;
    treat this document as the source of truth instead.
- `publicApiUrl`/`publicDashboardUrl` are just as required as for Docker
  Compose — left unset, the dashboard's `PUBLIC_API_URL`/`ORIGIN`/`api`'s
  `Cors__AllowedOrigins__0` resolve to Kubernetes' own in-cluster Service
  DNS names (e.g. `http://flare-api-service:8080`), unreachable from an
  external browser.
- **`ImagePullPolicy.Always`** is set on the ingest/api/dashboard images
  (to combat the mutable `"edge"` tag going stale), but did not show up as
  `imagePullPolicy: Always` in generated manifests during 2026-08-29
  verification (`IfNotPresent` throughout) — not yet root-caused. Only
  matters if running `imageTag: "edge"` against a Kubernetes target; a
  real deployment normally wouldn't (the pinned stable default is
  immutable).
- **The Resources page's topology graph works on Kubernetes too, as of
  `0.3.0`** — a real, separate provider from the Docker one (ADR-0005),
  scoped RBAC per ADR-0006. `flare-api`'s `KubernetesResourcePoller` lists
  Flare-labeled Pods (`flare.role`) plus every Service in the namespace,
  and renders a hierarchical graph: Namespace → synthesized "Deployment"
  groups (by `flare.role` label, not a live Deployments API read — see
  ADR-0006) → Pod, plus Service nodes with `Selects` edges. Off by
  default, same "absent config = off" story as Docker's own opt-in.

See
[the investigation](../../docs-internal/investigations/aspire-kubernetes-publish-and-resource-graph.md)
for the concrete bugs found verifying all of the above against a real k3s
cluster.