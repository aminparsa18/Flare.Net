# ADR-0013: ClickHouse as the storage/query engine, not a bespoke store

Status: Accepted
Date: 2026-08-07 (v1 design principles, stated from the project's outset)

## Context

Flare's differentiator was defined early as the dashboard and the
two-minute setup, not the storage engine — but something still has to
durably store and query potentially large volumes of log/trace/metric
data with the filtering and aggregation performance a "beautiful,
fast" dashboard depends on. Self-contained embedded log-store products
solve this by building bespoke, often separately-licensed storage and
clustering subsystems of their own.

## Decision

**Storage is a solved problem — ClickHouse is the backend.** Flare
doesn't reinvent a storage/query engine; it wraps ClickHouse well (via
.NET Aspire's ClickHouse integration for local dev, a plain container
otherwise) and buffers inserts properly (see
[ADR-0002](0002-redis-streams-buffering.md)) rather than building
anything storage-layer-novel.

## Alternatives considered

- **A bespoke, embedded storage engine**, the pattern self-contained
  log-store products use to avoid an external dependency. Rejected: this
  would mean building and maintaining a storage/query engine, and
  separately, a clustering subsystem for horizontal availability/
  throughput — both problems ClickHouse already solves well. Flare's
  effort goes into the dashboard and ingestion experience instead.
- Implicitly, other general-purpose analytical stores were not seriously
  weighed in the source material — the decision was framed from the start
  as "which existing engine to wrap," not "build vs. buy."

## Consequences

- **Flare inherits ClickHouse's own horizontal-availability/throughput
  characteristics "for free"** rather than needing a bespoke clustering
  subsystem — proven directly, not just asserted, by
  [the opt-in multi-node cluster mode](../../docs/explanation/clustering.md)
  and measured in
  [the ingest/query benchmark investigation](../investigations/benchmark-ingest-and-query.md).
- Every schema-shaping decision downstream of this one (attribute typing,
  `ORDER BY` design, the CRUD-table pattern) is a ClickHouse-specific
  decision, not a storage-engine-agnostic one — see ADR-0008 through
  ADR-0011.
- Ties Flare's deployment story to running a ClickHouse instance
  (container, Aspire resource, or cluster) as a hard dependency — there is
  no "smaller/simpler" storage mode for a trivial deployment; ClickHouse
  is not optional.
- The "wrap it well, don't reinvent it" posture extends to the ingest
  buffer too: Redis Streams (ADR-0002) rather than a hand-rolled queue.

## Related documentation

- `docs/explanation/architecture.md` — the full ingest → storage →
  dashboard pipeline this decision underlies
- ADR-0012 — the ingestion-side counterpart to this decision
- ADR-0002, ADR-0003, ADR-0008 through ADR-0011 — every ClickHouse-specific
  decision built on top of this one