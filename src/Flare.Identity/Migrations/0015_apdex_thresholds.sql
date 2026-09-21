-- ApdexThresholds: per-service Apdex threshold (T) overrides for the Traces > Services
-- tab (docs-internal/adr/0032-apdex-score-per-service.md). A service with no row here
-- uses Flare.Api's fixed 500ms default - only overrides are stored, not every known
-- service. Per-installation config, not telemetry, hence Identity's SQLite rather than
-- ClickHouse - same reasoning AuthSettings already establishes for this database.
--
-- Keyed by ServiceName (a plain string business key) rather than a Guid Id - the first
-- Identity table to do so. There's no natural surrogate key here: ServiceName IS the
-- identity of the thing being configured, and the read/write shape (get-by-name,
-- upsert-by-name, delete-by-name) never needs one.
CREATE TABLE IF NOT EXISTS ApdexThresholds
(
    ServiceName TEXT PRIMARY KEY,
    ThresholdMs INTEGER NOT NULL,
    UpdatedAt TEXT NOT NULL
);
