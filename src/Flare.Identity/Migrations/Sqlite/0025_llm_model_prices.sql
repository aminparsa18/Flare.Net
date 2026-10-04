-- LlmModelPrices: an admin's price-per-million-tokens for one model name, used by the /llm page to
-- estimate cost (docs-internal/adr/0101-llm-estimated-cost.md). Per-installation config, not
-- telemetry, so Identity's SQLite rather than ClickHouse - same reasoning as
-- MetricMetadataOverrides (0017). Only overrides are stored; the built-in defaults live in code
-- (Flare.Api's LlmPricing) so a release can refresh them without a migration.
--
-- Keyed by the model name as gen_ai.request.model reports it, matched case-insensitively and
-- exactly (a custom price is for that string, not a prefix). USD per 1,000,000 tokens.
CREATE TABLE IF NOT EXISTS LlmModelPrices
(
    Model TEXT PRIMARY KEY COLLATE NOCASE,
    InputPerMillion REAL NOT NULL,
    OutputPerMillion REAL NOT NULL,
    UpdatedAt TEXT NOT NULL
);
