# Aspire hosting integration reference

The exact API of `CommunityToolkit.Aspire.Hosting.Flare`, how it maps from the
deprecated `Flare.Hosting.Aspire`, and the deprecated package's own API and
deployment facts. For how to use it, see
[`../how-to/deploy/run-with-aspire.md`](../how-to/deploy/run-with-aspire.md).

| Package | Status | Aspire | Source |
|---|---|---|---|
| `CommunityToolkit.Aspire.Hosting.Flare` | Current, `17.0.0` | 17+ (`Aspire.Hosting` 17.0.0) | [CommunityToolkit/Aspire](https://github.com/CommunityToolkit/Aspire/tree/main/src/CommunityToolkit.Aspire.Hosting.Flare) |
| `Flare.Hosting.Aspire` | Deprecated, last release `0.5.1` | 13.x | `src/Aspire.Hosting.Flare` in this repo |

## `CommunityToolkit.Aspire.Hosting.Flare`

### `AddFlare`

```csharp
IResourceBuilder<FlareResource> AddFlare(
    this IDistributedApplicationBuilder builder,
    string name,
    IResourceBuilder<ClickHouseServerResource> clickhouse,
    IResourceBuilder<RedisResource> redis,
    int? dashboardPort = null)
```

| Parameter | Meaning |
|---|---|
| `name` | The ingest resource's name, also the connection string name for `WithReference`. Child resources are named `{name}-api`, `{name}-alert-worker` and `{name}-dashboard`. |
| `clickhouse` | The ClickHouse server Flare stores telemetry in. `AddFlare` adds a `{name}-clickhousedb` database to it; the images migrate their own schema on startup. |
| `redis` | The Redis instance that buffers telemetry before it's written to ClickHouse. |
| `dashboardPort` | The dashboard's host port. Random when `null`. |

What it runs, all from Docker Hub with one shared tag (default `0.6`):

| Resource | Image | Endpoints |
|---|---|---|
| `{name}` (the returned `FlareResource`) | `apardev/flare-ingest` | `otlp-grpc` on host port `4317`, `otlp-http` on `4318`, both unproxied |
| `{name}-api` | `apardev/flare-api` | HTTP |
| `{name}-alert-worker` | `apardev/flare-alert-worker` | HTTP (health only) |
| `{name}-dashboard` | `apardev/flare-dashboard` | HTTP on `dashboardPort` |

Because the returned resource is the ingest container itself, `.WaitFor(flare)`
waits for Flare to accept telemetry.

### Methods on the Flare resource

| Method | What it does |
|---|---|
| `WithDataVolume(string? name = null)` | Persists Flare's identity database (dashboard users, sessions, ingest API keys) in one volume shared by ingest and the API. Without it, each keeps a private database that is lost when its container is recreated. ClickHouse and Redis volumes are configured on those resources. |
| `WithIngestPorts(int? grpcPort = null, int? httpPort = null)` | Changes the OTLP host ports. `null` leaves a port unchanged. |
| `WithApiKey(IResourceBuilder<ParameterResource> apiKey)` | Requires `Authorization: Bearer <key>` on ingest. Resources wired with `WithOtlpExporter(flare)` send it automatically. |
| `WithPublicUrls(apiUrl, dashboardUrl)` | Parameters for the browser-facing API and dashboard URLs, needed once published. Either may be `null`. `dashboardUrl` is used verbatim as the API's CORS origin: `scheme://host[:port]`, no path, no trailing slash. |
| `WithFlareImageTag(string tag)` | The image tag for all four containers. |

### `WithOtlpExporter`

```csharp
IResourceBuilder<T> WithOtlpExporter<T>(
    this IResourceBuilder<T> builder,
    IResourceBuilder<FlareResource> flare,
    OtlpProtocol protocol = OtlpProtocol.Grpc)
    where T : IResourceWithEnvironment
```

Sets `OTEL_EXPORTER_OTLP_ENDPOINT` and `OTEL_EXPORTER_OTLP_PROTOCOL` on the
consuming resource.

| `OtlpProtocol` | Endpoint | `OTEL_EXPORTER_OTLP_PROTOCOL` |
|---|---|---|
| `Grpc` (default) | `otlp-grpc` (`4317`) | `grpc` |
| `HttpProtobuf` | `otlp-http` (`4318`) | `http/protobuf` |
| `HttpJson` | `otlp-http` (`4318`) | `http/json` |

The resource's telemetry then goes to Flare instead of the Aspire dashboard.
The Aspire dashboard's OTLP API key is removed from
`OTEL_EXPORTER_OTLP_HEADERS` (other headers are kept), and
`Authorization=Bearer <key>` is added when `WithApiKey` is set.

### Connection properties

`FlareResource` implements `IResourceWithConnectionString`. With
`.WithReference(flare)`, the consumer gets `ConnectionStrings__{name}` set to
the OTLP/gRPC URL, plus these properties as `{NAME}_{PROPERTY}` variables (for
example `FLARE_URI`):

| Property | Value |
|---|---|
| `Host` | Host of the OTLP/gRPC endpoint |
| `Port` | Port of the OTLP/gRPC endpoint |
| `Uri` | The OTLP/gRPC URL, for example `http://localhost:4317` |

This is the connection string the `Flare.Aspire` client package's
`AddFlareOtlpExporter(name)` reads, so that package works with either hosting
package.

## Migrating from `Flare.Hosting.Aspire`

Move the AppHost to Aspire 17 first, then swap the package reference.

| `Flare.Hosting.Aspire` | `CommunityToolkit.Aspire.Hosting.Flare` |
|---|---|
| `AddFlare("flare")` creates ClickHouse and Redis | `AddFlare("flare", builder.AddClickHouse("clickhouse"), builder.AddRedis("redis"))` |
| ClickHouse/Redis/identity volumes created for you | `WithDataVolume()` on the ClickHouse and Redis resources, and on Flare for the identity database |
| `AddFlare(..., imageTag: "x")` | `WithFlareImageTag("x")` |
| `WithIngestGrpcPort(p)` / `WithIngestHttpPort(p)` | `WithIngestPorts(grpcPort: p, httpPort: p)` |
| `WithDashboardPort(p)` | `AddFlare(..., dashboardPort: p)` |
| `WithPublicApiUrl(a)` / `WithPublicDashboardUrl(d)` | `WithPublicUrls(a, d)` |
| `WithApiKey(key)` | `WithApiKey(key)`; `WithOtlpExporter` now sends it for you |
| `WaitForFlare(flare)` | `WaitFor(flare)` |
| `WithOtlpEndpoint(flare)` | `WithOtlpExporter(flare)` |
| `WithOtlpEndpoint(flare, useHttp: true)` | `WithOtlpExporter(flare, OtlpProtocol.HttpProtobuf)` |
| `WithReference(flare)` + `AddFlareOtlpExporter("flare")` | Unchanged |

No equivalent yet: `WithApiPort`, `WithIngestImage`/`WithApiImage`/
`WithDashboardImage`/`WithAlertWorkerImage`, `WithPersistentStorage`,
`WithColdStorage` and `enableResourceGraph`. Keep `Flare.Hosting.Aspire`
0.5.1 if you depend on one of them.

Behavior changes to check:

- **Exporter variables.** `WithOtlpEndpoint` set only
  `OTEL_EXPORTER_OTLP_ENDPOINT`. `WithOtlpExporter` also sets
  `OTEL_EXPORTER_OTLP_PROTOCOL`, strips the Aspire dashboard's OTLP key and
  adds the Flare key. If you set the protocol yourself for `useHttp: true`,
  drop that and pass an `OtlpProtocol` instead.
- **Visibility.** The old package hid its backing resources in the Aspire
  dashboard; the toolkit shows ClickHouse, Redis and the four Flare containers.
- **Schema.** The old package embedded `db/clickhouse/*.sql` and built a
  ClickHouse init image; the toolkit images migrate the schema themselves, so
  no image is built at publish time.

## `Flare.Hosting.Aspire` (deprecated)

Package ID `Flare.Hosting.Aspire` (not `Aspire.Hosting.Flare`; that prefix is
reserved on nuget.org), project `src/Aspire.Hosting.Flare`. Deprecated on
nuget.org in favor of the toolkit package; `0.5.1` is the last release and
stays installable for Aspire 13.x.

### `AddFlare`

```csharp
IResourceBuilder<FlareResource> AddFlare(
    this IDistributedApplicationBuilder builder,
    string name = "flare",
    string imageTag = "0.6.0",
    bool enableResourceGraph = false)
```

Only the three things that decide *what gets built* (a new sidecar
container or RBAC objects, in `enableResourceGraph`'s case) are
constructor parameters; everything else is a `With*` chain method on the
returned `FlareResource` builder.

| Parameter | Meaning |
|---|---|
| `name` | The Flare resource group's name in the Aspire dashboard. |
| `imageTag` | Defaults to the latest stable Flare release this package version was tested against (`"0.6.0"` in 0.5.1), pulled from Docker Hub's immutable `v*.*.*` tags. Pass `imageTag: "edge"` to track Flare's unreleased `main` branch. The `With*Image` methods reuse this tag. |
| `enableResourceGraph` | Turns on the dashboard's Resources page. Off by default. On Docker it adds a read-only docker-socket-proxy sidecar ([ADR-0005](../../docs-internal/adr/0005-docker-socket-proxy-for-resources-page.md)); on Kubernetes, a scoped `ServiceAccount`/`Role`/`RoleBinding` ([ADR-0006](../../docs-internal/adr/0006-kubernetes-resource-graph-rbac-scoping.md)). Left off, `flare-api` gets no Docker or cluster access and the page shows a "not enabled" state. |

### `With*` chain methods

| Method | What it overrides |
|---|---|
| `WithIngestGrpcPort(int)` / `WithIngestHttpPort(int)` | The OTLP receiver's host ports. Default `4317`/`4318`. Always unproxied. |
| `WithApiPort(int)` / `WithDashboardPort(int)` | The query API / dashboard host ports. Normal proxied Aspire HTTP endpoints. |
| `WithIngestImage(string)` / `WithApiImage(string)` / `WithDashboardImage(string)` / `WithAlertWorkerImage(string)` | Image name/registry (not tag; `imageTag` supplies that), for example images built locally with `docker compose build`. |
| `WithApiKey(IResourceBuilder<ParameterResource>)` | Requires an ingest API key. No automatic flow-through: a project calling `AddFlareOtlpExporter` still needs the raw value in its `configureSettings: s => s.ApiKey = ...` delegate. |
| `WithPublicApiUrl(...)` / `WithPublicDashboardUrl(...)` | Override the `localhost`-pinned browser-facing URLs (`PUBLIC_API_URL`/`Cors__AllowedOrigins__0`). Only needed once publishing/deploying. |
| `WithPersistentStorage(clickHouseVolume, redisVolume, identityVolume)` | Binds three `kubernetesEnvironment.AddPersistentVolume(...)` results to ClickHouse's, Redis's and the identity database's storage; see [Kubernetes](#kubernetes). Marked `[Experimental("ASPIRECOMPUTE002")]`, like `AddPersistentVolume`. |
| `WithColdStorage(accessKey?, secretKey?, bucket = "flare-cold")` | Adds a RustFS (S3-compatible) container, a one-shot bucket job and the `flare_tiered` ClickHouse storage policy, so retention can move aged data to object storage ([ADR-0144](../../docs-internal/adr/0144-cold-storage-rustfs.md)). Nothing moves until you set a cold-after value with `PUT /api/retention`. Pass your own keys outside local use. |
| `WaitForFlare(flare)` | Waits on the dashboard resource. A plain `.WaitFor(flare)` doesn't wait, because this package's `FlareResource` is a lifetime-less grouping node. |
| `WithOtlpEndpoint(flare, useHttp: false)` | Sets only `OTEL_EXPORTER_OTLP_ENDPOINT`, to the ingest gRPC endpoint or, with `useHttp: true`, the HTTP one. |

### Deployment facts

Facts to know before deploying (not `aspire run`) an AppHost that uses this
package. Docker Compose and Kubernetes were verified as of `0.2.3`; Azure/AWS
targets are unverified.

#### Docker Compose

- `publicApiUrl`/`publicDashboardUrl` are unset by default. `aspire run`
  assumes the browser is on the same machine and points
  `PUBLIC_API_URL`/`Cors__AllowedOrigins__0` at `localhost`. Left unset,
  Aspire captures them as `.env.{environment}` placeholders you fill in per
  environment.
- ClickHouse init builds a small custom image at publish time (`FROM`
  whatever image `AddClickHouse` resolves to, plus the init scripts
  `COPY`-ed in) rather than a bind mount, because the bind-mount source path
  only exists on the machine that ran `aspire publish`. Local `aspire run`
  therefore needs `docker build` capability too.
- `enableResourceGraph`'s docker-socket-proxy needs a real Docker host: its
  `/var/run/docker.sock` bind mount requires the machine running
  `docker compose up` to have that socket.

#### Kubernetes

- **A container registry is required, but only because of the
  ClickHouse-init image.** The four Flare images come straight from Docker
  Hub; only the generated ClickHouse-init image is built and pushed by
  `aspire deploy` to the registry configured via
  `AddContainerRegistry`/`WithContainerRegistry`.
- **ClickHouse/Redis/identity data does NOT survive a pod restart unless you
  call `WithPersistentStorage`.** The package's volumes render as plain
  `emptyDir: {}` until you bind a real
  [`AddPersistentVolume`](https://aspire.dev/deployment/kubernetes/persistent-volumes/)
  to each of `{name}-clickhouse-data`, `{name}-redis-data` and
  `{name}-identity-data`:

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

    Verified live against a single-node k3s cluster (`aspire deploy`): PVCs
    bind, ClickHouse and Redis survive a pod delete with their data, and the
    shared identity PVC works mounted by both the ingest and api pods. Not
    verified: the shared identity PVC with ingest and api on two different
    nodes, where most block-storage drivers would need `ReadWriteMany`. See
    [the investigation](../../docs-internal/investigations/aspire-kubernetes-publish-and-resource-graph.md),
    finding #8.
  - Skipping `WithPersistentStorage` against a registered Kubernetes
    environment logs a warning, but Aspire's pipeline CLI writes it only to
    `~/.aspire/logs/cli_*.log`, not the console (finding #7). Treat this page
    as the source of truth.
- `publicApiUrl`/`publicDashboardUrl` are just as required as for Docker
  Compose. Left unset, they resolve to in-cluster Service DNS names (for
  example `http://flare-api-service:8080`), unreachable from a browser.
- **`ImagePullPolicy.Always`** is set on the ingest/api/dashboard images but
  rendered as `IfNotPresent` in generated manifests during 2026-08-29
  verification; not yet root-caused. Only matters with `imageTag: "edge"`.
- **The Resources page works on Kubernetes too**, through a separate provider
  scoped by RBAC (ADR-0006): `flare-api` lists Flare-labeled Pods
  (`flare.role`) and the namespace's Services, and renders Namespace →
  synthesized "Deployment" groups → Pod, plus Service nodes with `Selects`
  edges.
