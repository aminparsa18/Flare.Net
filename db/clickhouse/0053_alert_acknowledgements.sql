-- Alerting schema, migration 0053.
--
-- Alert acknowledgement and snooze - see
-- `docs-internal/adr/0124-alert-acknowledgement-and-snooze.md`.
--
-- `alert_acknowledgements` is an append-only log of acknowledge / snooze / clear actions on a
-- firing rule. The current state of a rule is its newest row, and it only applies to the
-- current incident: a row older than the rule's latest resolution (`alert_events.Resolved = 1`)
-- is ignored. There is no separate state table, same reasoning as ADR-0064.
--
-- `Kind`: 'Ack' (silences re-notifications for the rest of the incident), 'Snooze' (silences
-- them until `SnoozedUntil`) or 'Clear' (withdraws an earlier ack/snooze).
-- `AckedBy` is the acting user's name, snapshotted; '' when Flare's opt-in auth is off.
--
-- Existing numbered migrations are immutable once merged (see this directory's README's
-- "Migration convention"), hence a new file. Like migrations 0002-0052, run this by hand via
-- `clickhouse-client` against any already-running instance.
CREATE TABLE IF NOT EXISTS clickhousedb.alert_acknowledgements
(
    RuleId UUID,
    AckedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    AckedBy String CODEC(ZSTD(1)),
    Kind LowCardinality(String),
    SnoozedUntil Nullable(DateTime64(3)),
    Note String CODEC(ZSTD(1))
)
ENGINE = MergeTree
ORDER BY (RuleId, AckedAt)
SETTINGS index_granularity = 8192;
