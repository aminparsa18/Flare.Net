# Ingest reliability and delivery guarantees

What happens to telemetry when Flare.Ingest restarts, when ClickHouse is
unavailable, or when one app sends far more than its share? This page explains
what Flare promises between the moment an app's OTLP export is accepted and the
moment the data is queryable, and where that promise stops. For the pipeline
itself see [Architecture](architecture.md).

## The path, and where it can break

```
exporter ──OTLP──▶ Flare.Ingest ──XADD──▶ Redis Stream ──XREADGROUP──▶ flush worker ──INSERT──▶ ClickHouse
                   (accepts, maps)        (the buffer)                  (acks after insert)
```

Ingest does not write to ClickHouse while handling a request. It maps the OTLP
payload to Flare's internal events and appends them to a Redis Stream; a
separate flush worker reads batches, writes them to ClickHouse, and only then
acknowledges them. A request is acknowledged to the exporter once its events are
in the stream.

## Why a Redis Stream is the buffer

An in-memory buffer would be simpler, but every restart or deploy of
`Flare.Ingest` would silently drop whatever hadn't been flushed. Making that
durable by hand means building a queue with persistence, acknowledgement,
consumer coordination and replay, which is what Redis Streams already is
([ADR-0002](../../docs-internal/adr/0002-redis-streams-buffering.md)). The
buffer has a few properties worth knowing:

- Entries stay in the stream until acknowledged, so a restarted Ingest picks
  up where the old process stopped. Several Ingest replicas share one consumer
  group and don't take each other's work.
- Entries are stored in a compact binary format. A version upgrade is also a
  restart, so the reader still decodes entries written in the previous format
  ([ADR-0017](../../docs-internal/adr/0017-memorypack-ingest-redis-buffer.md)).
- Redis is therefore stateful infrastructure. Flushing or resetting its volume
  discards telemetry that hasn't reached ClickHouse yet.

## What happens when something fails

**Flare.Ingest restarts.** Buffered entries are still in Redis. On start the
flush worker rejoins the consumer group and reclaims entries that were read but
never acknowledged. Exports that were in flight at the moment of the restart
fail, and the exporter retries them.

**A flush to ClickHouse fails.** The batch is not acknowledged. It stays pending
in the stream and is retried after it has been idle for a while (30 seconds by
default). Meanwhile Ingest keeps accepting data into Redis, so ClickHouse can be
briefly unavailable without exporters noticing.

Two limits bound this, and both are configurable:

- **The stream has a size cap** (about one million entries per signal by
  default, trimmed approximately) so Redis memory stays bounded. If ClickHouse
  is down long enough for the backlog to exceed it, the oldest entries are
  trimmed away.
- **Retries have a budget.** An entry delivered more than the configured
  maximum number of times (five by default) is logged and dropped, as a guard
  against one malformed entry blocking the stream forever. The worker can't
  tell a poison entry from a healthy one during a database outage, so a long
  ClickHouse outage can consume that budget too. Treat a prolonged outage as a
  situation where some buffered data may be lost, and watch the Ingestion page
  and logs for flush failures.

**Redis is unavailable.** The sink can't append, so the export fails and a
well-behaved exporter retries. Nothing is accepted that isn't buffered.

## At-least-once, and what it means for duplicates

The flush worker acknowledges only after a successful insert. If it crashes
between the insert and the acknowledgement, the entry is delivered again and
inserted again. So delivery is **at-least-once**, not exactly-once.

Nothing collapses that duplicate: the `logs` table is a plain `MergeTree`, and
the per-row `EventId` exists as a pagination tiebreaker, not a deduplication
key. After an Ingest crash you may see an occasional duplicate row; the window is
the gap between the insert and the acknowledgement. Counts and rates are accurate to within that, and alert
thresholds shouldn't be set so tightly that one duplicate matters.

## What is and isn't durable

| State | Durable? |
| --- | --- |
| Data already in ClickHouse | Yes, subject to retention and your ClickHouse setup (replication in [cluster mode](clustering.md)) |
| Buffered in the Redis Stream | Only as durable as Redis. Flare's bundled Redis snapshots to disk (`--save 30 100`), not an append-only file, so a Redis crash can lose roughly the last 30 seconds or 100 key changes of buffered entries |
| Accepted by Ingest but not yet in the stream | No. A crash loses it, and the exporter's own retry is the only protection |
| Spans held for [tail sampling](sampling-clustering-and-cost.md) | No. Held in process memory, a crash loses whatever was held; a graceful shutdown decides every held trace first |
| Copies sent by forwarding (below) | No. Best effort and at-most-once |

