-- Dashboard report schedules and their run history, migration 0066.
--
-- See `docs-internal/adr/0142-scheduled-dashboard-reports.md`.
--
-- `dashboard_schedules` is a config table in the ADR-0009 / ADR-0074 shape: a version row per
-- edit, tombstone delete, read as the latest version per `Id`. `NextRunAt` is part of the version
-- row, so the worker advancing a schedule after a run (and the API's "send now", which sets it to
-- the current time) are ordinary new versions.
--
-- `dashboard_report_runs` is append-only history, one row per attempt, so a failed render or send
-- is visible in the UI. 90 days is kept; a weekly report needs far less.
--
-- Like migrations 0002-0065, run this by hand via `clickhouse-client` against any already-running
-- instance.
CREATE TABLE IF NOT EXISTS clickhousedb.dashboard_schedules
(
    Id UUID,
    DashboardId UUID,
    Name String CODEC(ZSTD(1)),
    IsDeleted UInt8 DEFAULT 0,
    Enabled UInt8 DEFAULT 1,

    -- Five-field cron expression, read in `TimeZone` (an IANA id).
    Cron String CODEC(ZSTD(1)),
    TimeZone String CODEC(ZSTD(1)),

    -- Comma-separated recipients, same shape as `notification_channels.EmailTo`.
    Recipients String CODEC(ZSTD(1)),

    -- The dashboard's `?range=` preset (e.g. `7d`), or '' for the dashboard's own default.
    TimeRange String CODEC(ZSTD(1)),

    -- The dashboard's `var-<id>=...` query string (no leading `?`), or ''.
    VariableQuery String CODEC(ZSTD(1)),

    -- `pdf` or `png`.
    Format String DEFAULT 'pdf' CODEC(ZSTD(1)),

    -- The user whose access the render runs with: the schedule's creator.
    OwnerUserId Nullable(UUID),

    NextRunAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplacingMergeTree(UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.dashboard_report_runs
(
    Id UUID,
    ScheduleId UUID,
    DashboardId UUID,
    StartedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    DurationMs UInt32,
    -- `Succeeded` or `Failed`.
    Status LowCardinality(String),
    -- The failure reason, '' on success.
    Error String CODEC(ZSTD(1)),
    RecipientCount UInt32,
    SizeBytes UInt64
)
ENGINE = MergeTree
ORDER BY (ScheduleId, StartedAt)
TTL toDateTime(StartedAt) + INTERVAL 90 DAY
SETTINGS index_granularity = 8192;
