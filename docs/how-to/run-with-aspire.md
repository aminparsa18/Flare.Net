# How to run Flare from your own .NET Aspire app

If your own app is already orchestrated with .NET Aspire,
`Flare.Hosting.Aspire` adds the whole Flare stack — ClickHouse, Redis, the
OTLP ingest receiver, the query API, and the dashboard — to your AppHost
with one call, pulling Flare's published Docker Hub images rather than
anything you build yourself. For the exact API and every deployment fact,
see [`../reference/aspire-hosting.md`](../reference/aspire-hosting.md).

> **Status:** published on nuget.org as `Flare.Hosting.Aspire` (currently
> `0.3.2`) — `dotnet add package Flare.Hosting.Aspire` works today. See
> [`../../examples/`](../../examples) for a full runnable demo, which
> references the package as a `ProjectReference` instead (useful for
> trying Flare's `main` before a release).

## 1. Add Flare to your AppHost

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var flare = builder.AddFlare("flare");

builder.Build().Run();
```

That's it — no `docker compose up`, no separate services to run. `AddFlare`
mirrors Flare's own `Flare.AppHost/Program.cs` resource graph: ClickHouse
(with the same `db/clickhouse/*.sql` schema, embedded in the package),
Redis (the same durable batched-insert buffer), and the three
`xracer007/flare-ingest`/`flare-api`/`flare-dashboard` containers.

Need a non-default port, a private ingest API key, or your own built
images instead of Docker Hub's? Chain `With*` methods off the returned
builder — see [the full parameter reference](../reference/aspire-hosting.md#addflare)
for every option.

## 2. Point your logger at it

The recommended way is `Flare.Aspire` (`builder.AddFlareOtlpExporter("flare")`),
a client-side package that reads the connection info `.WithReference(flare)`
injects and registers named OTLP log, trace, and metrics exporters pointed
at it — additive alongside whatever OpenTelemetry setup your project
already has (e.g. the Aspire dashboard collector via
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

`AddFlareOtlpExporter` requires `Flare.ServiceDefaults.ConfigureOpenTelemetry()`
(or whatever else you layer it on) to use the signal-specific
`AddOtlpExporter` family, not the cross-cutting `UseOtlpExporter()` — the
OTel SDK throws at startup if both land in the same `IServiceCollection`.

### Without the `Flare.Aspire` package

`FlareResource` also exposes its OTLP endpoint as a plain environment
variable via `WithOtlpEndpoint(flare)`, if you'd rather wire your
project's own `OpenTelemetry` SDK call directly instead of taking the
`Flare.Aspire` dependency:

```csharp
var flare = builder.AddFlare("flare");

builder.AddProject<Projects.MyApp_Web>("web")
    .WithOtlpEndpoint(flare) // OTEL_EXPORTER_OTLP_ENDPOINT -> Flare.Ingest's OTLP/gRPC endpoint
    .WaitForFlare(flare);
```

Pass `useHttp: true` for the OTLP/HTTP endpoint (`:4318`) instead of gRPC.

Not wired through Aspire's own `AddServiceDefaults()` pattern at all? See
[`run-standalone.md#point-your-logger-at-it`](run-standalone.md#point-your-logger-at-it)
for the same copy-paste snippet per logger — just set
`OTEL_EXPORTER_OTLP_ENDPOINT` from `WithOtlpEndpoint` above instead of a
hardcoded `http://localhost:4317`.

## Resources page (optional Docker access)

The dashboard's **Resources** page shows Flare's own containers as a live
graph — state, health, URLs, and the relationships between them — sourced
from the Docker Engine API (or, on a Kubernetes deploy target, a separate
Kubernetes-native provider), not from Aspire's own resource service. It's
off by default:

![Resources page](../screenshots/resources.png)

```csharp
var flare = builder.AddFlare("flare", enableResourceGraph: true);
```

For local `aspire run` (always backed by real Docker containers, regardless
of what deployment-target resources happen to be registered), this adds
one sidecar container reading a read-only, scope-limited Docker socket
proxy — see
[ADR-0005](../../docs-internal/adr/0005-docker-socket-proxy-for-resources-page.md)
for exactly what it can and can't do and why. Leave it unset (the default)
and no proxy container is added at all — `flare-api` never gains any form
of Docker access, and the Resources page shows a clean "not enabled" state
instead of an error.

Once you actually publish/deploy to a Kubernetes target, the same
`enableResourceGraph: true` instead wires up a Kubernetes-native RBAC-based
provider — see [Publishing / deploying](#publishing--deploying) below and
[ADR-0006](../../docs-internal/adr/0006-kubernetes-resource-graph-rbac-scoping.md).

## Installing

```sh
dotnet add package Flare.Hosting.Aspire
```

To build against Flare's `main` instead of a release, reference the
project directly the way
[`examples/ExampleApp.AppHost`](../../examples/ExampleApp.AppHost) does:

```xml
<ProjectReference Include="path\to\Flare.Net\src\Aspire.Hosting.Flare\Aspire.Hosting.Flare.csproj"
                  IsAspireProjectResource="false" />
```

`IsAspireProjectResource="false"` tells Aspire's tooling this is a plain
class-library reference, not a project resource to orchestrate.

## Publishing / deploying

`Flare.Hosting.Aspire` doesn't add a deployment target itself — a consuming
AppHost opts in the same way any other Aspire app does, by adding a
deployment environment resource. Docker Compose and Kubernetes are both
verified as of `0.2.3`; Azure/AWS targets are unverified. **Before you
deploy for real**, read the deployment facts in
[`../reference/aspire-hosting.md#deployment-facts`](../reference/aspire-hosting.md#deployment-facts)
— several defaults that are fine for local `aspire run` (public URLs,
persistent storage) need explicit attention once actually deployed.

### Docker Compose

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddDockerComposeEnvironment("env");

var flare = builder.AddFlare("flare");
// ...
builder.Build().Run();
```

This has no effect on the normal `aspire run`/`dotnet run` inner loop —
`AddDockerComposeEnvironment` only matters once you run `aspire publish`,
`aspire do prepare-compose`, or `aspire deploy`, which generate a
`docker-compose.yaml` (plus `.env`/`.env.{environment}` files) for the
whole AppHost, Flare included. See
[aspire.dev/deployment](https://aspire.dev/deployment/) and
[aspire.dev/deployment/docker-compose](https://aspire.dev/deployment/docker-compose/)
for the full publish/deploy workflow.
`examples/ExampleApp.AppHost` wires this in as a worked example.

### Kubernetes

Same `AddFlare` call, registering
[`AddKubernetesEnvironment`](https://aspire.dev/integrations/compute/kubernetes/)
(from the `Aspire.Hosting.Kubernetes` package — add it to *your own*
AppHost, not `Flare.Hosting.Aspire` itself, which stays
deployment-target-agnostic) instead of `AddDockerComposeEnvironment`:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var registry = builder.AddContainerRegistry("registry", "your-registry.example.com:5000");
builder.AddKubernetesEnvironment("k8s").WithContainerRegistry(registry);

var flare = builder.AddFlare("flare");
// ...
builder.Build().Run();
```

`aspire publish -o k8s-artifacts` generates a full Helm chart (`Chart.yaml`,
`values.yaml`, `templates/`) for the whole AppHost, Flare included; `aspire
deploy` installs it against your current `kubectl` context. Existing/external
clusters only for now — Azure Kubernetes Service (AKS,
`AddAzureKubernetesEnvironment`) is untested. See
[aspire.dev/deployment/kubernetes](https://aspire.dev/deployment/kubernetes/clusters/)
for the full workflow, and
[`../reference/aspire-hosting.md#kubernetes`](../reference/aspire-hosting.md#kubernetes)
for the persistent-storage/registry/public-URL facts you need before doing
this for real.

## Troubleshooting

### Aspire's own dashboard shows an SSL/certificate error

You may see `RemoteCertificateNameMismatch` errors in the AppHost console,
and the **Aspire orchestration dashboard** (the Resources/Console/Traces UI
at the AppHost's own dashboard URL) fail to load its resource list. This is
an [upstream Aspire 13.4.6 bug](https://aspire.dev/app-host/certificate-configuration/),
not caused by `Flare.Hosting.Aspire` or anything in your AppHost: an
internal Aspire component connects to its own resource service over the
loopback IP literal `127.0.0.1`, but the ASP.NET Core dev certificate only
carries DNS-name SANs (`localhost`, `*.dev.internal`, etc.) — no IP SAN —
so strict TLS hostname validation rejects it. The documented
`ASPIRE_DCP_USE_DEVELOPER_CERTIFICATE=false` opt-out does not fix it on
every machine (it can break DCP startup entirely) — check current Aspire
GitHub issues before trying it.

**This does not affect Flare.** Flare's own dashboard — the actual
product, at `flare-dashboard`'s URL — is a separate app (SvelteKit talking
to `flare-api` over plain HTTP, no gRPC/TLS involved) and works
independently of this bug. If you hit this, use `aspire ps` / `aspire
describe` / `aspire logs` for orchestration visibility instead of the
broken dashboard UI, and open Flare's own dashboard directly.