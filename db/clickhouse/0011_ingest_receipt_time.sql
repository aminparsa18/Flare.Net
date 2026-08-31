-- Adds Flare.Ingest's own receipt-time column to every OTLP signal table, migration 0011.
--
-- IngestedAt is NOT the same thing as logs.ObservedTimestamp. Both logs.Timestamp and
-- logs.ObservedTimestamp come from the OTLP wire - in every .NET OTel SDK export path
-- Flare's receivers actually see, both are stamped from the *client's own clock*,
-- generally at the same instant (the SDK's log processor sets observed_time when it
-- processes the log call, not when this server later reads the request off the wire).
-- Comparing them tells you almost nothing about clock skew against this server.
--
-- IngestedAt is Flare.Ingest's own wall-clock read (TimeProvider.GetUtcNow()), taken
-- once per accepted OTLP export request and stamped on every record/span/data point it
-- contains. It's the one timestamp on each row that's genuinely independent of the
-- sending client's clock, which is what makes `IngestedAt - <event time>` a real
-- clock-skew signal rather than a self-referential one. See ADR-0014 for the full
-- investigation and decision (including the sign convention and why this deliberately
-- never rewrites the event-time columns themselves).
--
-- Appended at the true end of each table (no AFTER clause), matching every prior
-- additive column this way (0002_logs_event_id.sql, 0010_logs_pattern.sql) - and, for
-- the three metrics tables, Flare.Ingest's ClickHouseMetricRowMapper.*Columns lists
-- append it after each type's own tail columns (Value/AggregationTemporality/etc.), not
-- into the shared prefix those lists otherwise share.
--
-- No backfill for existing rows (same precedent as every migration listed above) - they
-- get the column's implicit zero-value default (1970-01-01), not a guess at when they
-- were actually ingested.
ALTER TABLE clickhousedb.logs              ADD COLUMN IF NOT EXISTS IngestedAt DateTime64(9) CODEC(Delta, ZSTD(1));
ALTER TABLE clickhousedb.spans             ADD COLUMN IF NOT EXISTS IngestedAt DateTime64(9) CODEC(Delta, ZSTD(1));
ALTER TABLE clickhousedb.metrics_gauge     ADD COLUMN IF NOT EXISTS IngestedAt DateTime64(9) CODEC(Delta, ZSTD(1));
ALTER TABLE clickhousedb.metrics_sum       ADD COLUMN IF NOT EXISTS IngestedAt DateTime64(9) CODEC(Delta, ZSTD(1));
ALTER TABLE clickhousedb.metrics_histogram ADD COLUMN IF NOT EXISTS IngestedAt DateTime64(9) CODEC(Delta, ZSTD(1));
