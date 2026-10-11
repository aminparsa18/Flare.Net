# How Flare stores and queries telemetry

Why is Flare built on ClickHouse, how does telemetry end up in its tables,
and what keeps a query from taking the database down? This page explains the
storage and query design behind the Logs, Traces and Metrics pages. For the
end-to-end pipeline see [Architecture](architecture.md); for running more
than one ClickHouse node see [Clustering](clustering.md).

## Why ClickHouse

Something has to store large volumes of logs, spans and metric points and
filter and aggregate them fast enough for a dashboard. Flare doesn't build
that engine. ClickHouse is a hard dependency, and Flare's effort goes into
the ingest path and the UI instead
([ADR-0013](../../docs-internal/adr/0013-clickhouse-as-storage-engine.md)).
The cost is that there is no lighter storage mode for a tiny install, and
every schema decision below is ClickHouse-specific. The benefit is that
horizontal scale and replication come from ClickHouse itself, which is what
[cluster mode](clustering.md) uses.

Tables are created by numbered SQL migrations in `db/clickhouse/`. Each one
is additive (`ADD COLUMN`, a new table or view) and is never edited after
release, so an upgrade only ever applies the new files.

## Attributes are strings in maps

OTLP attributes can be strings, numbers, booleans, arrays or nested lists.
Flare flattens every value to a string at ingest and stores each of the
three attribute bags (resource, scope, and log, span or data-point
attributes) as a `Map(LowCardinality(String), String)`
([ADR-0008](../../docs-internal/adr/0008-clickhouse-attribute-typing.md)).
This is the same shape the OpenTelemetry Collector's ClickHouse exporter
uses, so queries and tooling familiar from that ecosystem carry over.

The trade-offs:

- **Filtering is by key and value, not a JSON path engine.** Bloom-filter
  skip indexes on the map keys and values keep attribute filters cheap,
  but nested structures are not queryable as structures.
- **Trace and span ids are plain lowercase-hex `String`s**, not `UUID`. That
  costs some storage efficiency and buys direct interoperability with every
  other OTel tool's way of showing ids.

## Compute once, read many times

Some pages would be slow if they scanned raw rows on every refresh. Flare
moves that work to insert time, using ClickHouse materialized views that
fill `AggregatingMergeTree` tables as spans are written:

- **Service RED metrics** (`service_metrics`) for the Services table,
  per-service rate, errors and latency percentile states
  ([ADR-0030](../../docs-internal/adr/0030-service-red-metrics-pre-aggregation.md)).
- **Service map nodes and call breakdown**, keyed by the effective service
  and by external peer or database operation
  ([ADR-0031](../../docs-internal/adr/0031-service-map-breakdown-pre-aggregation.md)).
- **Per-minute SLI counters** that SLOs and burn-rate alerts read, so a
  28-day error budget never scans 28 days of spans
  ([ADR-0108](../../docs-internal/adr/0108-slo-error-budgets.md)).

Because the view fires on the same single insert as the raw rows, there is
no second write path to double-count. Two limits follow, and they are
deliberate:

- **Filters that the aggregate doesn't carry fall back to the live query.**
  A resource-attribute filter chip can't be answered from a table keyed by
  service, so such requests read the raw spans instead of returning wrong
  numbers. A configuration switch also turns the pre-aggregated reads off.
- **No backfill.** A new aggregate starts empty and fills from the moment its
  migration is applied.

### Why the service map's edges are different

Nodes and breakdowns are plain `GROUP BY`s, so they pre-aggregate cleanly.
Edges (which service calls which) need a parent span and its child span,
which come from different processes and often arrive in different flush
batches. A materialized view only sees the batch being inserted, so it can't
pair them reliably, and the self-join design was spiked and rejected. Instead
the edges query stays live but is made cheap with a `StartTime`-ordered
projection on `spans` and a bounded parent lookup, which took a 15-minute
window at 50 million spans from seconds to tens of milliseconds
([ADR-0043](../../docs-internal/adr/0043-service-dependency-edges-start-time-projection.md)).
The price is extra storage for the projection, and a call whose parent started
more than an hour before the window no longer draws an edge.

## Search: indexes that match the query

