# ADR-0157: Managed telemetry export (forwarding targets, archive settings, durable queue)

Status: Accepted

Date: 2026-10-09

## Context

ADR-0155 (OTLP forwarding) and ADR-0156 (S3 archive) shipped configuration-only, with an in-memory forwarding
queue and no visibility into either. Changing a target meant editing environment variables and restarting.

## Decision

- **Settings live in ClickHouse.** Migration 0071 adds `telemetry_exports`: one row per forwarding target
  (Kind 0) and one fixed-id row for the archive (Kind 1), with the kind-specific settings in `ConfigJson`.
  Same CRUD-via-tombstone / latest-version shape as `metric_attribute_rules` (ADR-0074).
- **Admin-only API** under `/api/forwarding/*` and `/api/archive/settings`. Header values and S3 keys are
  masked in every response and audit snapshot; sending a mask back keeps the stored value
  (`NotificationSecrets`). `GET /api/archive/status` holds no secrets and is open to any signed-in user.
- **Saved settings add to or override configuration.** Managed forwarding targets run alongside
  `Forwarding:Targets`; saved archive settings replace the worker's `Archive` section until reset.
  Flare.Ingest re-reads targets every 30 s and the archive worker re-reads on each poll, so no restart is needed.
- **The forwarding queue is a Redis stream per target**, read by a consumer group. The receiver does a
  fire-and-forget `XADD` (trimmed to `QueueCapacity`); senders ack after the destination accepts the request or
  answers a non-retryable 4xx, and leave a failed one pending to be reclaimed after `ReclaimIdle`, until it is
  older than `MaxAge`. Queued requests now survive an ingest restart and replicas share the work. Delivery is
  at-least-once, so a timeout whose request had landed can duplicate.
- **Status goes through Redis.** Senders publish sent/failed counters, last success and last error; the
  archive worker publishes a per-table last exported hour, row count and error. Flare.Api only reads them.

## Consequences

- Credentials are stored unencrypted in ClickHouse, like notification channel secrets.
- A removed or disabled target has its queue and counters deleted.
- Not yet covered: the dashboard UI (Settings page and Ingestion-page archive card), and the Terraform
  provider and `flare` CLI config sync.
