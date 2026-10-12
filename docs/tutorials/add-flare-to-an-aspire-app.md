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

- The .NET 10 SDK
- The [Aspire CLI](https://aspire.dev) (`aspire --version` should print
  something)
- Docker Desktop, or another Docker-compatible engine, running

## 1. Create an Aspire app

Create the Aspire starter, a web front end that calls an API, with the AppHost
and shared service defaults:

```sh
aspire new aspire-starter --name FlareAspire
cd FlareAspire
```

If you'd rather use the .NET CLI, `dotnet new aspire-starter --name
FlareAspire` produces the same layout. You should end up with four projects:
`FlareAspire.AppHost`, `FlareAspire.ServiceDefaults`, `FlareAspire.ApiService`
and `FlareAspire.Web`. If you already have an Aspire app, use it instead; the
steps below only touch the AppHost, the service defaults and each service's
startup file.

## 2. Add Flare to the AppHost

```sh
dotnet add FlareAspire.AppHost package Flare.Hosting.Aspire
```

In the AppHost's entry file (`AppHost.cs`), add one line before your services
and connect each service to it:

```csharp
var flare = builder.AddFlare("flare");

var apiService = builder.AddProject<Projects.FlareAspire_ApiService>("apiservice")
    .WithHttpHealthCheck("/health")
    .WithReference(flare)
    .WaitForFlare(flare);

builder.AddProject<Projects.FlareAspire_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService)
    .WithReference(flare)
    .WaitForFlare(flare);
```

Keep the rest of your existing calls as they are. Only `AddFlare`,
`.WithReference(flare)` and `.WaitForFlare(flare)` are new.

`AddFlare("flare")` adds the whole Flare stack: ClickHouse, Redis, the OTLP
receiver, the query API, the alert worker and the dashboard, pulled as
published images. `.WithReference(flare)` hands each service Flare's OTLP
address as a connection string named `flare`. `.WaitForFlare(flare)` holds the
service back until Flare is ready, so the first logs aren't dropped.

## 3. Add the Flare exporter to your services

Add the client package to the shared service defaults project, so both
services get it:

```sh
dotnet add FlareAspire.ServiceDefaults package Flare.Aspire
```

Now one setting needs changing. The starter's `Extensions.cs` in
`FlareAspire.ServiceDefaults` registers its OTLP exporter with
`UseOtlpExporter()`. The OpenTelemetry SDK refuses to mix that call with the
signal-specific exporters Flare adds, and throws at startup if they meet.
Find the method that registers the exporters (it checks
`OTEL_EXPORTER_OTLP_ENDPOINT`) and use the signal-specific calls instead:

```csharp
if (useOtlpExporter)
{
    builder.Services.AddOpenTelemetry()
        .WithLogging(logging => logging.AddOtlpExporter())
        .WithMetrics(metrics => metrics.AddOtlpExporter())
        .WithTracing(tracing => tracing.AddOtlpExporter());
}
```

These read the same `OTEL_EXPORTER_OTLP_*` variables, so the Aspire
dashboard keeps receiving telemetry exactly as before. Flare's exporter is
added next to it, not in place of it.

Finally, in the startup file of each service (`FlareAspire.ApiService` and
`FlareAspire.Web`, both in `Program.cs`), add one line right after
`AddServiceDefaults()`:

```csharp
builder.AddServiceDefaults();
builder.AddFlareOtlpExporter("flare");
```

The string `"flare"` is the connection name that `WithReference(flare)`
injected.

## 4. Run it

```sh
aspire start
aspire describe
```

The first run pulls Flare's images, so give it a few minutes. When every
resource shows `Healthy`, `aspire describe` lists a URL for each. There are
two dashboards, and you'll use both:

- the **Aspire dashboard** (shown at the top of the output) for your app's
  resources and its own view of telemetry;
- **Flare's dashboard**, the URL on the **`flare-dashboard`** row.

If the Aspire dashboard shows a certificate error in your browser, see
[`../how-to/deploy/run-with-aspire.md`](../how-to/deploy/run-with-aspire.md#aspires-own-dashboard-shows-an-sslcertificate-error).

## 5. Generate a request

Open the **`webfrontend`** URL and click **Weather**. That page calls the API
service for a forecast, which is exactly the front-end-to-back-end hop worth
tracing. Reload it a few times.

## 6. Follow it in Flare

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

The Aspire dashboard shows the same telemetry too. Having both is deliberate:
Aspire's is a development-time view that disappears when you stop the app,
while Flare keeps logs, traces and metrics queryable, and gives you alerts,
dashboards and retention on top.

## 7. Stop it

```sh
aspire stop
```

## What you've learned

- That `AddFlare` puts a full Flare stack into an AppHost with one call, and
  that `WithReference` and `WaitForFlare` connect your services to it.
- That `Flare.Aspire`'s `AddFlareOtlpExporter` adds Flare as an additional
  exporter alongside the Aspire dashboard's, not a replacement.
- Why the service defaults must use signal-specific OTLP exporters rather than
  `UseOtlpExporter()`.
- How a request is followed across two services in one trace.

## Next steps

- Persisting Flare's data between runs, showing Flare's own containers on the
  Resources page, and deploying the same AppHost to Docker Compose or
  Kubernetes: [`../how-to/deploy/run-with-aspire.md`](../how-to/deploy/run-with-aspire.md)
  and [`../reference/aspire-hosting.md`](../reference/aspire-hosting.md) for
  every `With*` option.
- Seeing a much larger Aspire app using Flare, with Kafka, Postgres and a
  collector: [Tutorial: debug a slow checkout end to end](debug-a-slow-request.md).
- Get notified when something in your services goes wrong:
  [Tutorial: your first alert](first-alert.md).
