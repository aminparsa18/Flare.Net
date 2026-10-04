-- Alerting schema, migration 0048.
--
-- AI incident summaries on fired alerts - see `docs-internal/adr/0104-ai-incident-summary.md`.
--
-- One row per fired `alert_events` row that got a model-written summary. A separate append-only
-- table rather than a column on `alert_events`: the summary is produced after the plain
-- notification has gone out (it never delays it), and `alert_events` rows are immutable once
-- written, so there is no row to update. History reads look summaries up by EventId.
--
-- `Prompt` is the exact redacted text that was sent to the model - the record of what left the
-- box. `Summary` is the model's answer, stored and rendered as plain text.
--
-- Existing numbered migrations are immutable once merged (see this directory's README's
-- "Migration convention"), hence a new file.
CREATE TABLE IF NOT EXISTS clickhousedb.alert_event_summaries
(
    EventId UUID,
    RuleId UUID,
    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    Model LowCardinality(String),
    Summary String CODEC(ZSTD(1)),
    Prompt String CODEC(ZSTD(3))
)
ENGINE = MergeTree
PARTITION BY toStartOfMonth(CreatedAt)
ORDER BY (RuleId, CreatedAt)
SETTINGS index_granularity = 8192;
