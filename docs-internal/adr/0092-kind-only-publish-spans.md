# ADR-0092: Drop kind-only publish spans when operation publish spans exist

Status: Accepted

Date: 2026-10-03

## Context

ADR-0056 classifies a messaging span by its operation attribute first and falls back to
the span kind (PRODUCER = publish) only when no operation is set. A live run of
Azure.Messaging.ServiceBus 7.21 against Microsoft's Service Bus emulator showed that
fallback double counts: the SDK emits a PRODUCER `Message` span (no operation) for every
message and a `ServiceBusSender.Send` CLIENT span (`messaging.operation` = `publish`) per
call. Sending 10 messages showed 20 publishes.

## Decision

A span classified as a publish only by its kind is dropped when the same
`(system, destination, service)` in the window also has spans whose operation says
publish. This is the same shape as ADR-0056's rule for receive vs. process spans: a window
function over the classified spans, no new table.

## Consequences

- Service Bus publishes count once per `send` call, so a batched send counts once rather
  than once per message. Per-message counts would need the `Message` spans, which carry
  no latency.
- Instrumentations that emit only PRODUCER spans (no operation) are unaffected.
- The SDK doesn't mark a process span as failed when the handler throws, so Service Bus
  consume errors read 0. That is the SDK's behavior, not something Flare can infer.
