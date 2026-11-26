# Tutorial: add Flare to a .NET Aspire app

By the end of this tutorial you'll have a .NET Aspire app whose AppHost starts
Flare alongside your services, with logs, traces and metrics from two services
flowing into it, and you'll have followed one request from the web front end
into the API in Flare's dashboard. It takes about fifteen minutes.

This tutorial is different from the earlier ones: it doesn't use the
standalone `docker compose` stack. Flare joins your AppHost as a resource, so
it starts, stops and gets discovered the same way as everything else in your
app. Stop any standalone Flare stack first, since it would hold the same ports
(`4317` and `4318`).

## Prerequisites

- The .NET 10 or 11 SDK
- The [Aspire CLI](https://aspire.dev), version 17 or later
  (`aspire --version`)
- Docker Desktop, or another Docker-compatible engine, running

## 1. Create an Aspire app

Create the Aspire starter, a web front end that calls an API, with the AppHost
and shared service defaults:

```sh
aspire new aspire-starter --name FlareAspire
cd FlareAspire
```

You should end up with four projects: `FlareAspire.AppHost`,
`FlareAspire.ServiceDefaults`, `FlareAspire.ApiService` and
`FlareAspire.Web`. If you already have an Aspire 17 app, use it instead; the
steps below only touch the AppHost.

## 2. Add Flare to the AppHost

```sh
aspire add CommunityToolkit.Aspire.Hosting.Flare --apphost FlareAspire.AppHost
```

This adds the
[Aspire Community Toolkit's Flare integration](https://github.com/CommunityToolkit/Aspire/tree/main/src/CommunityToolkit.Aspire.Hosting.Flare).
In `FlareAspire.AppHost/AppHost.cs`, add ClickHouse, Redis and Flare before
your services, and connect each service to Flare:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var clickhouse = builder.AddClickHouse("clickhouse").WithDataVolume();
var redis = builder.AddRedis("redis").WithDataVolume();

var flare = builder.AddFlare("flare", clickhouse, redis)
    .WithDataVolume();

var apiService = builder.AddDotnetProject("apiservice", "../FlareAspire.ApiService/FlareAspire.ApiService.csproj")
    .WithHttpHealthCheck("/health")
    .WithOtlpExporter(flare)
    .WaitFor(flare);

builder.AddDotnetProject("webfrontend", "../FlareAspire.Web/FlareAspire.Web.csproj")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService)
    .WithOtlpExporter(flare)
    .WaitFor(flare);

builder.Build().Run();
```

Keep the rest of your existing calls as they are. The ClickHouse, Redis and
`AddFlare` lines, `.WithOtlpExporter(flare)` and `.WaitFor(flare)` are new.

- ClickHouse stores Flare's logs, traces and metrics, and Redis buffers them
  on the way in. `WithDataVolume()` on each keeps your data between runs.
- `AddFlare` runs Flare's OTLP receiver, query API, alert worker and
  dashboard, pulled as published images. Its own `WithDataVolume()` keeps
  Flare's user accounts and API keys.
- `.WithOtlpExporter(flare)` points the service's OpenTelemetry exporter at
  Flare. The starter's service defaults already export over OTLP when that
  variable is set, so the services themselves need no changes.
- `.WaitFor(flare)` holds the service back until Flare accepts telemetry, so
  the first logs aren't dropped.

## 3. Run it

```sh
aspire start
aspire describe
```

The first run pulls Flare's images, so give it a few minutes. When every
resource shows `Healthy`, `aspire describe` lists a URL for each. There are
two dashboards:

- the **Aspire dashboard** (shown at the top of the output) for your app's
  resources, their state and console output;
- **Flare's dashboard**, the URL on the **`flare-dashboard`** row.

If the Aspire dashboard shows a certificate error in your browser, see
[`../how-to/deploy/run-with-aspire.md`](../how-to/deploy/run-with-aspire.md#aspires-own-dashboard-shows-an-sslcertificate-error).

## 4. Generate a request

Open the **`webfrontend`** URL and click **Weather**. That page calls the API
service for a forecast, which is exactly the front-end-to-back-end hop worth
tracing. Reload it a few times.

## 5. Follow it in Flare

Open Flare's dashboard (the `flare-dashboard` URL) and choose **Traces**.

- You should see traces for both `webfrontend` and `apiservice`. Open one that
  starts at `webfrontend`: the waterfall shows the front end's request span,
  the outgoing HTTP call, and the API's span nested under it. They are one
  trace because the HTTP client instrumentation sends a `traceparent` header
  and the API continues it.
- Open the **Services** tab for the request rate, error rate and latency of
  each service, and the map of who calls whom.
- Choose **Logs**. Lines from both services are there, tagged by service.
  Expand one to see its trace id and the **Logs for this trace** link.
- Choose **Metrics** to find the runtime and ASP.NET Core metrics the service
  defaults already emit, such as request duration.

The Aspire dashboard's telemetry views stay empty for these two services:
`WithOtlpExporter` sends their telemetry to Flare instead. Flare keeps it
after you stop the app, and gives you alerts, dashboards and retention on top.
To keep both views, see
[Send your services' telemetry to Flare](../how-to/deploy/run-with-aspire.md#3-send-your-services-telemetry-to-flare).

## 6. Stop it

```sh
aspire stop
```

## What you've learned

- That `AddFlare` puts Flare into an AppHost on top of ClickHouse and Redis
  resources you own.
- That `WithOtlpExporter` and `WaitFor` connect a service to it without
  touching the service's code.
- That a service's telemetry then goes to Flare instead of the Aspire
  dashboard.
- How a request is followed across two services in one trace.

## Next steps

- Requiring an ingest API key, deploying the same AppHost, and the features
  still only in the older `Flare.Hosting.Aspire` package:
  [`../how-to/deploy/run-with-aspire.md`](../how-to/deploy/run-with-aspire.md),
  and [`../reference/aspire-hosting.md`](../reference/aspire-hosting.md) for
  every option.
- Seeing a much larger Aspire app using Flare, with Kafka, Postgres and a
  collector: [Tutorial: debug a slow checkout end to end](debug-a-slow-request.md).
- Get notified when something in your services goes wrong:
  [Tutorial: your first alert](first-alert.md).
