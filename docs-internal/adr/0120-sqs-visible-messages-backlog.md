# ADR-0120: Amazon SQS visible-message backlog

Status: Accepted

Date: 2026-10-04

Extends [ADR-0119](0119-service-bus-active-messages-backlog.md): the `Backlog` column gains a fifth source.

## Context

SQS queue depth is CloudWatch's `ApproximateNumberOfMessagesVisible`. The Collector's
`awscloudwatch` receiver reads CloudWatch *Logs*, not metrics, so the route is a
CloudWatch Metric Stream into Kinesis Data Firehose and the `awsfirehose` receiver
(`record_type: cwmetrics`, the JSON format). From the receiver's source (read, not run: no
AWS account to verify against):

- every record becomes a **Summary** data point (count, sum, min/max quantiles). Flare
  stores Gauge, Sum and Histogram points and drops Summaries at ingest;
- the metric keeps CloudWatch's own name, unprefixed; `cloud.account.id` and `cloud.region`
  are resource attributes, and each dimension, `QueueName` for SQS, is a data-point attribute.

The `transform` processor's `extract_avg_metric()` adds a Gauge named
`<name>_avg` (sum / count) from a Summary. SQS reports once a minute, so count is usually 1
and the average is the value itself. The .NET AWS instrumentation sets
`messaging.system` to `aws_sqs` (`aws.sqs` before the semantic convention settled) and the
destination to the queue name; some versions report the queue URL.

## Decision

- **Don't add a Summary table.** Summaries are only produced by this one receiver for this
  one need; a Collector `transform` statement converts it to a Gauge Flare already stores.
- **Read `ApproximateNumberOfMessagesVisible_avg`.** Latest value per `(account, region,
  queue)` (`argMax`), summed over them.
- **Match by queue name.** A row's candidates are its destination, plus the last path
  segment when the destination is a queue URL (`SqsQueueCandidates`). Both `aws_sqs` and
  `aws.sqs` count as SQS.
- Visible messages only: in-flight and delayed messages aren't waiting to be received. No
  new wire field, no detail table, same as ADR-0119.

## Consequences

- Users run a Collector reachable by Firehose over HTTPS and add the receiver plus one
  `transform` statement. No Flare configuration.
- **Unverified against real AWS.** Names and attributes come from the receivers' source. If
  a live run differs, correct `MessagingQueryBuilder` and this ADR.
- CloudWatch Metric Streams add about two minutes of delay on top of SQS's one-minute
  reporting.
- FIFO queues are named `*.fifo`, and CloudWatch's `QueueName` includes the suffix, so they
  match when the span destination does.
