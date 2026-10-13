# How to monitor Dapr applications

See what your [Dapr](https://dapr.io) sidecars are doing in Flare: service
invocation and pub/sub calls show up as spans in your traces and on the
service map, and the sidecars' own metrics land next to your application
metrics.

Dapr isn't part of Flare and Flare doesn't depend on it. This page is for
applications that already run on Dapr and want somewhere to send its
telemetry.

## Prerequisites

- A running Flare instance ([standalone](../deploy/run-standalone.md),
  [Aspire](../deploy/run-with-aspire.md), or the [CLI](../deploy/run-with-cli.md)), with its OTLP
  gRPC port (`4317`) reachable from the Dapr sidecars.
- Dapr with OTLP tracing support. This page was verified end to end against
  Dapr 1.18.7 in self-hosted mode. The Kubernetes snippets use the same
  `Configuration` resource and weren't run on a cluster.

## Send sidecar traces to Flare

Dapr writes traces over OTLP directly, so no collector is needed. Add an
OTLP exporter to the Dapr
[`Configuration`](https://docs.dapr.io/operations/configuration/configuration-overview/#tracing)
that your applications use:

```yaml
apiVersion: dapr.io/v1alpha1
kind: Configuration
metadata:
  name: flare
spec:
  tracing:
    samplingRate: "1"
    otel:
      endpointAddress: "flare.example.internal:4317"   # your Flare.Ingest host
      isSecure: false                                   # true if Ingest terminates TLS
      protocol: grpc
```

- **Set `samplingRate` explicitly.** Dapr's default is 1 in 10,000, which looks
  like Flare receiving nothing. `"1"` sends every trace. To keep volume down in
  production, lower it here or use [ingest trace sampling](../../explanation/sampling-clustering-and-cost.md)
  on the Flare side.
- **`endpointAddress` has no `http://` prefix for gRPC.** Use the host and port
  only. For `protocol: http`, point it at port `4318`.

Then tell each application to use the configuration.

On Kubernetes, annotate the pod template:

```yaml
annotations:
  dapr.io/enabled: "true"
  dapr.io/app-id: orders
  dapr.io/config: flare
```

In self-hosted mode, pass the file to the sidecar:

```bash
daprd --app-id orders --app-port 6001 --resources-path components --config config.yaml
# or with the Dapr CLI: dapr run --app-id orders --config config.yaml ...
```

If you've enabled [ingest API keys](../admin/configure-authentication.md#ingest-api-keys),
the `Configuration` resource has no field for request headers. Send the traces
through an OpenTelemetry Collector instead, and put the key on the Collector's
exporter.

## What you see in Flare

Open **Traces** and filter by the app id. The app id becomes the service name
(`service.name`).

| Span | Service | Meaning |
| --- | --- | --- |
| `CallLocal/<app-id>/<method>` (client) | the caller | Service invocation, as seen by the calling sidecar. |
| `CallLocal/<app-id>/<method>` (server) | the callee | The same call, received by the target sidecar. |
| `/v1.0/publish/<pubsub>/<topic>` | the publisher | The publish API call to the sidecar. |
| `pubsub/<topic>` | the publisher | The sidecar handing the message to the pub/sub component. Carries `messaging.system` and `messaging.destination.name`. |

Caller and callee spans share a trace, so a service-to-service call appears as
one trace with spans from both sidecars. **Traces > Services** draws the edge
from the calling app id to the called one, with call counts and latency.

Dapr spans carry `dapr.api`, `dapr.protocol`, and `dapr.status_code` where
relevant, so you can filter or group by them like any other attribute.

## Send sidecar metrics to Flare

Dapr exposes metrics in Prometheus format only, on port `9090` of every sidecar
(change it with `--metrics-port` or the `dapr.io/metrics-port` annotation).
Flare can scrape that endpoint itself, so there is nothing to install. Add one
target per sidecar to Flare.Ingest:

```yaml
services:
  ingest:
    environment:
      PrometheusScrape__Targets__0__Job: orders
      PrometheusScrape__Targets__0__Url: http://orders-host:9090/
      PrometheusScrape__Targets__0__Interval: "00:00:15"
```

The `Job` becomes the metrics' `service.name`, so set it to the app id to line
the metrics up with the sidecar's traces. See **Data sources > Prometheus** in the dashboard for the full set of
target options.

On Kubernetes, sidecars come and go with the pods, so a fixed target list
doesn't fit. Use an OpenTelemetry Collector with the `prometheus` receiver and
Kubernetes service discovery, scoped to pods that have the
`dapr.io/enabled` annotation, and export to Flare over OTLP.

Once scraped, metrics such as `dapr_http_server_request_count`,
`dapr_component_pubsub_egress_count`, and
`dapr_component_pubsub_ingress_count` appear in the metrics catalog.

Flare logs a warning about `go_gc_duration_seconds` being dropped.
That metric is a Prometheus summary, which Flare doesn't store. The other
metrics from the same scrape are unaffected.

## Known limits

- **Dapr pub/sub doesn't appear on the Messaging page.** Dapr marks its publish
  spans as client spans and doesn't set `messaging.operation`, and the page
  only counts producer and consumer spans. The spans are still in your
  traces. For per-topic rates and latency, use the sidecar metrics above.
- **Sidecar logs aren't covered here.** Dapr sidecars write logs to stdout.
  Collect them the same way as the rest of your container logs (see **Data
  sources > Docker** or **Kubernetes**). Starting `daprd` with `--log-as-json`
  gives structured fields.
- **Only Dapr's own spans.** Your application's code isn't traced by the
  sidecar. Add the OpenTelemetry SDK to your app, pointed at the same Flare
  endpoint, to get spans for your own code. Dapr propagates W3C trace context,
  so they join the sidecar spans in the same trace.
