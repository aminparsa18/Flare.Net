# Using Flare from your own .NET Aspire app

If your own app is already orchestrated with .NET Aspire, `Flare.Hosting.Aspire`
(`src/Aspire.Hosting.Flare` - package ID `Flare.Hosting.Aspire`, not `Aspire.Hosting.Flare`;
that prefix is reserved on nuget.org for Microsoft's own official integrations) adds the
whole Flare stack — ClickHouse, Redis, the OTLP ingest receiver, the query API, and the
dashboard — to your AppHost with one call, pulling Flare's published Docker Hub images
rather than anything you build yourself.

> **Status:** pre-alpha, not yet published to nuget.org. See
> [`examples/`](../examples) for a full runnable demo you can try today — it
> references the package directly rather than via a NuGet install. The rest of this
> page documents the shape of the API as it exists now.

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
    int? dashboardPort = null)
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

## 2. Point your logger at it

Once Flare is up, this is identical to the docker-compose story — see
[`getting-started.md`](getting-started.md#2-point-your-logger-at-it) for a
copy-paste snippet per logger (Serilog, NLog, ZLogger, `Microsoft.Extensions.Logging`).
They all just need `OTEL_EXPORTER_OTLP_ENDPOINT` pointed at `http://localhost:4317`
(or whatever you passed for `ingestGrpcPort`).

If your own project already has an Aspire `ServiceDefaults`-style project (the
standard `AddServiceDefaults()` template pattern), it likely already emits logs this
way — you only need to set `OTEL_EXPORTER_OTLP_ENDPOINT` in its environment:

```csharp
var flare = builder.AddFlare("flare");

builder.AddProject<Projects.MyApp_Web>("web")
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:4317");
```

`FlareResource` doesn't currently expose its child `ingest` resource for a
`WithReference`-style wiring — the endpoint is fixed and conventional by design, same
as pointing any OTLP source at Flare via docker-compose.

## Installing (once published)

```sh
dotnet add package Flare.Hosting.Aspire
```

Not usable yet — there's no published version. Until then, reference the project
directly the way [`examples/ExampleApp.AppHost`](../examples/ExampleApp.AppHost) does:

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
