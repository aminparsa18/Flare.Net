# Using Flare from your own .NET Aspire app

If your own app is already orchestrated with .NET Aspire, `Flare.Hosting.Aspire`
(`src/Aspire.Hosting.Flare` - package ID `Flare.Hosting.Aspire`, not `Aspire.Hosting.Flare`;
that prefix is reserved on nuget.org for Microsoft's own official integrations) adds the
whole Flare stack — ClickHouse, Redis, the OTLP ingest receiver, the query API, and the
dashboard — to your AppHost with one call, pulling Flare's published Docker Hub images
rather than anything you build yourself.

> **Status:** published on nuget.org as `Flare.Hosting.Aspire` (currently `0.1.1`) —
> `dotnet add package Flare.Hosting.Aspire` works today. See
> [`examples/`](../examples) for a full runnable demo, which references the package
> as a `ProjectReference` instead (useful for trying Flare's `main` before a release).

## 1. Add Flare to your AppHost

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var flare = builder.AddFlare("flare");

builder.Build().Run();
```

That's it — no `docker compose up`, no separate services to run. `AddFlare` mirrors
Flare's own `Flare.AppHost/Program.cs` resource graph: ClickHouse (with the same
`db/clickhouse/*.sql` schema, embedded in the package and materialized to a temp
directory so your repo doesn't need a copy of it), Redis (the same durable batched-
insert buffer), and the three `xracer007/flare-ingest`/`flare-api`/`flare-dashboard`
containers.

### Parameters

```csharp
IResourceBuilder<FlareResource> AddFlare(
    this IDistributedApplicationBuilder builder,
    string name = "flare",
    string imageTag = "edge",
    int? ingestGrpcPort = null,
    int? ingestHttpPort = null,
    int? apiPort = null,
    int? dashboardPort = null,
    string? ingestImage = null,
    string? apiImage = null,
    string? dashboardImage = null,
    IResourceBuilder<ParameterResource>? apiKey = null,
    bool enableResourceGraph = false)
```

- **`name`** — the Flare resource group's name in the Aspire dashboard.
- **`imageTag`** — defaults to `"edge"`. Flare has no stable release yet, and CI
  (`.github/workflows/docker-publish.yml` in Flare's own repo) only publishes `edge`
  until a first `v*.*.*` tag lands.
- **`ingestGrpcPort` / `ingestHttpPort`** — override the OTLP receiver's host ports.
  Left unset, these default to the conventional `4317`/`4318`. They're always
  unproxied, so external OTLP clients (your own app's logger) can point at them
  directly — the same fixed-port story as `docker-compose.yml`.
- **`apiPort` / `dashboardPort`** — override the query API / dashboard host ports.
  Normal proxied Aspire HTTP endpoints, left unset if you don't care what port you get.
- **`ingestImage` / `apiImage` / `dashboardImage`** — override an image name/registry
  (not tag — `imageTag` still supplies that) for local-dev use against images built
  with `docker compose build` instead of Docker Hub.
- **`apiKey`** — optional `secret: true` `AddParameter` result requiring OTLP callers
  to present an ingest API key. Left unset (the default), ingest stays anonymous.
- **`enableResourceGraph`** — turns on the dashboard's Resources page for this Flare
  instance. Off by default — see
  [Resources page (optional Docker access)](#resources-page-optional-docker-access)
  below.

## 2. Point your logger at it

The recommended way is `Flare.Aspire` (`builder.AddFlareOtlpExporter("flare")`), a
client-side package that reads the connection info `.WithReference(flare)` injects and
registers a named OTLP log exporter pointed at it — additive alongside whatever
OpenTelemetry setup your project already has (e.g. the Aspire dashboard collector via
`AddServiceDefaults()`/`UseOtlpExporter()`), not a replacement for it:

```csharp
// AppHost
var flare = builder.AddFlare("flare");

builder.AddProject<Projects.MyApp_Web>("web")
    .WithReference(flare) // injects ConnectionStrings__flare -> Flare.Ingest's OTLP/gRPC endpoint
    .WaitForFlare(flare);
```

```csharp
// MyApp.Web
builder.AddFlareOtlpExporter("flare");
```

```sh
dotnet add package Flare.Aspire
```

Logs only for now — `Flare.Ingest` doesn't receive traces or metrics yet (a separate
roadmap item).

### Without the `Flare.Aspire` package

`FlareResource` also exposes its OTLP endpoint as a plain environment variable via
`WithOtlpEndpoint(flare)`, if you'd rather wire your project's own `OpenTelemetry` SDK
call directly instead of taking the `Flare.Aspire` dependency:

```csharp
var flare = builder.AddFlare("flare");

builder.AddProject<Projects.MyApp_Web>("web")
    .WithOtlpEndpoint(flare) // OTEL_EXPORTER_OTLP_ENDPOINT -> Flare.Ingest's OTLP/gRPC endpoint
    .WaitForFlare(flare);
```

Pass `useHttp: true` for the OTLP/HTTP endpoint (`:4318`) instead of gRPC.

See [`docs/standalone.md`](standalone.md#point-your-logger-at-it) for the same
copy-paste snippet per logger (Serilog, NLog, ZLogger, `Microsoft.Extensions.Logging`)
if your project isn't wired through Aspire's own `AddServiceDefaults()` pattern at all —
just set `OTEL_EXPORTER_OTLP_ENDPOINT` from `WithOtlpEndpoint` above instead of a
hardcoded `http://localhost:4317`.

## Resources page (optional Docker access)

The dashboard's **Resources** page shows Flare's own containers as a live graph —
state, health, URLs, and the relationships between them — sourced from the Docker
Engine API, not from Aspire's own resource service (Aspire's resource-service gRPC API
only ever describes an AppHost's *own* process — nothing your AppHost consumers would
ever see once Flare is packaged as containers, which is why this page exists as a
separate Docker-backed feature at all). It's off by default:

```csharp
var flare = builder.AddFlare("flare", enableResourceGraph: true);
```

Passing `enableResourceGraph: true` adds one more sidecar container —
[`tecnativa/docker-socket-proxy`](https://github.com/Tecnativa/docker-socket-proxy),
the same image and scoping `docker-compose.yml`'s standalone mode uses (see
[`docs/standalone.md`](standalone.md#resources-page-optional-docker-access) for the
full security rationale) — with `/var/run/docker.sock` bind-mounted **read-only** into
it, configured with `CONTAINERS=1` and `POST=0` so it only ever answers container
list/inspect calls (no exec, no start/stop, no image/volume/network management), and
points `flare-api`'s `DockerResources__ProxyUrl` at it. `flare-api` itself never mounts
or touches the real Docker socket — only this proxy's scoped endpoint.

Leave it unset (the default) and no proxy container is added at all — `flare-api` never
gains any form of Docker access, and the Resources page shows a clean "not enabled"
state instead of an error.

## Installing

```sh
dotnet add package Flare.Hosting.Aspire
```

Published on nuget.org (currently `0.1.1`). To build against Flare's `main` instead of
a release, reference the project directly the way
[`examples/ExampleApp.AppHost`](../examples/ExampleApp.AppHost) does:

```xml
<ProjectReference Include="path\to\Flare.Net\src\Aspire.Hosting.Flare\Aspire.Hosting.Flare.csproj"
                  IsAspireProjectResource="false" />
```

`IsAspireProjectResource="false"` tells Aspire's tooling this is a plain class-library
reference, not a project resource to orchestrate.

## If Aspire's own dashboard shows an SSL/certificate error

You may see `RemoteCertificateNameMismatch` errors in the AppHost console, and the
**Aspire orchestration dashboard** (the Resources/Console/Traces UI at the AppHost's
own dashboard URL) fail to load its resource list. This is an
[upstream Aspire 13.4.6 bug](https://aspire.dev/app-host/certificate-configuration/),
not caused by `Flare.Hosting.Aspire` or anything in your AppHost: an internal Aspire
component (`Aspire.Hosting.Dashboard.ServiceClient.DashboardClient`) connects to its
own resource service over the loopback IP literal `127.0.0.1`, but the ASP.NET Core
dev certificate only carries DNS-name SANs (`localhost`, `*.dev.internal`, etc.) — no
IP SAN — so strict TLS hostname validation rejects it. Confirmed reproducible with a
clean `bin`/`obj`, cleared Aspire cache, and no background processes; the documented
`ASPIRE_DCP_USE_DEVELOPER_CERTIFICATE=false` opt-out does not fix it on every machine
(it broke DCP startup entirely when tried here) — check current Aspire GitHub issues
before trying it yourself.

**This does not affect Flare.** Flare's own dashboard - the actual product, at
`flare-dashboard`'s URL - is a separate app (SvelteKit talking to `flare-api` over
plain HTTP, no gRPC/TLS involved) and works independently of this bug. If you hit
this, use `aspire ps` / `aspire describe` / `aspire logs` for orchestration visibility
instead of the broken dashboard UI, and open Flare's own dashboard directly.

## Known limitations (v1 of the package)

- **`imageTag` defaults to `"edge"`** — pre-alpha, no stable image yet.
- **No `aspire publish` support** — deploying a consuming app that also brings up
  Flare via a publish/deployment pipeline isn't supported.
- **Multiple `AddFlare()` calls in one AppHost are untested** — the resource names are
  collision-safe (prefixed by `name`), but running two full Flare stacks side by side
  hasn't been exercised end-to-end.
- **No package-version-to-image-tag pinning** — `Flare.Hosting.Aspire`'s own NuGet
  version and the Docker image tag it defaults to (`edge`) aren't linked; this is an
  explicitly deferred decision until Flare has a real versioning scheme.
- **`enableResourceGraph`'s Docker labels are applied via `WithContainerRuntimeArgs`**
  (there's no more-direct "add a Docker label" API in Aspire 13.4) — this only reaches
  containers Aspire actually launches locally via `aspire run`/`aspire start`. It hasn't
  been exercised against `aspire publish`/a deployed target (see the "No `aspire
  publish` support" limitation above, which already covers `AddFlare()` generally).
