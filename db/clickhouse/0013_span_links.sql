-- Adds OTLP Span.Links to the spans table, migration 0013.
--
-- 0007_spans.sql originally left this out entirely, not even as empty columns,
-- following the "add a column when there's a concrete need" precedent set by
-- 0005_alert_rules_telegram.sql/0006_alert_rules_email.sql - see that migration's
-- remarks. The concrete need: async/batch/messaging spans (a queue consumer span
-- linking back to its producer's span in a different trace) have no representation in
-- Flare without it, and the trace waterfall view now surfaces them.
--
-- Same `Nested` desugaring as `Events` (see 0007_spans.sql's remarks and
-- Flare.Ingest's `ClickHouseSpanRowMapper`) - ClickHouse expands this into four
-- parallel `Array(...)` columns (`Links.TraceId`/`Links.SpanId`/`Links.TraceState`/
-- `Links.Attributes`). Unlike `Events`, a link carries no timestamp of its own on the
-- OTLP wire, so there's no `Links.TimeUnixNano` counterpart.
ALTER TABLE clickhousedb.spans ADD COLUMN IF NOT EXISTS Links Nested
(
    -- Lower-hex trace/span id of the linked-to span, same encoding convention as this
    -- table's own TraceId/SpanId.
    TraceId String,
    SpanId String,

    -- W3C tracestate of the linked-to span, if set - same empty-string-means-absent
    -- convention as the rest of this table.
    TraceState String,

    Attributes Map(LowCardinality(String), String)
);
