-- MetricMetadataOverrides: an admin's replacement unit and/or description for one metric
-- name, shown instead of what the instrumentation sent (docs-internal/adr/0065-metric-metadata-overrides.md).
-- Per-installation config, not telemetry, so Identity's SQLite rather than ClickHouse - same
-- reasoning as ApdexThresholds (0015), and keyed the same way, by the plain string business
-- key: MetricName is the identity of the thing being configured.
--
-- Unit and Description are each independently optional - NULL means "not overridden, show
-- the emitted value", so overriding one never freezes the other.
CREATE TABLE IF NOT EXISTS MetricMetadataOverrides
(
    MetricName TEXT PRIMARY KEY,
    Unit TEXT NULL,
    Description TEXT NULL,
    UpdatedAt TEXT NOT NULL
);
