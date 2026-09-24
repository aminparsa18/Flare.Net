-- StartTime-ordered projection on spans, migration 0025.
--
-- 0007_spans.sql orders `spans` by `(TraceId, StartTime, SpanId)` - right for the trace
-- waterfall's "every span of one trace" lookup, but it means a plain `StartTime` window
-- filter can't prune a single granule: every trace's spans are spread across the whole
-- table. `Flare.Api`'s `ServiceDependencyQueryBuilder` edges query (the Services-tab Map
-- view's `(source, target)` pairs - the one Map-view query ADR-0031 left un-pre-aggregated)
-- self-joins `spans` on every load, so it read the entire table *twice* regardless of the
-- selected window: measured at ~2.8 s for the default 15-minute window over 50M stored
-- spans, growing linearly with retention rather than with the window. See
-- docs-internal/adr/0043-service-dependency-edges-start-time-projection.md.
--
-- This projection stores a StartTime-sorted copy of just the columns that query reads, so
-- both sides of its join (the builder now bounds the parent side's `StartTime` too) prune
-- down to the window. Same 50M-span measurement afterwards: ~54 ms, identical results.
--
-- Not free: sorted by time, the random TraceId/SpanId strings compress worse than they do
-- in the base table's TraceId-first order - the benchmark's projection came out at roughly
-- 2x the base table's compressed size. Accepted over the pre-aggregation alternatives
-- ADR-0031 and its follow-up spike ruled out. `ResourceAttributes` is deliberately left
-- out to keep that cost down: an edges query with a resource-attribute filter chip can't
-- use the projection and falls back to the base table (the pre-0025 behavior, still
-- correct, just not faster).
--
-- Additive only. MATERIALIZE PROJECTION backfills parts written before this migration as
-- an ordinary background mutation (not waited on here); ClickHouse reads any part that
-- doesn't have the projection yet from the base table, so queries stay correct while it
-- runs.
ALTER TABLE clickhousedb.spans ADD PROJECTION IF NOT EXISTS spans_by_start_time
(
    SELECT TraceId, SpanId, ParentSpanId, StartTime, DurationNano, ServiceName, SpanAttributes
    ORDER BY StartTime
);
ALTER TABLE clickhousedb.spans MATERIALIZE PROJECTION spans_by_start_time;
