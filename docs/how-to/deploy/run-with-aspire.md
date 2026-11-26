# How to run Flare from your own .NET Aspire app

If your app is already orchestrated with .NET Aspire,
`CommunityToolkit.Aspire.Hosting.Flare` adds Flare to your AppHost: the OTLP
ingest service, the query API, the alert worker and the dashboard, pulled as
Flare's published Docker Hub images. You pass in the ClickHouse and Redis
resources Flare stores and buffers its data in, so they stay visible and
configurable in your app model. For the exact API, see
[`../reference/aspire-hosting.md`](../../reference/aspire-hosting.md).

> **Requires Aspire 17.** The package is part of the
> [Aspire Community Toolkit](https://github.com/CommunityToolkit/Aspire) and
> depends on `Aspire.Hosting` 17.0.0. On Aspire 13.x, or if you need cold
> storage, Kubernetes persistent storage or the Resources page, use the
> deprecated `Flare.Hosting.Aspire` 0.5.1 instead: see
> [Features not in the toolkit package yet](#features-not-in-the-toolkit-package-yet).

## 1. Install the package

From your AppHost directory:

```sh
aspire add CommunityToolkit.Aspire.Hosting.Flare
```

or `dotnet add package CommunityToolkit.Aspire.Hosting.Flare`.

## 2. Add Flare to your AppHost

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var clickhouse = builder.AddClickHouse("clickhouse").WithDataVolume();
var redis = builder.AddRedis("redis").WithDataVolume();

var flare = builder.AddFlare("flare", clickhouse, redis)
    .WithDataVolume();

builder.Build().Run();
```

`AddFlare` creates the database it needs inside your ClickHouse server, and
the Flare images migrate their own schema on startup. The `flare` resource is
the ingest service, on the conventional OTLP ports `4317` (gRPC) and `4318`
(HTTP). The API, alert worker and dashboard appear under it as `flare-api`,
`flare-alert-worker` and `flare-dashboard`.

The three `WithDataVolume()` calls keep data across runs: ClickHouse's
telemetry, Redis's buffer, and Flare's identity database (dashboard users,
sessions and ingest API keys). Leave them out for a throwaway stack.

## 3. Send your services' telemetry to Flare

```csharp
builder.AddDotnetProject("web", "../MyApp.Web/MyApp.Web.csproj")
    .WithOtlpExporter(flare)
    .WaitFor(flare);
```

`WithOtlpExporter(flare)` sets `OTEL_EXPORTER_OTLP_ENDPOINT` and
`OTEL_EXPORTER_OTLP_PROTOCOL` on the service, so the standard Aspire service
defaults (`UseOtlpExporter()`) send logs, traces and metrics to Flare with no
change to the service itself. It uses OTLP/gRPC by default. Pass
`OtlpProtocol.HttpProtobuf` or `OtlpProtocol.HttpJson` to use OTLP/HTTP
instead.

**The service's telemetry goes to Flare instead of the Aspire dashboard.** The
dashboard still shows the resource, its console output and its state, but not
its logs, traces or metrics. To keep both:

- Use `.WithReference(flare)` instead of `WithOtlpExporter`, and add Flare as a
  second exporter in the service with the
  [`Flare.Aspire`](../../../src/Aspire.Flare/README.md) client package
  (`builder.AddFlareOtlpExporter("flare")`). The service defaults must then
  use the signal-specific `AddOtlpExporter()` calls rather than
  `UseOtlpExporter()`; the tutorial
  [Add Flare to a .NET Aspire app](../../tutorials/add-flare-to-an-aspire-app.md)
  shows the simpler route.
- Or export to an OpenTelemetry Collector that forwards to both.

## 4. Require an ingest API key (optional)

```csharp
var flareKey = builder.AddParameter("flare-api-key", secret: true);

var flare = builder.AddFlare("flare", clickhouse, redis)
    .WithApiKey(flareKey);
```

OTLP clients then send `Authorization: Bearer <key>`. Services wired with
`WithOtlpExporter(flare)` send it automatically. `WithOtlpExporter` also
removes the Aspire dashboard's own OTLP key from
`OTEL_EXPORTER_OTLP_HEADERS`, so that key never reaches Flare.

## Publishing / deploying

Flare deploys like the rest of your AppHost: add a deployment environment such
as `AddDockerComposeEnvironment("env")` or `AddKubernetesEnvironment("k8s")`,
then run `aspire publish` or `aspire deploy`. See
[aspire.dev/deployment](https://aspire.dev/deployment/).

Once the dashboard is no longer opened on `localhost`, set the URLs a browser
uses:

```csharp
var apiUrl = builder.AddParameter("flare-public-api-url");
var dashboardUrl = builder.AddParameter("flare-public-dashboard-url");

var flare = builder.AddFlare("flare", clickhouse, redis)
    .WithPublicUrls(apiUrl, dashboardUrl);
```

`dashboardUrl` is also the API's allowed CORS origin, so pass an origin only,
such as `https://flare.example.com`, with no path or trailing slash.

Storage is configured on the ClickHouse and Redis resources you passed in,
the same way as for any other Aspire app. On Kubernetes, the toolkit package
doesn't yet bind persistent volumes for Flare's identity database; see the
next section.

## Features not in the toolkit package yet

These are only in the deprecated `Flare.Hosting.Aspire` 0.5.1, which also
runs on Aspire 13.x:

| Feature | In `Flare.Hosting.Aspire` 0.5.1 |
|---|---|
| Cold storage (RustFS tier for retention) | `WithColdStorage()` |
| Kubernetes persistent volumes for ClickHouse, Redis and the identity database | `WithPersistentStorage(...)` |
| The dashboard's Resources page | `AddFlare(..., enableResourceGraph: true)` |
| Per-container image or port overrides | `WithIngestImage`, `WithApiPort` and the rest |

How to use them, and how to move from that package to the toolkit one:
[`../reference/aspire-hosting.md`](../../reference/aspire-hosting.md#flarehostingaspire-deprecated).

For a full runnable demo, see [`../../examples/`](../../../examples): a
multi-service shop with Kafka, Postgres and an OpenTelemetry Collector that
puts live data on every dashboard page except Kubernetes. It still uses
`Flare.Hosting.Aspire`, because its Kubernetes deploy needs
`WithPersistentStorage`.

## Troubleshooting

### Aspire's own dashboard shows an SSL/certificate error

You may see `RemoteCertificateNameMismatch` errors in the AppHost console,
and the **Aspire orchestration dashboard** (the Resources/Console/Traces UI
at the AppHost's own dashboard URL) fail to load its resource list. This is
an [upstream Aspire 13.4.6 bug](https://aspire.dev/app-host/certificate-configuration/),
not caused by Flare or anything in your AppHost: an internal Aspire component
connects to its own resource service over the loopback IP literal
`127.0.0.1`, but the ASP.NET Core dev certificate only carries DNS-name SANs
(`localhost`, `*.dev.internal`, etc.) and no IP SAN, so strict TLS hostname
validation rejects it. The documented
`ASPIRE_DCP_USE_DEVELOPER_CERTIFICATE=false` opt-out does not fix it on every
machine (it can break DCP startup entirely), so check current Aspire GitHub
issues before trying it.

**This does not affect Flare.** Flare's own dashboard, at `flare-dashboard`'s
URL, is a separate app (SvelteKit talking to `flare-api` over plain HTTP, no
gRPC/TLS involved) and works independently of this bug. If you hit this, use
`aspire ps` / `aspire describe` / `aspire logs` for orchestration visibility
instead of the broken dashboard UI, and open Flare's own dashboard directly.

### The ingest service fails to start: port already in use

`4317` and `4318` are fixed host ports so external OTLP clients can use the
conventional numbers. A standalone Flare stack (`docker compose up` or
`flare start`) holds the same ports; stop it first, or move the AppHost's
with `WithIngestPorts(grpcPort, httpPort)`.
