# ADR-0091: MassTransit spans on the Messaging page

Status: Accepted

Date: 2026-10-03

## Context

ADR-0056's Messaging page only looks at spans that carry `messaging.system`. A live run
of MassTransit 8.5 over RabbitMQ 4 (its built-in `MassTransit` activity source) showed
that is not enough:

- Only `send` spans set `messaging.system`. `receive` and `process` spans don't, so every
  MassTransit consume was invisible: the page showed publishes and a consume count of 0.
- `process` spans have no `messaging.destination.name` at all. `receive` spans put the
  *queue* there (`SubmitOrder`), while `send` spans put the *exchange* (`Demo:SubmitOrder`),
  so even with the system fixed, producers and consumers would land on different rows.
- Every MassTransit span (send, receive, process) does carry
  `messaging.masstransit.destination_address`, e.g. `rabbitmq://localhost/Demo:SubmitOrder`.
- The `receive` span is the parent of the `process` span, so the receive-vs-process rule
  from ADR-0056 already applies once both are classified under the same destination.

## Decision

At query time, `MessagingQueryBuilder` treats a span as a messaging span when it has
`messaging.system` **or** `messaging.masstransit.destination_address`. For spans that
carry the address:

- the system is `messaging.system` when set, else the address scheme (`sb` is reported as
  `servicebus`, matching the semantic convention);
- the destination is the address's last path segment (query string dropped), the
  exchange/entity the message was sent to. Producers and consumers of one message type
  share one row.

No migration and no new table. The destination filter applies the normalizing expression
for every system except Kafka, whose raw attribute can still use `idx_span_attr_value`.

## Consequences

- MassTransit users send `AddSource("MassTransit")` and nothing else; no processor or
  attribute workaround is needed.
- A RabbitMQ queue's backlog (ADR-0057) is joined by queue name. MassTransit consumes
  from a queue named after the endpoint, while the row is named after the exchange, so
  the Backlog column is empty for MassTransit destinations unless the two names match.
