-- Cron and job check-ins, migration 0089.
--
-- `checkin_monitors` (Flare.Api.Model.CheckInMonitorModels.cs): a scheduled job Flare expects to hear from, on a cron
-- expression or a fixed interval. `checkin_events`: one row per check-in ping a job sends to Flare.Ingest's
-- `/v1/checkins/{slug}`. Flare.AlertWorker compares each monitor's last pings with its schedule and writes
-- `checkin.up` into `metrics_gauge`, so a late or failed job is an ordinary metric alert. See
-- `docs-internal/adr/0260-cron-job-checkins.md`. Same CRUD-via-tombstone shape as `synthetic_monitors` (0056).
--
-- Existing numbered migrations are immutable once merged (see this directory's README's "Migration
-- convention"), hence a new file. Like migrations 0002-0088, run this by hand via `clickhouse-client`
-- against any already-running instance.
CREATE TABLE IF NOT EXISTS clickhousedb.checkin_monitors
(
    Id UUID,

    -- The name a job puts in the ping URL. Unique among live monitors (checked on write), fixed after creation.
    Slug String CODEC(ZSTD(1)),
    Name String CODEC(ZSTD(1)),
    Description String CODEC(ZSTD(1)),
    IsDeleted UInt8 DEFAULT 0,
    Enabled UInt8 DEFAULT 1,

    -- The schedule is a five-field cron expression (or a macro such as `@daily`) or, when this is empty, a fixed
    -- `IntervalSeconds`. Exactly one is set.
    CronExpression String CODEC(ZSTD(1)),
    IntervalSeconds UInt32 DEFAULT 0,

    -- IANA zone the cron expression is read in. Ignored for an interval schedule.
    TimeZone String DEFAULT 'UTC' CODEC(ZSTD(1)),

    -- How long after the expected time a ping may arrive before the job counts as late.
    GraceSeconds UInt32,

    -- A run that has started and not finished within this many seconds counts as failed. 0 means no limit.
    MaxRuntimeSeconds UInt32 DEFAULT 0,

    CreatedAt DateTime64(3) CODEC(Delta, ZSTD(1)),
    UpdatedAt DateTime64(3) CODEC(Delta, ZSTD(1))
)
ENGINE = ReplacingMergeTree(UpdatedAt)
ORDER BY (Id)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS clickhousedb.checkin_events
(
    Slug String CODEC(ZSTD(1)),
    Time DateTime64(3) CODEC(Delta, ZSTD(1)),

    -- 'start' | 'ok' | 'fail'
    Status LowCardinality(String),

    -- Free text from the ping body, clipped at ingest. Empty when the ping had none.
    Message String CODEC(ZSTD(3))
)
ENGINE = MergeTree
PARTITION BY toStartOfMonth(Time)
ORDER BY (Slug, Time)
TTL toDateTime(Time) + INTERVAL 1 YEAR
SETTINGS index_granularity = 256;
