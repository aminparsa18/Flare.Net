-- Adds OTLP Span.Links to the spans table, migration 0013 - CLUSTER VARIANT.
--
-- Same column/rationale as db/clickhouse/0013_span_links.sql - see that file for the
-- full explanation. Same `_local` + Distributed-keeps-the-name pattern as every other
-- cluster-variant migration (0002_logs_event_id.sql's cluster variant explains why both
-- need the same ADD COLUMN: a Distributed table's column set doesn't auto-sync from its
-- underlying local table).
ALTER TABLE clickhousedb.spans_local ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS Links Nested
(
    TraceId String,
    SpanId String,
    TraceState String,
    Attributes Map(LowCardinality(String), String)
);
ALTER TABLE clickhousedb.spans ON CLUSTER 'flare_cluster' ADD COLUMN IF NOT EXISTS Links Nested
(
    TraceId String,
    SpanId String,
    TraceState String,
    Attributes Map(LowCardinality(String), String)
);
