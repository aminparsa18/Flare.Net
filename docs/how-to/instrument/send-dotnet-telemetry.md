# How to send telemetry from a .NET app to Flare

Send an ASP.NET Core or worker-service app's logs, traces and metrics to Flare
with one call, using the `Flare.OpenTelemetry` package. It is a thin helper over
the stock OpenTelemetry .NET SDK: no agent, no proprietary format, and you can
still add any other OpenTelemetry configuration next to it.

If your app runs inside a .NET Aspire AppHost, use
[`Flare.Aspire`](../deploy/run-with-aspire.md) instead; it reads the address that
`.WithReference(flare)` injects. This page is for every other .NET app.

## Prerequisites

- A running Flare instance ([standalone](../deploy/run-standalone.md),
  [CLI](../deploy/run-with-cli.md) or [Aspire](../deploy/run-with-aspire.md)) with
  its OTLP port reachable from your app: `4317` for gRPC, `4318` for HTTP.
- A .NET 10 app.

## Add the package and the call

```bash
dotnet add package Flare.OpenTelemetry
```

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddFlareTelemetry("http://localhost:4317");
```

That is the whole setup. It registers:

- An OTLP exporter for logs, traces and metrics.
- ASP.NET Core and `HttpClient` instrumentation, so requests and outgoing calls
  become spans, and the matching metrics are exported.
- .NET runtime metrics: garbage collection, thread pool, exceptions.

Logs written through `ILogger` carry the trace and span ID of the request that
was running, so you can move between a log line and its trace in Flare.

## Configure it

Every setting has a default. Set only what you need:

```csharp
builder.AddFlareTelemetry(o =>
{
    o.Endpoint = new Uri("https://flare.example.com:4318");
    o.Protocol = OtlpExportProtocol.HttpProtobuf;
    o.ApiKey = "<ingest key>";
    o.ServiceName = "orders";
    o.TraceSampleRatio = 0.1;
    o.ActivitySources.Add("MyApp.Orders");
    o.Meters.Add("MyApp.Orders");
});
```

| Option | Default | Meaning |
| --- | --- | --- |
| `Endpoint` | `http://localhost:4317` | Flare.Ingest's OTLP address. |
| `Protocol` | `Grpc` | `HttpProtobuf` for networks where gRPC is blocked. Use the HTTP port (`4318`); the `/v1/traces`, `/v1/logs` and `/v1/metrics` paths are added for you. |
| `ApiKey` | none | Sent as `Authorization: Bearer`. See [ingest API keys](#use-an-ingest-key). |
| `ServiceName`, `ServiceVersion` | the app's existing resource | The `service.name` and `service.version` attributes. |
| `TraceSampleRatio` | `1` | Fraction of new traces to keep, from 0 to 1. |
| `InstrumentAspNetCore`, `InstrumentHttpClient`, `InstrumentRuntime` | `true` | Turn a built-in instrumentation off. |
| `ActivitySources`, `Meters` | empty | Names of your own `ActivitySource` and `Meter` instances to export. |

The same settings bind from the `Flare` configuration section, so a deployment
can set them with environment variables and the code stays
`builder.AddFlareTelemetry()`:

```bash
Flare__Endpoint=http://flare:4317
Flare__ApiKey=<ingest key>
Flare__TraceSampleRatio=0.1
```

Values you set in the delegate override the configuration section.

## Sample traces

`TraceSampleRatio` below `1` installs a parent-based ratio sampler. The service
that starts a trace decides whether to keep it, and every service that joins that
trace follows the decision, so a kept trace is never missing spans from a
downstream service. For that to hold, give every service in the path the same
helper or an equivalent parent-based sampler.

This is head sampling in the app. Flare can also sample at ingest; see
[sampling, clustering and cost](../../explanation/sampling-clustering-and-cost.md)
if you want the decision made centrally instead.

## Use an ingest key

If your Flare instance
[requires ingest API keys](../admin/configure-authentication.md#ingest-api-keys),
pass the key as the second argument, or set `Flare__ApiKey`:

```csharp
builder.AddFlareTelemetry("http://flare:4317", apiKey);
```

Keep the key out of source control: read it from configuration or a secret store.

## Use it next to other exporters

The helper registers its exporters under their own names (`flare-logs`,
`flare-traces`, `flare-metrics`), so it adds to an exporter your app already has
instead of replacing it, for example one that sends to another backend. Do not
mix it with the all-signals `UseOtlpExporter()`: the OpenTelemetry SDK refuses to
start when that and a signal-specific exporter are both registered.

## Troubleshooting

- **Nothing arrives.** Check the port matches the protocol (`4317` gRPC, `4318`
  HTTP) and that the app can reach it. Metrics export every 60 seconds by
  default, so the first points take up to a minute.
- **Nothing arrives and a proxy in front of Flare logs `301` or `308`.** OpenTelemetry
  .NET 1.20.1 and later do not follow HTTP redirects, so a proxy that redirects
  `http://` to `https://` makes every export fail. Point the endpoint at the final
  `https://` address, or have the proxy serve the OTLP port without redirecting.
- **`401` from Flare.** The instance requires an ingest key; see
  [Use an ingest key](#use-an-ingest-key).
- **`ArgumentOutOfRangeException` at startup.** `TraceSampleRatio` must be
  between 0 and 1.
- **A span or log shows "Dropped attributes: N".** The app's OpenTelemetry SDK hit a
  count limit and discarded N items before sending, so Flare cannot show them.
  Raise the matching limit in the app: `OTEL_SPAN_ATTRIBUTE_COUNT_LIMIT`,
  `OTEL_SPAN_EVENT_COUNT_LIMIT`, `OTEL_SPAN_LINK_COUNT_LIMIT` or
  `OTEL_LOGRECORD_ATTRIBUTE_COUNT_LIMIT` (each falls back to
  `OTEL_ATTRIBUTE_COUNT_LIMIT`, 128 by default). The counts need
  [migration 0091](../../../db/clickhouse/0091_dropped_counts.sql) on an existing
  ClickHouse; rows stored before it show nothing.