Free-text log search is `lowerUTF8(Body) LIKE lowerUTF8(term)` over an
n-gram index on exactly that expression. ClickHouse uses a skip index only
when the query expression matches the index expression, so the two are kept
in lockstep, and a unit test checks the migration against the query builder.
`lowerUTF8` rather than `lower` makes case-insensitive search work for
Cyrillic and other non-ASCII text. The index applies to data inserted after
the migration; older parts are scanned as before
([ADR-0073](../../docs-internal/adr/0073-logs-body-ngram-search-index.md)).
Search terms shorter than four bytes, and terms present in most granules, still
scan.

## Keeping queries bounded

A self-hosted ClickHouse has no default limits, so Flare sets them on every
query it sends.

- **Execution caps.** Each query carries a maximum execution time, a maximum
  number of rows read and a maximum result size. The defaults (30 seconds,
  1 billion rows read, 10,000 result rows) are configurable through the
  `Query__*` settings. A result that hits the row cap is truncated, not
  failed. Alert evaluation has its own, shorter time cap.
- **Default lookback.** A log search with no start time looks back one hour,
  so an unfiltered request can't scan the whole table.
- **Keyset pagination.** Paging uses a cursor of the last row's
  `(Timestamp, EventId)` rather than an offset, so page 500 costs the same
  as page 1 and rows added mid-scroll don't shift the page. `EventId` breaks
  ties between rows with the same timestamp. The cursor is an opaque token,
  not a public contract. Search and aggregate are `POST` endpoints because
  filters are structured and multi-valued.

## Result caching

Dashboard panels and saved views re-run the same query on a timer. A Redis
cache sits in front of log search, log aggregate and metric queries, keyed by
a hash of the request itself, with a short TTL (30 seconds by default)
([ADR-0029](../../docs-internal/adr/0029-query-result-caching.md)).

- **Recent ranges are never cached.** A request that ends at "now", or within
  two minutes of it by default, bypasses the cache, because rows may still be
  in the ingest buffer and a cached undercount would stick for the whole TTL.
- **Settled ranges can lag by up to the TTL.** Nothing invalidates a key when
  late data arrives. Set `QueryCache:Enabled` to false if you would rather
  not accept that.
- **No stampede protection.** The cache serves one caller re-polling its own
  query, not many callers racing one key.

## Config tables read the latest version without `FINAL`

Alert rules, channels, dashboards, saved views and similar settings live in
ClickHouse too, as `ReplacingMergeTree` tables where every edit inserts a new
version and a delete inserts a tombstone. The obvious way to read the latest
version is `FINAL`, but in cluster mode a row's versions can land on
different shards and `FINAL` only collapses duplicates within one shard. The
symptom was real: an edited dashboard flipped between its old and new names
on reload, and an edited alert rule could keep being evaluated in its old
form.

Reads therefore pick the newest version per id across the whole distributed
table (`ORDER BY UpdatedAt DESC LIMIT 1 BY Id`) and only then apply the
deleted flag and other filters. This fixes existing clusters without a
migration or resharding. It scans every version of every row, which is
negligible for tables with tens to hundreds of rows
([ADR-0074](../../docs-internal/adr/0074-config-tables-latest-version-reads.md)).

## Related

- Explanation: [Architecture](architecture.md), [Clustering](clustering.md), [Ingest reliability and delivery guarantees](ingest-reliability-and-delivery-guarantees.md), [Sampling, clustering and cost](sampling-clustering-and-cost.md)
- How-to: [See what drives storage](../how-to/data/see-what-drives-storage.md), [Run cluster mode](../how-to/deploy/run-cluster-mode.md)
- Reference: [Clustering configuration](../reference/clustering-config.md)
- ADRs: [0008](../../docs-internal/adr/0008-clickhouse-attribute-typing.md), [0013](../../docs-internal/adr/0013-clickhouse-as-storage-engine.md), [0029](../../docs-internal/adr/0029-query-result-caching.md), [0030](../../docs-internal/adr/0030-service-red-metrics-pre-aggregation.md), [0031](../../docs-internal/adr/0031-service-map-breakdown-pre-aggregation.md), [0043](../../docs-internal/adr/0043-service-dependency-edges-start-time-projection.md), [0073](../../docs-internal/adr/0073-logs-body-ngram-search-index.md), [0074](../../docs-internal/adr/0074-config-tables-latest-version-reads.md)
