# Forward telemetry to another OTLP endpoint

Flare can send a copy of everything it accepts to one or more other OTLP/HTTP endpoints, for example to
run Flare next to another backend while you migrate, or to feed a second consumer.

## Add a target in the dashboard

Open **Settings > Workspace > Telemetry export** (admins only) and choose **New target**. Give it a name and the
endpoint's base URL, optionally add headers (one `Name: value` per line), pick the signals, services and
ingest keys to forward, and save. Flare.Ingest re-reads saved targets every 30 seconds, so a new, edited,
disabled or deleted target takes effect without a restart.

Header values are hidden once saved. When you edit a target, leave a hidden value as it is to keep it, or
type a new value to replace it.

The table shows each target's live status, refreshed every few seconds: requests **pending** in its queue,
**sent** and **failed** counts, and the last error. Targets defined in configuration appear below the saved
ones, marked *From configuration*; they are read-only here.

## Or configure a target in files

Targets can also be configuration on **Flare.Ingest** (appsettings or environment variables), which suits
infrastructure-as-code. Saved targets run alongside them. Using environment variables, which map onto
`Forwarding:Targets:<index>:<setting>`:

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
| `QueueCapacity` | `10000` | Requests the target's Redis queue keeps; past this the oldest are trimmed. |
| `MaxAttempts` | `3` | Immediate delivery attempts for network errors, 429 and 5xx before the request waits to be retried. |

Three settings apply to every target and sit directly under `Forwarding`:

| Setting | Default | Meaning |
|---|---|---|
| `Forwarding__MaxAge` | `06:00:00` | A queued request older than this is dropped instead of delivered. |
| `Forwarding__ReclaimIdle` | `00:00:30` | How long an undelivered request waits before it is retried. |
| `Forwarding__RefreshInterval` | `00:00:30` | How often saved targets are re-read. |

An invalid target in configuration (bad URL, duplicate name) stops Flare.Ingest at startup.

## What to expect

- Only requests Flare accepted are forwarded. Rejected ones (over an ingest key's limit, service not
  allowed, malformed) are not.
- Each target has its own queue in Redis. A request is removed from it once the destination accepts it
  (or answers with a non-retryable 4xx such as 401, which is counted as failed and dropped). A network error,
  429 or 5xx leaves the request queued; it is retried after `ReclaimIdle`, until it is older than `MaxAge`.
  The queue survives a restart of Flare.Ingest, and several Flare.Ingest replicas share the work.
- Delivery is at least once: if a request timed out after the destination had in fact stored it, the retry
  duplicates it. If the destination is down for longer than `MaxAge`, or the queue passes `QueueCapacity`,
  the oldest requests are dropped. Flare's own copy is never affected, and a slow destination never slows
  ingest.
- Deleting or disabling a target discards its queue and counters.
- Profiles are not forwarded.
- A target with ingest keys receives nothing from requests that carry no ingest key.

Design notes: [ADR-0155](../../docs-internal/adr/0155-otlp-forwarding.md), [ADR-0157](../../docs-internal/adr/0157-managed-telemetry-export.md).
