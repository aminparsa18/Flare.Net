# Forward telemetry to another OTLP endpoint

Flare can send a copy of everything it accepts to one or more other OTLP/HTTP endpoints, for example to
run Flare next to another backend while you migrate, or to feed a second consumer.

## Configure a target

Targets are configuration on **Flare.Ingest** (appsettings or environment variables). Using environment
variables, which map onto `Forwarding:Targets:<index>:<setting>`:

```yaml
services:
  ingest:
    environment:
      Forwarding__Targets__0__Name: second-backend
      Forwarding__Targets__0__Endpoint: https://collector.example.com:4318
      Forwarding__Targets__0__Headers__Authorization: Bearer <token>
      Forwarding__Targets__0__Signals__0: Logs
      Forwarding__Targets__0__Services__0: checkout
```

Flare appends `/v1/logs`, `/v1/traces` or `/v1/metrics` to `Endpoint` and sends protobuf, gzip-compressed.

| Setting | Default | Meaning |
|---|---|---|
| `Name` | required | Unique label used in log messages. |
| `Endpoint` | required | Base URL of the receiving OTLP/HTTP endpoint (`http` or `https`). |
| `Headers` | none | Extra request headers, such as `Authorization`. |
| `Signals` | all | Any of `Logs`, `Traces`, `Metrics`. |
| `Services` | all | Only forward these `service.name` values; other services' data is removed from the copy. |
| `IngestKeyIds` | any | Only forward requests authenticated with these ingest keys (key ids). |
| `Gzip` | `true` | Compress request bodies. |
| `Timeout` | `00:00:10` | Per-request timeout. |
| `QueueCapacity` | `1000` | Requests buffered in memory per target. |
| `MaxAttempts` | `3` | Delivery attempts for network errors, 429 and 5xx. |

An invalid target (bad URL, duplicate name) stops Flare.Ingest at startup.

## What to expect

- Only requests Flare accepted are forwarded. Rejected ones (over an ingest key's limit, service not
  allowed, malformed) are not.
- Forwarding is best effort. If the destination is slow or down, the queue fills and the newest requests
  are dropped with a warning in the ingest log; Flare's own copy is never affected. Restarting
  Flare.Ingest loses whatever was still queued. If you need durable fan-out, put an OpenTelemetry
  Collector in front of Flare instead.
- Profiles are not forwarded.
- A target with `IngestKeyIds` receives nothing from requests that carry no ingest key.

Design notes: [ADR-0155](../../docs-internal/adr/0155-otlp-forwarding.md).
