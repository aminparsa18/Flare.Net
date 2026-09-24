-- StartTime-ordered projection on spans, migration 0025 - CLUSTER VARIANT.
--
-- Same projection/rationale as db/clickhouse/0025_spans_start_time_projection.sql - see
-- that file for the full explanation. Only `spans_local` gets it: a projection is part of
-- a MergeTree table's storage, and the `spans` Distributed table has none of its own - it
-- forwards each query to every shard's `spans_local`, where the projection is picked up.
ALTER TABLE clickhousedb.spans_local ON CLUSTER 'flare_cluster' ADD PROJECTION IF NOT EXISTS spans_by_start_time
(
    SELECT TraceId, SpanId, ParentSpanId, StartTime, DurationNano, ServiceName, SpanAttributes
    ORDER BY StartTime
);
ALTER TABLE clickhousedb.spans_local ON CLUSTER 'flare_cluster' MATERIALIZE PROJECTION spans_by_start_time;
