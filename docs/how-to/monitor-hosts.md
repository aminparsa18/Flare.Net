# How to monitor hosts with the OpenTelemetry Collector

Send CPU, memory, disk, and load metrics from your machines to Flare with the
OpenTelemetry Collector's `hostmetrics` receiver, and see them on the
**Hosts** page: one row per host, with a drill-down chart for each metric.

The **Hosts** page is separate from **Resources**. Resources shows
infrastructure Flare discovers itself (Docker containers, Kubernetes objects,
and the machine Flare runs on). Hosts shows machines that report their own
metrics to Flare over OTLP.

## Prerequisites

- A running Flare instance ([standalone](run-standalone.md),
  [Aspire](run-with-aspire.md), or the [CLI](run-with-cli.md)), with its OTLP
  port (`4317` gRPC or `4318` HTTP) reachable from the hosts you want to
  monitor.
- The [OpenTelemetry Collector Contrib](https://github.com/open-telemetry/opentelemetry-collector-contrib)
  distribution (`otelcol-contrib`) on each host. The core distribution doesn't
  include the `resourcedetection` processor.

## Configure the collector

On each host, point the collector at Flare with this configuration:

```yaml
receivers:
  hostmetrics:
    collection_interval: 60s
    scrapers:
      cpu: {}
      memory: {}
      load: {}
      filesystem: {}

processors:
  resourcedetection:
    detectors: [system]

exporters:
  otlp:
    endpoint: flare.example.internal:4317   # your Flare.Ingest host
    tls:
      insecure: true                        # or configure TLS

service:
  pipelines:
    metrics:
      receivers: [hostmetrics]
      processors: [resourcedetection]
      exporters: [otlp]
```

The `resourcedetection` processor is required. It sets the `host.name` and
`os.type` resource attributes, and Flare identifies hosts by `host.name`.
Metrics without it don't appear on the Hosts page.

If you've enabled [ingest API keys](configure-authentication.md#ingest-api-keys), add the key
to the exporter's `headers`.

### Running the collector in a container

A containerized collector reports the container's own filesystems unless you
mount the host's root and set `root_path`:

```yaml
receivers:
  hostmetrics:
    root_path: /hostfs
```

```bash
docker run -v /:/hostfs:ro --hostname "$(hostname)" ... otel/opentelemetry-collector-contrib
```

Without this, the **Disk** column stays empty (—). Pass `--hostname` too;
otherwise `host.name` is the container ID.

## Read the Hosts page

Open **Hosts** in the top nav. Hosts appear within one collection interval.

| Column | Source metric | Meaning |
|---|---|---|
| CPU | `system.cpu.time` | Non-idle share of CPU time over the window |
| Memory | `system.memory.usage` | `used` as a share of total memory, averaged over the window |
| Disk | `system.filesystem.usage` | `used` as a share of total capacity, summed across all reported filesystems |
| Load (15m) | `system.cpu.load_average.15m` | 15-minute load average, averaged over the window |
| Last seen | any `system.*` metric | When the host last reported |

A **—** means the host sent no data for that metric in the window, for
example because the `filesystem` scraper is disabled. It is never shown as 0%.
A host that hasn't reported for more than five minutes is marked **stale**.

- **Filter** by host name (substring, case-insensitive) or by OS type.
- **Sort** by clicking any column header.
- **Change the window** with the time selector (5 minutes to 24 hours).
- **Drill down** by clicking a host name. This opens charts of all four
  metrics over the selected window.

The page lists up to 500 hosts. If more match, a notice asks you to narrow the
filter.

## Troubleshooting

**A host doesn't appear.** Check the collector's logs for export errors, then
confirm its metrics carry `host.name`. Hosts only appear if they send at least
one `system.*` metric within the selected window.

**CPU, Memory, or Load is always empty.** Only the receiver's default metrics
are read. Flare doesn't use the opt-in `system.cpu.utilization`,
`system.memory.utilization`, or `system.filesystem.utilization` gauges, so
make sure the `cpu`, `memory`, and `load` scrapers are enabled.
