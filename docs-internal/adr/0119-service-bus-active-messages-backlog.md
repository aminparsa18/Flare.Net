# ADR-0119: Service Bus active-message backlog

Status: Accepted

Date: 2026-10-04

Extends [ADR-0057](0057-rabbitmq-queue-depth.md) and [ADR-0093](0093-nats-jetstream-backlog.md): the `Backlog` column gains a fourth source.

## Context

Azure.Messaging.ServiceBus spans already fill the Messaging page, but Service Bus has no
backlog column value. The queue depth isn't in spans; Azure exposes it as the
`ActiveMessages` metric. The Collector's `azuremonitor` receiver reads Azure Monitor and,
from its source (`receiver/azuremonitorreceiver`, read, not run, since there is no Azure
subscription to verify against):

- names a metric `azure_<metric>_<aggregation>`, lowercased, and emits it as a gauge, so
  it lands in `metrics_gauge` as `azure_activemessages_average`;
- puts the resource id (`azuremonitor.resource_id`) and every metric dimension on the
  data point, a dimension as `metadata_<name>`, so the `EntityName` dimension is
  `metadata_entityname`;
- emits only the aggregations configured, per metric.

For a queue `EntityName` is the queue name; for a topic subscription it is the
subscription name. A span's destination is the queue, the topic, or on receive
`topic/Subscriptions/<subscription>`.

## Decision

- **Read `azure_activemessages_average`.** The latest value per `(resource id, entity)`
  in the window (`argMax`), summed over namespaces so the same entity name in two
  namespaces adds up, as RabbitMQ's queue name across vhosts does.
- **Match by entity name.** A row's candidates are its destination, plus the subscription
  name for a `topic/Subscriptions/<name>` receive path (`ServiceBusEntityCandidates`).
- **Active messages only.** Dead-lettered messages aren't waiting to be processed and would
  inflate a backlog that means "work to do". They stay in Azure Monitor / the Collector.
- **No new wire field, no detail table.** Only the destinations list's `Backlog` is
  filled: the queue-depth table's `Ready`/`Unacked` columns don't describe active versus
  dead-letter counts.
- A topic receiver whose subscription name isn't in the metric, or an entity without the
  metric configured, shows no backlog rather than a guess.

## Consequences

- Users add the `azuremonitor` receiver with `ActiveMessages: [Average]` and the
  `EntityName` dimension. No Flare configuration.
- **Unverified against real Azure.** The metric and attribute names come from the
  receiver's source. If a live run shows different names, correct the constants in
  `MessagingQueryBuilder` and this ADR.
- Azure Monitor's minimum granularity is one minute, so the backlog lags by that much.
- A subscription name reused under two topics adds up on both rows.
