-- Materialized views run as their definer, migration 0092 (see docs-internal/adr/0283-clickhouse-service-users-implementation.md).
--
-- The views on `spans` (migrations 0022, 0023, 0035, 0047, 0049) were created without an SQL SECURITY clause, so
-- ClickHouse checks them against the user who inserts into `spans`. As `default` that passed. As `flare_ingest`
-- (ADR-0282), which has INSERT but no SELECT on `spans`, every view insert fails with ACCESS_DENIED, and
-- materialized_views_ignore_errors (ClickHouseSpanWriter) drops the rollups silently: Services RED metrics, the
-- dependency map, outbound calls, LLM calls and SLO minutes all stay empty.
--
-- SQL SECURITY DEFINER with DEFINER = CURRENT_USER makes each view run as the user applying this migration (the
-- `clickhousedb` DDL credential), so Ingest needs no SELECT on telemetry. Idempotent: re-running sets the same
-- definer again. A NEW materialized view must say `SQL SECURITY DEFINER` in its own CREATE.
ALTER TABLE clickhousedb.service_metrics_mv MODIFY SQL SECURITY DEFINER DEFINER = CURRENT_USER;
ALTER TABLE clickhousedb.service_call_breakdown_database_mv MODIFY SQL SECURITY DEFINER DEFINER = CURRENT_USER;
ALTER TABLE clickhousedb.service_call_breakdown_external_mv MODIFY SQL SECURITY DEFINER DEFINER = CURRENT_USER;
ALTER TABLE clickhousedb.service_dependency_nodes_mv MODIFY SQL SECURITY DEFINER DEFINER = CURRENT_USER;
ALTER TABLE clickhousedb.outbound_calls_mv MODIFY SQL SECURITY DEFINER DEFINER = CURRENT_USER;
ALTER TABLE clickhousedb.llm_model_calls_mv MODIFY SQL SECURITY DEFINER DEFINER = CURRENT_USER;
ALTER TABLE clickhousedb.span_sli_minute_mv MODIFY SQL SECURITY DEFINER DEFINER = CURRENT_USER;
