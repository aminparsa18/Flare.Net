-- Alerting schema, migration 0028.
--
-- Anomaly-detection alerting - see `docs-internal/adr/0048-anomaly-detection-alerting.md`.
--
-- `ConditionKind` gains a fourth value, `'Anomaly'` - no schema change needed for that, it's a
-- plain `LowCardinality(String)` since migration 0014.
--
-- `alert_rules.AnomalyConditionJson`: a JSON-serialized `Flare.Api.Model.AnomalyCondition`
-- (source kind, seasonality, baseline periods, z-score threshold, direction), stored opaque
-- like `MetricConditionJson`/`ExceptionConditionJson`. Empty for every non-anomaly rule. The
-- scored series itself still comes from `ConditionJson`/`MetricConditionJson`/
-- `ExceptionConditionJson`, per the condition's source kind.
--
-- `alert_events.BaselineMean`/`ZScore`: set only for an anomaly fire - the baseline mean the
-- current value (in `ObservedValue`) was scored against, and its z-score. NULL for every other
-- event, including every pre-existing one.
--
-- Existing numbered migrations are immutable once merged (see this directory's README's
-- "Migration convention"), hence a new file. Like migrations 0002-0027, run this by hand via
-- `clickhouse-client` against any already-running instance.
ALTER TABLE clickhousedb.alert_rules
    ADD COLUMN IF NOT EXISTS AnomalyConditionJson String DEFAULT '' CODEC(ZSTD(1)) AFTER EvaluationIntervalSeconds;
ALTER TABLE clickhousedb.alert_events
    ADD COLUMN IF NOT EXISTS BaselineMean Nullable(Float64);
ALTER TABLE clickhousedb.alert_events
    ADD COLUMN IF NOT EXISTS ZScore Nullable(Float64);
