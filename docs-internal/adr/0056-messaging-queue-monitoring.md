# ADR-0056: Messaging-queue monitoring from `messaging.*` spans

Status: Accepted

Date: 2026-09-26

## Context

Nothing in Flare showed how an application's message queues behave: how
fast each topic or queue is written to and read from, how long publishing
and processing take, how often they fail, and how far consumers have
fallen behind. The data mostly already arrives. OTel's messaging semantic
conventions put `messaging.system`, `messaging.destination.name`, the
partition, the consumer group and the message size on producer and
consumer spans, and .NET's Kafka, RabbitMQ, Azure Service Bus and
MassTransit instrumentations emit them. Consumer lag is not a span
property, but the OTel Collector's `kafkametrics` receiver exports it as
the `kafka.consumer_group.lag` gauge. Prior art:
[signoz 481bb6e](https://github.com/SigNoz/signoz/commit/481bb6e8b8d68b40d5b6706b91bb78b71d59a3c7)
added producer and consumer tables per topic and partition, computed from
spans at query time, and consumer lag from metrics later.

## Decision

A new `/messaging` dashboard page backed by two endpoints:
`POST /api/messaging/destinations` (one row per `(system, destination)`)
and `POST /api/messaging/destination-detail` (one destination's producers,
consumers and partitions). Both take a `MessagingFilter` (time window plus
optional services and systems).

- **Computed from `spans` at query time. No new table or migration.**
  Messaging spans are a small share of all spans, and every query requires
  `mapContains(SpanAttributes, 'messaging.system')`, which the existing
  `idx_span_attr_key` bloom-filter index can use to skip granules without
  that key. This follows the `/errors` page, which also aggregates `spans`
  live over a chosen window, with no polling. A materialized view like
  `service_metrics` (ADR-0030) is the named follow-up if this turns out
  slow on real data. It isn't built speculatively because the grouping
  dimensions (destination, partition, consumer group) come from attributes
  whose names have changed between semantic-convention versions (below),
  and a view bakes one reading of them into stored rows.
- **Not Kafka-only.** Rows are keyed by `messaging.system` as well as the
  destination, so RabbitMQ queues and Service Bus entities show up next
  to Kafka topics. Partitions, consumer groups and lag are Kafka concepts;
  for other systems those columns are simply empty.
- **Producer or consumer is decided by the operation first, then the span
  kind.** The conventions moved "send" and "receive" spans from
  `PRODUCER`/`CONSUMER` to `CLIENT` in some cases, so kind alone
  misclassifies them. A span is a *publish* when its operation type is
  `publish`, `create` or `send`, and a *consume* when it is `receive`,
  `process` or `deliver`. Only when no operation attribute is set does
  `Kind` decide (4 = producer, 5 = consumer). `settle` spans and anything
  else are ignored.
- **A consumer's `receive` spans are dropped when it also emits
  `process` spans.** OpenTelemetry.Instrumentation.ConfluentKafka emits
  both a `receive` (poll) span and a `process` span for every message. A
  live run against a real broker showed that counting both doubled every
  consume figure and mixed fetch time into handling latency. So for each
  consumer (system, destination, service and consumer group), `receive`
  spans count only if it has no `process` spans in the window. That keeps
  receive-only consumers, such as pull-style receivers whose handling
  isn't instrumented. The check is a window function over the consumer's
  spans, not a lookup that joins receive spans to process spans by trace.
- **Old and new attribute names are both read.** Operation:
  `messaging.operation.type`, else `messaging.operation`. Partition:
  `messaging.destination.partition.id`, else
  `messaging.kafka.destination.partition`. Consumer group:
  `messaging.consumer.group.name`, else `messaging.kafka.consumer.group`.
  Instrumentations in the wild still emit every one of these, so reading
  only the current names would show empty columns for most .NET apps.
- **Consume latency means the consume span's duration.** For a `process`
  span that is handling time; for a receive-only consumer it is the fetch. The
  page doesn't try to stitch publish to process spans into an end-to-end
  delivery latency. That needs span links or parent lookups across
  traces, which is a separate feature.
- **Consumer lag comes from the `kafka.consumer_group.lag` gauge when it
  exists.** The detail endpoint reads the latest value in the window per
  `(group, partition)` for that topic from `metrics_gauge`, and the
  destinations endpoint reports each topic's latest total lag. When no
  collector exports the metric, lag is null and the page says how to get
  it, rather than showing zero.

## Consequences

- Kafka, RabbitMQ and Service Bus users get throughput, error rate,
  latency percentiles and (for Kafka, with the collector receiver) lag
  per topic without any ingest or schema change, and it works on data
  already stored.
- Every page load scans spans in the window that carry
  `messaging.system`. Like `/errors`, it's an on-demand page, not a
  polled one.
- The operation/kind rules and the attribute fallbacks live in one
  place, `MessagingQueryBuilder`, so a later convention change is a one-file
  edit and is unit-tested.
- Not covered: end-to-end publish-to-process latency, messaging alert
  rules, and a pre-aggregated table. Each can be added on top without
  changing these endpoints' shapes.
