# How to send Orleans telemetry to Flare

See what your [Microsoft Orleans](https://learn.microsoft.com/dotnet/orleans/)
silos are doing in Flare: every grain call becomes a span in your traces, the
Orleans runtime meters land on the **Metrics** page, and the logs your grains
write are linked to the call that produced them.

Orleans emits OpenTelemetry data itself, so there is no agent, sidecar or
collector to run. You register Orleans' activity sources and meter with the
OpenTelemetry SDK and export over OTLP.

## Prerequisites

- A running Flare instance ([standalone](../deploy/run-standalone.md),
  [Aspire](../deploy/run-with-aspire.md), or the [CLI](../deploy/run-with-cli.md)), with its OTLP
  gRPC port (`4317`) reachable from your silos and clients.
- An Orleans silo on .NET. This page was verified end to end with Orleans 10.4.0
  on .NET 10 and OpenTelemetry 1.19.

## Send traces, metrics and logs

Add the OpenTelemetry packages to the silo project:

```bash
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
```

Then enable activity propagation on the silo and register the sources and meter:

```csharp
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var flare = new Uri("http://flare.example.internal:4317");   // your Flare.Ingest host

builder.UseOrleans(silo => silo
    .UseLocalhostClustering()        // or your real clustering
    .AddActivityPropagation());      // required: without it Orleans emits no spans

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("orders-silo"))
    .WithTracing(t => t
        .AddSource(Orleans.Diagnostics.ActivitySources.ApplicationGrainActivitySourceName)
        .AddOtlpExporter(o => o.Endpoint = flare))
    .WithMetrics(m => m
        .AddMeter("Microsoft.Orleans")
        .AddOtlpExporter(o => o.Endpoint = flare));

builder.Logging.AddOpenTelemetry(l => l.AddOtlpExporter(o => o.Endpoint = flare));
```

If you also run an Orleans client in a separate process (a web frontend, for
example), call `AddActivityPropagation()` on its client builder too and give it
its own `service.name`. The client's calls and the silo's grain executions then
share one trace.

If you've enabled [ingest API keys](../admin/configure-authentication.md#ingest-api-keys),
add the key to each exporter with `o.Headers = "Authorization=Bearer <key>"`.

## What you see in Flare

### Traces

Every grain call produces two spans in the same trace, named
`<GrainInterface>/<Method>`, for example `IOrderGrain/Place`:

| Span kind | Meaning |
| --- | --- |
| Client | The caller's side of the call: another grain, or your code via `IGrainFactory`. |
| Server | The grain method executing. |

A call from one grain to another nests inside the first call's server span, so
a request that fans out across grains reads top to bottom in the trace
waterfall. The spans carry `rpc.system.name = orleans`, `orleans.rpc.service`
(the grain interface), `orleans.rpc.target_id` (the grain type and key, such as
`inventory/sku-2`) and `orleans.rpc.source_id` (the calling grain).

A grain method that throws gives both spans an error status, with
`exception.type`, `exception.message` and `exception.stacktrace` as span
attributes. They count in the service's error rate on **Traces > Services**.

### Metrics

The `Microsoft.Orleans` meter exports the runtime's own measurements as
metrics such as `orleans-catalog-activations`, `orleans-grains`,
`orleans-app-requests-latency` and the `orleans-directory-*` family. A single
silo exported 42 of them, and they show up in the metrics catalog under the
silo's `service.name`. The OpenTelemetry SDK exports metrics every 60 seconds by
default, so the first points take up to a minute to appear.

### Logs

Logs written through `ILogger` inside a grain carry the trace and span ID of the
call that was running, so you can jump from a log line to its trace and the
other way round. Orleans' own logs (silo startup and shutdown, for example) are
exported too.

## Choose the activity sources

`ApplicationGrainActivitySourceName` gives you the grain calls and nothing
else, and is what you want in production. Orleans has more sources, and adding
them changes the volume a lot:

- `Orleans.Diagnostics.ActivitySources.AllActivitySourceName` subscribes to
  everything. In the verified run, most spans were runtime internals
  (activating and deactivating grains, directory registrations, membership table
  reads), and shutting the silo down alone added a pair of spans for every
  active grain.
- `LifecycleActivitySourceName`, `StorageActivitySourceName` and
  `RuntimeActivitySourceName` each add one slice of that. Turn one on while you
  debug something specific, such as slow activations, and take it out again.

## Known limits

- **Grain failures don't appear on the Errors page.** That page reads
  `exception` span events, and Orleans records the exception as span
  attributes instead. Failed calls are still easy to find: filter **Traces** by
  the error status.
- **Grain-to-grain calls don't draw edges on the service map.** They happen
  inside one service (your silo), so the map shows a single node.
