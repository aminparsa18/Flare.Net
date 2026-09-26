# ADR-0057: RabbitMQ queue depth and default-exchange destinations

Status: Accepted

Date: 2026-09-26

Amends [ADR-0056](0056-messaging-queue-monitoring.md). Its consumer-lag
decision still holds; this ADR generalizes it into a backlog figure.

## Context

The `/messaging` page showed a backlog figure only for Kafka (consumer lag
from `kafka.consumer_group.lag`), so RabbitMQ rows showed a dash. The
OTel Collector's `rabbitmq` receiver scrapes the management API and exports
`rabbitmq.message.current`. A live run against RabbitMQ 4 and
otelcol-contrib 0.161 confirmed its shape. It is a non-monotonic sum, so
it lands in `metrics_sum`. It has one data point per `state`
(`ready`/`unacknowledged`), and `rabbitmq.queue.name`, `rabbitmq.vhost.name`
and `rabbitmq.node.name` are resource attributes. `ServiceName` is empty.

The same run showed that the span side doesn't name queues. RabbitMQ.Client
7 sets `messaging.destination.name` to the exchange, and uses
`amq.default` for the default exchange. The queue name appears only as
`messaging.rabbitmq.destination.routing_key`. So every queue published to
directly collapsed into one `amq.default` row, and a queue metric can never
be matched on destination name alone.

## Decision

- **Default-exchange spans use the routing key as their destination.** When
  `messaging.system = 'rabbitmq'` and the destination is `''` or
  `amq.default`, `MessagingQueryBuilder.DestinationExpr` uses the routing
  key. The default exchange routes by queue name, so the routing key is the
  queue. This is also what the semantic conventions ask for when the
  exchange is empty. Filtering on a destination uses this expression only
  for RabbitMQ. Other systems keep the raw attribute comparison, which
  `idx_span_attr_value` can serve.
- **A destination's queues are its name plus its routing keys.** The
  destinations query also returns each row's distinct routing keys (up to
  20), and `QueueCandidates` joins them with the destination name. A queue
  addressed directly, an exchange named after its queue (MassTransit's
  convention), and a direct exchange whose routing key is a queue name all
  match. Topic or fanout routing that names no queue matches nothing. The
  backlog then stays null rather than guessing.
- **Depth is the latest value per `(vhost, queue, state)`, not per node.**
  `argMax(Value, Time)` over `metrics_sum` in the window. Dropping the node
  from the grouping stops a queue whose leader moved mid-window from being
  counted twice. A queue name that exists in several vhosts is summed on the
  list, because destinations don't carry a vhost. The drill-down lists each
  vhost separately.
- **One system-neutral `Backlog` field.** `MessagingDestination.ConsumerLag`
  becomes `Backlog`: Kafka lag or RabbitMQ ready + unacknowledged, and null
  when no broker metric feeds it. The drill-down keeps the Kafka per-group
  lag table and adds a `QueueDepth` list (vhost, queue, ready,
  unacknowledged).

## Consequences

- RabbitMQ users who run the collector's `rabbitmq` receiver see backlog
  per destination, with no ingest or schema change.
- Existing `amq.default` rows split into one row per queue. Saved links to
  an `amq.default` drill-down no longer match anything.
- The list makes one more ClickHouse query, and only when a RabbitMQ row is
  present. The drill-down makes two more, also only for RabbitMQ.
- Still not covered: Service Bus active and dead-letter counts, Amazon SQS
  and NATS. Each would add its own metric lookup behind the same `Backlog`
  field.
