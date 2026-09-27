-- TreatAsCounter: chart this metric's Gauge points like a counter - the Sum query's
-- reset-aware per-bucket increase instead of a per-bucket average. For untyped Prometheus
-- counters (`*_total`) that arrive as Gauges. Its type can't simply be overridden like its
-- unit/description, because the type picks the ClickHouse table
-- (docs-internal/adr/0066-treat-gauge-as-counter.md).
--
-- An override row may now carry only this flag, with Unit and Description both NULL.
ALTER TABLE MetricMetadataOverrides ADD COLUMN TreatAsCounter INTEGER NOT NULL DEFAULT 0;
