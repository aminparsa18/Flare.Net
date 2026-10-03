# ADR-0093: NATS spans and JetStream consumer backlog

Status: Accepted

Date: 2026-10-03

Extends [ADR-0057](0057-rabbitmq-queue-depth.md): the `Backlog` column gains a third source.

## Context

A live run of NATS.Net 3.3 against nats-server (JetStream on) and
`prometheus-nats-exporter` with `-jsz=all`, scraped by otelcol-contrib 0.161's
`prometheus` receiver, showed:

- **NATS.Net's spans are mostly its own traffic.** JetStream API calls
  (`$JS.API.…`), a per-message ack publish whose subject embeds the delivery sequence
  (`$JS.ACK.ORDERS.worker.1.1.1.<timestamp>.9`, a new subject every message) and
  receives on the client's temporary reply inbox (`messaging.destination.temporary` =
  `true`) all carry `messaging.system = nats`. Unfiltered, each would be a row.
- **A JetStream receive span names its consumer.** `messaging.nats.message.reply_to` is the
  ack subject, `$JS.ACK.<stream>.<consumer>.…` (or, with a domain and account hash,
  `$JS.ACK.<domain>.<hash>.<stream>.<consumer>.…`). Names can't contain `.`.
- **No Collector receiver reads NATS.** The exporter's `jetstream_consumer_num_pending` and
  `jetstream_consumer_num_ack_pending` gauges arrive in `metrics_gauge` with
  `stream_name`, `consumer_name` and `is_consumer_leader` as data-point attributes.
- A subject (`orders.created`) can't be mapped to a stream from metrics alone, because a
  stream's subjects can be wildcards.

## Decision

- **Drop NATS client traffic.** A span with `messaging.system = nats` whose destination
  starts with `$` or whose `messaging.destination.temporary` is `true` is excluded from
  every messaging query (`MessagingQueryBuilder.NatsNoiseExpr`). Core NATS subscriptions
  and JetStream consumers on application subjects are unaffected.
- **Match backlog through the spans.** Each row also returns the distinct
  `stream/consumer` pairs parsed from its receive spans' ack subject (9 tokens, or 12 with
  a domain). Backlog is the latest pending plus ack-pending of those consumers,
  `argMax` per `(stream, consumer, metric)`, leader series only so a replicated consumer
  isn't counted once per server. A consumer that serves several subjects shows its one
  backlog on each of them.
- **No new wire field.** The drill-down reuses the queue-depth table: stream in `Vhost`,
  consumer in `Queue`, pending in `Ready`, ack-pending in `Unacknowledged`.
- Subjects read by core subscriptions, and consumers that haven't received a message in
  the window (no span to name them), show no backlog rather than a guess.

## Consequences

- NATS users add `AddSource("NATS.Net")`, and for backlog a `prometheus-nats-exporter`
  plus the Collector's `prometheus` receiver. No Flare configuration.
- A consumer with a large backlog but no deliveries in the window shows no backlog until
  it receives a message.
