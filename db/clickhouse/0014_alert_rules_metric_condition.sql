-- Alerting schema, migration 0014.
--
-- Adds a second condition kind to `alert_rules` (migration 0003, extended since by
-- migrations 0005/0006/0012's notification-channel columns): a metric-query threshold
-- (e.g. "p99 latency > 500ms", a gauge crossing a value) alongside the existing
-- log-filter-count condition (`ConditionJson`/`ThresholdCount`/`ThresholdComparator`).
-- See `docs-internal/adr/0020-metric-threshold-alerting.md` for the full design.
--
-- `ConditionKind` is the discriminator - "LogCount" (the only value that existed before
-- this migration, hence the `DEFAULT` making every pre-existing row correct with zero
-- backfill) or "MetricThreshold". `Flare.Api.Model.AlertRule`'s existing
-- `ConditionJson`/`ThresholdCount` are ignored (client sends harmless placeholders) when
-- `ConditionKind = 'MetricThreshold'`, and the two columns below are ignored when
-- `ConditionKind = 'LogCount'` - the same "column present, meaningful only for one mode"
-- shape `WebhookUrl`/`TelegramBotToken`+`TelegramChatId`/`EmailTo`/`PagerDutyRoutingKey`
-- already use for the (also mutually-exclusive) notification channel.
--
-- `MetricConditionJson` mirrors `ConditionJson`'s own "store opaque, round-trip through the
-- C# model, don't explode into columns" reasoning (see migration 0003's comment) - it's a
-- JSON-serialized `Flare.Api.Model.MetricAlertCondition` (metric name/point type/filter/
-- aggregation). `MetricThresholdValue` is `Nullable(Float64)`, not `Float64 DEFAULT 0`:
-- unlike a count, `0.0` is a plausible real threshold (e.g. a gauge alerting on "== 0
-- available replicas"), so a real `NULL` is needed to mean "not a metric-threshold rule"
-- rather than colliding with a legitimate zero threshold.
--
-- Existing numbered migrations are immutable once merged (see this directory's README's
-- "Migration convention"), hence a new file rather than editing 0003_alert_rules.sql.
-- Like migrations 0002-0013, there's no automated apply path yet beyond the local-dev
-- init-mount that only runs 0001 automatically - run this by hand via `clickhouse-client`
-- against any already-running instance.
ALTER TABLE clickhousedb.alert_rules
    ADD COLUMN IF NOT EXISTS ConditionKind LowCardinality(String) DEFAULT 'LogCount' AFTER IsDeleted,
    ADD COLUMN IF NOT EXISTS MetricConditionJson String DEFAULT '' CODEC(ZSTD(1)) AFTER PagerDutyRoutingKey,
    ADD COLUMN IF NOT EXISTS MetricThresholdValue Nullable(Float64) AFTER MetricConditionJson;
