-- Materialized views run as their definer, migration 0092 - CLUSTER VARIANT.
--
-- Same change as db/clickhouse/0092_materialized_views_sql_security_definer.sql, on every node. The cluster views read
-- `spans_local`, and an insert through the `spans` Distributed table reaches them as the user who sent it (the
-- inter-server secret), so without this they fail for `flare_ingest` on every shard.
ALTER TABLE clickhousedb.service_metrics_mv ON CLUSTER 'flare_cluster' MODIFY SQL SECURITY DEFINER DEFINER = CURRENT_USER;
ALTER TABLE clickhousedb.service_call_breakdown_database_mv ON CLUSTER 'flare_cluster' MODIFY SQL SECURITY DEFINER DEFINER = CURRENT_USER;
ALTER TABLE clickhousedb.service_call_breakdown_external_mv ON CLUSTER 'flare_cluster' MODIFY SQL SECURITY DEFINER DEFINER = CURRENT_USER;
ALTER TABLE clickhousedb.service_dependency_nodes_mv ON CLUSTER 'flare_cluster' MODIFY SQL SECURITY DEFINER DEFINER = CURRENT_USER;
ALTER TABLE clickhousedb.outbound_calls_mv ON CLUSTER 'flare_cluster' MODIFY SQL SECURITY DEFINER DEFINER = CURRENT_USER;
ALTER TABLE clickhousedb.llm_model_calls_mv ON CLUSTER 'flare_cluster' MODIFY SQL SECURITY DEFINER DEFINER = CURRENT_USER;
ALTER TABLE clickhousedb.span_sli_minute_mv ON CLUSTER 'flare_cluster' MODIFY SQL SECURITY DEFINER DEFINER = CURRENT_USER;