So the honest summary is: at-least-once from Redis to ClickHouse, bounded by
Redis's persistence settings, not zero loss. If you need stronger guarantees,
run Redis with append-only persistence or fan out through an OpenTelemetry
Collector with a durable queue in front of Flare.

## Protecting the pipeline from a single sender

The buffer is shared, so one noisy app can crowd out the rest. Two guards sit at
the receiver:

- **A request size cap** (`Otlp:MaxRequestSizeBytes`, 64 MiB by default, the
  OpenTelemetry spec's recommended maximum) applies to gRPC and HTTP alike. It
  bounds per-request memory, because HTTP bodies are buffered in full. It
  matches the exporters' own default on purpose: an exporter treats a size
  rejection as non-retryable and drops the batch, so a server cap lower than the
  exporter's would turn large batches into silent loss. An oversized HTTP
  request gets `413`.
- **Per-ingest-key limits** cap events and bytes per UTC minute and per UTC day
  for a key, so an admin can throttle one source without revoking it and
  discarding its data
  ([ADR-0051](../../docs-internal/adr/0051-per-ingest-key-ingestion-limits.md)).
  Counters live in Redis because Ingest can run as several replicas, and a
  per-process counter would let a key through at N times its cap.

Some details of the limits are worth knowing. They are a **soft** cap: a request
is admitted while usage is below the cap and its full size is added afterwards,
so a window can overshoot by the requests in flight. Exceeding a cap returns the
OTLP throttling response (HTTP `429` with `Retry-After`, or gRPC
`RESOURCE_EXHAUSTED` with retry information), so well-behaved exporters back off
and retry instead of dropping data. If Redis can't be read, the limit check
fails open, because a limit is a fairness guard and not a security boundary.
Limits apply only while ingest keys are required, and never to a static key.

## One protocol in: OTLP

Flare accepts OTLP and nothing else, over gRPC and HTTP. It has no proprietary
agent or per-logger adapter. Any logger or SDK that speaks OTLP works, .NET or
not, and Flare's ingest side depends on the wire format rather than any client
package's version
([ADR-0012](../../docs-internal/adr/0012-otlp-only-ingestion.md)). The cost is
that Flare will never offer a faster proprietary client path.

### Forwarding

Because everything arrives as OTLP, Flare can also copy what it accepts to other
OTLP/HTTP endpoints, for running two backends side by side or feeding a SIEM.
Forwarding happens at the receiver after a request is accepted, so rejected
requests are never copied. It is deliberately **best effort**: each target has a
bounded in-memory queue, a full queue drops the newest request, and an Ingest
restart loses what was queued. This guarantees a slow or dead destination can't
apply back-pressure to ingestion or cost you data inside Flare, and the
trade-off is that the forwarded copy is at-most-once. If the second destination
must not lose data, put a Collector with a durable queue in front instead
([ADR-0155](../../docs-internal/adr/0155-otlp-forwarding.md)).

## Related

- Explanation: [Architecture](architecture.md), [How Flare stores and queries telemetry](how-flare-stores-and-queries-telemetry.md), [Clustering](clustering.md), [Projects and access scoping](projects-and-access-scoping.md)
- How-to: [Forward telemetry to another OTLP endpoint](../how-to/data/forward-telemetry-to-another-otlp-endpoint.md), [Run standalone](../how-to/deploy/run-standalone.md)
- Reference: [OTLP logger versions](../reference/otlp-logger-versions.md)
- ADRs: [0002](../../docs-internal/adr/0002-redis-streams-buffering.md), [0012](../../docs-internal/adr/0012-otlp-only-ingestion.md), [0017](../../docs-internal/adr/0017-memorypack-ingest-redis-buffer.md), [0051](../../docs-internal/adr/0051-per-ingest-key-ingestion-limits.md), [0155](../../docs-internal/adr/0155-otlp-forwarding.md). The request size cap has no ADR of its own; it is documented in `src/Flare.Ingest/README.md`.
