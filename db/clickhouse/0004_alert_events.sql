-- Alerting schema, migration 0004.
--
-- One row per fired alert notification attempt - the audit trail behind the dashboard's
-- "recent alerts" view, and also the sole source of cooldown state (see below). Plain
-- append-only MergeTree, mirroring `logs`' own simplicity: unlike `alert_rules`, a fired
-- event is immutable once written, so there's no ReplacingMergeTree/FINAL story needed
-- here at all.
CREATE TABLE IF NOT EXISTS clickhousedb.alert_events
(
    EventId UUID,
    RuleId UUID,
    -- Snapshot of the rule's name at fire time, not a join target - survives the rule
    -- being renamed or deleted later without the history view going blank or needing a
    -- lookup against alert_rules FINAL.
    RuleName String CODEC(ZSTD(1)),
    FiredAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    ObservedCount UInt64,
    ThresholdCount UInt64,
    WindowSeconds UInt32,
    -- "Sent" | "Failed" - Flare.Api.Model.AlertHistoryEntry's NotificationStatus verbatim.
    NotificationStatus LowCardinality(String),
    -- Webhook response HTTP status; 0 if the POST never completed (DNS/timeout/connection
    -- failure - see WebhookAlertNotifier).
    NotificationStatusCode Int32,
    NotificationError String CODEC(ZSTD(1))
)
ENGINE = MergeTree
PARTITION BY toStartOfMonth(FiredAt)
ORDER BY (RuleId, FiredAt)
SETTINGS index_granularity = 8192;

-- No separate cooldown-tracking table/cache: `AlertEvaluationWorker` checks
-- `SELECT max(FiredAt) FROM alert_events WHERE RuleId = {ruleId:UUID}` before notifying,
-- a cheap point query at this table's expected row count/poll frequency (ORDER BY leads
-- with RuleId for exactly this lookup). A rule in cooldown simply isn't re-evaluated into
-- a new row here - no "Suppressed" rows are written, so this table's semantics stay to
-- "one row per actual notification attempt," which is also exactly what the dashboard's
-- history view wants to show. Redis (already available in Flare.Api for live-tail) would
-- also work for this and was considered - rejected as a second source of truth for state
-- that has to agree with the audit log Flare needs to keep anyway.
